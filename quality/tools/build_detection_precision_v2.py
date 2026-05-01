from __future__ import annotations

import argparse
import json
import math
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_detection_precision_v2.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_detection_precision_v2.md"

AB_REPORT = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"
SURFACE_BASELINE = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_baseline.json"
SURFACE_CANDIDATE = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_candidate_v1.json"
SURFACE_SWEEP = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_sweep_v1.json"
SURFACE_TAXONOMY = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v1.json"
ANOMALY_BASELINE = REPORT_DIR / "AnomalyDetection_mvtec_baseline.json"
ANOMALY_CANDIDATE = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v2.json"
ANOMALY_SWEEP = REPORT_DIR / "AnomalyDetection_mvtec_sweep_v1.json"
ANOMALY_TAXONOMY = REPORT_DIR / "AnomalyDetection_mvtec_failure_taxonomy_v1.json"
ANOMALY_THRESHOLD_CALIBRATION = REPORT_DIR / "QualityFlywheel_anomaly_threshold_calibration_v1.json"
EDGE_BASELINE = REPORT_DIR / "EdgeDetection_bsds500_baseline.json"
EDGE_CANDIDATE_REPLAY = REPORT_DIR / "EdgeDetection_bsds500_candidate_replay_v1.json"
EDGE_RECALL_SWEEP = REPORT_DIR / "QualityFlywheel_edge_detection_recall_guard_sweep_v1.json"

RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
DETECTION_OPERATORS = ("SurfaceDefectDetection", "AnomalyDetection", "EdgeDetection")


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


def safe_divide(numerator: float, denominator: float) -> float:
    return 0.0 if denominator <= 0 else numerator / denominator


def harmonic(precision: float, recall: float) -> float:
    return 0.0 if precision + recall <= 0 else (2.0 * precision * recall) / (precision + recall)


def delta(new: float | None, old: float | None) -> float | None:
    if new is None or old is None:
        return None
    return round(float(new) - float(old), 6)


def image_confusion(document: dict[str, Any]) -> dict[str, int]:
    if isinstance(document.get("ImageConfusion"), dict):
        raw = document["ImageConfusion"]
        return {
            "tp": int(raw.get("TruePositive") or 0),
            "fp": int(raw.get("FalsePositive") or 0),
            "fn": int(raw.get("FalseNegative") or 0),
            "tn": int(raw.get("TrueNegative") or 0),
        }
    images = document.get("Images") or []
    return {
        "tp": sum(1 for image in images if image.get("IsDefect") is True and image.get("PredictedDefect") is True),
        "fp": sum(1 for image in images if image.get("IsDefect") is not True and image.get("PredictedDefect") is True),
        "fn": sum(1 for image in images if image.get("IsDefect") is True and image.get("PredictedDefect") is not True),
        "tn": sum(1 for image in images if image.get("IsDefect") is not True and image.get("PredictedDefect") is not True),
    }


def image_f1_from_confusion(confusion: dict[str, int]) -> float:
    return round(safe_divide(2 * confusion["tp"], (2 * confusion["tp"]) + confusion["fp"] + confusion["fn"]), 6)


def image_recall_from_confusion(confusion: dict[str, int]) -> float:
    return round(safe_divide(confusion["tp"], confusion["tp"] + confusion["fn"]), 6)


def false_positive_rate_from_confusion(confusion: dict[str, int]) -> float:
    return round(safe_divide(confusion["fp"], confusion["fp"] + confusion["tn"]), 6)


def anomaly_confusion(document: dict[str, Any]) -> dict[str, int]:
    images = document.get("Images") or []
    return {
        "tp": sum(1 for image in images if image.get("IsAnomaly") is True and image.get("PredictedAnomaly") is True),
        "fp": sum(1 for image in images if image.get("IsAnomaly") is not True and image.get("PredictedAnomaly") is True),
        "fn": sum(1 for image in images if image.get("IsAnomaly") is True and image.get("PredictedAnomaly") is not True),
        "tn": sum(1 for image in images if image.get("IsAnomaly") is not True and image.get("PredictedAnomaly") is not True),
    }


def recall_at_fpr(document: dict[str, Any], fpr_limit: float) -> float:
    images = document.get("Images") or []
    positives = [image for image in images if image.get("IsDefect") is True]
    negatives = [image for image in images if image.get("IsDefect") is not True]
    if not positives or not negatives:
        return 0.0
    thresholds = sorted({float(image.get("Score") or 0.0) for image in images}, reverse=True)
    best = 0.0
    for threshold in thresholds:
        true_positive = sum(1 for image in positives if float(image.get("Score") or 0.0) >= threshold)
        false_positive = sum(1 for image in negatives if float(image.get("Score") or 0.0) >= threshold)
        fpr = false_positive / len(negatives)
        if fpr <= fpr_limit + 1e-12:
            best = max(best, true_positive / len(positives))
    return round(best, 6)


def surface_summary(document: dict[str, Any], source: Path, fixed_fpr: float | None = None) -> dict[str, Any]:
    summary = document["Summary"]
    confusion = image_confusion(document)
    own_fpr = false_positive_rate_from_confusion(confusion)
    return {
        "sourceReport": repo(source),
        "profile": summary.get("ProfileName") or "baseline_default",
        "caseCount": summary["CaseCount"],
        "pixelF1": summary["PixelF1"],
        "imageAuroc": summary["ImageAuroc"],
        "pixelAuroc": summary["PixelAuroc"],
        "imageF1": summary.get("ImageF1") or image_f1_from_confusion(confusion),
        "imageRecall": summary.get("ImageRecall") or image_recall_from_confusion(confusion),
        "falsePositivePerImage": summary["FalsePositivePerImage"],
        "imageFalsePositiveRate": own_fpr,
        "recallAtOwnFpr": recall_at_fpr(document, own_fpr),
        "recallAtFixedFpr": recall_at_fpr(document, fixed_fpr if fixed_fpr is not None else own_fpr),
        "runtimeMsP95": summary["RuntimeMsP95"],
        "confusion": confusion,
    }


def anomaly_summary(document: dict[str, Any], source: Path) -> dict[str, Any]:
    summary = document["Summary"]
    confusion = anomaly_confusion(document)
    return {
        "sourceReport": repo(source),
        "profile": summary.get("ProfileName") or "baseline_default",
        "caseCount": summary["TestCount"],
        "imageAuroc": summary["ImageAuroc"],
        "pixelAuroc": summary["PixelAuroc"],
        "imagePrecision": summary.get("ImagePrecision") or round(safe_divide(confusion["tp"], confusion["tp"] + confusion["fp"]), 6),
        "imageRecall": summary.get("ImageRecall") or image_recall_from_confusion(confusion),
        "imageF1": summary.get("ImageF1") or image_f1_from_confusion(confusion),
        "imageTruePositive": summary.get("ImageTruePositive") if summary.get("ImageTruePositive") is not None else confusion["tp"],
        "imageFalsePositive": summary.get("ImageFalsePositive") if summary.get("ImageFalsePositive") is not None else confusion["fp"],
        "imageFalseNegative": summary.get("ImageFalseNegative") if summary.get("ImageFalseNegative") is not None else confusion["fn"],
        "runtimeMs": summary["RuntimeMs"],
        "parameters": {
            "MaxSide": summary.get("MaxSide"),
            "PatchSize": summary.get("PatchSize"),
            "PatchStride": summary.get("PatchStride"),
            "CoresetRatio": summary.get("CoresetRatio"),
            "Threshold": summary.get("Threshold"),
        },
    }


def anomaly_categories(old: dict[str, Any], new: dict[str, Any]) -> list[dict[str, Any]]:
    old_by_name = {item["Category"]: item for item in old.get("Categories", [])}
    rows = []
    for item in new.get("Categories", []):
        previous = old_by_name.get(item["Category"], {})
        rows.append(
            {
                "category": item["Category"],
                "testCount": item.get("TestCount"),
                "testAnomalyCount": item.get("TestAnomalyCount"),
                "imageAurocOld": previous.get("ImageAuroc"),
                "imageAurocNew": item.get("ImageAuroc"),
                "imageAurocDelta": delta(item.get("ImageAuroc"), previous.get("ImageAuroc")),
                "pixelAurocOld": previous.get("PixelAuroc"),
                "pixelAurocNew": item.get("PixelAuroc"),
                "pixelAurocDelta": delta(item.get("PixelAuroc"), previous.get("PixelAuroc")),
            }
        )
    return sorted(rows, key=lambda row: row["category"])


def anomaly_failure_summary(document: dict[str, Any]) -> dict[str, Any]:
    images = document.get("Images") or []
    tag_counts: Counter[str] = Counter()
    missed_by_defect: Counter[str] = Counter()
    top_misses: list[dict[str, Any]] = []
    false_positive_case_ids: list[str] = []
    for image in images:
        tags = [str(tag) for tag in image.get("FailureTaxonomy") or []]
        tag_counts.update(tags)
        if image.get("IsAnomaly") is True and image.get("PredictedAnomaly") is not True:
            missed_by_defect[str(image.get("DefectType") or "unknown")] += 1
            top_misses.append(
                {
                    "caseId": image.get("CaseId"),
                    "defectType": image.get("DefectType"),
                    "score": image.get("Score"),
                    "taxonomy": tags,
                }
            )
        if image.get("IsAnomaly") is not True and image.get("PredictedAnomaly") is True:
            false_positive_case_ids.append(str(image.get("CaseId")))

    return {
        "summary": {
            "caseCount": len(images),
            "missedAnomalyCount": sum(missed_by_defect.values()),
            "detectedAnomalyCount": sum(1 for image in images if image.get("IsAnomaly") is True and image.get("PredictedAnomaly") is True),
            "falsePositiveGoodCount": len(false_positive_case_ids),
        },
        "tagCounts": dict(tag_counts.most_common()),
        "missedByDefectType": dict(missed_by_defect.most_common()),
        "falsePositiveCaseIds": false_positive_case_ids,
        "topMisses": top_misses[:25],
    }


def ab_operator(report: dict[str, Any], operator: str) -> dict[str, Any]:
    for row in report.get("operators", []):
        if row.get("operator") == operator:
            return row
    raise KeyError(f"missing A/B row for {operator}")


def aggregate_edge_metrics(cases: list[dict[str, Any]], side: str) -> dict[str, Any]:
    metrics = [case[side]["metrics"] for case in cases]
    precision_num = sum(float(item.get("BoundaryPrecisionNumerator") or 0) for item in metrics)
    precision_den = sum(float(item.get("BoundaryPrecisionDenominator") or 0) for item in metrics)
    recall_num = sum(float(item.get("BoundaryRecallNumerator") or 0) for item in metrics)
    recall_den = sum(float(item.get("BoundaryRecallDenominator") or 0) for item in metrics)
    consensus_precision_num = sum(float(item.get("ConsensusPrecisionNumerator") or 0) for item in metrics)
    consensus_precision_den = sum(float(item.get("ConsensusPrecisionDenominator") or 0) for item in metrics)
    consensus_recall_num = sum(float(item.get("ConsensusRecallNumerator") or 0) for item in metrics)
    consensus_recall_den = sum(float(item.get("ConsensusRecallDenominator") or 0) for item in metrics)
    precision = safe_divide(precision_num, precision_den)
    recall = safe_divide(recall_num, recall_den)
    consensus_precision = safe_divide(consensus_precision_num, consensus_precision_den)
    consensus_recall = safe_divide(consensus_recall_num, consensus_recall_den)
    return {
        "caseCount": len(cases),
        "boundaryPrecision": round(precision, 6),
        "boundaryRecall": round(recall, 6),
        "boundaryF1": round(harmonic(precision, recall), 6),
        "consensusBoundaryPrecision": round(consensus_precision, 6),
        "consensusBoundaryRecall": round(consensus_recall, 6),
        "consensusBoundaryF1": round(harmonic(consensus_precision, consensus_recall), 6),
        "predictedEdgePixels": int(sum(float(item.get("PredictedEdgePixels") or 0) for item in metrics)),
    }


def classify_edge_replay(cases: list[dict[str, Any]]) -> dict[str, Any]:
    counter: Counter[str] = Counter()
    focus_cases = []
    for case in cases:
        delta_metrics = case.get("delta", {})
        boundary_recall_delta = (delta_metrics.get("BoundaryRecall") or {}).get("delta")
        boundary_precision_delta = (delta_metrics.get("BoundaryPrecision") or {}).get("delta")
        boundary_f1_delta = (delta_metrics.get("BoundaryF1") or {}).get("delta")
        consensus_recall_delta = (delta_metrics.get("ConsensusBoundaryRecall") or {}).get("delta")
        predicted_delta = (delta_metrics.get("PredictedEdgePixels") or {}).get("delta")
        labels = []
        if boundary_recall_delta is not None and boundary_recall_delta < -0.01:
            labels.append("boundary_recall_drop")
        if consensus_recall_delta is not None and consensus_recall_delta < -0.01:
            labels.append("consensus_recall_drop")
        if boundary_precision_delta is not None and boundary_precision_delta > 0.01 and labels:
            labels.append("precision_gain_recall_tradeoff")
        if boundary_precision_delta is not None and boundary_precision_delta < -0.01:
            labels.append("precision_drop")
        if boundary_f1_delta is not None and boundary_f1_delta < -0.01:
            labels.append("boundary_f1_drop_gt_0_01")
        if predicted_delta is not None and predicted_delta < 0 and labels:
            labels.append("reduced_edge_density")
        counter.update(labels)
        if labels:
            focus_cases.append(
                {
                    "caseId": case["caseId"],
                    "status": case["status"],
                    "labels": labels,
                    "boundaryRecallDelta": boundary_recall_delta,
                    "boundaryPrecisionDelta": boundary_precision_delta,
                    "boundaryF1Delta": boundary_f1_delta,
                    "predictedEdgePixelsDelta": predicted_delta,
                }
            )
    return {
        "taxonomyCounts": dict(counter),
        "focusCases": sorted(focus_cases, key=lambda item: (item["status"], item["caseId"])),
    }


def edge_full_summary(document: dict[str, Any], source: Path) -> dict[str, Any]:
    summary = document["Summary"]
    return {
        "sourceReport": repo(source),
        "profile": summary.get("Profile") or "baseline",
        "caseCount": summary["CaseCount"],
        "boundaryPrecision": summary["BoundaryPrecision"],
        "boundaryRecall": summary["BoundaryRecall"],
        "boundaryF1": summary["BoundaryF1"],
        "consensusBoundaryPrecision": summary["ConsensusBoundaryPrecision"],
        "consensusBoundaryRecall": summary["ConsensusBoundaryRecall"],
        "consensusBoundaryF1": summary["ConsensusBoundaryF1"],
        "predictedToBoundaryMeanDistancePx": summary.get("PredictedToBoundaryMeanDistancePx"),
        "boundaryToPredictedMeanDistancePx": summary.get("BoundaryToPredictedMeanDistancePx"),
        "predictedToConsensusMeanDistancePx": summary.get("PredictedToConsensusMeanDistancePx"),
        "consensusToPredictedMeanDistancePx": summary.get("ConsensusToPredictedMeanDistancePx"),
        "runtimeMsP95": summary["RuntimeMsP95"],
    }


def build_document() -> dict[str, Any]:
    ab = read_json(AB_REPORT)
    surface_old_doc = read_json(SURFACE_BASELINE)
    surface_new_doc = read_json(SURFACE_CANDIDATE)
    anomaly_old_doc = read_json(ANOMALY_BASELINE)
    anomaly_new_doc = read_json(ANOMALY_CANDIDATE)
    edge_old_doc = read_json(EDGE_BASELINE)
    edge_new_doc = read_json(EDGE_CANDIDATE_REPLAY)
    edge_recall_sweep = read_json(EDGE_RECALL_SWEEP) if EDGE_RECALL_SWEEP.exists() else None
    anomaly_threshold_calibration = read_json(ANOMALY_THRESHOLD_CALIBRATION)
    surface_taxonomy = read_json(SURFACE_TAXONOMY)
    anomaly_legacy_taxonomy = read_json(ANOMALY_TAXONOMY) if ANOMALY_TAXONOMY.exists() else None

    baseline_surface_fpr = false_positive_rate_from_confusion(image_confusion(surface_old_doc))
    surface_old = surface_summary(surface_old_doc, SURFACE_BASELINE, baseline_surface_fpr)
    surface_new = surface_summary(surface_new_doc, SURFACE_CANDIDATE, baseline_surface_fpr)
    anomaly_old = anomaly_summary(anomaly_old_doc, ANOMALY_BASELINE)
    anomaly_new = anomaly_summary(anomaly_new_doc, ANOMALY_CANDIDATE)
    anomaly_failure = anomaly_failure_summary(anomaly_new_doc)
    edge_row = ab_operator(ab, "EdgeDetection")
    edge_cases = edge_row["replayCases"]
    edge_old_replay = aggregate_edge_metrics(edge_cases, "old")
    edge_new_replay = aggregate_edge_metrics(edge_cases, "new")
    edge_taxonomy = classify_edge_replay(edge_cases)

    surface_row = ab_operator(ab, "SurfaceDefectDetection")
    anomaly_row = ab_operator(ab, "AnomalyDetection")

    return {
        "schemaVersion": "2026-04-30.detection-precision.v2",
        "generatedAtUtc": utc_now(),
        "accepted": True,
        "claimBoundary": "Public dataset and replay evidence only; no real production-site sign-off.",
        "sourceReports": [
            repo(AB_REPORT),
            repo(SURFACE_BASELINE),
            repo(SURFACE_CANDIDATE),
            repo(SURFACE_SWEEP),
            repo(SURFACE_TAXONOMY),
            repo(ANOMALY_BASELINE),
            repo(ANOMALY_CANDIDATE),
            repo(ANOMALY_SWEEP),
            repo(ANOMALY_TAXONOMY),
            repo(ANOMALY_THRESHOLD_CALIBRATION),
            repo(EDGE_BASELINE),
            repo(EDGE_CANDIDATE_REPLAY),
            *([repo(EDGE_RECALL_SWEEP)] if edge_recall_sweep else []),
        ],
        "summary": {
            "surfacePixelF1Delta": delta(surface_new["pixelF1"], surface_old["pixelF1"]),
            "surfaceRecallAtFixedFprDelta": delta(surface_new["recallAtFixedFpr"], surface_old["recallAtFixedFpr"]),
            "surfaceFalsePositivePerImageDelta": delta(surface_new["falsePositivePerImage"], surface_old["falsePositivePerImage"]),
            "anomalyImageAurocDelta": delta(anomaly_new["imageAuroc"], anomaly_old["imageAuroc"]),
            "anomalyPixelAurocDelta": delta(anomaly_new["pixelAuroc"], anomaly_old["pixelAuroc"]),
            "anomalyImageRecallDelta": delta(anomaly_new["imageRecall"], anomaly_old["imageRecall"]),
            "anomalyImageF1Delta": delta(anomaly_new["imageF1"], anomaly_old["imageF1"]),
            "anomalyImageFalsePositiveDelta": delta(anomaly_new["imageFalsePositive"], anomaly_old["imageFalsePositive"]),
            "anomalyThreshold": anomaly_new["parameters"]["Threshold"],
            "anomalyRemainingMissedAnomalyCount": anomaly_failure["summary"]["missedAnomalyCount"],
            "edgeReplayBoundaryRecallDelta": delta(edge_new_replay["boundaryRecall"], edge_old_replay["boundaryRecall"]),
            "edgeReplayBoundaryF1Delta": delta(edge_new_replay["boundaryF1"], edge_old_replay["boundaryF1"]),
            "abReplayRegressedCaseCount": sum(ab_operator(ab, operator).get("regressedCaseCount", 0) for operator in DETECTION_OPERATORS),
        },
        "surfaceDefectDetection": {
            "decision": "candidate-ready-with-low-contrast-backlog",
            "baseline": surface_old,
            "candidate": surface_new,
            "abReplay": {
                "replayCaseCount": surface_row["replayCaseCount"],
                "improvedMetricCaseCount": surface_row.get("improvedMetricCaseCount"),
                "worseMetricCaseCount": surface_row.get("worseMetricCaseCount"),
                "regressedCaseCount": surface_row.get("regressedCaseCount"),
            },
            "taxonomySummary": surface_taxonomy["summary"],
            "nextAction": "Do not lower the global threshold further; target low-contrast and undersegmentation misses with guarded local normalization or component filtering.",
        },
        "anomalyDetection": {
            "decision": "candidate-ready-threshold-calibrated-runtime-heavy",
            "baseline": anomaly_old,
            "candidate": anomaly_new,
            "categories": anomaly_categories(anomaly_old_doc, anomaly_new_doc),
            "thresholdCalibration": anomaly_threshold_calibration,
            "taxonomySummary": anomaly_failure["summary"],
            "taxonomyCounts": anomaly_failure["tagCounts"],
            "legacyTaxonomySummary": anomaly_legacy_taxonomy.get("summary") if anomaly_legacy_taxonomy else None,
            "missedByDefectType": anomaly_failure["missedByDefectType"],
            "falsePositiveCaseIds": anomaly_failure["falsePositiveCaseIds"],
            "abReplay": {
                "replayCaseCount": anomaly_row["replayCaseCount"],
                "improvedMetricCaseCount": anomaly_row.get("improvedMetricCaseCount"),
                "regressedCaseCount": anomaly_row.get("regressedCaseCount"),
                "detectedAnomalyCaseCount": ab["summary"].get("anomalyDetectionDetectedAnomalyCaseCount"),
                "imageCorrectCaseCount": ab["summary"].get("anomalyDetectionImageCorrectCaseCount"),
            },
            "nextAction": "Keep v2 as a promotion-ready candidate and require field replay before any default threshold promotion.",
        },
        "edgeDetection": {
            "decision": "hold-recall-tuning",
            "fullBaseline": edge_full_summary(edge_old_doc, EDGE_BASELINE),
            "candidateReplayReport": edge_full_summary(edge_new_doc, EDGE_CANDIDATE_REPLAY),
            "replayOld": edge_old_replay,
            "replayCandidate": edge_new_replay,
            "taxonomySummary": edge_taxonomy["taxonomyCounts"],
            "focusCases": edge_taxonomy["focusCases"][:20],
            "recallGuardSweep": edge_recall_sweep,
            "abReplay": {
                "replayCaseCount": edge_row["replayCaseCount"],
                "improvedMetricCaseCount": edge_row.get("improvedMetricCaseCount"),
                "worseMetricCaseCount": edge_row.get("worseMetricCaseCount"),
                "regressedCaseCount": edge_row.get("regressedCaseCount"),
            },
            "nextAction": "Keep EdgeDetection in hold status until recall-guard replay has a profile that restores recall without losing the F1/precision gain.",
        },
        "gates": {
            "productDefaultChange": False,
            "detectionScopedReplay": ab.get("policy", {}).get("validationScope"),
            "noPassFailRegressions": sum(ab_operator(ab, operator).get("regressedCaseCount", 0) for operator in DETECTION_OPERATORS) == 0,
            "surfaceFalsePositiveNotHigher": surface_new["falsePositivePerImage"] <= surface_old["falsePositivePerImage"],
            "surfaceRecallAtFixedFprNotLower": surface_new["recallAtFixedFpr"] >= surface_old["recallAtFixedFpr"],
            "anomalyAurocNotLower": anomaly_new["imageAuroc"] >= anomaly_old["imageAuroc"] and anomaly_new["pixelAuroc"] >= anomaly_old["pixelAuroc"],
            "anomalyCandidateVersion": anomaly_new_doc["Summary"].get("CandidateVersion"),
            "anomalyThresholdCalibrationAttached": abs(float(anomaly_threshold_calibration["selected"]["threshold"]) - float(anomaly_new["parameters"]["Threshold"])) < 1e-9,
            "anomalyPrecisionFloor": anomaly_new["imagePrecision"] >= 0.95,
            "anomalyFalsePositiveLimit": anomaly_new["imageFalsePositive"] <= 3,
            "edgeRecallNeedsFollowup": edge_new_replay["boundaryRecall"] < edge_old_replay["boundaryRecall"],
        },
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("accepted") is not True:
        errors.append("detection precision report must be accepted")
    gates = document.get("gates", {})
    for key in (
        "noPassFailRegressions",
        "surfaceFalsePositiveNotHigher",
        "surfaceRecallAtFixedFprNotLower",
        "anomalyAurocNotLower",
        "anomalyThresholdCalibrationAttached",
        "anomalyPrecisionFloor",
        "anomalyFalsePositiveLimit",
    ):
        if gates.get(key) is not True:
            errors.append(f"gate must pass: {key}")
    if gates.get("anomalyCandidateVersion") != "v2":
        errors.append("AnomalyDetection candidate report must use candidate v2")
    if gates.get("productDefaultChange") is not False:
        errors.append("Phase D report must not change product defaults")
    if gates.get("detectionScopedReplay") != "detection":
        errors.append("A/B replay report must be regenerated with validationScope=detection")
    if not document.get("edgeDetection", {}).get("taxonomySummary"):
        errors.append("EdgeDetection recall/precision taxonomy must not be empty")
    if not document.get("surfaceDefectDetection", {}).get("taxonomySummary", {}).get("taxonomyCounts"):
        errors.append("SurfaceDefectDetection taxonomy must not be empty")
    if not document.get("anomalyDetection", {}).get("missedByDefectType"):
        errors.append("AnomalyDetection missed-by-defect-type summary must not be empty")
    edge_full = document.get("edgeDetection", {}).get("fullBaseline", {})
    if edge_full.get("boundaryToPredictedMeanDistancePx") is None:
        errors.append("EdgeDetection full baseline must include boundary localization distance")
    if not document.get("edgeDetection", {}).get("recallGuardSweep"):
        errors.append("EdgeDetection recall-guard sweep must be attached")
    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("detection precision report contains raw local path pattern")
    return errors


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.4f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    surface = document["surfaceDefectDetection"]
    anomaly = document["anomalyDetection"]
    edge = document["edgeDetection"]
    lines = [
        "# Quality Flywheel Detection Precision v2",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"Accepted: `{document['accepted']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "## Summary",
        "",
        "| Family | Decision | Replay improved | Replay worse | Key delta | Next action |",
        "|---|---|---:|---:|---:|---|",
        f"| SurfaceDefectDetection | {surface['decision']} | {surface['abReplay']['improvedMetricCaseCount']} | {surface['abReplay']['worseMetricCaseCount']} | PixelF1 {fmt(document['summary']['surfacePixelF1Delta'])} | {surface['nextAction']} |",
        f"| AnomalyDetection | {anomaly['decision']} | {anomaly['abReplay']['improvedMetricCaseCount']} | 0 | ImageF1 {fmt(document['summary']['anomalyImageF1Delta'])} | {anomaly['nextAction']} |",
        f"| EdgeDetection | {edge['decision']} | {edge['abReplay']['improvedMetricCaseCount']} | {edge['abReplay']['worseMetricCaseCount']} | BoundaryRecall {fmt(document['summary']['edgeReplayBoundaryRecallDelta'])} | {edge['nextAction']} |",
        "",
        "## SurfaceDefectDetection",
        "",
        "| Metric | Baseline | Candidate | Delta |",
        "|---|---:|---:|---:|",
        f"| Pixel F1 | {fmt(surface['baseline']['pixelF1'])} | {fmt(surface['candidate']['pixelF1'])} | {fmt(document['summary']['surfacePixelF1Delta'])} |",
        f"| Recall at fixed FPR | {fmt(surface['baseline']['recallAtFixedFpr'])} | {fmt(surface['candidate']['recallAtFixedFpr'])} | {fmt(document['summary']['surfaceRecallAtFixedFprDelta'])} |",
        f"| FP/normal | {fmt(surface['baseline']['falsePositivePerImage'])} | {fmt(surface['candidate']['falsePositivePerImage'])} | {fmt(document['summary']['surfaceFalsePositivePerImageDelta'])} |",
        "",
        "## AnomalyDetection",
        "",
        "| Metric | Baseline | Candidate | Delta |",
        "|---|---:|---:|---:|",
        f"| Image AUROC | {fmt(anomaly['baseline']['imageAuroc'])} | {fmt(anomaly['candidate']['imageAuroc'])} | {fmt(document['summary']['anomalyImageAurocDelta'])} |",
        f"| Pixel AUROC | {fmt(anomaly['baseline']['pixelAuroc'])} | {fmt(anomaly['candidate']['pixelAuroc'])} | {fmt(document['summary']['anomalyPixelAurocDelta'])} |",
        f"| Threshold | {fmt(anomaly['baseline']['parameters']['Threshold'])} | {fmt(anomaly['candidate']['parameters']['Threshold'])} | - |",
        f"| Image precision | {fmt(anomaly['baseline']['imagePrecision'])} | {fmt(anomaly['candidate']['imagePrecision'])} | - |",
        f"| Image recall | {fmt(anomaly['baseline']['imageRecall'])} | {fmt(anomaly['candidate']['imageRecall'])} | {fmt(document['summary']['anomalyImageRecallDelta'])} |",
        f"| Image F1 | {fmt(anomaly['baseline']['imageF1'])} | {fmt(anomaly['candidate']['imageF1'])} | {fmt(document['summary']['anomalyImageF1Delta'])} |",
        f"| Image FP | {fmt(anomaly['baseline']['imageFalsePositive'])} | {fmt(anomaly['candidate']['imageFalsePositive'])} | {fmt(document['summary']['anomalyImageFalsePositiveDelta'])} |",
        f"| Image FN | {fmt(anomaly['baseline']['imageFalseNegative'])} | {fmt(anomaly['candidate']['imageFalseNegative'])} | - |",
        "",
        "### Threshold Calibration",
        "",
        f"Selected: `{anomaly['thresholdCalibration']['selected']['threshold']}`; precision `{anomaly['thresholdCalibration']['selected']['imagePrecision']}`; recall `{anomaly['thresholdCalibration']['selected']['imageRecall']}`; false positives `{anomaly['thresholdCalibration']['selected']['imageFalsePositive']}`.",
        "",
        "### Missed By Defect Type",
        "",
        "| Defect | Missed |",
        "|---|---:|",
    ]
    for label, count in sorted(anomaly["missedByDefectType"].items()):
        lines.append(f"| {label} | {count} |")

    lines.extend(
        [
            "",
            "## EdgeDetection",
            "",
            "| Metric | Replay old | Replay candidate | Delta |",
            "|---|---:|---:|---:|",
            f"| Boundary precision | {fmt(edge['replayOld']['boundaryPrecision'])} | {fmt(edge['replayCandidate']['boundaryPrecision'])} | {fmt(delta(edge['replayCandidate']['boundaryPrecision'], edge['replayOld']['boundaryPrecision']))} |",
            f"| Boundary recall | {fmt(edge['replayOld']['boundaryRecall'])} | {fmt(edge['replayCandidate']['boundaryRecall'])} | {fmt(document['summary']['edgeReplayBoundaryRecallDelta'])} |",
            f"| Boundary F1 | {fmt(edge['replayOld']['boundaryF1'])} | {fmt(edge['replayCandidate']['boundaryF1'])} | {fmt(document['summary']['edgeReplayBoundaryF1Delta'])} |",
            f"| Full baseline boundary->predicted px | {fmt(edge['fullBaseline']['boundaryToPredictedMeanDistancePx'])} | - | - |",
            "",
            "### Edge Replay Taxonomy",
            "",
            "| Taxonomy | Count |",
            "|---|---:|",
        ]
    )
    for label, count in sorted(edge["taxonomySummary"].items()):
        lines.append(f"| {label} | {count} |")

    sweep = edge.get("recallGuardSweep") or {}
    if sweep:
        lines.extend(
            [
                "",
                "### Edge Recall-Guard Sweep",
                "",
                f"Decision: `{sweep.get('decision')}`; SelectedProfile: `{sweep.get('selectedProfile')}`",
                "",
                "| Profile | Precision | Recall | F1 | B->P px |",
                "|---|---:|---:|---:|---:|",
            ]
        )
        for row in sweep.get("rows", []):
            lines.append(
                f"| {row['profile']} | {fmt(row['boundaryPrecision'])} | {fmt(row['boundaryRecall'])} | "
                f"{fmt(row['boundaryF1'])} | {fmt(row.get('boundaryToPredictedMeanDistancePx'))} |"
            )

    lines.extend(["", "## Gates", ""])
    lines.extend(f"- {key}: `{value}`" for key, value in document["gates"].items())
    lines.extend(["", "## Evidence", ""])
    lines.extend(f"- `{source}`" for source in document["sourceReports"])
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build or validate Phase D detection precision report.")
    parser.add_argument("--validate-only", action="store_true", help="Validate existing QualityFlywheel_detection_precision_v2 without rewriting it.")
    args = parser.parse_args()

    document = read_json(OUTPUT_JSON) if args.validate_only else build_document()
    errors = validate_document(document)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    if not args.validate_only:
        write_json(OUTPUT_JSON, document)
        write_text(OUTPUT_MD, render_markdown(document))
    action = "valid" if args.validate_only else "complete"
    print(
        "detection precision v2 report "
        f"{action}: surfaceDelta={document['summary']['surfacePixelF1Delta']} "
        f"anomalyImageF1Delta={document['summary']['anomalyImageF1Delta']} "
        f"edgeRecallDelta={document['summary']['edgeReplayBoundaryRecallDelta']} "
        f"output={repo(OUTPUT_JSON)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
