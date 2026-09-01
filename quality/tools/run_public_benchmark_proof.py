from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from evaluate_delivery_model_evidence import evaluate_delivery_evidence


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
DATASET_DIR = REPO_ROOT / "quality" / "datasets"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.json"
OUTPUT_SUMMARY_JSON = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.summary.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_public_benchmark_proof_baseline.md"
REPLAY_JSON = REPORT_DIR / "QualityFlywheel_public_benchmark_replay_manifest.json"
REPLAY_MD = REPORT_DIR / "QualityFlywheel_public_benchmark_replay_manifest.md"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
GENERATED_AT = "2026-04-29T00:00:00Z"
PROOF_SCHEMA_VERSION = "2026-04-29.public-benchmark-proof.v1"
REPLAY_SCHEMA_VERSION = "2026-04-29.public-benchmark-replay.v1"
ALLOWED_REPLAY_COMMANDS = {
    ("python", "quality/tools/run_algorithm_ab_replay.py", "--execute-camera-calibration"),
    ("python", "quality/tools/run_algorithm_ab_replay.py", "--execute-matching"),
}
REQUIRED_RESULT_FIELDS = {
    "datasetId",
    "manifestSha256",
    "splitSummary",
    "metrics",
    "thresholdResults",
    "perCaseResults",
    "failureTaxonomy",
    "privacyLeakCount",
    "accepted",
}


PROOF_SOURCES: tuple[dict[str, Any], ...] = (
    {
        "operator": "AnomalyDetection",
        "datasetId": "mvtec_ad_lite",
        "sourceBaseline": "quality/evals/reports/AnomalyDetection_mvtec_baseline.json",
        "manifest": "quality/datasets/mvtec_ad_lite_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "public industrial anomaly benchmark proof",
        "primaryMetrics": ("ImageAuroc", "PixelAuroc"),
        "boundaryMetric": "Score",
        "thresholds": {"ImageAuroc": "MinImageAuroc", "PixelAuroc": "MinPixelAuroc", "Failed": 0},
        "caseListKeys": ("Images",),
    },
    {
        "operator": "SurfaceDefectDetection",
        "datasetId": "kolektorsdd2",
        "sourceBaseline": "quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_baseline.json",
        "manifest": "quality/datasets/kolektorsdd2_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "public industrial surface defect benchmark proof",
        "primaryMetrics": ("ImageAuroc", "PixelF1"),
        "boundaryMetric": "PixelTotals.F1",
        "thresholds": {"ImageAuroc": "MinImageAuroc", "PixelF1": "MinPixelF1", "Failed": 0},
        "caseListKeys": ("Images",),
    },
    {
        "operator": "EdgeDetection",
        "datasetId": "bsds500",
        "sourceBaseline": "quality/evals/reports/EdgeDetection_bsds500_baseline.json",
        "manifest": "quality/datasets/bsds500_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "public boundary benchmark proof",
        "primaryMetrics": ("BoundaryF1", "BoundaryRecall"),
        "boundaryMetric": "BoundaryF1",
        "thresholds": {"BoundaryF1": {"freezeRatio": 0.98}, "Failed": 0},
        "caseListKeys": ("Cases",),
    },
    {
        "operator": "CameraCalibration",
        "datasetId": "opencv_calibration_samples",
        "sourceBaseline": "quality/evals/reports/CameraCalibration_opencv_samples_baseline.json",
        "manifest": "quality/datasets/opencv_calibration_samples_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "public calibration sample proof",
        "primaryMetrics": ("ReprojectionRmsPx", "MaxReprojectionErrorPx"),
        "boundaryMetric": "ReprojectionRmsPx",
        "thresholds": {"ReprojectionRmsPx": "MaxReprojectionRmsPx", "Failed": 0},
        "caseListKeys": ("Cases",),
        "lowerBetter": ("ReprojectionRmsPx", "MaxReprojectionErrorPx"),
    },
    {
        "operator": "DeepLearning",
        "datasetId": "coco2017",
        "sourceBaseline": "quality/evals/reports/DeepLearning_coco_real_model_baseline.json",
        "manifest": "quality/datasets/coco2017_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "Inference smoke only until an approved delivery model, dataset manifest and nonzero precision gate are present.",
        "primaryMetrics": ("AP50", "PrecisionAt50", "RecallAt50"),
        "boundaryMetric": "BestMatchedIou",
        "thresholds": {"AP50": 0.45, "PrecisionAt50": 0.45, "RecallAt50": 0.35},
        "deliveryManifest": "quality/evals/baselines/deep-learning-delivery-model-manifest.json",
        "providerReport": "quality/evals/reports/DeepLearning_provider_inference_baseline.json",
        "caseListKeys": ("Cases",),
    },
    {
        "operator": "SemanticSegmentation",
        "datasetId": "voc-style-protocol-bridge",
        "sourceBaseline": "quality/evals/reports/SemanticSegmentation_dataset_baseline.json",
        "manifest": "quality/evals/reports/SemanticSegmentation_dataset_baseline.json",
        "proofLevel": "golden",
        "evidenceClaim": "VOC-style semisynthetic segmentation protocol proof",
        "primaryMetrics": ("MeanIoU", "PixelAccuracy"),
        "boundaryMetric": "MeanIoU",
        "thresholds": {"MeanIoU": {"freezeRatio": 0.98}, "PixelAccuracy": {"freezeRatio": 0.98}, "Failed": 0},
        "caseListKeys": ("Cases",),
    },
    {
        "operator": "ShapeMatching",
        "datasetId": "semisynthetic-geometric-shape-scenes",
        "sourceBaseline": "quality/evals/reports/ShapeMatching_dataset_baseline.json",
        "manifest": "quality/evals/reports/ShapeMatching_dataset_baseline.json",
        "proofLevel": "golden",
        "evidenceClaim": "semisynthetic geometric matching proof",
        "primaryMetrics": ("F1", "MeanPositionErrorPx"),
        "boundaryMetric": "MeanPositionErrorPx",
        "thresholds": {"F1": {"freezeRatio": 0.98}, "MeanPositionErrorPx": {"freezeRatio": 1.02, "lowerBetter": True}, "Failed": 0},
        "caseListKeys": ("Cases",),
        "lowerBetter": ("MeanPositionErrorPx", "MeanAngleErrorDeg", "MeanScaleError"),
    },
    {
        "operator": "TemplateMatching",
        "datasetId": "hpatches-style-homography-bridge",
        "sourceBaseline": "quality/evals/reports/TemplateMatching_public_bridge_baseline.json",
        "manifest": "quality/evals/reports/TemplateMatching_public_bridge_baseline.json",
        "proofLevel": "golden",
        "evidenceClaim": "HPatches-style synthetic homography bridge; public HPatches image proof pending",
        "primaryMetrics": ("P95PositionErrorPx", "MeanPositionErrorPx"),
        "boundaryMetric": "PositionErrorPx",
        "thresholds": {"P95PositionErrorPx": "PositionTolerancePx", "Failed": 0},
        "caseListKeys": ("Cases",),
        "lowerBetter": ("P95PositionErrorPx", "MeanPositionErrorPx", "PositionErrorPx"),
    },
    {
        "operator": "AkazeFeatureMatch",
        "datasetId": "hpatches",
        "sourceBaseline": "quality/evals/reports/AkazeFeatureMatch_hpatches_baseline.json",
        "manifest": "quality/datasets/hpatches_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "public HPatches real-image homography feature matching proof",
        "primaryMetrics": ("PassRate", "P95PositionErrorPx", "MeanInliers"),
        "boundaryMetric": "PositionErrorPx",
        "thresholds": {"PassRate": "MinPassRate", "P95PositionErrorPx": "MaxP95PositionErrorPx"},
        "caseListKeys": ("Cases",),
        "lowerBetter": ("P95PositionErrorPx", "MeanPositionErrorPx", "PositionErrorPx"),
    },
    {
        "operator": "OrbFeatureMatch",
        "datasetId": "hpatches",
        "sourceBaseline": "quality/evals/reports/OrbFeatureMatch_hpatches_baseline.json",
        "manifest": "quality/datasets/hpatches_index.json",
        "proofLevel": "public-benchmark",
        "evidenceClaim": "public HPatches real-image homography feature matching proof",
        "primaryMetrics": ("PassRate", "P95PositionErrorPx", "MeanInliers"),
        "boundaryMetric": "PositionErrorPx",
        "thresholds": {"PassRate": "MinPassRate", "P95PositionErrorPx": "MaxP95PositionErrorPx"},
        "caseListKeys": ("Cases",),
        "lowerBetter": ("P95PositionErrorPx", "MeanPositionErrorPx", "PositionErrorPx"),
    },
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def repo_path(value: str) -> Path:
    path = Path(value)
    if path.is_absolute():
        return path
    return REPO_ROOT / path


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def numeric(value: Any) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


def strict_int(value: Any) -> int | None:
    if isinstance(value, int) and not isinstance(value, bool):
        return value
    return None


def normalize_sha256(value: Any) -> str:
    text = str(value or "").strip()
    if text.startswith("sha256:"):
        text = text[len("sha256:") :]
    return text


def is_sha256_hex(value: str) -> bool:
    return bool(re.fullmatch(r"[0-9a-fA-F]{64}", value))


def summary(document: dict[str, Any]) -> dict[str, Any]:
    value = document.get("Summary", document.get("summary", {}))
    return value if isinstance(value, dict) else {}


def case_id(case: dict[str, Any], index: int) -> str:
    for key in ("CaseId", "caseId", "Id", "id"):
        value = case.get(key)
        if value:
            return str(value)
    image_path = case.get("ImagePath") or case.get("image_path")
    if image_path:
        parts = [
            str(case.get("Category") or case.get("category") or "").strip(),
            str(case.get("DefectType") or case.get("defectType") or "").strip(),
            Path(str(image_path)).stem,
        ]
        return "/".join(part for part in parts if part)
    return f"case_{index:04d}"


def case_passed(case: dict[str, Any]) -> bool:
    for key in ("Passed", "passed", "Accepted", "accepted"):
        if isinstance(case.get(key), bool):
            return bool(case[key])
    if case.get("Error") or case.get("ErrorMessage") or case.get("Failure") or case.get("FailureReasonCode"):
        return False
    return True


def collect_metrics(value: dict[str, Any]) -> dict[str, float]:
    metrics: dict[str, float] = {}
    for key, raw in value.items():
        if key in {"Detections", "ScoredPredictions", "MatchedIous", "Diagnostics"}:
            continue
        number = numeric(raw)
        if number is not None:
            metrics[key] = round(number, 6)
        elif isinstance(raw, dict):
            for child_key, child_raw in raw.items():
                child_number = numeric(child_raw)
                if child_number is not None:
                    metrics[f"{key}.{child_key}"] = round(child_number, 6)
    return metrics


def find_cases(document: dict[str, Any], keys: tuple[str, ...]) -> list[dict[str, Any]]:
    for key in keys:
        value = document.get(key)
        if isinstance(value, list):
            return [item for item in value if isinstance(item, dict)]
    return []


def threshold_value(metric: str, rule: Any, metrics: dict[str, Any]) -> Any:
    if isinstance(rule, str):
        return metrics.get(rule, metrics.get(f"Thresholds.{rule}"))
    if isinstance(rule, (int, float)) and not isinstance(rule, bool):
        return rule
    if isinstance(rule, dict):
        value = metrics.get(metric)
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            ratio = float(rule.get("freezeRatio", 1.0))
            return round(float(value) * ratio, 6)
    return None


def is_lower_better(metric: str, config: dict[str, Any], rule: Any) -> bool:
    if isinstance(rule, dict) and rule.get("lowerBetter") is True:
        return True
    if metric in set(config.get("lowerBetter", ())):
        return True
    lower = metric.lower()
    return any(token in lower for token in ("error", "failed", "falsepositive", "falsenegative", "runtime", "memory"))


def threshold_gate(metric: str, value: Any, threshold: Any, lower_better: bool) -> dict[str, Any]:
    if not isinstance(threshold, (int, float)) or isinstance(threshold, bool):
        return {"metric": metric, "threshold": threshold, "value": value, "passed": False, "missingThreshold": True}
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return {"metric": metric, "threshold": threshold, "value": value, "passed": False, "missingValue": True}
    if lower_better:
        passed = float(value) <= float(threshold)
        comparator = "<="
    else:
        passed = float(value) >= float(threshold)
        comparator = ">="
    return {
        "metric": metric,
        "value": round(float(value), 6),
        "threshold": round(float(threshold), 6),
        "comparator": comparator,
        "passed": passed,
    }


def failure_labels(case: dict[str, Any], passed: bool) -> list[str]:
    labels: list[str] = []
    for key in ("Failure", "FailureReasonCode", "Error", "ErrorMessage"):
        value = case.get(key)
        if value:
            labels.append(str(value))
    if not labels and not passed:
        labels.append("threshold-failed")
    return labels


def replay_rank_value(case: dict[str, Any], metric: str, lower_better: bool) -> float | None:
    value = case.get("metrics", {}).get(metric)
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return None
    return float(value) if lower_better else -float(value)


def select_replay_cases(config: dict[str, Any], per_case: list[dict[str, Any]], limit: int = 20) -> list[dict[str, Any]]:
    metric = str(config.get("boundaryMetric") or "")
    lower_better = metric in set(config.get("lowerBetter", ())) or any(
        token in metric.lower() for token in ("error", "runtime", "positionerror")
    )
    failed = [case for case in per_case if not case.get("passed")]
    candidates = [case for case in per_case if case.get("passed")]
    ranked = sorted(
        (
            (replay_rank_value(case, metric, lower_better), case)
            for case in candidates
            if replay_rank_value(case, metric, lower_better) is not None
        ),
        key=lambda item: item[0],
        reverse=True,
    )
    selected = failed + [case for _, case in ranked[: max(0, limit - len(failed))]]
    if not selected:
        selected = per_case[:limit]

    replay_cases: list[dict[str, Any]] = []
    for case in selected[:limit]:
        replay_class = "failure" if not case.get("passed") else "boundary"
        operator = str(config["operator"])
        replay_command = (
            ["python", "quality/tools/run_algorithm_ab_replay.py", "--execute-camera-calibration"]
            if operator == "CameraCalibration"
            else ["python", "quality/tools/run_algorithm_ab_replay.py", "--execute-matching"]
        )
        replay_cases.append(
            {
                "caseId": case["caseId"],
                "split": case["split"],
                "operator": operator,
                "datasetId": config["datasetId"],
                "replayClass": replay_class,
                "triageLabel": "threshold-failed" if replay_class == "failure" else f"worst-{metric or 'case'}",
                "boundaryMetric": metric,
                "boundaryMetricValue": case.get("metrics", {}).get(metric),
                "replayCommand": replay_command,
            }
        )
    return replay_cases


def split_summary(source_summary: dict[str, Any], cases: list[dict[str, Any]]) -> dict[str, Any]:
    splits = Counter(str(case.get("Split") or case.get("split") or source_summary.get("Split") or "test") for case in cases)
    result = dict(sorted(splits.items()))
    if "TrainCount" in source_summary:
        result.setdefault("train", int(source_summary.get("TrainCount") or 0))
    if "TestCount" in source_summary:
        result.setdefault("test", int(source_summary.get("TestCount") or 0))
    return result


def privacy_leak_count(value: Any) -> int:
    raw = json.dumps(value, ensure_ascii=False)
    return len(RAW_PATH_RE.findall(raw))


def build_operator_result(config: dict[str, Any]) -> dict[str, Any]:
    source_path = repo_path(config["sourceBaseline"])
    manifest_path = repo_path(config["manifest"])
    if not source_path.exists():
        raise FileNotFoundError(f"source baseline not found: {repo(source_path)}")
    if not manifest_path.exists():
        raise FileNotFoundError(f"manifest/index not found: {repo(manifest_path)}")

    document = read_json(source_path)
    source_summary = summary(document)
    source_metrics = collect_metrics(source_summary)
    source_metrics.setdefault("Passed", float(source_summary.get("Passed") or 0))
    source_metrics.setdefault("Failed", float(source_summary.get("Failed") or 0))

    cases = find_cases(document, tuple(config["caseListKeys"]))
    if "CaseCount" not in source_metrics:
        source_metrics["CaseCount"] = float(source_summary.get("CaseCount") or len(cases))
    per_case: list[dict[str, Any]] = []
    taxonomy = Counter()
    for index, case in enumerate(cases):
        passed = case_passed(case)
        labels = failure_labels(case, passed)
        if passed:
            taxonomy["passed"] += 1
        for label in labels:
            taxonomy[label] += 1
        per_case.append(
            {
                "caseId": case_id(case, index),
                "split": str(case.get("Split") or case.get("split") or source_summary.get("Split") or "test"),
                "passed": passed,
                "metrics": collect_metrics(case),
                "failureTaxonomy": labels,
            }
        )

    threshold_results = []
    for metric, rule in config["thresholds"].items():
        threshold = threshold_value(metric, rule, source_metrics)
        threshold_results.append(
            threshold_gate(metric, source_metrics.get(metric), threshold, is_lower_better(metric, config, rule))
        )

    leak_count = privacy_leak_count({"source": document, "perCaseResults": per_case})
    accepted = bool(per_case) and leak_count == 0 and all(item["passed"] for item in threshold_results)
    missing_case_results = int(source_metrics.get("CaseCount") or 0) != len(per_case)
    if missing_case_results:
        accepted = False

    replay_cases = select_replay_cases(config, per_case)

    result = {
        "operator": config["operator"],
        "datasetId": config["datasetId"],
        "proofLevel": config["proofLevel"],
        "evidenceClaim": config["evidenceClaim"],
        "industrialStatus": "real field sign-off pending; industrial validation not complete",
        "sourceBaseline": repo(source_path),
        "sourceBaselineSha256": sha256_file(source_path),
        "manifestPath": repo(manifest_path),
        "manifestSha256": sha256_file(manifest_path),
        "splitSummary": split_summary(source_summary, cases),
        "primaryMetrics": list(config["primaryMetrics"]),
        "metrics": source_metrics,
        "thresholds": {item["metric"]: item.get("threshold") for item in threshold_results},
        "thresholdResults": threshold_results,
        "perCaseResults": per_case,
        "failureTaxonomy": dict(sorted(taxonomy.items())),
        "replayCases": replay_cases,
        "privacyLeakCount": leak_count,
        "missingCaseResults": missing_case_results,
        "accepted": accepted,
    }
    if config["operator"] == "DeepLearning":
        delivery_manifest_path = repo_path(config["deliveryManifest"])
        provider_report_path = repo_path(config["providerReport"])
        delivery_evaluation = evaluate_delivery_evidence(
            document,
            read_json(delivery_manifest_path) if delivery_manifest_path.exists() else None,
            read_json(provider_report_path) if provider_report_path.exists() else None,
        )
        result["proofLevel"] = (
            "delivery-precision" if delivery_evaluation["releaseReady"] else "inference-smoke-only"
        )
        result["precisionDisposition"] = delivery_evaluation["precisionDisposition"]
        result["precisionBlockingReasons"] = delivery_evaluation["blockingReasons"]
        result["providerProfiles"] = delivery_evaluation["providerProfiles"]
        result["accepted"] = accepted and delivery_evaluation["releaseReady"]
    return result


def build_document() -> dict[str, Any]:
    results = [build_operator_result(config) for config in PROOF_SOURCES]
    accepted = all(result["accepted"] for result in results)
    return {
        "schemaVersion": PROOF_SCHEMA_VERSION,
        "generatedAtUtc": GENERATED_AT,
        "requiredRunnerFields": sorted(REQUIRED_RESULT_FIELDS),
        "accepted": accepted,
        "summary": {
            "operatorCount": len(results),
            "acceptedCount": sum(1 for result in results if result["accepted"]),
            "failedCount": sum(1 for result in results if not result["accepted"]),
            "publicBenchmarkCount": sum(1 for result in results if result["proofLevel"] == "public-benchmark"),
            "goldenBridgeCount": sum(1 for result in results if result["proofLevel"] == "golden"),
            "realIndustrialValidationComplete": 0,
            "replayCaseCount": sum(len(result["replayCases"]) for result in results),
        },
        "claimBoundary": {
            "highestCurrentClaim": "public/semisynthetic quasi-industrial proof",
            "realFieldSignoff": "pending",
            "rule": "Do not claim real industrial validation complete from public datasets or semisynthetic protocol bridges.",
        },
        "operators": results,
    }


def build_replay_manifest(document: dict[str, Any]) -> dict[str, Any]:
    replay_cases = [
        replay_case
        for row in document.get("operators", [])
        for replay_case in row.get("replayCases", [])
    ]
    class_counts = Counter(str(item.get("replayClass")) for item in replay_cases)
    return {
        "schemaVersion": REPLAY_SCHEMA_VERSION,
        "generatedAtUtc": document["generatedAtUtc"],
        "sourceProofBaseline": repo(OUTPUT_JSON),
        "sourceProofSha256": sha256_file(OUTPUT_JSON) if OUTPUT_JSON.exists() else "",
        "accepted": bool(replay_cases) and all(item.get("replayCommand") for item in replay_cases),
        "summary": {
            "replayCaseCount": len(replay_cases),
            "operatorCount": len({item.get("operator") for item in replay_cases}),
            "classCounts": dict(sorted(class_counts.items())),
        },
        "cases": replay_cases,
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("schemaVersion") != PROOF_SCHEMA_VERSION:
        errors.append(f"public benchmark proof schemaVersion must be {PROOF_SCHEMA_VERSION}")
    expected_document_accepted = all(
        isinstance(row, dict) and row.get("accepted") is True
        for row in document.get("operators", [])
    )
    if document.get("accepted") is not expected_document_accepted:
        errors.append("public benchmark proof accepted must equal all operator dispositions")
    operators = document.get("operators")
    if not isinstance(operators, list) or not operators:
        return ["public benchmark proof must contain operator results"]
    for row in operators:
        operator = row.get("operator", "<unknown>")
        missing = [field for field in REQUIRED_RESULT_FIELDS if field not in row]
        if missing:
            errors.append(f"{operator} missing required fields: {', '.join(sorted(missing))}")
        if row.get("proofLevel") == "real-field":
            errors.append(f"{operator} overclaims real-field proof")
        if str(row.get("industrialStatus", "")).strip().lower() == "real industrial validation complete":
            errors.append(f"{operator} overclaims real industrial validation")
        if row.get("privacyLeakCount") != 0:
            errors.append(f"{operator} privacyLeakCount must be 0")
        if row.get("missingCaseResults"):
            errors.append(f"{operator} source CaseCount does not match perCaseResults")
        if not row.get("perCaseResults"):
            errors.append(f"{operator} missing perCaseResults")
        if operator == "DeepLearning":
            if row.get("sourceBaseline") != "quality/evals/reports/DeepLearning_coco_real_model_baseline.json":
                errors.append("DeepLearning row sourceBaseline must be DeepLearning_coco_real_model_baseline.json")
            if "annotation-seeded" in str(row.get("evidenceClaim", "")).lower():
                errors.append("DeepLearning row evidenceClaim must not describe annotation-seeded results")
            if row.get("proofLevel") == "inference-smoke-only":
                if row.get("precisionDisposition") != "FAIL":
                    errors.append("DeepLearning inference smoke must have precisionDisposition=FAIL")
                if row.get("accepted") is True:
                    errors.append("DeepLearning inference smoke must not be accepted as precision evidence")
        failed_thresholds = [item.get("metric") for item in row.get("thresholdResults", []) if not item.get("passed")]
        if failed_thresholds and row.get("accepted") is True:
            errors.append(f"{operator} failed thresholds: {', '.join(map(str, failed_thresholds))}")
        case_ids = [case.get("caseId") for case in row.get("perCaseResults", [])]
        if len(case_ids) != len(set(case_ids)):
            errors.append(f"{operator} perCaseResults contain duplicate caseId")
        replay_cases = row.get("replayCases", [])
        if not isinstance(replay_cases, list) or not replay_cases:
            errors.append(f"{operator} missing replayCases")
        elif any(not item.get("triageLabel") or not item.get("replayCommand") for item in replay_cases):
            errors.append(f"{operator} replayCases missing triageLabel or replayCommand")
    summary_row = document.get("summary")
    if not isinstance(summary_row, dict):
        errors.append("public benchmark proof missing summary")
        summary_row = {}
    operator_count = strict_int(summary_row.get("operatorCount"))
    accepted_count = strict_int(summary_row.get("acceptedCount"))
    failed_count = strict_int(summary_row.get("failedCount"))
    replay_case_count = strict_int(summary_row.get("replayCaseCount"))
    if operator_count != len(operators):
        errors.append("summary.operatorCount must equal operator row count")
    if accepted_count != sum(1 for row in operators if row.get("accepted") is True):
        errors.append("summary.acceptedCount must equal accepted operator rows")
    if failed_count != sum(1 for row in operators if row.get("accepted") is not True):
        errors.append("summary.failedCount must equal failed operator rows")
    if replay_case_count != sum(len(row.get("replayCases", [])) for row in operators if isinstance(row, dict)):
        errors.append("summary.replayCaseCount must equal operator replayCases")
    if summary_row.get("realIndustrialValidationComplete") != 0:
        errors.append("summary must keep realIndustrialValidationComplete at 0")
    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("proof document contains raw path pattern")
    return errors


def validate_retained_summary(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("schemaVersion") != "quality-report-summary/v1":
        errors.append("retained summary schemaVersion must be quality-report-summary/v1")
    if document.get("evidenceKind") != "public-benchmark-proof":
        errors.append("retained summary evidenceKind must be public-benchmark-proof")

    source_report = document.get("sourceReport")
    if not isinstance(source_report, dict):
        errors.append("retained summary missing sourceReport")
        source_report = {}
    if source_report.get("originalPath") != repo(OUTPUT_JSON):
        errors.append("retained summary originalPath must match public benchmark proof baseline")
    if "removed-from-git" not in str(source_report.get("retentionDecision") or ""):
        errors.append("retained summary must document raw-json removal from git")
    original_sha = normalize_sha256(source_report.get("originalSha256"))
    if not is_sha256_hex(original_sha):
        errors.append("retained summary originalSha256 must be a sha256 digest")
    original_size = strict_int(source_report.get("originalSizeBytes"))
    if original_size is None or original_size <= 0:
        errors.append("retained summary originalSizeBytes must be a positive integer")

    summary = document.get("summary")
    if not isinstance(summary, dict):
        errors.append("retained summary missing summary")
        summary = {}
    operator_count = strict_int(summary.get("operatorCount"))
    accepted_count = strict_int(summary.get("acceptedCount"))
    failed_count = strict_int(summary.get("failedCount"))
    replay_case_count = strict_int(summary.get("replayCaseCount"))
    if operator_count is None or operator_count < 8:
        errors.append("retained summary operatorCount must be at least 8")
    if accepted_count is None or failed_count is None or accepted_count + failed_count != operator_count:
        errors.append("retained summary acceptedCount + failedCount must equal operatorCount")
    if replay_case_count is None or replay_case_count <= 0:
        errors.append("retained summary replayCaseCount must be positive")
    if summary.get("realIndustrialValidationComplete") != 0:
        errors.append("retained summary must keep realIndustrialValidationComplete at 0")

    required_fields = set(document.get("requiredRunnerFields") or [])
    if not REQUIRED_RESULT_FIELDS.issubset(required_fields):
        errors.append("retained summary requiredRunnerFields missing canonical runner fields")

    operators = document.get("operators")
    if not isinstance(operators, list) or not operators:
        errors.append("retained summary must contain operator rows")
        operators = []
    elif operator_count is not None and len(operators) != operator_count:
        errors.append("retained summary operator row count must equal summary.operatorCount")

    operator_replay_case_count = 0

    for row in operators:
        if not isinstance(row, dict):
            errors.append("retained summary contains non-object operator row")
            continue
        operator = row.get("operator", "<unknown>")
        for field in (
            "operator",
            "datasetId",
            "proofLevel",
            "sourceBaselineSha256",
            "manifestSha256",
            "metrics",
            "thresholdResultCount",
            "perCaseResultCount",
            "replayCaseCount",
            "accepted",
        ):
            if field not in row:
                errors.append(f"{operator} retained summary missing {field}")
        if row.get("proofLevel") == "real-field":
            errors.append(f"{operator} overclaims real-field proof")
        if str(row.get("industrialStatus", "")).strip().lower() == "real industrial validation complete":
            errors.append(f"{operator} overclaims real industrial validation")
        if row.get("privacyLeakCount") != 0:
            errors.append(f"{operator} privacyLeakCount must be 0")
        if row.get("missingCaseResults") is True:
            errors.append(f"{operator} source CaseCount does not match perCaseResults")
        if row.get("accepted") is not True and operator != "DeepLearning":
            errors.append(f"{operator} non-DeepLearning retained evidence must remain accepted")
        if operator == "DeepLearning" and row.get("proofLevel") == "inference-smoke-only":
            if row.get("precisionDisposition") != "FAIL":
                errors.append("DeepLearning retained inference smoke must have precisionDisposition=FAIL")
            if row.get("accepted") is True:
                errors.append("DeepLearning retained inference smoke must not be accepted as precision evidence")
        per_case_count = strict_int(row.get("perCaseResultCount"))
        threshold_count = strict_int(row.get("thresholdResultCount"))
        operator_replay_count = strict_int(row.get("replayCaseCount"))
        if per_case_count is None or per_case_count <= 0:
            errors.append(f"{operator} retained summary perCaseResultCount must be positive")
        if threshold_count is None or threshold_count <= 0:
            errors.append(f"{operator} retained summary thresholdResultCount must be positive")
        if operator_replay_count is None or operator_replay_count <= 0:
            errors.append(f"{operator} retained summary replayCaseCount must be positive")
        else:
            operator_replay_case_count += operator_replay_count
        if operator == "DeepLearning" and row.get("sourceBaseline") != "quality/evals/reports/DeepLearning_coco_real_model_baseline.json":
            errors.append("DeepLearning row sourceBaseline must be DeepLearning_coco_real_model_baseline.json")

    if replay_case_count is not None and operator_replay_case_count != replay_case_count:
        errors.append("retained summary replayCaseCount must equal operator replayCaseCount sum")
    expected_accepted = failed_count == 0
    if document.get("accepted") is not expected_accepted:
        errors.append("retained summary accepted must reflect failedCount")

    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("retained summary contains raw path pattern")
    return errors


def validate_replay_manifest(
    manifest: dict[str, Any],
    expected_source_sha: str | None = None,
    expected_replay_case_count: int | None = None,
    expected_operator_count: int | None = None,
) -> list[str]:
    errors: list[str] = []
    if manifest.get("schemaVersion") != REPLAY_SCHEMA_VERSION:
        errors.append(f"replay manifest schemaVersion must be {REPLAY_SCHEMA_VERSION}")
    if manifest.get("sourceProofBaseline") != repo(OUTPUT_JSON):
        errors.append("replay manifest sourceProofBaseline must match public benchmark proof baseline")
    source_sha = normalize_sha256(manifest.get("sourceProofSha256"))
    if not is_sha256_hex(source_sha):
        errors.append("replay manifest sourceProofSha256 must be a sha256 digest")
    expected_sha = normalize_sha256(expected_source_sha)
    if expected_sha and source_sha != expected_sha:
        errors.append("replay manifest sourceProofSha256 must match the validated proof baseline")

    cases = manifest.get("cases")
    if not isinstance(cases, list) or not cases:
        return ["replay manifest must contain cases"]
    summary_row = manifest.get("summary")
    if not isinstance(summary_row, dict):
        errors.append("replay manifest missing summary")
        summary_row = {}

    for index, case in enumerate(cases):
        for key in ("caseId", "operator", "datasetId", "replayClass", "triageLabel", "replayCommand"):
            if not case.get(key):
                errors.append(f"replay case {index} missing {key}")
        replay_class = case.get("replayClass")
        if replay_class not in {"boundary", "failure"}:
            errors.append(f"replay case {index} replayClass must be boundary or failure")
        replay_command = case.get("replayCommand")
        if not isinstance(replay_command, list) or any(not isinstance(item, str) for item in replay_command):
            errors.append(f"replay case {index} replayCommand must be a list of strings")
        elif tuple(replay_command) not in ALLOWED_REPLAY_COMMANDS:
            errors.append(f"replay case {index} replayCommand is not allowed")
        elif case.get("operator") == "CameraCalibration" and replay_command[-1] != "--execute-camera-calibration":
            errors.append("CameraCalibration replay cases must use --execute-camera-calibration")
        elif case.get("operator") != "CameraCalibration" and replay_command[-1] != "--execute-matching":
            errors.append(f"replay case {index} non-camera operator must use --execute-matching")

    replay_keys = [(case.get("operator"), case.get("datasetId"), case.get("caseId")) for case in cases]
    if len(replay_keys) != len(set(replay_keys)):
        errors.append("replay manifest contains duplicate operator/dataset/caseId entries")
    if manifest.get("accepted") is not True:
        errors.append("replay manifest accepted must be true")
    class_counts = Counter(str(case.get("replayClass")) for case in cases)
    operator_count = len({case.get("operator") for case in cases})
    replay_case_count = strict_int(summary_row.get("replayCaseCount"))
    summary_operator_count = strict_int(summary_row.get("operatorCount"))
    summary_class_counts = summary_row.get("classCounts")
    if replay_case_count != len(cases):
        errors.append("replay manifest summary.replayCaseCount must equal case count")
    if expected_replay_case_count is not None and replay_case_count != expected_replay_case_count:
        errors.append("replay manifest summary.replayCaseCount must match proof summary")
    if summary_operator_count != operator_count:
        errors.append("replay manifest summary.operatorCount must equal case operator count")
    if expected_operator_count is not None and summary_operator_count != expected_operator_count:
        errors.append("replay manifest summary.operatorCount must match proof summary")
    if summary_class_counts != dict(sorted(class_counts.items())):
        errors.append("replay manifest summary.classCounts must equal case replayClass counts")
    if RAW_PATH_RE.search(json.dumps(manifest, ensure_ascii=False)):
        errors.append("replay manifest contains raw path pattern")
    return errors


def expected_replay_source_sha(document: dict[str, Any], retained_summary: bool) -> str | None:
    if retained_summary:
        source_report = document.get("sourceReport")
        if isinstance(source_report, dict):
            return normalize_sha256(source_report.get("originalSha256"))
        return None
    if OUTPUT_JSON.exists():
        return sha256_file(OUTPUT_JSON)
    return None


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Public Benchmark Proof Baseline",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"Accepted: `{'Yes' if document['accepted'] else 'No'}`",
        "",
        "## Summary",
        "",
        f"- Operators: {document['summary']['operatorCount']}",
        f"- Accepted: {document['summary']['acceptedCount']}",
        f"- Public benchmark proof rows: {document['summary']['publicBenchmarkCount']}",
        f"- Golden/protocol bridge rows: {document['summary']['goldenBridgeCount']}",
        "- Real industrial validation complete: 0",
        "",
        "## Operators",
        "",
        "| Operator | Dataset | Proof | Accepted | Cases | Primary Metrics | Thresholds |",
        "|---|---|---|---|---:|---|---|",
    ]
    for row in document["operators"]:
        metrics = ", ".join(f"{name}={row['metrics'].get(name)}" for name in row["primaryMetrics"])
        thresholds = ", ".join(
            f"{item['metric']} {item.get('comparator', '?')} {item.get('threshold')}"
            for item in row["thresholdResults"]
        )
        lines.append(
            f"| {row['operator']} | {row['datasetId']} | {row['proofLevel']} | "
            f"{'Yes' if row['accepted'] else 'No'} | {len(row['perCaseResults'])} | {metrics} | {thresholds} |"
        )
    lines.extend(
        [
            "",
            "## Replay Seeds",
            "",
            f"- Replay cases: {document['summary']['replayCaseCount']}",
            "- Replay command: `python quality/tools/run_public_benchmark_proof.py --validate-only`",
            "",
            "## Claim Boundary",
            "",
            document["claimBoundary"]["rule"],
            "",
        ]
    )
    return "\n".join(lines)


def render_replay_markdown(manifest: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Public Benchmark Replay Manifest",
        "",
        f"GeneratedAtUtc: `{manifest['generatedAtUtc']}`",
        f"Accepted: `{'Yes' if manifest['accepted'] else 'No'}`",
        "",
        "## Summary",
        "",
        f"- Replay cases: {manifest['summary']['replayCaseCount']}",
        f"- Operators: {manifest['summary']['operatorCount']}",
        f"- Class counts: `{manifest['summary']['classCounts']}`",
        "",
        "## Cases",
        "",
        "| Operator | Dataset | Case | Class | Triage | Metric | Value |",
        "|---|---|---|---|---|---|---:|",
    ]
    for case in manifest["cases"]:
        lines.append(
            f"| {case['operator']} | {case['datasetId']} | {case['caseId']} | {case['replayClass']} | "
            f"{case['triageLabel']} | {case.get('boundaryMetric')} | {case.get('boundaryMetricValue')} |"
        )
    lines.append("")
    return "\n".join(lines)


def generate() -> dict[str, Any]:
    document = build_document()
    errors = validate_document(document)
    if errors:
        raise SystemExit("\n".join(f"error: {error}" for error in errors))
    write_json(OUTPUT_JSON, document)
    write_text(OUTPUT_MD, render_markdown(document))
    replay_manifest = build_replay_manifest(document)
    replay_errors = validate_replay_manifest(
        replay_manifest,
        expected_source_sha=sha256_file(OUTPUT_JSON),
        expected_replay_case_count=document["summary"]["replayCaseCount"],
        expected_operator_count=document["summary"]["operatorCount"],
    )
    if replay_errors:
        raise SystemExit("\n".join(f"error: {error}" for error in replay_errors))
    write_json(REPLAY_JSON, replay_manifest)
    write_text(REPLAY_MD, render_replay_markdown(replay_manifest))
    return document


def main() -> int:
    parser = argparse.ArgumentParser(description="Build or validate public benchmark proof baseline.")
    parser.add_argument("--validate-only", action="store_true", help="Validate existing generated public benchmark proof baseline.")
    args = parser.parse_args()

    retained_summary = args.validate_only and not OUTPUT_JSON.exists() and OUTPUT_SUMMARY_JSON.exists()
    if retained_summary:
        document = read_json(OUTPUT_SUMMARY_JSON)
        errors = validate_retained_summary(document)
    else:
        document = read_json(OUTPUT_JSON) if args.validate_only else generate()
        errors = validate_document(document)
    if args.validate_only:
        if not REPLAY_JSON.exists():
            errors.append(f"missing replay manifest: {repo(REPLAY_JSON)}")
        else:
            proof_summary = document.get("summary") if isinstance(document.get("summary"), dict) else {}
            errors.extend(
                validate_replay_manifest(
                    read_json(REPLAY_JSON),
                    expected_source_sha=expected_replay_source_sha(document, retained_summary),
                    expected_replay_case_count=strict_int(proof_summary.get("replayCaseCount")),
                    expected_operator_count=strict_int(proof_summary.get("operatorCount")),
                )
            )
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    print(
        "public benchmark proof valid: "
        f"operators={document['summary']['operatorCount']} "
        f"accepted={document['summary']['acceptedCount']} "
        f"source={'summary' if retained_summary else 'raw'} "
        f"generatedAt={utc_now()}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
