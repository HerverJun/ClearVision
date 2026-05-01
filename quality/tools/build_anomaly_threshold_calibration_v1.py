from __future__ import annotations

import argparse
import json
import re
import subprocess
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
SOURCE_CANDIDATE = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v1.json"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_anomaly_threshold_calibration_v1.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_anomaly_threshold_calibration_v1.md"
TARGET_CANDIDATE_JSON = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v2.json"
TARGET_CANDIDATE_MD = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v2.md"

RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
THRESHOLDS = (0.35, 0.30, 0.25, 0.20, 0.15, 0.10, 0.05, 0.01)
PRECISION_FLOOR = 0.95
FALSE_POSITIVE_LIMIT = 3
TARGET_CANDIDATE_VERSION = "v2"
TARGET_PROFILE = "max192_dense_stride8_threshold_010"


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
    return 0.0 if precision + recall <= 0 else (2 * precision * recall) / (precision + recall)


def round_metric(value: float) -> float:
    return round(float(value), 6)


def threshold_taxonomy(image: dict[str, Any], threshold: float) -> list[str]:
    score = float(image.get("Score") or 0.0)
    predicted = score >= threshold
    is_anomaly = image.get("IsAnomaly") is True
    labels: list[str] = []
    if is_anomaly and not predicted:
        labels.append("anomaly_miss")
        labels.append("zero_score_anomaly" if score <= 1e-9 else "below_threshold_anomaly")
        labels.append(f"defect_{image.get('DefectType') or 'unknown'}")
    if not is_anomaly and predicted:
        labels.append("false_positive_good")
        labels.append("above_threshold_good")
    return labels


def evaluate_threshold(images: list[dict[str, Any]], threshold: float, source_threshold: float) -> dict[str, Any]:
    positives = [image for image in images if image.get("IsAnomaly") is True]
    negatives = [image for image in images if image.get("IsAnomaly") is not True]
    true_positive = sum(1 for image in positives if float(image.get("Score") or 0.0) >= threshold)
    false_negative = len(positives) - true_positive
    false_positive = sum(1 for image in negatives if float(image.get("Score") or 0.0) >= threshold)
    true_negative = len(negatives) - false_positive
    precision = safe_divide(true_positive, true_positive + false_positive)
    recall = safe_divide(true_positive, true_positive + false_negative)
    source_missed = {
        str(image.get("CaseId"))
        for image in positives
        if float(image.get("Score") or 0.0) < source_threshold
    }
    selected_detected = {
        str(image.get("CaseId"))
        for image in positives
        if float(image.get("Score") or 0.0) >= threshold
    }
    recovered = [
        image for image in positives
        if str(image.get("CaseId")) in source_missed
        and str(image.get("CaseId")) in selected_detected
    ]
    missed = [image for image in positives if float(image.get("Score") or 0.0) < threshold]
    false_positives = [image for image in negatives if float(image.get("Score") or 0.0) >= threshold]
    tags = Counter()
    for image in images:
        tags.update(threshold_taxonomy(image, threshold))

    return {
        "threshold": round_metric(threshold),
        "imageTruePositive": true_positive,
        "imageFalsePositive": false_positive,
        "imageFalseNegative": false_negative,
        "imageTrueNegative": true_negative,
        "imagePrecision": round_metric(precision),
        "imageRecall": round_metric(recall),
        "imageF1": round_metric(harmonic(precision, recall)),
        "recoveredAnomalyCount": len(recovered),
        "remainingMissedAnomalyCount": len(missed),
        "recoveredByDefectType": dict(Counter(str(image.get("DefectType") or "unknown") for image in recovered).most_common()),
        "remainingMissedByDefectType": dict(Counter(str(image.get("DefectType") or "unknown") for image in missed).most_common()),
        "falsePositiveCaseIds": [str(image.get("CaseId")) for image in false_positives],
        "taxonomyCounts": dict(tags.most_common()),
    }


def select_threshold(rows: list[dict[str, Any]], current_threshold: float) -> dict[str, Any]:
    eligible = [
        row for row in rows
        if row["threshold"] < current_threshold
        and row["imagePrecision"] >= PRECISION_FLOOR
        and row["imageFalsePositive"] <= FALSE_POSITIVE_LIMIT
    ]
    if not eligible:
        return max(rows, key=lambda row: (row["imageF1"], row["imageRecall"], row["imagePrecision"], -row["imageFalsePositive"]))
    return max(
        eligible,
        key=lambda row: (
            row["imageF1"],
            row["imageRecall"],
            row["imagePrecision"],
            -row["imageFalsePositive"],
            row["threshold"],
        ),
    )


def build_document() -> dict[str, Any]:
    candidate = read_json(SOURCE_CANDIDATE)
    summary = candidate["Summary"]
    images = candidate.get("Images") or []
    source_threshold = float(summary.get("Threshold") or 0.35)
    thresholds = sorted({source_threshold, *THRESHOLDS}, reverse=True)
    rows = [evaluate_threshold(images, threshold, source_threshold) for threshold in thresholds]
    current = min(rows, key=lambda row: abs(float(row["threshold"]) - source_threshold))
    selected = select_threshold(rows, source_threshold)
    return {
        "schemaVersion": "2026-04-30.anomaly-threshold-calibration.v1",
        "generatedAtUtc": utc_now(),
        "accepted": True,
        "claimBoundary": "MVTec AD Lite score-threshold calibration only; no product default promotion and no field sign-off claim.",
        "sourceCandidate": repo(SOURCE_CANDIDATE),
        "targetCandidateVersion": TARGET_CANDIDATE_VERSION,
        "targetProfile": TARGET_PROFILE,
        "productDefaultChange": False,
        "selectionPolicy": (
            "Select highest image F1 below the current threshold while keeping image precision >= "
            f"{PRECISION_FLOOR:.2f} and false positives <= {FALSE_POSITIVE_LIMIT}."
        ),
        "current": current,
        "selected": selected,
        "thresholdRows": rows,
        "summary": {
            "thresholdOld": current["threshold"],
            "thresholdNew": selected["threshold"],
            "imagePrecisionOld": current["imagePrecision"],
            "imagePrecisionNew": selected["imagePrecision"],
            "imageRecallOld": current["imageRecall"],
            "imageRecallNew": selected["imageRecall"],
            "imageRecallDelta": round_metric(selected["imageRecall"] - current["imageRecall"]),
            "imageF1Old": current["imageF1"],
            "imageF1New": selected["imageF1"],
            "imageF1Delta": round_metric(selected["imageF1"] - current["imageF1"]),
            "falsePositiveOld": current["imageFalsePositive"],
            "falsePositiveNew": selected["imageFalsePositive"],
            "falseNegativeOld": current["imageFalseNegative"],
            "falseNegativeNew": selected["imageFalseNegative"],
            "recoveredAnomalyCount": selected["recoveredAnomalyCount"],
            "remainingMissedAnomalyCount": selected["remainingMissedAnomalyCount"],
        },
        "v2RunnerCommand": [
            "dotnet",
            "run",
            "--project",
            "quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj",
            "--",
            "--index",
            "quality/datasets/mvtec_ad_lite_index.json",
            "--output",
            "quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.json",
            "--report",
            "quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.md",
            "--candidate-version",
            TARGET_CANDIDATE_VERSION,
            "--profile",
            TARGET_PROFILE,
            "--max-side",
            str(summary.get("MaxSide") or 192),
            "--patch-size",
            str(summary.get("PatchSize") or 16),
            "--patch-stride",
            str(summary.get("PatchStride") or 8),
            "--pixel-sample-stride",
            str(summary.get("PixelSampleStride") or 2),
            "--coreset-ratio",
            str(summary.get("CoresetRatio") or 0.02),
            "--threshold",
            str(selected["threshold"]),
            "--min-image-auroc",
            "0.70",
            "--min-pixel-auroc",
            "0.70",
            "--min-category-image-auroc",
            "0.70",
            "--min-category-pixel-auroc",
            "0.70",
        ],
        "gates": {
            "productDefaultChange": False,
            "selectedBelowCurrentThreshold": selected["threshold"] < current["threshold"],
            "precisionFloor": selected["imagePrecision"] >= PRECISION_FLOOR,
            "falsePositiveLimit": selected["imageFalsePositive"] <= FALSE_POSITIVE_LIMIT,
            "recallImproved": selected["imageRecall"] > current["imageRecall"],
            "f1Improved": selected["imageF1"] > current["imageF1"],
        },
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("accepted") is not True:
        errors.append("Anomaly threshold calibration must be accepted")
    if document.get("productDefaultChange") is not False:
        errors.append("Anomaly threshold calibration must not change product defaults")
    gates = document.get("gates", {})
    for key in ("selectedBelowCurrentThreshold", "precisionFloor", "falsePositiveLimit", "recallImproved", "f1Improved"):
        if gates.get(key) is not True:
            errors.append(f"gate must pass: {key}")
    selected = document.get("selected", {})
    if selected.get("imagePrecision", 0) < PRECISION_FLOOR:
        errors.append("selected threshold precision is below floor")
    if selected.get("imageFalsePositive", 999) > FALSE_POSITIVE_LIMIT:
        errors.append("selected threshold has too many false positives")
    if not selected.get("remainingMissedByDefectType"):
        errors.append("selected threshold must report remaining missed defect types")
    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("Anomaly threshold calibration contains raw local path pattern")
    return errors


def validate_candidate(document: dict[str, Any], candidate: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    summary = candidate.get("Summary", {})
    selected = document.get("selected", {})
    expected = {
        "CandidateVersion": document.get("targetCandidateVersion"),
        "ProfileName": document.get("targetProfile"),
        "Threshold": selected.get("threshold"),
        "ImagePrecision": selected.get("imagePrecision"),
        "ImageRecall": selected.get("imageRecall"),
        "ImageF1": selected.get("imageF1"),
        "ImageTruePositive": selected.get("imageTruePositive"),
        "ImageFalsePositive": selected.get("imageFalsePositive"),
        "ImageFalseNegative": selected.get("imageFalseNegative"),
        "ImageTrueNegative": selected.get("imageTrueNegative"),
    }
    for key, expected_value in expected.items():
        actual_value = summary.get(key)
        if isinstance(expected_value, float):
            if actual_value is None or abs(float(actual_value) - expected_value) > 1e-6:
                errors.append(f"candidate v2 summary mismatch for {key}: expected {expected_value}, got {actual_value}")
        elif actual_value != expected_value:
            errors.append(f"candidate v2 summary mismatch for {key}: expected {expected_value}, got {actual_value}")
    if int(summary.get("Failed") or 0) != 0:
        errors.append("candidate v2 must have zero failed gates")
    if float(summary.get("ImageAuroc") or 0.0) < 0.70:
        errors.append("candidate v2 image AUROC must stay above 0.70")
    if float(summary.get("PixelAuroc") or 0.0) < 0.70:
        errors.append("candidate v2 pixel AUROC must stay above 0.70")
    if RAW_PATH_RE.search(json.dumps(candidate, ensure_ascii=False)):
        errors.append("candidate v2 report contains raw local path pattern")
    return errors


def execute_candidate(document: dict[str, Any]) -> dict[str, Any]:
    command = [str(item) for item in document["v2RunnerCommand"]]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            "error: AnomalyDetection candidate v2 execution failed\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return read_json(TARGET_CANDIDATE_JSON)


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.4f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# AnomalyDetection Threshold Calibration v1",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"Accepted: `{document['accepted']}`",
        f"TargetCandidateVersion: `{document['targetCandidateVersion']}`",
        f"TargetProfile: `{document['targetProfile']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "## Summary",
        "",
        "| Metric | Current | Selected | Delta |",
        "|---|---:|---:|---:|",
        f"| Threshold | {fmt(document['summary']['thresholdOld'])} | {fmt(document['summary']['thresholdNew'])} | - |",
        f"| Image precision | {fmt(document['summary']['imagePrecisionOld'])} | {fmt(document['summary']['imagePrecisionNew'])} | - |",
        f"| Image recall | {fmt(document['summary']['imageRecallOld'])} | {fmt(document['summary']['imageRecallNew'])} | {fmt(document['summary']['imageRecallDelta'])} |",
        f"| Image F1 | {fmt(document['summary']['imageF1Old'])} | {fmt(document['summary']['imageF1New'])} | {fmt(document['summary']['imageF1Delta'])} |",
        f"| False positives | {document['summary']['falsePositiveOld']} | {document['summary']['falsePositiveNew']} | - |",
        f"| False negatives | {document['summary']['falseNegativeOld']} | {document['summary']['falseNegativeNew']} | - |",
        "",
        "## Threshold Sweep",
        "",
        "| Threshold | Precision | Recall | F1 | TP | FP | FN | Recovered |",
        "|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in document["thresholdRows"]:
        marker = " selected" if row["threshold"] == document["selected"]["threshold"] else ""
        lines.append(
            f"| {fmt(row['threshold'])}{marker} | {fmt(row['imagePrecision'])} | {fmt(row['imageRecall'])} | "
            f"{fmt(row['imageF1'])} | {row['imageTruePositive']} | {row['imageFalsePositive']} | "
            f"{row['imageFalseNegative']} | {row['recoveredAnomalyCount']} |"
        )
    lines.extend(
        [
            "",
            "## Remaining Misses",
            "",
            "| Defect | Count |",
            "|---|---:|",
        ]
    )
    for label, count in document["selected"]["remainingMissedByDefectType"].items():
        lines.append(f"| {label} | {count} |")
    lines.extend(["", "## Gates", ""])
    lines.extend(f"- {key}: `{value}`" for key, value in document["gates"].items())
    lines.extend(["", "## Evidence", "", f"- `{document['sourceCandidate']}`", ""])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build or validate AnomalyDetection threshold calibration report.")
    parser.add_argument("--validate-only", action="store_true", help="Validate existing calibration report without rewriting it.")
    parser.add_argument("--execute-candidate", action="store_true", help="Regenerate and validate AnomalyDetection v2 full candidate from the calibrated threshold.")
    parser.add_argument("--validate-candidate", action="store_true", help="Validate existing AnomalyDetection v2 full candidate against the calibration report.")
    args = parser.parse_args()

    document = read_json(OUTPUT_JSON) if args.validate_only else build_document()
    errors = validate_document(document)
    candidate = None
    if args.execute_candidate:
        candidate = execute_candidate(document)
        errors.extend(validate_candidate(document, candidate))
    elif args.validate_candidate:
        candidate = read_json(TARGET_CANDIDATE_JSON)
        errors.extend(validate_candidate(document, candidate))
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    if not args.validate_only:
        write_json(OUTPUT_JSON, document)
        write_text(OUTPUT_MD, render_markdown(document))
    action = "valid" if args.validate_only else "complete"
    print(
        "AnomalyDetection threshold calibration "
        f"{action}: selected={document['selected']['threshold']} "
        f"recallDelta={document['summary']['imageRecallDelta']} "
        f"fp={document['selected']['imageFalsePositive']} "
        f"candidateV2={'validated' if candidate else 'not-run'} "
        f"output={repo(OUTPUT_JSON)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
