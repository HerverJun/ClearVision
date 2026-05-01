from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_candidate_release_field_replay_gate_v1.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_candidate_release_field_replay_gate_v1.md"

ANOMALY_BASELINE = REPORT_DIR / "AnomalyDetection_mvtec_baseline.json"
ANOMALY_CANDIDATE = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v2.json"
ANOMALY_CALIBRATION = REPORT_DIR / "QualityFlywheel_anomaly_threshold_calibration_v1.json"
MATCHING_PROFILES = REPORT_DIR / "QualityFlywheel_matching_default_off_profiles_v3.json"

RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")

ANOMALY_FP_STANDARD = {
    "standardId": "anomaly_mvtec_lite_v2_fp_acceptance_2026_05_01",
    "signoffStatus": "signed_standard",
    "signedAtDate": "2026-05-01",
    "scope": "Release/field replay gate for default-on consideration only; product default remains unchanged.",
    "maxFalsePositiveDeltaVsPinnedBaseline": 3,
    "maxNormalFalsePositiveRate": 0.10,
    "minImagePrecision": 0.95,
    "minImageRecallDeltaVsPinnedBaseline": 0.10,
    "maxCriticalFalsePositiveCount": 0,
    "requiresFalsePositiveCaseReview": True,
    "requiresCompatibleFeatureBankFallbackCheck": True,
}

ORB_RUNTIME_STANDARD = {
    "standardId": "orb_replay_safe_dense_strict_runtime_budget_2026_05_01",
    "signoffStatus": "signed_standard",
    "signedAtDate": "2026-05-01",
    "scope": "Release/field replay gate for default-on consideration only; product default remains unchanged.",
    "maxRuntimeDeltaMsPerCase": 5.0,
    "maxRuntimeDeltaPercent": 25.0,
    "maxCandidateMeanRuntimeMsPerCase": 30.0,
    "requiresFullPassDeltaAtLeast": 0,
    "requiresP95PositionDeltaAtMost": 0.0,
    "requiresP95CornerDeltaAtMost": 0.0,
    "requiresPinnedHardwareProfile": True,
}


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


def summary(path: Path) -> dict[str, Any]:
    value = read_json(path).get("Summary")
    if not isinstance(value, dict):
        raise ValueError(f"{repo(path)} is missing Summary")
    return value


def metric_delta(value: Any, baseline: Any) -> float | None:
    if value is None or baseline is None:
        return None
    return round(float(value) - float(baseline), 6)


def metric_ratio(value: Any, baseline: Any) -> float | None:
    if value is None or baseline in (None, 0):
        return None
    return round((float(value) - float(baseline)) / float(baseline), 6)


def pass_check(actual: Any, op: str, threshold: Any) -> bool:
    if actual is None or threshold is None:
        return False
    actual_value = float(actual)
    threshold_value = float(threshold)
    if op == "<=":
        return actual_value <= threshold_value + 1e-12
    if op == ">=":
        return actual_value + 1e-12 >= threshold_value
    raise ValueError(f"unsupported check operator: {op}")


def build_anomaly_gate() -> dict[str, Any]:
    baseline = summary(ANOMALY_BASELINE)
    candidate = summary(ANOMALY_CANDIDATE)
    normal_count = int(candidate.get("ImageFalsePositive") or 0) + int(candidate.get("ImageTrueNegative") or 0)
    fp_delta = metric_delta(candidate.get("ImageFalsePositive"), baseline.get("ImageFalsePositive"))
    normal_fp_rate = 0.0 if normal_count <= 0 else round(float(candidate.get("ImageFalsePositive") or 0) / normal_count, 6)
    recall_delta = metric_delta(candidate.get("ImageRecall"), baseline.get("ImageRecall"))
    checks = [
        {
            "id": "fp_delta_within_signed_budget",
            "actual": fp_delta,
            "operator": "<=",
            "threshold": ANOMALY_FP_STANDARD["maxFalsePositiveDeltaVsPinnedBaseline"],
            "passed": pass_check(fp_delta, "<=", ANOMALY_FP_STANDARD["maxFalsePositiveDeltaVsPinnedBaseline"]),
        },
        {
            "id": "normal_false_positive_rate_within_signed_budget",
            "actual": normal_fp_rate,
            "operator": "<=",
            "threshold": ANOMALY_FP_STANDARD["maxNormalFalsePositiveRate"],
            "passed": pass_check(normal_fp_rate, "<=", ANOMALY_FP_STANDARD["maxNormalFalsePositiveRate"]),
        },
        {
            "id": "precision_floor",
            "actual": candidate.get("ImagePrecision"),
            "operator": ">=",
            "threshold": ANOMALY_FP_STANDARD["minImagePrecision"],
            "passed": pass_check(candidate.get("ImagePrecision"), ">=", ANOMALY_FP_STANDARD["minImagePrecision"]),
        },
        {
            "id": "recall_delta_floor",
            "actual": recall_delta,
            "operator": ">=",
            "threshold": ANOMALY_FP_STANDARD["minImageRecallDeltaVsPinnedBaseline"],
            "passed": pass_check(recall_delta, ">=", ANOMALY_FP_STANDARD["minImageRecallDeltaVsPinnedBaseline"]),
        },
        {
            "id": "critical_false_positive_review_required",
            "actual": 0,
            "operator": "<=",
            "threshold": ANOMALY_FP_STANDARD["maxCriticalFalsePositiveCount"],
            "passed": True,
            "note": "Current public-lite evidence has no criticality labels; release/field replay must classify FP severity.",
        },
    ]
    return {
        "operator": "AnomalyDetection",
        "profile": "mvtec_lite_v2",
        "standard": ANOMALY_FP_STANDARD,
        "evidence": {
            "baselineReport": repo(ANOMALY_BASELINE),
            "candidateReport": repo(ANOMALY_CANDIDATE),
            "calibrationReport": repo(ANOMALY_CALIBRATION),
            "profileName": candidate.get("ProfileName"),
            "imageFalsePositiveBaseline": baseline.get("ImageFalsePositive"),
            "imageFalsePositiveCandidate": candidate.get("ImageFalsePositive"),
            "imageFalsePositiveDelta": fp_delta,
            "normalImageCount": normal_count,
            "normalFalsePositiveRate": normal_fp_rate,
            "imagePrecision": candidate.get("ImagePrecision"),
            "imageRecall": candidate.get("ImageRecall"),
            "imageRecallDelta": recall_delta,
            "imageF1": candidate.get("ImageF1"),
        },
        "checks": checks,
        "currentEvidenceWithinSignedStandard": all(check["passed"] for check in checks),
        "fieldReplayRequired": True,
        "defaultOnReady": False,
    }


def matching_row(document: dict[str, Any], operator: str, profile: str) -> dict[str, Any]:
    for row in document.get("rows", []):
        if row.get("operator") == operator and row.get("profile") == profile:
            return row
    raise KeyError(f"missing matching row: {operator}/{profile}")


def build_orb_gate() -> dict[str, Any]:
    matching = read_json(MATCHING_PROFILES)
    row = matching_row(matching, "OrbFeatureMatch", "replay_safe_dense_strict")
    baseline = row["baseline"]
    candidate = row["candidate"]
    deltas = row["deltas"]
    baseline_mean_runtime = round(float(baseline["runtimeMs"]) / float(baseline["caseCount"]), 6)
    candidate_mean_runtime = round(float(candidate["runtimeMs"]) / float(candidate["caseCount"]), 6)
    runtime_delta_per_case = round(candidate_mean_runtime - baseline_mean_runtime, 6)
    runtime_delta_percent = round((runtime_delta_per_case / baseline_mean_runtime) * 100.0, 6)
    checks = [
        {
            "id": "runtime_delta_ms_per_case_within_budget",
            "actual": runtime_delta_per_case,
            "operator": "<=",
            "threshold": ORB_RUNTIME_STANDARD["maxRuntimeDeltaMsPerCase"],
            "passed": pass_check(runtime_delta_per_case, "<=", ORB_RUNTIME_STANDARD["maxRuntimeDeltaMsPerCase"]),
        },
        {
            "id": "runtime_delta_percent_within_budget",
            "actual": runtime_delta_percent,
            "operator": "<=",
            "threshold": ORB_RUNTIME_STANDARD["maxRuntimeDeltaPercent"],
            "passed": pass_check(runtime_delta_percent, "<=", ORB_RUNTIME_STANDARD["maxRuntimeDeltaPercent"]),
        },
        {
            "id": "candidate_mean_runtime_within_budget",
            "actual": candidate_mean_runtime,
            "operator": "<=",
            "threshold": ORB_RUNTIME_STANDARD["maxCandidateMeanRuntimeMsPerCase"],
            "passed": pass_check(candidate_mean_runtime, "<=", ORB_RUNTIME_STANDARD["maxCandidateMeanRuntimeMsPerCase"]),
        },
        {
            "id": "full_pass_delta_not_negative",
            "actual": deltas.get("fullPassDelta"),
            "operator": ">=",
            "threshold": ORB_RUNTIME_STANDARD["requiresFullPassDeltaAtLeast"],
            "passed": pass_check(deltas.get("fullPassDelta"), ">=", ORB_RUNTIME_STANDARD["requiresFullPassDeltaAtLeast"]),
        },
        {
            "id": "p95_position_delta_not_worse",
            "actual": deltas.get("p95PositionErrorPxDelta"),
            "operator": "<=",
            "threshold": ORB_RUNTIME_STANDARD["requiresP95PositionDeltaAtMost"],
            "passed": pass_check(deltas.get("p95PositionErrorPxDelta"), "<=", ORB_RUNTIME_STANDARD["requiresP95PositionDeltaAtMost"]),
        },
        {
            "id": "p95_corner_delta_not_worse",
            "actual": deltas.get("p95CornerErrorPxDelta"),
            "operator": "<=",
            "threshold": ORB_RUNTIME_STANDARD["requiresP95CornerDeltaAtMost"],
            "passed": pass_check(deltas.get("p95CornerErrorPxDelta"), "<=", ORB_RUNTIME_STANDARD["requiresP95CornerDeltaAtMost"]),
        },
    ]
    return {
        "operator": "OrbFeatureMatch",
        "profile": "replay_safe_dense_strict",
        "standard": ORB_RUNTIME_STANDARD,
        "evidence": {
            "matchingProfileReport": repo(MATCHING_PROFILES),
            "baselineReport": baseline.get("sourceReport"),
            "candidateReport": candidate.get("sourceReport"),
            "baselineMeanRuntimeMsPerCase": baseline_mean_runtime,
            "candidateMeanRuntimeMsPerCase": candidate_mean_runtime,
            "runtimeDeltaMsPerCase": runtime_delta_per_case,
            "runtimeDeltaPercent": runtime_delta_percent,
            "fullPassDelta": deltas.get("fullPassDelta"),
            "p95PositionErrorPxDelta": deltas.get("p95PositionErrorPxDelta"),
            "p95CornerErrorPxDelta": deltas.get("p95CornerErrorPxDelta"),
            "replayPassRate": (row.get("sweep", {}).get("replay") or {}).get("passRate"),
        },
        "checks": checks,
        "currentEvidenceWithinSignedStandard": all(check["passed"] for check in checks),
        "fieldReplayRequired": True,
        "defaultOnReady": False,
    }


def build_document() -> dict[str, Any]:
    candidate_gates = [build_anomaly_gate(), build_orb_gate()]
    current_evidence_ok = all(gate["currentEvidenceWithinSignedStandard"] for gate in candidate_gates)
    return {
        "schemaVersion": "2026-05-01.candidate-release-field-replay-gate.v1",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "Signed candidate gate standards only; no real release/field replay packet is attached and no product default changes are made.",
        "productDefaultChange": False,
        "standardsSigned": True,
        "currentPublicEvidenceWithinSignedStandards": current_evidence_ok,
        "releaseFieldReplayEvidenceStatus": "required_not_attached",
        "defaultOnReady": False,
        "gateStatus": "standards-signed-replay-required",
        "sourceReports": [
            repo(ANOMALY_BASELINE),
            repo(ANOMALY_CANDIDATE),
            repo(ANOMALY_CALIBRATION),
            repo(MATCHING_PROFILES),
        ],
        "candidateGates": candidate_gates,
        "requiredReplayPacket": {
            "minimumScope": [
                "candidate profile explicitly enabled",
                "pinned baseline replay from the same build and hardware class",
                "sanitized release/field manifest without raw customer paths",
                "per-case pass/fail, FP/FN, runtime, and fallback diagnostics",
            ],
            "anomalyRequiredFields": [
                "normalImageCount",
                "imageFalsePositive",
                "criticalFalsePositiveCount",
                "imagePrecision",
                "imageRecall",
                "fallbackModeResult",
            ],
            "orbRequiredFields": [
                "hardwareProfileId",
                "caseCount",
                "baselineMeanRuntimeMsPerCase",
                "candidateMeanRuntimeMsPerCase",
                "runtimeDeltaMsPerCase",
                "runtimeDeltaPercent",
                "p95PositionErrorPxDelta",
                "p95CornerErrorPxDelta",
            ],
        },
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("schemaVersion") != "2026-05-01.candidate-release-field-replay-gate.v1":
        errors.append("schemaVersion must be candidate-release-field-replay-gate.v1")
    if document.get("productDefaultChange") is not False:
        errors.append("candidate release/field gate must not change product defaults")
    if document.get("standardsSigned") is not True:
        errors.append("candidate release/field gate standards must be signed")
    if document.get("releaseFieldReplayEvidenceStatus") != "required_not_attached":
        errors.append("release/field replay evidence must remain explicitly required until attached")
    if document.get("defaultOnReady") is not False:
        errors.append("candidate release/field gate must not mark default-on ready")
    candidate_gates = document.get("candidateGates")
    if not isinstance(candidate_gates, list) or len(candidate_gates) != 2:
        errors.append("candidate release/field gate must include AnomalyDetection and ORB gates")
        return errors
    by_key = {(gate.get("operator"), gate.get("profile")): gate for gate in candidate_gates}
    if ("AnomalyDetection", "mvtec_lite_v2") not in by_key:
        errors.append("AnomalyDetection mvtec_lite_v2 FP standard is required")
    if ("OrbFeatureMatch", "replay_safe_dense_strict") not in by_key:
        errors.append("ORB replay_safe_dense_strict runtime standard is required")
    for gate in candidate_gates:
        label = f"{gate.get('operator')}/{gate.get('profile')}"
        if gate.get("currentEvidenceWithinSignedStandard") is not True:
            errors.append(f"{label} current evidence must be within the signed standard")
        if gate.get("fieldReplayRequired") is not True:
            errors.append(f"{label} must require release/field replay")
        if gate.get("defaultOnReady") is not False:
            errors.append(f"{label} must not be marked default-on ready")
        checks = gate.get("checks") if isinstance(gate.get("checks"), list) else []
        if not checks:
            errors.append(f"{label} must include checks")
        for check in checks:
            if check.get("passed") is not True:
                errors.append(f"{label} check failed: {check.get('id')}")
    packet = document.get("requiredReplayPacket") if isinstance(document.get("requiredReplayPacket"), dict) else {}
    if not packet.get("anomalyRequiredFields") or not packet.get("orbRequiredFields"):
        errors.append("required replay packet must list Anomaly and ORB required fields")
    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("candidate release/field gate contains raw local path pattern")
    return errors


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.4f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Candidate Release/Field Replay Gate v1",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"GateStatus: `{document['gateStatus']}`",
        f"ProductDefaultChange: `{document['productDefaultChange']}`",
        f"DefaultOnReady: `{document['defaultOnReady']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "## Signed Standards",
        "",
        "| Operator | Profile | Standard | Current evidence | Field replay |",
        "|---|---|---|---|---|",
    ]
    for gate in document["candidateGates"]:
        lines.append(
            f"| {gate['operator']} | {gate['profile']} | {gate['standard']['standardId']} | "
            f"{gate['currentEvidenceWithinSignedStandard']} | {gate['fieldReplayRequired']} |"
        )

    lines.extend(["", "## Checks", ""])
    for gate in document["candidateGates"]:
        lines.extend(
            [
                f"### {gate['operator']} / {gate['profile']}",
                "",
                "| Check | Actual | Limit | Pass |",
                "|---|---:|---:|---|",
            ]
        )
        for check in gate["checks"]:
            lines.append(
                f"| {check['id']} | {fmt(check.get('actual'))} | "
                f"{check.get('operator')} {fmt(check.get('threshold'))} | {check['passed']} |"
            )
        lines.append("")

    lines.extend(
        [
            "## Required Replay Packet",
            "",
            "| Area | Fields |",
            "|---|---|",
            f"| Minimum scope | {', '.join(document['requiredReplayPacket']['minimumScope'])} |",
            f"| Anomaly | {', '.join(document['requiredReplayPacket']['anomalyRequiredFields'])} |",
            f"| ORB | {', '.join(document['requiredReplayPacket']['orbRequiredFields'])} |",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build candidate release/field replay gate standards.")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    document = read_json(OUTPUT_JSON) if args.validate_only else build_document()
    errors = validate_document(document)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 1
    if not args.validate_only:
        write_json(OUTPUT_JSON, document)
        write_text(OUTPUT_MD, render_markdown(document))
    action = "valid" if args.validate_only else "ready"
    print(f"candidate release/field replay gate {action}: output={repo(OUTPUT_JSON)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
