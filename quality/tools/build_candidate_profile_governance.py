from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_candidate_profile_governance_v1.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_candidate_profile_governance_v1.md"

RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")


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


def optional_summary(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    return read_json(path).get("Summary", {})


def delta(new: float | None, old: float | None) -> float | None:
    if new is None or old is None:
        return None
    return round(float(new) - float(old), 6)


def build_document() -> dict[str, Any]:
    surface_path = REPORT_DIR / "QualityFlywheel_surface_defect_algorithm_improvement_v2.json"
    edge_path = REPORT_DIR / "QualityFlywheel_edge_detection_recall_guard_sweep_v2.json"
    anomaly_base_path = REPORT_DIR / "AnomalyDetection_mvtec_baseline.json"
    anomaly_candidate_path = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v2.json"
    anomaly_calibration_path = REPORT_DIR / "QualityFlywheel_anomaly_threshold_calibration_v1.json"
    matching_path = REPORT_DIR / "QualityFlywheel_hpatches_matching_sweep_v5.json"
    matching_profile_path = REPORT_DIR / "QualityFlywheel_matching_default_off_profiles_v3.json"
    release_field_gate_path = REPORT_DIR / "QualityFlywheel_candidate_release_field_replay_gate_v1.json"
    deep_learning_real_model_path = REPORT_DIR / "DeepLearning_coco_real_model_candidate_v1.json"
    calibration_path = REPORT_DIR / "CameraCalibration_opencv_samples_baseline.json"

    surface = read_json(surface_path) if surface_path.exists() else {}
    edge = read_json(edge_path) if edge_path.exists() else {}
    anomaly_base = optional_summary(anomaly_base_path)
    anomaly_candidate = optional_summary(anomaly_candidate_path)
    matching = read_json(matching_path) if matching_path.exists() else {}
    matching_profiles = read_json(matching_profile_path) if matching_profile_path.exists() else {}
    release_field_gate = read_json(release_field_gate_path) if release_field_gate_path.exists() else {}
    release_field_gate_rows = {
        (row.get("operator"), row.get("profile")): row
        for row in release_field_gate.get("candidateGates", [])
        if isinstance(row, dict)
    }
    matching_profile_rows = {
        (row.get("operator"), row.get("profile")): row
        for row in matching_profiles.get("rows", [])
        if isinstance(row, dict)
    }

    entries: list[dict[str, Any]] = [
        {
            "operator": "AnomalyDetection",
            "profile": "mvtec_lite_v2",
            "status": "default_off_candidate_ready_with_fp_tradeoff",
            "defaultOff": True,
            "productDefaultChange": False,
            "dataset": "quality/public_datasets/mvtec_ad_lite",
            "evidence": [
                repo(path)
                for path in (anomaly_base_path, anomaly_candidate_path, anomaly_calibration_path, release_field_gate_path)
                if path.exists()
            ],
            "profileParameters": {
                "EvidenceProfileName": anomaly_candidate.get("ProfileName"),
                "MaxSide": anomaly_candidate.get("MaxSide"),
                "PatchSize": anomaly_candidate.get("PatchSize"),
                "PatchStride": anomaly_candidate.get("PatchStride"),
                "CoresetRatio": anomaly_candidate.get("CoresetRatio"),
                "Threshold": anomaly_candidate.get("Threshold"),
                "FeatureExtractorId": anomaly_candidate.get("FeatureExtractorId"),
            },
            "metrics": {
                "imageAuroc": anomaly_candidate.get("ImageAuroc"),
                "imageAurocDelta": delta(anomaly_candidate.get("ImageAuroc"), anomaly_base.get("ImageAuroc")),
                "pixelAuroc": anomaly_candidate.get("PixelAuroc"),
                "pixelAurocDelta": delta(anomaly_candidate.get("PixelAuroc"), anomaly_base.get("PixelAuroc")),
                "imageF1": anomaly_candidate.get("ImageF1"),
                "imageF1Delta": delta(anomaly_candidate.get("ImageF1"), anomaly_base.get("ImageF1")),
                "imageRecall": anomaly_candidate.get("ImageRecall"),
                "imageRecallDelta": delta(anomaly_candidate.get("ImageRecall"), anomaly_base.get("ImageRecall")),
                "imagePrecision": anomaly_candidate.get("ImagePrecision"),
                "imagePrecisionDelta": delta(anomaly_candidate.get("ImagePrecision"), anomaly_base.get("ImagePrecision")),
                "falsePositiveDelta": delta(anomaly_candidate.get("ImageFalsePositive"), anomaly_base.get("ImageFalsePositive")),
            },
            "releaseFieldReplayGate": {
                "sourceReport": repo(release_field_gate_path) if release_field_gate_path.exists() else None,
                "status": release_field_gate.get("gateStatus"),
                "standardId": release_field_gate_rows.get(("AnomalyDetection", "mvtec_lite_v2"), {}).get("standard", {}).get("standardId"),
                "currentEvidenceWithinSignedStandard": release_field_gate_rows.get(("AnomalyDetection", "mvtec_lite_v2"), {}).get("currentEvidenceWithinSignedStandard"),
                "defaultOnReady": release_field_gate_rows.get(("AnomalyDetection", "mvtec_lite_v2"), {}).get("defaultOnReady"),
            },
            "promotionGate": "Default-off candidate only. Promotion requires release/field replay to meet the signed FP standard and compatible feature-bank fallback/Fail behavior.",
            "blockers": [
                "MVTec lite is advisory only; not MVTec AD full sign-off.",
                "Signed FP standard accepts at most +3 FP delta, <=10% normal FPR, and image precision >=0.95; release/field replay packet is still required.",
                "MaxSide=192 is runner/evidence preprocessing; product profile enforces PatchSize/PatchStride/Coreset/Threshold but not MaxSide.",
            ],
        },
        {
            "operator": "SurfaceDefectDetection",
            "profile": "taxonomy_v2",
            "status": surface.get("status", "missing_evidence"),
            "defaultOff": True,
            "productDefaultChange": False,
            "dataset": "quality/public_datasets/kolektorsdd2",
            "evidence": [repo(surface_path)],
            "targetTaxonomy": surface.get("targetTaxonomy", []),
            "metrics": {
                "candidateProfile": surface.get("candidate", {}).get("profile"),
                "pixelF1": surface.get("candidate", {}).get("pixelF1"),
                "imageAuroc": surface.get("candidate", {}).get("imageAuroc"),
                "targetTaxonomyCaseCount": surface.get("candidate", {}).get("targetTaxonomyCaseCount"),
            },
            "promotionGate": "Target taxonomy cases must drop while PixelF1/ImageAUROC do not regress; global threshold lowering is forbidden.",
            "blockers": [] if surface.get("accepted") is True else ["No targeted taxonomy improvement has beaten baseline under the no-global-threshold-lowering policy."],
        },
        {
            "operator": "EdgeDetection",
            "profile": "recall_not_lower_v2",
            "status": "paused",
            "defaultOff": True,
            "productDefaultChange": False,
            "dataset": "quality/public_datasets/bsds500",
            "evidence": [repo(edge_path)],
            "metrics": {
                "decision": edge.get("decision"),
                "selectedProfile": edge.get("selectedProfile"),
                "recallSafeNextRoundProfiles": edge.get("recallSafeNextRoundProfiles", []),
            },
            "promotionGate": "Boundary recall and consensus boundary recall must not drop versus canny_l2_50_150; F1 alone is insufficient.",
            "blockers": ["No recall-safe profile is currently selected."],
        },
        {
            "operator": "DeepLearning",
            "profile": "coco_yolo_external_onnx",
            "status": "blocked_external_model",
            "defaultOff": True,
            "productDefaultChange": False,
            "dataset": "quality/public_datasets/coco2017",
            "evidence": [repo(deep_learning_real_model_path)] if deep_learning_real_model_path.exists() else [],
            "metrics": {},
            "promotionGate": "Real ONNX candidate must provide AnnotationSeeded=false, model artifact manifest, AP50 >= 0.45, Precision@50 >= 0.45, Recall@50 >= 0.35.",
            "blockers": ["Real ONNX model artifact is not present; mainline validation treats this as blocked, not failed."],
        },
        {
            "operator": "CameraCalibration",
            "profile": "opencv_samples_smoke",
            "status": "stable_smoke",
            "defaultOff": False,
            "productDefaultChange": False,
            "dataset": "quality/public_datasets/opencv_calibration_samples",
            "evidence": [repo(calibration_path)] if calibration_path.exists() else [],
            "metrics": optional_summary(calibration_path),
            "promotionGate": "Keep geometry smoke green; do not treat OpenCV samples as production calibration sign-off.",
            "blockers": [],
        },
    ]

    for result in matching.get("results", []):
        profile_key = (result.get("operator"), result.get("selectedProfile"))
        profile_row = matching_profile_rows.get(profile_key, {})
        release_gate_row = release_field_gate_rows.get(profile_key, {})
        profile_candidate = profile_row.get("candidate", {}) if isinstance(profile_row.get("candidate"), dict) else {}
        profile_deltas = profile_row.get("deltas", {}) if isinstance(profile_row.get("deltas"), dict) else {}
        evidence = [
            value
            for value in [
                result.get("candidateJson"),
                repo(matching_profile_path) if matching_profile_path.exists() else None,
                repo(release_field_gate_path) if release_gate_row and release_field_gate_path.exists() else None,
            ]
            if value
        ]
        entries.append(
            {
                "operator": result.get("operator"),
                "profile": result.get("selectedProfile"),
                "status": profile_row.get("status", "optional_profile_ready"),
                "defaultOff": True,
                "productDefaultChange": False,
                "dataset": "quality/public_datasets/hpatches",
                "evidence": evidence,
                "metrics": {
                    "fullPassRate": profile_candidate.get("passRate"),
                    "fullPassDelta": profile_deltas.get("fullPassDelta"),
                    "p95PositionErrorPxDelta": profile_deltas.get("p95PositionErrorPxDelta"),
                    "p95CornerErrorPxDelta": profile_deltas.get("p95CornerErrorPxDelta"),
                    "runtimeMsDelta": profile_deltas.get("runtimeMsDelta"),
                    "hasAccuracyGain": profile_row.get("hasAccuracyGain"),
                    "validationPassRate": result.get("validation", {}).get("passRate"),
                    "replayPassRate": result.get("replay", {}).get("passRate"),
                    "holdoutPassRate": result.get("holdout", {}).get("passRate"),
                    "replayP95PositionErrorPx": result.get("replay", {}).get("p95PositionErrorPx"),
                },
                "profileParameters": profile_candidate.get("parameters", {}),
                "releaseFieldReplayGate": {
                    "sourceReport": repo(release_field_gate_path) if release_gate_row and release_field_gate_path.exists() else None,
                    "status": release_field_gate.get("gateStatus") if release_gate_row else None,
                    "standardId": release_gate_row.get("standard", {}).get("standardId") if release_gate_row else None,
                    "currentEvidenceWithinSignedStandard": release_gate_row.get("currentEvidenceWithinSignedStandard") if release_gate_row else None,
                    "defaultOnReady": release_gate_row.get("defaultOnReady") if release_gate_row else None,
                },
                "promotionGate": profile_row.get(
                    "promotionGate",
                    "Replay regressions must remain zero; profile stays opt-in until broader HPatches gate is accepted.",
                ),
                "blockers": profile_row.get("blockers", []),
            }
        )

    statuses: dict[str, int] = {}
    for entry in entries:
        statuses[entry["status"]] = statuses.get(entry["status"], 0) + 1

    return {
        "schemaVersion": "2026-05-01.candidate-profile-governance.v1",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "Candidate profile governance over current public benchmark evidence only; not production sign-off.",
        "productDefaultChange": False,
        "mainlineValidationPolicy": "Default-off/advisory/paused profiles may pass governance; blocked external model work is tracked separately from mainline algorithm validation.",
        "releaseFieldReplayGate": {
            "sourceReport": repo(release_field_gate_path) if release_field_gate_path.exists() else None,
            "gateStatus": release_field_gate.get("gateStatus"),
            "standardsSigned": release_field_gate.get("standardsSigned"),
            "currentPublicEvidenceWithinSignedStandards": release_field_gate.get("currentPublicEvidenceWithinSignedStandards"),
            "releaseFieldReplayEvidenceStatus": release_field_gate.get("releaseFieldReplayEvidenceStatus"),
            "defaultOnReady": release_field_gate.get("defaultOnReady"),
        },
        "statusCounts": statuses,
        "entries": entries,
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("schemaVersion") != "2026-05-01.candidate-profile-governance.v1":
        errors.append("schemaVersion must be candidate-profile-governance.v1")
    if document.get("productDefaultChange") is not False:
        errors.append("candidate profile governance must not change product defaults")
    entries = document.get("entries", [])
    if not isinstance(entries, list) or len(entries) < 6:
        errors.append("candidate profile governance must include the current major candidate families")
        return errors
    for entry in entries:
        if not entry.get("operator") or not entry.get("profile") or not entry.get("status"):
            errors.append("every candidate profile entry must include operator, profile, and status")
        if entry.get("productDefaultChange") is not False:
            errors.append(f"{entry.get('operator')}:{entry.get('profile')} changes product defaults")
    if not any(entry.get("operator") == "DeepLearning" and entry.get("status") == "blocked_external_model" for entry in entries):
        errors.append("DeepLearning real-model blocker must be explicit")
    if not any(entry.get("operator") == "EdgeDetection" and entry.get("status") == "paused" for entry in entries):
        errors.append("EdgeDetection must remain paused until recall-not-lower gate passes")
    release_gate = document.get("releaseFieldReplayGate") if isinstance(document.get("releaseFieldReplayGate"), dict) else {}
    if release_gate.get("gateStatus") != "standards-signed-replay-required":
        errors.append("release/field replay gate standards must be signed and replay-required")
    if release_gate.get("defaultOnReady") is not False:
        errors.append("release/field replay gate must not mark candidate profiles default-on ready")
    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("candidate profile governance contains raw path pattern")
    return errors


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Candidate Profile Governance v1",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"ProductDefaultChange: `{document['productDefaultChange']}`",
        f"Policy: {document['mainlineValidationPolicy']}",
        f"ReleaseFieldReplayGate: `{document.get('releaseFieldReplayGate', {}).get('gateStatus')}`",
        "",
        "| Operator | Profile | Status | Default off | Dataset | Blockers |",
        "|---|---|---|---|---|---|",
    ]
    for entry in document["entries"]:
        blockers = "; ".join(entry.get("blockers") or [])
        lines.append(
            f"| {entry['operator']} | {entry['profile']} | {entry['status']} | "
            f"{entry['defaultOff']} | `{entry['dataset']}` | {blockers or '-'} |"
        )
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build candidate profile governance report.")
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
    print(f"candidate profile governance ready: output={repo(OUTPUT_JSON)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
