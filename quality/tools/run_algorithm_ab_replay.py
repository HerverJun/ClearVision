from __future__ import annotations

import argparse
import json
import re
import subprocess
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
PROOF_JSON = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.json"
REPLAY_JSON = REPORT_DIR / "QualityFlywheel_public_benchmark_replay_manifest.json"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.md"
HPATCHES_PROJECT = "quality/tools/HPatchesFeatureMatchDatasetRunner/HPatchesFeatureMatchDatasetRunner.csproj"
SURFACE_DEFECT_PROJECT = "quality/tools/KolektorSurfaceDefectDatasetRunner/KolektorSurfaceDefectDatasetRunner.csproj"
ANOMALY_DETECTION_PROJECT = "quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj"
DEEP_LEARNING_PROJECT = "quality/tools/DeepLearningCocoRealModelRunner/DeepLearningCocoRealModelRunner.csproj"
EDGE_DETECTION_PROJECT = "quality/tools/BsdsEdgeContourDatasetRunner/BsdsEdgeContourDatasetRunner.csproj"
SEMANTIC_SEGMENTATION_PROJECT = "quality/tools/SemanticSegmentationDatasetRunner/SemanticSegmentationDatasetRunner.csproj"
TEMPLATE_MATCHING_PROJECT = "quality/tools/TemplateMatchingHomographyBridgeRunner/TemplateMatchingHomographyBridgeRunner.csproj"
SHAPE_MATCHING_PROJECT = "quality/tools/ShapeMatchingGeometricDatasetRunner/ShapeMatchingGeometricDatasetRunner.csproj"
CAMERA_CALIBRATION_PROJECT = "quality/tools/OpenCvCalibrationDatasetRunner/OpenCvCalibrationDatasetRunner.csproj"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
MATCHING_OPERATORS = {"AkazeFeatureMatch", "OrbFeatureMatch"}
DETECTION_OPERATORS = {"SurfaceDefectDetection", "AnomalyDetection", "EdgeDetection"}
SURFACE_DEFECT_OPERATORS = {"SurfaceDefectDetection"}
ANOMALY_DETECTION_OPERATORS = {"AnomalyDetection"}
DEEP_LEARNING_OPERATORS = {"DeepLearning"}
EDGE_DETECTION_OPERATORS = {"EdgeDetection"}
SEMANTIC_SEGMENTATION_OPERATORS = {"SemanticSegmentation"}
TEMPLATE_MATCHING_OPERATORS = {"TemplateMatching"}
SHAPE_MATCHING_OPERATORS = {"ShapeMatching"}
CAMERA_CALIBRATION_OPERATORS = {"CameraCalibration"}
DEFAULT_MATCHING_CANDIDATE_VERSION = "center_only_v1"
DEFAULT_SURFACE_DEFECT_CANDIDATE_VERSION = "v1"
DEFAULT_ANOMALY_DETECTION_CANDIDATE_VERSION = "v2"
DEFAULT_DEEP_LEARNING_CANDIDATE_VERSION = "v2"
DEFAULT_EDGE_DETECTION_CANDIDATE_VERSION = "v1"
DEFAULT_SEMANTIC_SEGMENTATION_CANDIDATE_VERSION = "v1"
DEFAULT_TEMPLATE_MATCHING_CANDIDATE_VERSION = "v1"
DEFAULT_SHAPE_MATCHING_CANDIDATE_VERSION = "v1"
DEFAULT_CAMERA_CALIBRATION_CANDIDATE_VERSION = "v1"
DEFAULT_CAMERA_CALIBRATION_CANDIDATE_PROFILE = "camera_calibration"
LOWER_BETTER_METRICS = {
    "PositionErrorPx",
    "P95PositionErrorPx",
    "MeanPositionErrorPx",
    "meanPositionErrorPx",
    "RuntimeMs",
    "MemoryAllocationBytes",
    "failed",
    "Failed",
    "PixelTotals.FalsePositive",
    "PixelTotals.FalseNegative",
    "FalsePositivePerImage",
}
IMPROVEMENT_METRIC_ORDER = (
    "ReprojectionRmsPx",
    "MaxReprojectionErrorPx",
    "DetectedImageCount",
    "TotalImages",
    "PositionErrorPx",
    "PixelTotals.F1",
    "PixelTotals.IoU",
    "ImageF1",
    "PixelF1",
    "Dice",
    "MaskIoU",
    "BoundaryF1",
    "ConsensusBoundaryF1",
    "BoundaryRecall",
    "ConsensusBoundaryRecall",
)
HPATCHES_DIAGNOSTIC_FIELDS = (
    "InlierRatio",
    "MeanReprojectionError",
    "MaxReprojectionError",
    "AreaRatio",
    "CornersInsideCount",
    "ProjectedCenterInside",
    "HomographyFailureReason",
)
SURFACE_DEFECT_DIAGNOSTIC_FIELDS = (
    "Diagnostics",
    "FailureTaxonomy",
    "ImageCorrect",
    "IsDefect",
    "PredictedDefect",
)
ANOMALY_DETECTION_DIAGNOSTIC_FIELDS = (
    "Category",
    "DefectType",
    "ImageCorrect",
    "IsAnomaly",
    "PredictedAnomaly",
)
DEEP_LEARNING_DIAGNOSTIC_FIELDS = (
    "ProcessingError",
    "OutputTensorName",
    "OutputTensorShape",
    "OutputSelectionRule",
    "YoloVersion",
)
EDGE_DETECTION_DIAGNOSTIC_FIELDS = (
    "Threshold1Used",
    "Threshold2Used",
    "PredictedEdgePixels",
    "UnionBoundaryPixels",
    "ConsensusBoundaryPixels",
    "BoundaryPrecision",
    "BoundaryRecall",
    "BoundaryF1",
    "ConsensusBoundaryPrecision",
    "ConsensusBoundaryRecall",
    "ConsensusBoundaryF1",
)
SEMANTIC_SEGMENTATION_DIAGNOSTIC_FIELDS = (
    "Scenario",
    "InputSize",
    "ChannelOrder",
    "PresentClasses",
)
TEMPLATE_MATCHING_DIAGNOSTIC_FIELDS = (
    "Sequence",
    "TemplateSource",
    "ExpectedX",
    "ExpectedY",
    "ActualX",
    "ActualY",
    "Score",
    "NormalizedScore",
)
SHAPE_MATCHING_DIAGNOSTIC_FIELDS = (
    "Scenario",
    "Width",
    "Height",
    "GroundTruthCount",
    "PredictedCount",
    "TruePositiveCount",
    "FalsePositiveCount",
    "FalseNegativeCount",
)
CAMERA_CALIBRATION_DIAGNOSTIC_FIELDS = (
    "RejectedDetectionCount",
    "RejectedOutlierCount",
    "BundleRoundTripValid",
    "OutputFileWritten",
    "FailureReasonCode",
    "Error",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def numeric(value: Any) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


def collect_flat_metrics(value: dict[str, Any]) -> dict[str, float]:
    metrics: dict[str, float] = {}
    for key, raw in value.items():
        number = numeric(raw)
        if number is not None:
            metrics[key] = round(number, 6)
        elif isinstance(raw, dict):
            for child_key, child_raw in raw.items():
                child_number = numeric(child_raw)
                if child_number is not None:
                    metrics[f"{key}.{child_key}"] = round(child_number, 6)
    return metrics


def is_lower_better_metric(metric: str) -> bool:
    if metric in LOWER_BETTER_METRICS:
        return True
    lower = metric.lower()
    return any(token in lower for token in ("error", "failed", "falsepositive", "falsenegative", "runtime", "memory"))


def normalize_case_result(case: dict[str, Any]) -> dict[str, Any]:
    case_id = str(case.get("caseId") or case.get("CaseId") or case.get("id") or case.get("Id"))
    metrics = case.get("metrics")
    if not isinstance(metrics, dict):
        metrics = collect_flat_metrics(case)
    passed = case.get("passed")
    if not isinstance(passed, bool):
        passed = bool(case.get("Passed") or case.get("Accepted") or case.get("accepted"))
    accepted = case.get("accepted")
    if not isinstance(accepted, bool):
        accepted = bool(case.get("Accepted") or case.get("accepted"))
        if not accepted and isinstance(case.get("Accepted"), bool):
            accepted = bool(case.get("Accepted"))
    diagnostic_fields = (
        *CAMERA_CALIBRATION_DIAGNOSTIC_FIELDS,
        *HPATCHES_DIAGNOSTIC_FIELDS,
        *SURFACE_DEFECT_DIAGNOSTIC_FIELDS,
        *ANOMALY_DETECTION_DIAGNOSTIC_FIELDS,
        *DEEP_LEARNING_DIAGNOSTIC_FIELDS,
        *EDGE_DETECTION_DIAGNOSTIC_FIELDS,
        *SEMANTIC_SEGMENTATION_DIAGNOSTIC_FIELDS,
        *TEMPLATE_MATCHING_DIAGNOSTIC_FIELDS,
        *SHAPE_MATCHING_DIAGNOSTIC_FIELDS,
    )
    diagnostics = {key: case.get(key) for key in diagnostic_fields if key in case}
    if isinstance(case.get("metrics"), dict):
        metric_payload = case["metrics"]
        diagnostics.update({key: metric_payload.get(key) for key in diagnostic_fields if key in metric_payload})
    return {
        "caseId": case_id,
        "split": str(case.get("split") or case.get("Split") or "test"),
        "passed": passed,
        "accepted": accepted,
        "metrics": metrics,
        "diagnostics": diagnostics,
        "failureTaxonomy": case.get("failureTaxonomy") or case.get("FailureTaxonomy") or case.get("HomographyFailureReason") or case.get("Failure") or case.get("FailureReason") or [],
    }


def read_proof_cases(proof: dict[str, Any]) -> dict[str, dict[str, Any]]:
    cases: dict[str, dict[str, Any]] = {}
    for row in proof.get("operators", []):
        operator = str(row.get("operator"))
        for case in row.get("perCaseResults", []):
            normalized = normalize_case_result(case)
            cases[f"{operator}|{normalized['caseId']}"] = normalized
    return cases


def read_matching_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def read_surface_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Images", []))
    }


def read_anomaly_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Images", []))
    }


def read_deep_learning_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def read_edge_detection_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def read_semantic_segmentation_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def read_template_matching_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def read_shape_matching_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def read_camera_calibration_candidate_cases(path: Path) -> dict[str, dict[str, Any]]:
    if not path.exists():
        return {}
    document = read_json(path)
    return {
        normalized["caseId"]: normalized
        for normalized in (normalize_case_result(case) for case in document.get("Cases", []))
    }


def summarize_cases(cases: list[dict[str, Any]]) -> dict[str, Any]:
    if not cases:
        return {"caseCount": 0, "passed": 0, "passRate": 0.0}
    passed = sum(1 for case in cases if case["passed"])
    errors = [
        float(case["metrics"]["PositionErrorPx"])
        for case in cases
        if isinstance(case.get("metrics", {}).get("PositionErrorPx"), (int, float))
    ]
    return {
        "caseCount": len(cases),
        "passed": passed,
        "failed": len(cases) - passed,
        "passRate": round(passed / len(cases), 6),
        "meanPositionErrorPx": round(sum(errors) / len(errors), 6) if errors else None,
    }


def metric_delta(old_metrics: dict[str, Any], new_metrics: dict[str, Any]) -> dict[str, Any]:
    delta: dict[str, Any] = {}
    for metric in sorted(set(old_metrics) | set(new_metrics)):
        old_value = old_metrics.get(metric)
        new_value = new_metrics.get(metric)
        if not isinstance(old_value, (int, float)) or not isinstance(new_value, (int, float)):
            continue
        change = round(float(new_value) - float(old_value), 6)
        improved = change < 0 if is_lower_better_metric(metric) else change > 0
        delta[metric] = {
            "old": round(float(old_value), 6),
            "new": round(float(new_value), 6),
            "delta": change,
            "improved": improved,
        }
    return delta


def case_status(old_case: dict[str, Any], new_case: dict[str, Any], operator: str = "") -> str:
    if not old_case["passed"] and new_case["passed"]:
        return "fixed"
    if old_case["passed"] and not new_case["passed"]:
        return "regressed"
    deltas = metric_delta(old_case["metrics"], new_case["metrics"])
    if operator in ANOMALY_DETECTION_OPERATORS:
        change = deltas.get("Score")
        if change and abs(float(change["delta"])) > 1e-9:
            is_anomaly = new_case.get("diagnostics", {}).get("IsAnomaly", True)
            score_improved = change["new"] > change["old"] if is_anomaly else change["new"] < change["old"]
            return "improved" if score_improved else "worse-metric"
    for metric in IMPROVEMENT_METRIC_ORDER:
        change = deltas.get(metric)
        if change and abs(float(change["delta"])) > 1e-9:
            return "improved" if change["improved"] else "worse-metric"
    for metric in ("PixelTotals.FalsePositive", "PixelTotals.FalseNegative"):
        change = deltas.get(metric)
        if change and abs(float(change["delta"])) > 1e-9:
            return "improved" if change["improved"] else "worse-metric"
    return "unchanged"


def edge_detection_taxonomy(old_case: dict[str, Any], new_case: dict[str, Any], status: str) -> list[str]:
    if status != "worse-metric":
        return []

    old_metrics = old_case.get("metrics", {})
    new_metrics = new_case.get("metrics", {})
    old_f1 = numeric(old_metrics.get("BoundaryF1"))
    new_f1 = numeric(new_metrics.get("BoundaryF1"))
    old_recall = numeric(old_metrics.get("BoundaryRecall"))
    new_recall = numeric(new_metrics.get("BoundaryRecall"))
    old_precision = numeric(old_metrics.get("BoundaryPrecision"))
    new_precision = numeric(new_metrics.get("BoundaryPrecision"))
    old_pixels = numeric(old_metrics.get("PredictedEdgePixels"))
    new_pixels = numeric(new_metrics.get("PredictedEdgePixels"))

    labels: list[str] = []
    if old_pixels is not None and new_pixels is not None and new_pixels < old_pixels:
        labels.append("reduced_edge_density")
    if old_recall is not None and new_recall is not None and new_recall < old_recall:
        labels.append("boundary_recall_drop")
        if old_recall - new_recall >= 0.05:
            labels.append("large_recall_drop")
    if old_precision is not None and new_precision is not None and new_precision > old_precision:
        labels.append("precision_gain_recall_tradeoff")
    elif old_precision is not None and new_precision is not None and new_precision < old_precision:
        labels.append("precision_drop")
    if old_f1 is not None and new_f1 is not None:
        labels.append("boundary_f1_drop_gt_0_01" if old_f1 - new_f1 >= 0.01 else "minor_boundary_f1_drop")
    if new_recall is not None and new_recall < 0.5:
        labels.append("low_absolute_recall")

    return labels or ["unclassified_worse_metric"]


def replay_case_ids(replay: dict[str, Any], operator: str) -> list[str]:
    return [
        str(case.get("caseId"))
        for case in replay.get("cases", [])
        if case.get("operator") == operator
    ]


def candidate_config_path(operator: str, candidate_version: str) -> Path:
    return REPORT_DIR / f"{operator}_hpatches_candidate_{candidate_version}.json"


def candidate_path(operator: str, candidate_version: str) -> Path:
    return REPORT_DIR / f"{operator}_hpatches_candidate_replay_{candidate_version}.json"


def candidate_report_path(operator: str, candidate_version: str) -> Path:
    return REPORT_DIR / f"{operator}_hpatches_candidate_replay_{candidate_version}.md"


def surface_candidate_config_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_candidate_{candidate_version}.json"


def surface_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_candidate_replay_{candidate_version}.json"


def surface_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_candidate_replay_{candidate_version}.md"


def anomaly_candidate_config_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"AnomalyDetection_mvtec_candidate_{candidate_version}.json"


def anomaly_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"AnomalyDetection_mvtec_candidate_replay_{candidate_version}.json"


def anomaly_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"AnomalyDetection_mvtec_candidate_replay_{candidate_version}.md"


def deep_learning_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"DeepLearning_coco_real_model_candidate_{candidate_version}.json"


def deep_learning_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"DeepLearning_coco_real_model_candidate_{candidate_version}.md"


def edge_detection_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"EdgeDetection_bsds500_candidate_replay_{candidate_version}.json"


def edge_detection_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"EdgeDetection_bsds500_candidate_replay_{candidate_version}.md"


def semantic_segmentation_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"SemanticSegmentation_dataset_candidate_replay_{candidate_version}.json"


def semantic_segmentation_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"SemanticSegmentation_dataset_candidate_replay_{candidate_version}.md"


def template_matching_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"TemplateMatching_public_bridge_candidate_replay_{candidate_version}.json"


def template_matching_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"TemplateMatching_public_bridge_candidate_replay_{candidate_version}.md"


def shape_matching_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"ShapeMatching_geometric_dataset_candidate_replay_{candidate_version}.json"


def shape_matching_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"ShapeMatching_geometric_dataset_candidate_replay_{candidate_version}.md"


def camera_calibration_candidate_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"CameraCalibration_opencv_samples_candidate_replay_{candidate_version}.json"


def camera_calibration_candidate_report_path(candidate_version: str) -> Path:
    return REPORT_DIR / f"CameraCalibration_opencv_samples_candidate_replay_{candidate_version}.md"


def matching_candidate_parameters(operator: str, candidate_version: str) -> dict[str, Any]:
    defaults = {
        "MaxFeatures": 1200,
        "MinInliers": 6,
        "MatchRatio": 0.75,
        "RansacThreshold": 5.0,
        "MinInlierRatio": 0.25,
        "FastThreshold": 20,
        "EdgeThreshold": 15,
        "AkazeThreshold": 0.001,
        "AllowCenterOnlyProjection": False,
    }
    source = candidate_config_path(operator, candidate_version)
    if not source.exists():
        return defaults

    summary = read_json(source).get("Summary", {})
    return {
        key: summary.get(key, value)
        for key, value in defaults.items()
    }


def surface_candidate_parameters(candidate_version: str) -> dict[str, Any]:
    defaults = {
        "CandidateVersion": candidate_version,
        "ProfileName": "baseline_default",
        "MaxSide": 256,
        "Method": "LocalContrast",
        "ThresholdMode": "Manual",
        "NormalizationMode": "LocalMean",
        "Threshold": 15.0,
        "MinArea": 4,
        "MaxArea": 1_000_000,
        "MorphCleanSize": 1,
        "MorphMode": "OpenClose",
        "BackgroundKernelSize": 31,
        "ReferenceStatsSigma": 2.5,
        "RobustReferenceStats": False,
        "ResponseNormalizeMode": "RawClamp",
        "PixelSampleStride": 4,
        "MinImageAuroc": 0.70,
        "MinPixelF1": 0.20,
    }
    source = surface_candidate_config_path(candidate_version)
    if not source.exists():
        return defaults

    summary = read_json(source).get("Summary", {})
    result = {
        key: summary.get(key, value)
        for key, value in defaults.items()
    }
    result["ProfileName"] = summary.get("ProfileName") or summary.get("Profile") or result["ProfileName"]
    return result


def anomaly_candidate_parameters(candidate_version: str) -> dict[str, Any]:
    defaults = {
        "CandidateVersion": candidate_version,
        "ProfileName": "baseline_default",
        "MaxSide": 128,
        "PatchSize": 16,
        "PatchStride": 16,
        "PixelSampleStride": 2,
        "CoresetRatio": 0.02,
        "Threshold": 0.35,
        "MinImageAuroc": 0.5,
        "MinPixelAuroc": 0.5,
        "MinCategoryImageAuroc": 0.5,
        "MinCategoryPixelAuroc": 0.5,
    }
    source = anomaly_candidate_config_path(candidate_version)
    if not source.exists():
        return defaults

    summary = read_json(source).get("Summary", {})
    result = {
        key: summary.get(key, value)
        for key, value in defaults.items()
    }
    result["ProfileName"] = summary.get("ProfileName") or summary.get("Profile") or result["ProfileName"]
    return result


def edge_detection_candidate_parameters(candidate_version: str) -> dict[str, Any]:
    return {
        "CandidateVersion": candidate_version,
        "ProfileName": "fixed_50_150_l2",
        "Threshold1": 50,
        "Threshold2": 150,
        "AutoThreshold": False,
        "AutoThresholdSigma": 0.33,
        "AutoThresholdStrategy": "MedianIntensity",
        "EnableGaussianBlur": True,
        "GaussianKernelSize": 5,
        "ApertureSize": 3,
        "L2Gradient": True,
    }


def semantic_segmentation_candidate_parameters(candidate_version: str) -> dict[str, Any]:
    return {
        "CandidateVersion": candidate_version,
        "ProfileName": "protocol_bridge_exact_map_v1",
    }


def template_matching_candidate_parameters(candidate_version: str) -> dict[str, Any]:
    return {
        "CandidateVersion": candidate_version,
        "ProfileName": "homography_bridge_ncc_v1",
    }


def shape_matching_candidate_parameters(candidate_version: str) -> dict[str, Any]:
    return {
        "CandidateVersion": candidate_version,
        "ProfileName": "geometric_dataset_bridge_v1",
    }


def camera_calibration_candidate_parameters(candidate_version: str, profile: str) -> dict[str, Any]:
    return {
        "CandidateVersion": candidate_version,
        "ProfileName": profile or "default",
    }


def execute_matching_candidate(operator: str, case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = candidate_path(operator, candidate_version)
    report = candidate_report_path(operator, candidate_version)
    parameters = matching_candidate_parameters(operator, candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        HPATCHES_PROJECT,
        "--",
        "--operator",
        operator,
        "--index",
        "quality/datasets/hpatches_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--max-sequences",
        "200",
        "--pair-index",
        "2",
        "--case-ids",
        ",".join(case_ids),
        "--max-features",
        str(parameters["MaxFeatures"]),
        "--min-inliers",
        str(parameters["MinInliers"]),
        "--match-ratio",
        str(parameters["MatchRatio"]),
        "--ransac-threshold",
        str(parameters["RansacThreshold"]),
        "--min-inlier-ratio",
        str(parameters["MinInlierRatio"]),
        "--fast-threshold",
        str(parameters["FastThreshold"]),
        "--edge-threshold",
        str(parameters["EdgeThreshold"]),
        "--akaze-threshold",
        str(parameters["AkazeThreshold"]),
        "--min-pass-rate",
        "0",
        "--max-p95-position-error-px",
        "100000",
    ]
    if parameters.get("AllowCenterOnlyProjection"):
        command.append("--allow-center-only-projection")
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: matching candidate replay failed for "
            f"{operator}\n{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_surface_defect_candidate(case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = surface_candidate_path(candidate_version)
    report = surface_candidate_report_path(candidate_version)
    parameters = surface_candidate_parameters(candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        SURFACE_DEFECT_PROJECT,
        "--",
        "--index",
        "quality/datasets/kolektorsdd2_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
        "--split",
        "test",
        "--case-ids",
        ",".join(case_ids),
        "--max-side",
        str(parameters["MaxSide"]),
        "--method",
        str(parameters["Method"]),
        "--threshold-mode",
        str(parameters["ThresholdMode"]),
        "--normalization-mode",
        str(parameters["NormalizationMode"]),
        "--threshold",
        str(parameters["Threshold"]),
        "--min-area",
        str(parameters["MinArea"]),
        "--max-area",
        str(parameters["MaxArea"]),
        "--morph-clean-size",
        str(parameters["MorphCleanSize"]),
        "--morph-mode",
        str(parameters["MorphMode"]),
        "--background-kernel-size",
        str(parameters["BackgroundKernelSize"]),
        "--reference-stats-sigma",
        str(parameters["ReferenceStatsSigma"]),
        "--robust-reference-stats",
        str(parameters["RobustReferenceStats"]).lower(),
        "--response-normalize-mode",
        str(parameters["ResponseNormalizeMode"]),
        "--pixel-sample-stride",
        str(parameters["PixelSampleStride"]),
        "--min-image-auroc",
        "0",
        "--min-pixel-f1",
        "0",
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: SurfaceDefectDetection candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_anomaly_candidate(case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = anomaly_candidate_path(candidate_version)
    report = anomaly_candidate_report_path(candidate_version)
    parameters = anomaly_candidate_parameters(candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        ANOMALY_DETECTION_PROJECT,
        "--",
        "--index",
        "quality/datasets/mvtec_ad_lite_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
        "--case-ids",
        ",".join(case_ids),
        "--max-side",
        str(parameters["MaxSide"]),
        "--patch-size",
        str(parameters["PatchSize"]),
        "--patch-stride",
        str(parameters["PatchStride"]),
        "--pixel-sample-stride",
        str(parameters["PixelSampleStride"]),
        "--coreset-ratio",
        str(parameters["CoresetRatio"]),
        "--threshold",
        str(parameters["Threshold"]),
        "--min-image-auroc",
        "0",
        "--min-pixel-auroc",
        "0",
        "--min-category-image-auroc",
        "0",
        "--min-category-pixel-auroc",
        "0",
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: AnomalyDetection candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_deep_learning_candidate(
    case_ids: list[str],
    candidate_version: str,
    model_manifest: str,
    model_path: str | None,
) -> tuple[Path, dict[str, Any]]:
    output = deep_learning_candidate_path(candidate_version)
    report = deep_learning_candidate_report_path(candidate_version)
    model_manifest_ref = model_manifest if model_path else "generated-smoke-fixture"
    parameters: dict[str, Any] = {
        "CandidateVersion": candidate_version,
        "ModelManifest": model_manifest_ref,
        "ModelPathProvided": bool(model_path),
        "GeneratedSmokeModel": not bool(model_path),
        "Confidence": 0.25,
        "NmsIou": 0.45,
    }
    command = [
        "dotnet",
        "run",
        "--project",
        DEEP_LEARNING_PROJECT,
        "--",
        "--index",
        "quality/datasets/coco2017_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--candidate-version",
        candidate_version,
        "--profile",
        "real_model_hard_nms_045",
        "--case-ids",
        ",".join(case_ids),
        "--confidence",
        str(parameters["Confidence"]),
        "--nms-iou",
        str(parameters["NmsIou"]),
    ]
    if model_path:
        command.extend(["--model-manifest", model_manifest, "--model", model_path])
    else:
        command.append("--generate-smoke-model")
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: DeepLearning real-model candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_edge_detection_candidate(case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = edge_detection_candidate_path(candidate_version)
    report = edge_detection_candidate_report_path(candidate_version)
    parameters = edge_detection_candidate_parameters(candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        EDGE_DETECTION_PROJECT,
        "--",
        "--index",
        "quality/datasets/bsds500_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--split",
        "test",
        "--case-ids",
        ",".join(case_ids),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
        "--threshold1",
        str(parameters["Threshold1"]),
        "--threshold2",
        str(parameters["Threshold2"]),
        "--auto-threshold",
        str(parameters["AutoThreshold"]).lower(),
        "--auto-threshold-sigma",
        str(parameters["AutoThresholdSigma"]),
        "--auto-threshold-strategy",
        str(parameters["AutoThresholdStrategy"]),
        "--enable-gaussian-blur",
        str(parameters["EnableGaussianBlur"]).lower(),
        "--gaussian-kernel-size",
        str(parameters["GaussianKernelSize"]),
        "--aperture-size",
        str(parameters["ApertureSize"]),
        "--l2-gradient",
        str(parameters["L2Gradient"]).lower(),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: EdgeDetection candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_semantic_segmentation_candidate(case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = semantic_segmentation_candidate_path(candidate_version)
    report = semantic_segmentation_candidate_report_path(candidate_version)
    parameters = semantic_segmentation_candidate_parameters(candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        SEMANTIC_SEGMENTATION_PROJECT,
        "--",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--case-ids",
        ",".join(case_ids),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: SemanticSegmentation candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_template_matching_candidate(case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = template_matching_candidate_path(candidate_version)
    report = template_matching_candidate_report_path(candidate_version)
    parameters = template_matching_candidate_parameters(candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        TEMPLATE_MATCHING_PROJECT,
        "--",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--case-ids",
        ",".join(case_ids),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: TemplateMatching candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_shape_matching_candidate(case_ids: list[str], candidate_version: str) -> tuple[Path, dict[str, Any]]:
    output = shape_matching_candidate_path(candidate_version)
    report = shape_matching_candidate_report_path(candidate_version)
    parameters = shape_matching_candidate_parameters(candidate_version)
    command = [
        "dotnet",
        "run",
        "--project",
        SHAPE_MATCHING_PROJECT,
        "--",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--case-ids",
        ",".join(case_ids),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: ShapeMatching candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def execute_camera_calibration_candidate(
    case_ids: list[str],
    candidate_version: str,
    candidate_profile: str,
) -> tuple[Path, dict[str, Any]]:
    output = camera_calibration_candidate_path(candidate_version)
    report = camera_calibration_candidate_report_path(candidate_version)
    parameters = camera_calibration_candidate_parameters(candidate_version, candidate_profile)
    command = [
        "dotnet",
        "run",
        "--project",
        CAMERA_CALIBRATION_PROJECT,
        "--",
        "--index",
        "quality/datasets/opencv_calibration_samples_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--case-ids",
        ",".join(case_ids),
        "--candidate-version",
        candidate_version,
        "--profile",
        str(parameters["ProfileName"]),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: CameraCalibration candidate replay failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return output, parameters


def build_report(
    execute_matching: bool,
    execute_surface_defect: bool,
    execute_anomaly_detection: bool,
    execute_edge_detection: bool,
    execute_semantic_segmentation: bool,
    execute_template_matching: bool,
    execute_shape_matching: bool,
    execute_deep_learning: bool,
    execute_camera_calibration: bool,
    matching_candidate_version: str,
    surface_defect_candidate_version: str,
    anomaly_detection_candidate_version: str,
    edge_detection_candidate_version: str,
    semantic_segmentation_candidate_version: str,
    template_matching_candidate_version: str,
    shape_matching_candidate_version: str,
    deep_learning_candidate_version: str,
    camera_calibration_candidate_version: str,
    camera_calibration_candidate_profile: str,
    deep_learning_model_manifest: str,
    deep_learning_model_path: str | None,
) -> dict[str, Any]:
    proof = read_json(PROOF_JSON)
    replay = read_json(REPLAY_JSON)
    old_cases = read_proof_cases(proof)
    proof_by_operator = {str(row.get("operator")): row for row in proof.get("operators", [])}
    replay_by_operator: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for item in replay.get("cases", []):
        replay_by_operator[str(item.get("operator"))].append(item)

    matching_candidate_cases: dict[str, dict[str, dict[str, Any]]] = {}
    surface_candidate_cases: dict[str, dict[str, Any]] = {}
    anomaly_candidate_cases: dict[str, dict[str, Any]] = {}
    edge_detection_candidate_cases: dict[str, dict[str, Any]] = {}
    semantic_segmentation_candidate_cases: dict[str, dict[str, Any]] = {}
    template_matching_candidate_cases: dict[str, dict[str, Any]] = {}
    shape_matching_candidate_cases: dict[str, dict[str, Any]] = {}
    deep_learning_candidate_cases: dict[str, dict[str, Any]] = {}
    camera_calibration_candidate_cases: dict[str, dict[str, Any]] = {}
    execution_log: list[dict[str, Any]] = []
    for operator in sorted(MATCHING_OPERATORS):
        ids = replay_case_ids(replay, operator)
        if execute_matching and ids:
            path, parameters = execute_matching_candidate(operator, ids, matching_candidate_version)
            execution_log.append(
                {
                    "operator": operator,
                    "command": "dotnet run --project quality/tools/HPatchesFeatureMatchDatasetRunner/HPatchesFeatureMatchDatasetRunner.csproj",
                    "caseCount": len(ids),
                    "candidateVersion": matching_candidate_version,
                    "sourceCandidate": repo(candidate_config_path(operator, matching_candidate_version)),
                    "parameters": parameters,
                    "candidateBaseline": repo(path),
                }
            )
        matching_candidate_cases[operator] = read_matching_candidate_cases(candidate_path(operator, matching_candidate_version))

    surface_ids = replay_case_ids(replay, "SurfaceDefectDetection")
    if execute_surface_defect and surface_ids:
        path, parameters = execute_surface_defect_candidate(surface_ids, surface_defect_candidate_version)
        execution_log.append(
            {
                "operator": "SurfaceDefectDetection",
                "command": "dotnet run --project quality/tools/KolektorSurfaceDefectDatasetRunner/KolektorSurfaceDefectDatasetRunner.csproj",
                "caseCount": len(surface_ids),
                "candidateVersion": surface_defect_candidate_version,
                "sourceCandidate": repo(surface_candidate_config_path(surface_defect_candidate_version)),
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    surface_candidate_cases = read_surface_candidate_cases(surface_candidate_path(surface_defect_candidate_version))

    anomaly_ids = replay_case_ids(replay, "AnomalyDetection")
    if execute_anomaly_detection and anomaly_ids:
        path, parameters = execute_anomaly_candidate(anomaly_ids, anomaly_detection_candidate_version)
        execution_log.append(
            {
                "operator": "AnomalyDetection",
                "command": "dotnet run --project quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj",
                "caseCount": len(anomaly_ids),
                "candidateVersion": anomaly_detection_candidate_version,
                "sourceCandidate": repo(anomaly_candidate_config_path(anomaly_detection_candidate_version)),
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    anomaly_candidate_cases = read_anomaly_candidate_cases(anomaly_candidate_path(anomaly_detection_candidate_version))

    edge_ids = replay_case_ids(replay, "EdgeDetection")
    if execute_edge_detection and edge_ids:
        path, parameters = execute_edge_detection_candidate(edge_ids, edge_detection_candidate_version)
        execution_log.append(
            {
                "operator": "EdgeDetection",
                "command": "dotnet run --project quality/tools/BsdsEdgeContourDatasetRunner/BsdsEdgeContourDatasetRunner.csproj",
                "caseCount": len(edge_ids),
                "candidateVersion": edge_detection_candidate_version,
                "sourceCandidate": "quality/evals/reports/EdgeDetection_bsds500_baseline.json",
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    edge_detection_candidate_cases = read_edge_detection_candidate_cases(edge_detection_candidate_path(edge_detection_candidate_version))

    semantic_ids = replay_case_ids(replay, "SemanticSegmentation")
    if execute_semantic_segmentation and semantic_ids:
        path, parameters = execute_semantic_segmentation_candidate(semantic_ids, semantic_segmentation_candidate_version)
        execution_log.append(
            {
                "operator": "SemanticSegmentation",
                "command": "dotnet run --project quality/tools/SemanticSegmentationDatasetRunner/SemanticSegmentationDatasetRunner.csproj",
                "caseCount": len(semantic_ids),
                "candidateVersion": semantic_segmentation_candidate_version,
                "sourceCandidate": "quality/evals/reports/SemanticSegmentation_dataset_baseline.json",
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    semantic_segmentation_candidate_cases = read_semantic_segmentation_candidate_cases(semantic_segmentation_candidate_path(semantic_segmentation_candidate_version))

    template_ids = replay_case_ids(replay, "TemplateMatching")
    if execute_template_matching and template_ids:
        path, parameters = execute_template_matching_candidate(template_ids, template_matching_candidate_version)
        execution_log.append(
            {
                "operator": "TemplateMatching",
                "command": "dotnet run --project quality/tools/TemplateMatchingHomographyBridgeRunner/TemplateMatchingHomographyBridgeRunner.csproj",
                "caseCount": len(template_ids),
                "candidateVersion": template_matching_candidate_version,
                "sourceCandidate": "quality/evals/reports/TemplateMatching_public_bridge_baseline.json",
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    template_matching_candidate_cases = read_template_matching_candidate_cases(template_matching_candidate_path(template_matching_candidate_version))

    shape_ids = replay_case_ids(replay, "ShapeMatching")
    if execute_shape_matching and shape_ids:
        path, parameters = execute_shape_matching_candidate(shape_ids, shape_matching_candidate_version)
        execution_log.append(
            {
                "operator": "ShapeMatching",
                "command": "dotnet run --project quality/tools/ShapeMatchingGeometricDatasetRunner/ShapeMatchingGeometricDatasetRunner.csproj",
                "caseCount": len(shape_ids),
                "candidateVersion": shape_matching_candidate_version,
                "sourceCandidate": "quality/evals/reports/ShapeMatching_dataset_baseline.json",
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    shape_matching_candidate_cases = read_shape_matching_candidate_cases(shape_matching_candidate_path(shape_matching_candidate_version))

    deep_learning_ids = replay_case_ids(replay, "DeepLearning")
    if execute_deep_learning and deep_learning_ids:
        path, parameters = execute_deep_learning_candidate(
            deep_learning_ids,
            deep_learning_candidate_version,
            deep_learning_model_manifest,
            deep_learning_model_path)
        execution_log.append(
            {
                "operator": "DeepLearning",
                "command": "dotnet run --project quality/tools/DeepLearningCocoRealModelRunner/DeepLearningCocoRealModelRunner.csproj",
                "caseCount": len(deep_learning_ids),
                "candidateVersion": deep_learning_candidate_version,
                "sourceCandidate": deep_learning_model_manifest,
                "parameters": parameters,
                "candidateBaseline": repo(path),
            }
        )
    if execute_deep_learning:
        deep_learning_candidate_cases = read_deep_learning_candidate_cases(deep_learning_candidate_path(deep_learning_candidate_version))

    camera_ids = replay_case_ids(replay, "CameraCalibration")
    if execute_camera_calibration and camera_ids:
        path, parameters = execute_camera_calibration_candidate(
            camera_ids,
            camera_calibration_candidate_version,
            camera_calibration_candidate_profile,
        )
        execution_log.append(
            {
                "operator": "CameraCalibration",
                "command": "dotnet run --project quality/tools/OpenCvCalibrationDatasetRunner/OpenCvCalibrationDatasetRunner.csproj",
                "caseCount": len(camera_ids),
                "candidateVersion": camera_calibration_candidate_version,
                "sourceCandidate": "quality/evals/reports/CameraCalibration_opencv_samples_baseline.json",
                "parameters": parameters,
                "candidateBaseline": repo(camera_calibration_candidate_path(camera_calibration_candidate_version)),
            }
        )
    if execute_camera_calibration:
        camera_calibration_candidate_cases = read_camera_calibration_candidate_cases(camera_calibration_candidate_path(camera_calibration_candidate_version))

    rows = []
    comparisons = []
    for operator, replay_cases in sorted(replay_by_operator.items()):
        proof_row = proof_by_operator.get(operator, {})
        operator_comparisons = []
        candidate_baseline = "same-as-old-control"
        execution_mode = "unchanged-baseline-control"
        if operator in MATCHING_OPERATORS:
            candidate_baseline = repo(candidate_path(operator, matching_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in SURFACE_DEFECT_OPERATORS:
            candidate_baseline = repo(surface_candidate_path(surface_defect_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in ANOMALY_DETECTION_OPERATORS:
            candidate_baseline = repo(anomaly_candidate_path(anomaly_detection_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in EDGE_DETECTION_OPERATORS:
            candidate_baseline = repo(edge_detection_candidate_path(edge_detection_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in SEMANTIC_SEGMENTATION_OPERATORS:
            candidate_baseline = repo(semantic_segmentation_candidate_path(semantic_segmentation_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in TEMPLATE_MATCHING_OPERATORS:
            candidate_baseline = repo(template_matching_candidate_path(template_matching_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in SHAPE_MATCHING_OPERATORS:
            candidate_baseline = repo(shape_matching_candidate_path(shape_matching_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in CAMERA_CALIBRATION_OPERATORS and execute_camera_calibration and camera_calibration_candidate_cases:
            candidate_baseline = repo(camera_calibration_candidate_path(camera_calibration_candidate_version))
            execution_mode = "candidate-executed"
        elif operator in DEEP_LEARNING_OPERATORS and deep_learning_candidate_cases:
            candidate_baseline = repo(deep_learning_candidate_path(deep_learning_candidate_version))
            execution_mode = "candidate-executed"

        for replay_case in replay_cases:
            case_id = str(replay_case.get("caseId"))
            old_case = old_cases.get(f"{operator}|{case_id}")
            if old_case is None:
                raise SystemExit(f"error: missing old replay case result for {operator} {case_id}")

            if operator in MATCHING_OPERATORS:
                new_case = matching_candidate_cases[operator].get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing matching candidate case result for {operator} {case_id}")
            elif operator in SURFACE_DEFECT_OPERATORS:
                new_case = surface_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing SurfaceDefectDetection candidate case result for {case_id}")
            elif operator in ANOMALY_DETECTION_OPERATORS:
                new_case = anomaly_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing AnomalyDetection candidate case result for {case_id}")
            elif operator in EDGE_DETECTION_OPERATORS:
                new_case = edge_detection_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing EdgeDetection candidate case result for {case_id}")
            elif operator in SEMANTIC_SEGMENTATION_OPERATORS:
                new_case = semantic_segmentation_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing SemanticSegmentation candidate case result for {case_id}")
            elif operator in TEMPLATE_MATCHING_OPERATORS:
                new_case = template_matching_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing TemplateMatching candidate case result for {case_id}")
            elif operator in SHAPE_MATCHING_OPERATORS:
                new_case = shape_matching_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing ShapeMatching candidate case result for {case_id}")
            elif operator in CAMERA_CALIBRATION_OPERATORS and execute_camera_calibration:
                if not camera_calibration_candidate_cases:
                    raise SystemExit(f"error: missing CameraCalibration candidate execution result for {operator} {case_id}")
                new_case = camera_calibration_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing CameraCalibration candidate case result for {case_id}")
            elif operator in DEEP_LEARNING_OPERATORS and deep_learning_candidate_cases:
                new_case = deep_learning_candidate_cases.get(case_id)
                if new_case is None:
                    raise SystemExit(f"error: missing DeepLearning real-model candidate case result for {case_id}")
            else:
                new_case = old_case

            status = case_status(old_case, new_case, operator)
            comparison = {
                "operator": operator,
                "datasetId": replay_case.get("datasetId"),
                "caseId": case_id,
                "split": replay_case.get("split", "test"),
                "replayClass": replay_case.get("replayClass"),
                "triageLabel": replay_case.get("triageLabel"),
                "old": old_case,
                "new": new_case,
                "delta": metric_delta(old_case["metrics"], new_case["metrics"]),
                "status": status,
                "executionMode": execution_mode,
            }
            if operator in EDGE_DETECTION_OPERATORS:
                comparison["edgeTaxonomy"] = edge_detection_taxonomy(old_case, new_case, status)
            comparisons.append(comparison)
            operator_comparisons.append(comparison)

        old_summary = summarize_cases([item["old"] for item in operator_comparisons])
        new_summary = summarize_cases([item["new"] for item in operator_comparisons])
        rows.append(
            {
                "operator": operator,
                "datasetId": proof_row.get("datasetId"),
                "proofLevel": proof_row.get("proofLevel"),
                "oldBaseline": proof_row.get("sourceBaseline"),
                "oldMetrics": old_summary,
                "candidateBaseline": candidate_baseline,
                "candidateMetrics": new_summary,
                "delta": metric_delta(old_summary, new_summary),
                "comparisonStatus": execution_mode,
                "replayCaseCount": len(operator_comparisons),
                "fixedCaseCount": sum(1 for item in operator_comparisons if item["status"] == "fixed"),
                "regressedCaseCount": sum(1 for item in operator_comparisons if item["status"] == "regressed"),
                "worseMetricCaseCount": sum(1 for item in operator_comparisons if item["status"] == "worse-metric"),
                "improvedMetricCaseCount": sum(1 for item in operator_comparisons if item["status"] in {"fixed", "improved"}),
                "replayCases": operator_comparisons,
            }
        )

    viewpoint = [
        item for item in comparisons
        if item["operator"] in MATCHING_OPERATORS and str(item["caseId"]).startswith("v_")
    ]
    surface_defect = [
        item for item in comparisons
        if item["operator"] in SURFACE_DEFECT_OPERATORS
    ]
    anomaly_detection = [
        item for item in comparisons
        if item["operator"] in ANOMALY_DETECTION_OPERATORS
    ]
    deep_learning = [
        item for item in comparisons
        if item["operator"] in DEEP_LEARNING_OPERATORS
    ]
    camera_calibration = [
        item for item in comparisons
        if item["operator"] in CAMERA_CALIBRATION_OPERATORS
    ]
    edge_detection = [
        item for item in comparisons
        if item["operator"] in EDGE_DETECTION_OPERATORS
    ]
    semantic_segmentation = [
        item for item in comparisons
        if item["operator"] in SEMANTIC_SEGMENTATION_OPERATORS
    ]
    template_matching = [
        item for item in comparisons
        if item["operator"] in TEMPLATE_MATCHING_OPERATORS
    ]
    shape_matching = [
        item for item in comparisons
        if item["operator"] in SHAPE_MATCHING_OPERATORS
    ]
    accepted = bool(comparisons) and all(item.get("new") for item in comparisons)
    return {
        "schemaVersion": "2026-04-29.algorithm-ab-replay.v2",
        "generatedAtUtc": utc_now(),
        "accepted": accepted,
        "sourceProofBaseline": repo(PROOF_JSON),
        "sourceReplayManifest": repo(REPLAY_JSON),
        "summary": {
            "operatorCount": len(rows),
            "replayCaseCount": len(comparisons),
            "comparedCaseCount": len(comparisons),
            "candidatePendingCount": 0,
            "executedCandidateCaseCount": sum(1 for item in comparisons if item["executionMode"] == "candidate-executed"),
            "controlCaseCount": sum(1 for item in comparisons if item["executionMode"] == "unchanged-baseline-control"),
            "fixedCaseCount": sum(1 for item in comparisons if item["status"] == "fixed"),
            "regressedCaseCount": sum(1 for item in comparisons if item["status"] == "regressed"),
            "improvedMetricCaseCount": sum(1 for item in comparisons if item["status"] in {"fixed", "improved"}),
            "readyForABCount": len(rows),
            "matchingViewpointCaseCount": len(viewpoint),
            "matchingViewpointFixedCaseCount": sum(1 for item in viewpoint if item["status"] == "fixed"),
            "matchingViewpointRegressedCaseCount": sum(1 for item in viewpoint if item["status"] == "regressed"),
            "surfaceDefectCaseCount": len(surface_defect),
            "surfaceDefectImprovedCaseCount": sum(1 for item in surface_defect if item["status"] in {"fixed", "improved"}),
            "surfaceDefectRegressedCaseCount": sum(1 for item in surface_defect if item["status"] == "regressed"),
            "surfaceDefectWorseMetricCaseCount": sum(1 for item in surface_defect if item["status"] == "worse-metric"),
            "anomalyDetectionCaseCount": len(anomaly_detection),
            "anomalyDetectionImprovedCaseCount": sum(1 for item in anomaly_detection if item["status"] in {"fixed", "improved"}),
            "anomalyDetectionRegressedCaseCount": sum(1 for item in anomaly_detection if item["status"] == "regressed"),
            "anomalyDetectionWorseMetricCaseCount": sum(1 for item in anomaly_detection if item["status"] == "worse-metric"),
            "anomalyDetectionImageCorrectCaseCount": sum(1 for item in anomaly_detection if item["new"].get("diagnostics", {}).get("ImageCorrect") is True),
            "anomalyDetectionDetectedAnomalyCaseCount": sum(
                1
                for item in anomaly_detection
                if item["new"].get("diagnostics", {}).get("IsAnomaly") is True
                and item["new"].get("diagnostics", {}).get("PredictedAnomaly") is True
            ),
            "edgeDetectionCaseCount": len(edge_detection),
            "edgeDetectionImprovedCaseCount": sum(1 for item in edge_detection if item["status"] in {"fixed", "improved"}),
            "edgeDetectionRegressedCaseCount": sum(1 for item in edge_detection if item["status"] == "regressed"),
            "edgeDetectionWorseMetricCaseCount": sum(1 for item in edge_detection if item["status"] == "worse-metric"),
            "edgeDetectionWorseMetricTaxonomy": dict(sorted(Counter(
                label
                for item in edge_detection
                if item["status"] == "worse-metric"
                for label in item.get("edgeTaxonomy", [])
            ).items())),
            "semanticSegmentationCaseCount": len(semantic_segmentation),
            "semanticSegmentationImprovedCaseCount": sum(1 for item in semantic_segmentation if item["status"] in {"fixed", "improved"}),
            "semanticSegmentationRegressedCaseCount": sum(1 for item in semantic_segmentation if item["status"] == "regressed"),
            "semanticSegmentationWorseMetricCaseCount": sum(1 for item in semantic_segmentation if item["status"] == "worse-metric"),
            "templateMatchingCaseCount": len(template_matching),
            "templateMatchingImprovedCaseCount": sum(1 for item in template_matching if item["status"] in {"fixed", "improved"}),
            "templateMatchingRegressedCaseCount": sum(1 for item in template_matching if item["status"] == "regressed"),
            "templateMatchingWorseMetricCaseCount": sum(1 for item in template_matching if item["status"] == "worse-metric"),
            "shapeMatchingCaseCount": len(shape_matching),
            "shapeMatchingImprovedCaseCount": sum(1 for item in shape_matching if item["status"] in {"fixed", "improved"}),
            "shapeMatchingRegressedCaseCount": sum(1 for item in shape_matching if item["status"] == "regressed"),
            "shapeMatchingWorseMetricCaseCount": sum(1 for item in shape_matching if item["status"] == "worse-metric"),
            "cameraCalibrationCaseCount": len(camera_calibration),
            "cameraCalibrationExecutedCaseCount": sum(1 for item in camera_calibration if item["executionMode"] == "candidate-executed"),
            "cameraCalibrationRegressedCaseCount": sum(1 for item in camera_calibration if item["status"] == "regressed"),
            "cameraCalibrationWorseMetricCaseCount": sum(1 for item in camera_calibration if item["status"] == "worse-metric"),
            "deepLearningCaseCount": len(deep_learning),
            "deepLearningRealModelCaseCount": sum(1 for item in deep_learning if item["executionMode"] == "candidate-executed"),
            "deepLearningRegressedCaseCount": sum(1 for item in deep_learning if item["status"] == "regressed"),
            "deepLearningProcessingErrorCaseCount": sum(1 for item in deep_learning if item["new"].get("diagnostics", {}).get("ProcessingError") is True),
        },
        "policy": {
            "purpose": "Execute old/new replay comparisons for every public benchmark replay seed.",
            "candidateRule": f"Matching-family HPatches replay uses candidate_{matching_candidate_version}; TemplateMatching homography bridge replay uses candidate_{template_matching_candidate_version}; ShapeMatching geometric dataset replay uses candidate_{shape_matching_candidate_version}; SurfaceDefectDetection KolektorSDD2 replay uses candidate_{surface_defect_candidate_version}; AnomalyDetection MVTec replay uses candidate_{anomaly_detection_candidate_version}; EdgeDetection BSDS500 replay uses candidate_{edge_detection_candidate_version}; SemanticSegmentation dataset replay uses candidate_{semantic_segmentation_candidate_version}; CameraCalibration OpenCV sample replay uses candidate_{camera_calibration_candidate_version}; DeepLearning real-model replay uses candidate_{deep_learning_candidate_version} with the supplied external model or the generated smoke fixture; other rows remain unchanged controls until their algorithm PRs supply executable candidates.",
            "claimBoundary": "This report is algorithm A/B evidence over public and semisynthetic replay seeds, not real field sign-off.",
            "deepLearningBoundary": "DeepLearning real-model candidates are ONNX Runtime outputs with AnnotationSeeded=false; do not compare annotation-seeded proof as model accuracy.",
        },
        "executionLog": execution_log,
        "operators": rows,
    }


def validate(report: dict[str, Any], validation_scope: str = "full") -> list[str]:
    errors: list[str] = []
    rows = report.get("operators")
    summary = report.get("summary", {})
    if not isinstance(rows, list) or not rows:
        return ["A/B replay report must include operators"]
    if report.get("accepted") is not True:
        errors.append("A/B replay report accepted must be true")
    if summary.get("candidatePendingCount") != 0:
        errors.append("A/B replay report must not contain candidate-pending rows")
    if summary.get("comparedCaseCount") != summary.get("replayCaseCount"):
        errors.append("A/B replay report must compare every replay case")
    if validation_scope == "matching":
        matching_rows = [row for row in rows if row.get("operator") in MATCHING_OPERATORS]
        matching_cases = [
            case
            for row in matching_rows
            for case in row.get("replayCases", [])
        ]
        if {row.get("operator") for row in matching_rows} != MATCHING_OPERATORS:
            errors.append("matching-scoped A/B replay must include AkazeFeatureMatch and OrbFeatureMatch")
        if len(matching_cases) < 40:
            errors.append("matching-scoped A/B replay must include the full HPatches matching replay set")
        if any(row.get("comparisonStatus") != "candidate-executed" for row in matching_rows):
            errors.append("matching-scoped A/B replay rows must be candidate-executed")
        if any(case.get("executionMode") != "candidate-executed" for case in matching_cases):
            errors.append("matching-scoped A/B replay cases must be candidate-executed")
        if any(case.get("old") is None or case.get("new") is None or case.get("delta") is None for case in matching_cases):
            errors.append("matching-scoped A/B replay cases must include old/new/delta")
        if sum(1 for case in matching_cases if case.get("status") == "regressed") != 0:
            errors.append("matching-scoped A/B replay must have zero regressions")
        if RAW_PATH_RE.search(json.dumps(report, ensure_ascii=False)):
            errors.append("A/B replay report contains raw path pattern")
        return errors

    if validation_scope == "detection":
        detection_rows = [row for row in rows if row.get("operator") in DETECTION_OPERATORS]
        detection_cases = [
            case
            for row in detection_rows
            for case in row.get("replayCases", [])
        ]
        if {row.get("operator") for row in detection_rows} != DETECTION_OPERATORS:
            errors.append("detection-scoped A/B replay must include SurfaceDefectDetection, AnomalyDetection, and EdgeDetection")
        if any(row.get("comparisonStatus") != "candidate-executed" for row in detection_rows):
            errors.append("detection-scoped A/B replay rows must be candidate-executed")
        if any(row.get("replayCaseCount", 0) < 20 for row in detection_rows):
            errors.append("detection-scoped A/B replay rows must include the full public replay subset")
        if any(case.get("executionMode") != "candidate-executed" for case in detection_cases):
            errors.append("detection-scoped A/B replay cases must be candidate-executed")
        if any(case.get("old") is None or case.get("new") is None or case.get("delta") is None for case in detection_cases):
            errors.append("detection-scoped A/B replay cases must include old/new/delta")
        if sum(1 for case in detection_cases if case.get("status") == "regressed") != 0:
            errors.append("detection-scoped A/B replay must have zero pass/fail regressions")
        if summary.get("surfaceDefectImprovedCaseCount", 0) <= 0:
            errors.append("detection-scoped A/B replay must improve at least one SurfaceDefectDetection replay case")
        if summary.get("anomalyDetectionImprovedCaseCount", 0) <= 0:
            errors.append("detection-scoped A/B replay must improve at least one AnomalyDetection replay case")
        if summary.get("edgeDetectionImprovedCaseCount", 0) <= 0:
            errors.append("detection-scoped A/B replay must improve at least one EdgeDetection replay case")
        if RAW_PATH_RE.search(json.dumps(report, ensure_ascii=False)):
            errors.append("A/B replay report contains raw path pattern")
        return errors

    if summary.get("replayCaseCount", 0) < 100:
        errors.append("A/B replay report must include the full replay set")
    if summary.get("executedCandidateCaseCount", 0) < 183:
        errors.append("A/B replay report must execute at least 183 candidate cases")
    if summary.get("deepLearningRealModelCaseCount", 0) < 20:
        errors.append("A/B replay report must execute at least 20 DeepLearning real-model cases")
    if summary.get("deepLearningProcessingErrorCaseCount", 0) != 0:
        errors.append("A/B replay report must have zero DeepLearning processing-error cases")
    for row in rows:
        operator = row.get("operator")
        cases = row.get("replayCases", [])
        if row.get("replayCaseCount", 0) <= 0 or not cases:
            errors.append(f"{operator} missing replay comparisons")
        if operator == "DeepLearning":
            if row.get("comparisonStatus") != "candidate-executed":
                errors.append("DeepLearning row must be candidate-executed")
            candidate_baseline = row.get("candidateBaseline")
            if not candidate_baseline:
                errors.append("DeepLearning row missing candidate baseline path")
            else:
                candidate_path = REPO_ROOT / str(candidate_baseline)
                if not candidate_path.exists():
                    errors.append(f"DeepLearning candidate baseline missing: {candidate_baseline}")
                else:
                    candidate_summary = read_json(candidate_path).get("Summary", {})
                    if candidate_summary.get("Profile") != "real_model_hard_nms_045":
                        errors.append("DeepLearning candidate profile must be real_model_hard_nms_045")
                    if candidate_summary.get("AnnotationSeeded") is not False:
                        errors.append("DeepLearning candidate must have AnnotationSeeded=false")
                    if str(candidate_summary.get("ModelArtifactRef", "")).strip() == "":
                        errors.append("DeepLearning candidate must include a model artifact reference")
        if row.get("comparisonStatus") == "candidate-pending":
            errors.append(f"{operator} still candidate-pending")
        for case in cases:
            if not case.get("old") or not case.get("new") or case.get("delta") is None:
                errors.append(f"{operator} {case.get('caseId')} missing old/new/delta")
    if RAW_PATH_RE.search(json.dumps(report, ensure_ascii=False)):
        errors.append("A/B replay report contains raw path pattern")
    return errors


def render_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Algorithm A/B Replay Report",
        "",
        f"GeneratedAtUtc: `{report['generatedAtUtc']}`",
        f"Accepted: `{'Yes' if report['accepted'] else 'No'}`",
        "",
        "## Summary",
        "",
        f"- Operators: {report['summary']['operatorCount']}",
        f"- Replay cases compared: {report['summary']['comparedCaseCount']}",
        f"- Executed candidate cases: {report['summary']['executedCandidateCaseCount']}",
        f"- Control cases: {report['summary']['controlCaseCount']}",
        f"- Fixed cases: {report['summary']['fixedCaseCount']}",
        f"- Regressed cases: {report['summary']['regressedCaseCount']}",
        f"- Matching viewpoint cases: {report['summary']['matchingViewpointCaseCount']}",
        f"- Matching viewpoint fixed: {report['summary']['matchingViewpointFixedCaseCount']}",
        f"- Surface defect replay cases: {report['summary'].get('surfaceDefectCaseCount', 0)}",
        f"- Surface defect improved cases: {report['summary'].get('surfaceDefectImprovedCaseCount', 0)}",
        f"- Surface defect regressed cases: {report['summary'].get('surfaceDefectRegressedCaseCount', 0)}",
        f"- Surface defect worse metric cases: {report['summary'].get('surfaceDefectWorseMetricCaseCount', 0)}",
        f"- Anomaly detection replay cases: {report['summary'].get('anomalyDetectionCaseCount', 0)}",
        f"- Anomaly detection improved cases: {report['summary'].get('anomalyDetectionImprovedCaseCount', 0)}",
        f"- Anomaly detection regressed cases: {report['summary'].get('anomalyDetectionRegressedCaseCount', 0)}",
        f"- Anomaly detection worse metric cases: {report['summary'].get('anomalyDetectionWorseMetricCaseCount', 0)}",
        f"- Anomaly detection image-correct cases: {report['summary'].get('anomalyDetectionImageCorrectCaseCount', 0)}",
        f"- Anomaly detection detected anomaly cases: {report['summary'].get('anomalyDetectionDetectedAnomalyCaseCount', 0)}",
        f"- EdgeDetection replay cases: {report['summary'].get('edgeDetectionCaseCount', 0)}",
        f"- EdgeDetection improved cases: {report['summary'].get('edgeDetectionImprovedCaseCount', 0)}",
        f"- EdgeDetection regressed cases: {report['summary'].get('edgeDetectionRegressedCaseCount', 0)}",
        f"- EdgeDetection worse metric cases: {report['summary'].get('edgeDetectionWorseMetricCaseCount', 0)}",
        f"- EdgeDetection worse taxonomy: {report['summary'].get('edgeDetectionWorseMetricTaxonomy', {})}",
        f"- SemanticSegmentation replay cases: {report['summary'].get('semanticSegmentationCaseCount', 0)}",
        f"- SemanticSegmentation improved cases: {report['summary'].get('semanticSegmentationImprovedCaseCount', 0)}",
        f"- SemanticSegmentation regressed cases: {report['summary'].get('semanticSegmentationRegressedCaseCount', 0)}",
        f"- SemanticSegmentation worse metric cases: {report['summary'].get('semanticSegmentationWorseMetricCaseCount', 0)}",
        f"- CameraCalibration replay cases: {report['summary'].get('cameraCalibrationCaseCount', 0)}",
        f"- CameraCalibration executed cases: {report['summary'].get('cameraCalibrationExecutedCaseCount', 0)}",
        f"- CameraCalibration regressed cases: {report['summary'].get('cameraCalibrationRegressedCaseCount', 0)}",
        f"- CameraCalibration worse metric cases: {report['summary'].get('cameraCalibrationWorseMetricCaseCount', 0)}",
        f"- TemplateMatching replay cases: {report['summary'].get('templateMatchingCaseCount', 0)}",
        f"- TemplateMatching improved cases: {report['summary'].get('templateMatchingImprovedCaseCount', 0)}",
        f"- TemplateMatching regressed cases: {report['summary'].get('templateMatchingRegressedCaseCount', 0)}",
        f"- TemplateMatching worse metric cases: {report['summary'].get('templateMatchingWorseMetricCaseCount', 0)}",
        f"- ShapeMatching replay cases: {report['summary'].get('shapeMatchingCaseCount', 0)}",
        f"- ShapeMatching improved cases: {report['summary'].get('shapeMatchingImprovedCaseCount', 0)}",
        f"- ShapeMatching regressed cases: {report['summary'].get('shapeMatchingRegressedCaseCount', 0)}",
        f"- ShapeMatching worse metric cases: {report['summary'].get('shapeMatchingWorseMetricCaseCount', 0)}",
        f"- DeepLearning replay cases: {report['summary'].get('deepLearningCaseCount', 0)}",
        f"- DeepLearning real-model candidate cases: {report['summary'].get('deepLearningRealModelCaseCount', 0)}",
        f"- DeepLearning processing-error cases: {report['summary'].get('deepLearningProcessingErrorCaseCount', 0)}",
        "",
        "## Operators",
        "",
        "| Operator | Dataset | Status | Replay | Old Pass | New Pass | Fixed | Regressed | Worse metric | Candidate |",
        "|---|---|---|---:|---:|---:|---:|---:|---:|---|",
    ]
    for row in report["operators"]:
        lines.append(
            f"| {row['operator']} | {row['datasetId']} | {row['comparisonStatus']} | "
            f"{row['replayCaseCount']} | {row['oldMetrics'].get('passRate')} | "
            f"{row['candidateMetrics'].get('passRate')} | {row['fixedCaseCount']} | "
            f"{row['regressedCaseCount']} | {row.get('worseMetricCaseCount', 0)} | {row['candidateBaseline']} |"
        )
    lines.extend(
        [
            "",
            "## Matching Viewpoint Focus",
            "",
            "| Operator | Case | Status | New pass | Old error | New error | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |",
            "|---|---|---|---|---:|---:|---:|---:|---:|---:|---|---|",
        ]
    )
    viewpoint_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] not in MATCHING_OPERATORS:
            continue
        for case in row["replayCases"]:
            if str(case["caseId"]).startswith("v_"):
                viewpoint_rows.append(case)
    viewpoint_rows.sort(
        key=lambda case: (
            str(case["new"].get("diagnostics", {}).get("HomographyFailureReason") or case.get("status") or ""),
            str(case["operator"]),
            str(case["caseId"]),
        )
    )
    for case in viewpoint_rows:
        old_error = case["old"]["metrics"].get("PositionErrorPx")
        new_error = case["new"]["metrics"].get("PositionErrorPx")
        diagnostics = case["new"].get("diagnostics", {})
        lines.append(
            f"| {case['operator']} | {case['caseId']} | {case['status']} | {case['new']['passed']} | "
            f"{format_cell(old_error)} | {format_cell(new_error)} | "
            f"{format_cell(diagnostics.get('InlierRatio'))} | "
            f"{format_cell(diagnostics.get('MeanReprojectionError'))} | "
            f"{format_cell(diagnostics.get('AreaRatio'))} | "
            f"{diagnostics.get('CornersInsideCount', '-')} | "
            f"{diagnostics.get('ProjectedCenterInside', '-')} | "
            f"{diagnostics.get('HomographyFailureReason') or '-'} |"
        )

    lines.extend(
        [
            "",
            "## Surface Defect Focus",
            "",
            "| Case | Status | Is defect | Predicted | Old F1 | New F1 | Old FP | New FP | Old FN | New FN | Taxonomy |",
            "|---|---|---|---|---:|---:|---:|---:|---:|---:|---|",
        ]
    )
    surface_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "SurfaceDefectDetection":
            continue
        surface_rows.extend(row["replayCases"])
    surface_rows.sort(
        key=lambda case: (
            0 if case["status"] in {"regressed", "worse-metric"} else 1,
            str(case["caseId"]),
        )
    )
    for case in surface_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        taxonomy = case["new"].get("failureTaxonomy") or diagnostics.get("FailureTaxonomy") or []
        if isinstance(taxonomy, list):
            taxonomy_text = ", ".join(str(item) for item in taxonomy) or "-"
        else:
            taxonomy_text = str(taxonomy)
        lines.append(
            f"| {case['caseId']} | {case['status']} | {diagnostics.get('IsDefect', '-')} | "
            f"{diagnostics.get('PredictedDefect', '-')} | "
            f"{format_cell(old_metrics.get('PixelTotals.F1'))} | {format_cell(new_metrics.get('PixelTotals.F1'))} | "
            f"{format_cell(old_metrics.get('PixelTotals.FalsePositive'))} | {format_cell(new_metrics.get('PixelTotals.FalsePositive'))} | "
            f"{format_cell(old_metrics.get('PixelTotals.FalseNegative'))} | {format_cell(new_metrics.get('PixelTotals.FalseNegative'))} | "
            f"{taxonomy_text} |"
        )

    lines.extend(
        [
            "",
            "## Anomaly Detection Focus",
            "",
            "| Case | Status | Is anomaly | Predicted | Old score | New score | Image correct | Taxonomy |",
            "|---|---|---|---|---:|---:|---|---|",
        ]
    )
    anomaly_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "AnomalyDetection":
            continue
        anomaly_rows.extend(row["replayCases"])
    anomaly_rows.sort(
        key=lambda case: (
            0 if case["status"] in {"regressed", "worse-metric"} else 1,
            str(case["caseId"]),
        )
    )
    for case in anomaly_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        taxonomy = case["new"].get("failureTaxonomy") or []
        if isinstance(taxonomy, list):
            taxonomy_text = ", ".join(str(item) for item in taxonomy) or "-"
        else:
            taxonomy_text = str(taxonomy)
        lines.append(
            f"| {case['caseId']} | {case['status']} | {diagnostics.get('IsAnomaly', '-')} | "
            f"{diagnostics.get('PredictedAnomaly', '-')} | "
            f"{format_cell(old_metrics.get('Score'))} | {format_cell(new_metrics.get('Score'))} | "
            f"{diagnostics.get('ImageCorrect', '-')} | {taxonomy_text} |"
        )
    lines.extend(
        [
            "",
            "## EdgeDetection BSDS500 Focus",
            "",
            "| Case | Status | Old F1 | New F1 | Old recall | New recall | Old precision | New precision | Thresholds | Predicted px | Taxonomy |",
            "|---|---|---:|---:|---:|---:|---:|---:|---|---:|---|",
        ]
    )
    edge_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "EdgeDetection":
            continue
        edge_rows.extend(row["replayCases"])
    edge_rows.sort(
        key=lambda case: (
            0 if case["status"] in {"regressed", "worse-metric"} else 1,
            str(case["caseId"]),
        )
    )
    for case in edge_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        thresholds = f"{format_cell(diagnostics.get('Threshold1Used'))}/{format_cell(diagnostics.get('Threshold2Used'))}"
        lines.append(
            f"| {case['caseId']} | {case['status']} | "
            f"{format_cell(old_metrics.get('BoundaryF1'))} | {format_cell(new_metrics.get('BoundaryF1'))} | "
            f"{format_cell(old_metrics.get('BoundaryRecall'))} | {format_cell(new_metrics.get('BoundaryRecall'))} | "
            f"{format_cell(old_metrics.get('BoundaryPrecision'))} | {format_cell(new_metrics.get('BoundaryPrecision'))} | "
            f"{thresholds} | {format_cell(new_metrics.get('PredictedEdgePixels'))} | "
            f"{', '.join(case.get('edgeTaxonomy', [])) or '-'} |"
        )
    lines.extend(
        [
            "",
            "## SemanticSegmentation Focus",
            "",
            "| Case | Status | Old mIoU | New mIoU | Old boundary IoU | New boundary IoU | Input | Classes |",
            "|---|---|---:|---:|---:|---:|---|---|",
        ]
    )
    semantic_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "SemanticSegmentation":
            continue
        semantic_rows.extend(row["replayCases"])
    semantic_rows.sort(key=lambda case: (str(case["status"]), str(case["caseId"])))
    for case in semantic_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        classes = diagnostics.get("PresentClasses")
        classes_text = ", ".join(str(item) for item in classes) if isinstance(classes, list) else str(classes or "-")
        lines.append(
            f"| {case['caseId']} | {case['status']} | "
            f"{format_cell(old_metrics.get('MeanIoU'))} | {format_cell(new_metrics.get('MeanIoU'))} | "
            f"{format_cell(old_metrics.get('BoundaryIoU'))} | {format_cell(new_metrics.get('BoundaryIoU'))} | "
            f"{diagnostics.get('InputSize', '-')} / {diagnostics.get('ChannelOrder', '-')} | {classes_text} |"
        )
    lines.extend(
        [
            "",
            "## TemplateMatching Homography Bridge Focus",
            "",
            "| Case | Status | Sequence | Template | Old error | New error | Old norm score | New norm score |",
            "|---|---|---|---|---:|---:|---:|---:|",
        ]
    )
    template_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "TemplateMatching":
            continue
        template_rows.extend(row["replayCases"])
    template_rows.sort(key=lambda case: (str(case["status"]), str(case["caseId"])))
    for case in template_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        lines.append(
            f"| {case['caseId']} | {case['status']} | {diagnostics.get('Sequence', '-')} | "
            f"{diagnostics.get('TemplateSource', '-')} | "
            f"{format_cell(old_metrics.get('PositionErrorPx'))} | {format_cell(new_metrics.get('PositionErrorPx'))} | "
            f"{format_cell(old_metrics.get('NormalizedScore'))} | {format_cell(new_metrics.get('NormalizedScore'))} |"
        )
    lines.extend(
        [
            "",
            "## ShapeMatching Geometric Dataset Focus",
            "",
            "| Case | Status | Scenario | Old F1 | New F1 | Old pos err | New pos err | GT | Pred | FP | FN |",
            "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    shape_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "ShapeMatching":
            continue
        shape_rows.extend(row["replayCases"])
    shape_rows.sort(key=lambda case: (str(case["status"]), str(case["caseId"])))
    for case in shape_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        lines.append(
            f"| {case['caseId']} | {case['status']} | {diagnostics.get('Scenario', '-')} | "
            f"{format_cell(old_metrics.get('F1'))} | {format_cell(new_metrics.get('F1'))} | "
            f"{format_cell(old_metrics.get('MeanPositionErrorPx'))} | {format_cell(new_metrics.get('MeanPositionErrorPx'))} | "
            f"{format_cell(diagnostics.get('GroundTruthCount'))} | {format_cell(diagnostics.get('PredictedCount'))} | "
            f"{format_cell(diagnostics.get('FalsePositiveCount'))} | {format_cell(diagnostics.get('FalseNegativeCount'))} |"
        )
    lines.extend(
        [
            "",
            "## CameraCalibration Focus",
            "",
            "| Case | Status | New pass | Accepted | Old RMS | New RMS | Old max error | New max error | Old detected | New detected | Total | Failure reason |",
            "|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|",
        ]
    )
    camera_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] == "CameraCalibration":
            camera_rows.extend(row["replayCases"])
    camera_rows.sort(key=lambda case: (str(case["status"]), str(case["caseId"])))
    for case in camera_rows:
        old_metrics = case["old"].get("metrics", {})
        new_metrics = case["new"].get("metrics", {})
        taxonomy = case["new"].get("failureTaxonomy")
        if isinstance(taxonomy, list):
            taxonomy_text = ", ".join(str(item) for item in taxonomy) or "-"
        else:
            taxonomy_text = str(taxonomy or "-")
        lines.append(
            f"| {case['caseId']} | {case['status']} | {case['new']['passed']} | {case['new'].get('accepted')} | "
            f"{format_cell(old_metrics.get('ReprojectionRmsPx'))} | {format_cell(new_metrics.get('ReprojectionRmsPx'))} | "
            f"{format_cell(old_metrics.get('MaxReprojectionErrorPx'))} | {format_cell(new_metrics.get('MaxReprojectionErrorPx'))} | "
            f"{format_cell(old_metrics.get('DetectedImageCount'))} | {format_cell(new_metrics.get('DetectedImageCount'))} | "
            f"{format_cell(new_metrics.get('TotalImages'))} | {taxonomy_text} |"
        )
    lines.extend(
        [
            "",
            "## DeepLearning Real Model Focus",
            "",
            "| Case | Status | Execution | Old pass | New pass | New detections | TP | FP | FN | Processing error | Output shape |",
            "|---|---|---|---|---|---:|---:|---:|---:|---|---|",
        ]
    )
    deep_rows: list[dict[str, Any]] = []
    for row in report["operators"]:
        if row["operator"] != "DeepLearning":
            continue
        deep_rows.extend(row["replayCases"])
    for case in deep_rows:
        new_metrics = case["new"].get("metrics", {})
        diagnostics = case["new"].get("diagnostics", {})
        shape = diagnostics.get("OutputTensorShape")
        if isinstance(shape, list):
            shape_text = "x".join(str(item) for item in shape)
        else:
            shape_text = str(shape or "-")
        lines.append(
            f"| {case['caseId']} | {case['status']} | {case['executionMode']} | "
            f"{case['old'].get('passed')} | {case['new'].get('passed')} | "
            f"{format_cell(new_metrics.get('DetectionCount'))} | {format_cell(new_metrics.get('TruePositiveCount'))} | "
            f"{format_cell(new_metrics.get('FalsePositiveCount'))} | {format_cell(new_metrics.get('FalseNegativeCount'))} | "
            f"{diagnostics.get('ProcessingError', '-')} | {shape_text} |"
        )
    lines.extend(["", "## Policy", "", report["policy"]["claimBoundary"], ""])
    if report.get("policy", {}).get("deepLearningBoundary"):
        lines.extend([report["policy"]["deepLearningBoundary"], ""])
    return "\n".join(lines)


def format_cell(value: Any) -> str:
    if isinstance(value, (int, float)):
        return f"{value:.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def generate(
    execute_matching: bool,
    execute_surface_defect: bool,
    execute_anomaly_detection: bool,
    execute_edge_detection: bool,
    execute_semantic_segmentation: bool,
    execute_template_matching: bool,
    execute_shape_matching: bool,
    execute_deep_learning: bool,
    execute_camera_calibration: bool,
    matching_candidate_version: str,
    surface_defect_candidate_version: str,
    anomaly_detection_candidate_version: str,
    edge_detection_candidate_version: str,
    semantic_segmentation_candidate_version: str,
    template_matching_candidate_version: str,
    shape_matching_candidate_version: str,
    deep_learning_candidate_version: str,
    camera_calibration_candidate_version: str,
    camera_calibration_candidate_profile: str,
    deep_learning_model_manifest: str,
    deep_learning_model_path: str | None,
    validation_scope: str = "full",
) -> dict[str, Any]:
    report = build_report(
        execute_matching,
        execute_surface_defect,
        execute_anomaly_detection,
        execute_edge_detection,
        execute_semantic_segmentation,
        execute_template_matching,
        execute_shape_matching,
        execute_deep_learning,
        execute_camera_calibration,
        matching_candidate_version,
        surface_defect_candidate_version,
        anomaly_detection_candidate_version,
        edge_detection_candidate_version,
        semantic_segmentation_candidate_version,
        template_matching_candidate_version,
        shape_matching_candidate_version,
        deep_learning_candidate_version,
        camera_calibration_candidate_version,
        camera_calibration_candidate_profile,
        deep_learning_model_manifest,
        deep_learning_model_path,
    )
    report["policy"]["validationScope"] = validation_scope
    errors = validate(report, validation_scope)
    if errors:
        raise SystemExit("\n".join(f"error: {error}" for error in errors))
    write_json(OUTPUT_JSON, report)
    write_text(OUTPUT_MD, render_markdown(report))
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description="Run or validate executable algorithm A/B replay comparisons.")
    parser.add_argument("--execute-matching", action="store_true", help="Run HPatches matching-family candidate replay before building the report.")
    parser.add_argument("--execute-surface-defect", action="store_true", help="Run KolektorSDD2 SurfaceDefectDetection candidate replay before building the report.")
    parser.add_argument("--execute-anomaly-detection", action="store_true", help="Run MVTec AnomalyDetection candidate replay before building the report.")
    parser.add_argument("--execute-edge-detection", action="store_true", help="Run BSDS500 EdgeDetection candidate replay before building the report.")
    parser.add_argument("--execute-semantic-segmentation", action="store_true", help="Run SemanticSegmentation dataset candidate replay before building the report.")
    parser.add_argument("--execute-template-matching", action="store_true", help="Run TemplateMatching homography bridge candidate replay before building the report.")
    parser.add_argument("--execute-shape-matching", action="store_true", help="Run ShapeMatching geometric dataset candidate replay before building the report.")
    parser.add_argument("--execute-deep-learning", action="store_true", help="Run DeepLearning COCO real-model candidate replay; uses a generated smoke model unless --deep-learning-model is provided.")
    parser.add_argument("--execute-camera-calibration", action="store_true", help="Run CameraCalibration OpenCV calibration sample candidate replay before building the report.")
    parser.add_argument("--execute-candidates", action="store_true", help="Run every currently wired executable candidate family.")
    parser.add_argument("--candidate-version", default=DEFAULT_MATCHING_CANDIDATE_VERSION, help="HPatches matching candidate version to execute.")
    parser.add_argument("--surface-defect-candidate-version", default=DEFAULT_SURFACE_DEFECT_CANDIDATE_VERSION, help="KolektorSDD2 SurfaceDefectDetection candidate version to execute.")
    parser.add_argument("--anomaly-detection-candidate-version", default=DEFAULT_ANOMALY_DETECTION_CANDIDATE_VERSION, help="MVTec AnomalyDetection candidate version to execute.")
    parser.add_argument("--edge-detection-candidate-version", default=DEFAULT_EDGE_DETECTION_CANDIDATE_VERSION, help="BSDS500 EdgeDetection candidate version to execute.")
    parser.add_argument("--semantic-segmentation-candidate-version", default=DEFAULT_SEMANTIC_SEGMENTATION_CANDIDATE_VERSION, help="SemanticSegmentation dataset candidate version to execute.")
    parser.add_argument("--template-matching-candidate-version", default=DEFAULT_TEMPLATE_MATCHING_CANDIDATE_VERSION, help="TemplateMatching homography bridge candidate version to execute.")
    parser.add_argument("--shape-matching-candidate-version", default=DEFAULT_SHAPE_MATCHING_CANDIDATE_VERSION, help="ShapeMatching geometric dataset candidate version to execute.")
    parser.add_argument("--deep-learning-candidate-version", default=DEFAULT_DEEP_LEARNING_CANDIDATE_VERSION, help="DeepLearning real-model candidate version to execute.")
    parser.add_argument("--camera-calibration-candidate-version", default=DEFAULT_CAMERA_CALIBRATION_CANDIDATE_VERSION, help="CameraCalibration OpenCV sample candidate version to execute.")
    parser.add_argument("--camera-calibration-candidate-profile", default=DEFAULT_CAMERA_CALIBRATION_CANDIDATE_PROFILE, help="CameraCalibration OpenCV sample candidate profile.")
    parser.add_argument("--deep-learning-model-manifest", default="models/object_detection/coco_yolo_real_model_manifest.template.json", help="DeepLearning real-model manifest path.")
    parser.add_argument("--deep-learning-model", default=None, help="Optional DeepLearning ONNX model artifact path. Model files must not be committed.")
    parser.add_argument("--validate-only", action="store_true", help="Validate the existing generated A/B replay report.")
    parser.add_argument("--validation-scope", choices=("full", "matching", "detection"), default="full", help="Validation scope. Use matching or detection for focused replay gates.")
    args = parser.parse_args()

    execute_matching = args.execute_matching or args.execute_candidates
    execute_surface_defect = args.execute_surface_defect or args.execute_candidates
    execute_anomaly_detection = args.execute_anomaly_detection or args.execute_candidates
    execute_edge_detection = args.execute_edge_detection or args.execute_candidates
    execute_semantic_segmentation = args.execute_semantic_segmentation or args.execute_candidates
    execute_template_matching = args.execute_template_matching or args.execute_candidates
    execute_shape_matching = args.execute_shape_matching or args.execute_candidates
    execute_deep_learning = args.execute_deep_learning or args.execute_candidates
    execute_camera_calibration = args.execute_camera_calibration or args.execute_candidates
    report = read_json(OUTPUT_JSON) if args.validate_only else generate(
        execute_matching,
        execute_surface_defect,
        execute_anomaly_detection,
        execute_edge_detection,
        execute_semantic_segmentation,
        execute_template_matching,
        execute_shape_matching,
        execute_deep_learning,
        execute_camera_calibration,
        args.candidate_version,
        args.surface_defect_candidate_version,
        args.anomaly_detection_candidate_version,
        args.edge_detection_candidate_version,
        args.semantic_segmentation_candidate_version,
        args.template_matching_candidate_version,
        args.shape_matching_candidate_version,
        args.deep_learning_candidate_version,
        args.camera_calibration_candidate_version,
        args.camera_calibration_candidate_profile,
        args.deep_learning_model_manifest,
        args.deep_learning_model,
        args.validation_scope,
    )
    errors = validate(report, args.validation_scope)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    print(
        "algorithm A/B replay report valid: "
        f"operators={report['summary']['operatorCount']} "
        f"replayCases={report['summary']['replayCaseCount']} "
        f"executedCandidateCases={report['summary']['executedCandidateCaseCount']} "
        f"viewpointFixed={report['summary']['matchingViewpointFixedCaseCount']} "
        f"surfaceImproved={report['summary'].get('surfaceDefectImprovedCaseCount', 0)} "
        f"anomalyImproved={report['summary'].get('anomalyDetectionImprovedCaseCount', 0)} "
        f"anomalyDetected={report['summary'].get('anomalyDetectionDetectedAnomalyCaseCount', 0)} "
        f"edgeImproved={report['summary'].get('edgeDetectionImprovedCaseCount', 0)} "
        f"semanticCases={report['summary'].get('semanticSegmentationCaseCount', 0)} "
        f"templateCases={report['summary'].get('templateMatchingCaseCount', 0)} "
        f"shapeCases={report['summary'].get('shapeMatchingCaseCount', 0)} "
        f"cameraCases={report['summary'].get('cameraCalibrationCaseCount', 0)} "
        f"cameraExecuted={report['summary'].get('cameraCalibrationExecutedCaseCount', 0)} "
        f"deepLearningRealModel={report['summary'].get('deepLearningRealModelCaseCount', 0)} "
        f"validationScope={args.validation_scope} "
        f"generatedAt={utc_now()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
