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
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
MATCHING_OPERATORS = {"AkazeFeatureMatch", "OrbFeatureMatch"}
SURFACE_DEFECT_OPERATORS = {"SurfaceDefectDetection"}
ANOMALY_DETECTION_OPERATORS = {"AnomalyDetection"}
DEFAULT_MATCHING_CANDIDATE_VERSION = "v4"
DEFAULT_SURFACE_DEFECT_CANDIDATE_VERSION = "v1"
DEFAULT_ANOMALY_DETECTION_CANDIDATE_VERSION = "v1"
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
    "PositionErrorPx",
    "PixelTotals.F1",
    "PixelTotals.IoU",
    "ImageF1",
    "PixelF1",
    "Dice",
    "MaskIoU",
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
    diagnostics = {
        key: case.get(key)
        for key in (*HPATCHES_DIAGNOSTIC_FIELDS, *SURFACE_DEFECT_DIAGNOSTIC_FIELDS, *ANOMALY_DETECTION_DIAGNOSTIC_FIELDS)
        if key in case
    }
    return {
        "caseId": case_id,
        "split": str(case.get("split") or case.get("Split") or "test"),
        "passed": passed,
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


def build_report(
    execute_matching: bool,
    execute_surface_defect: bool,
    execute_anomaly_detection: bool,
    matching_candidate_version: str,
    surface_defect_candidate_version: str,
    anomaly_detection_candidate_version: str,
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
        },
        "policy": {
            "purpose": "Execute old/new replay comparisons for every public benchmark replay seed.",
            "candidateRule": f"Matching-family HPatches replay uses candidate_{matching_candidate_version}; SurfaceDefectDetection KolektorSDD2 replay uses candidate_{surface_defect_candidate_version}; AnomalyDetection MVTec replay uses candidate_{anomaly_detection_candidate_version}; other rows remain unchanged controls until their algorithm PRs supply executable candidates.",
            "claimBoundary": "This report is algorithm A/B evidence over public and semisynthetic replay seeds, not real field sign-off.",
        },
        "executionLog": execution_log,
        "operators": rows,
    }


def validate(report: dict[str, Any]) -> list[str]:
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
    if summary.get("replayCaseCount", 0) < 100:
        errors.append("A/B replay report must include the full replay set")
    for row in rows:
        operator = row.get("operator")
        cases = row.get("replayCases", [])
        if row.get("replayCaseCount", 0) <= 0 or not cases:
            errors.append(f"{operator} missing replay comparisons")
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
    lines.extend(["", "## Policy", "", report["policy"]["claimBoundary"], ""])
    return "\n".join(lines)


def format_cell(value: Any) -> str:
    if isinstance(value, (int, float)):
        return f"{value:.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def generate(
    execute_matching: bool,
    execute_surface_defect: bool,
    execute_anomaly_detection: bool,
    matching_candidate_version: str,
    surface_defect_candidate_version: str,
    anomaly_detection_candidate_version: str,
) -> dict[str, Any]:
    report = build_report(
        execute_matching,
        execute_surface_defect,
        execute_anomaly_detection,
        matching_candidate_version,
        surface_defect_candidate_version,
        anomaly_detection_candidate_version,
    )
    errors = validate(report)
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
    parser.add_argument("--execute-candidates", action="store_true", help="Run every currently wired executable candidate family.")
    parser.add_argument("--candidate-version", default=DEFAULT_MATCHING_CANDIDATE_VERSION, help="HPatches matching candidate version to execute.")
    parser.add_argument("--surface-defect-candidate-version", default=DEFAULT_SURFACE_DEFECT_CANDIDATE_VERSION, help="KolektorSDD2 SurfaceDefectDetection candidate version to execute.")
    parser.add_argument("--anomaly-detection-candidate-version", default=DEFAULT_ANOMALY_DETECTION_CANDIDATE_VERSION, help="MVTec AnomalyDetection candidate version to execute.")
    parser.add_argument("--validate-only", action="store_true", help="Validate the existing generated A/B replay report.")
    args = parser.parse_args()

    execute_matching = args.execute_matching or args.execute_candidates
    execute_surface_defect = args.execute_surface_defect or args.execute_candidates
    execute_anomaly_detection = args.execute_anomaly_detection or args.execute_candidates
    report = read_json(OUTPUT_JSON) if args.validate_only else generate(
        execute_matching,
        execute_surface_defect,
        execute_anomaly_detection,
        args.candidate_version,
        args.surface_defect_candidate_version,
        args.anomaly_detection_candidate_version,
    )
    errors = validate(report)
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
        f"generatedAt={utc_now()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
