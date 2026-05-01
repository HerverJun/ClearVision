from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any

import build_detection_precision_v2 as v2


OUTPUT_JSON = v2.REPORT_DIR / "QualityFlywheel_detection_precision_v3.json"
OUTPUT_MD = v2.REPORT_DIR / "QualityFlywheel_detection_precision_v3.md"
GOVERNANCE_JSON = v2.REPORT_DIR / "QualityFlywheel_candidate_profile_governance_v1.json"
RELEASE_FIELD_GATE_JSON = v2.REPORT_DIR / "QualityFlywheel_candidate_release_field_replay_gate_v1.json"


NEW_SOURCE_REPORTS = [
    v2.REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_candidate_v2.json",
    v2.REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_sweep_v2.json",
    v2.REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v2.json",
    v2.REPORT_DIR / "AnomalyDetection_mvtec_baseline.json",
    v2.REPORT_DIR / "AnomalyDetection_mvtec_candidate_v2.json",
    v2.REPORT_DIR / "QualityFlywheel_anomaly_threshold_calibration_v1.json",
    v2.REPORT_DIR / "DeepLearning_coco_image_inference_baseline.json",
    v2.REPORT_DIR / "DeepLearning_coco_real_model_baseline.json",
    v2.REPORT_DIR / "EdgeDetection_bsds500_baseline.json",
    v2.REPORT_DIR / "QualityFlywheel_edge_detection_recall_guard_sweep_v2.json",
    v2.REPORT_DIR / "AkazeFeatureMatch_hpatches_baseline.json",
    v2.REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_v5.json",
    v2.REPORT_DIR / "OrbFeatureMatch_hpatches_baseline.json",
    v2.REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_v5.json",
    v2.REPORT_DIR / "QualityFlywheel_hpatches_matching_sweep_v5.json",
    v2.REPORT_DIR / "QualityFlywheel_matching_default_off_profiles_v3.json",
    v2.REPORT_DIR / "QualityFlywheel_hpatches_matching_family_leaderboard.json",
    v2.REPORT_DIR / "CameraCalibration_opencv_samples_baseline.json",
    RELEASE_FIELD_GATE_JSON,
    GOVERNANCE_JSON,
]

PLAN_DATASET_SCOPE = [
    "quality/public_datasets/hpatches",
    "quality/public_datasets/coco2017",
    "quality/public_datasets/kolektorsdd2",
    "quality/public_datasets/mvtec_ad_lite",
    "quality/public_datasets/bsds500",
    "quality/public_datasets/opencv_calibration_samples",
]


def optional_repo(path: Path) -> str | None:
    return v2.repo(path) if path.exists() else None


def build_document() -> dict[str, Any]:
    document = v2.build_document()
    document["schemaVersion"] = "2026-05-01.detection-precision.v3"
    document["claimBoundary"] = (
        "Public benchmark and replay evidence only; no report in this flywheel represents real production-site sign-off."
    )
    document["planDate"] = "2026-05-01"
    document["datasetScope"] = PLAN_DATASET_SCOPE
    document["scopePolicy"] = {
        "includedOnly": True,
        "excludedThisRound": [
            "quality/public_datasets/mvtec_ad_full",
            "quality/public_datasets/uded",
            "MVTec LOCO AD",
            "MVTec AD2",
            "BIPEDv2",
        ],
        "reason": "This v3 report implements the 2026-05-01 existing-public-dataset tuning plan and does not use additional public datasets for claims.",
    }
    document["sourceReports"] = [
        *document.get("sourceReports", []),
        *[value for value in (optional_repo(path) for path in NEW_SOURCE_REPORTS) if value],
    ]
    document["capabilityBackfillPlan"] = {
        "surfaceDefectDetection": {
            "candidateVersion": "v2",
            "targetTaxonomy": [
                "texture_noise_false_positive",
                "low_contrast_defect_miss",
                "undersegmentation_false_negative",
            ],
            "candidateParameters": {
                "NormalizationMode": "ClaheLocalMean",
                "ComponentFilterMode": "ResponseStats",
                "ClaheClipLimit": 2.0,
                "ClaheTileGridSize": 8,
            },
            "promotionGate": "KSDD2 v2 tuning is restricted to the target taxonomy and must not lower the global manual threshold below baseline.",
        },
        "anomalyDetection": {
            "dataset": "mvtec_ad_lite",
            "candidateProfile": "mvtec_lite_v2",
            "candidateDefault": "off",
            "profileParameters": {
                "PatchSize": 16,
                "PatchStride": 8,
                "CoresetRatio": 0.02,
                "Threshold": 0.10,
                "FeatureExtractorId": "lab_gradient_stats",
            },
            "featureExtractorId": "lab_gradient_stats",
            "promotionGate": "Lite advisory only: mvtec_lite_v2 remains default-off. Release/field replay must meet the signed FP standard before default-on.",
        },
        "deepLearning": {
            "modelId": "coco_yolo_external_onnx",
            "artifactPolicy": "Real ONNX model files stay outside git; the current no-model path is contract/postprocess smoke only.",
            "promotionGate": "When a real model is provided: 500-case COCO AP50 >= 0.45, Precision@50 >= 0.45, Recall@50 >= 0.35, AnnotationSeeded=false.",
        },
        "edgeDetection": {
            "cannyCandidate": "RecallGuardPercentile",
            "onnxCandidate": "Method=OnnxEdge with EdgeModelPath/Id default-off.",
            "promotionGate": "Promotion paused. Next profile must keep boundary recall and consensus boundary recall not lower than canny_l2_50_150; F1 alone is insufficient.",
        },
        "matchingPlanarTemplate": {
            "dataset": "hpatches",
            "sourceReport": "quality/evals/reports/QualityFlywheel_matching_default_off_profiles_v3.json",
            "optionalProfiles": {
                "AkazeFeatureMatch": "default_v3",
                "OrbFeatureMatch": "replay_safe_dense_strict",
            },
            "promotionGate": "Overall pass rate must not fall below baseline; P95 corner error must not worsen; ORB must meet the signed runtime budget before default-on.",
        },
        "cameraCalibration": {
            "dataset": "opencv_calibration_samples",
            "promotionGate": "Board detection failures must not increase and reprojection error must not rise versus baseline.",
        },
    }
    if GOVERNANCE_JSON.exists():
        governance = v2.read_json(GOVERNANCE_JSON)
        document["candidateProfileGovernance"] = {
            "sourceReport": v2.repo(GOVERNANCE_JSON),
            "statusCounts": governance.get("statusCounts", {}),
            "mainlineValidationPolicy": governance.get("mainlineValidationPolicy"),
        }
    if RELEASE_FIELD_GATE_JSON.exists():
        release_gate = v2.read_json(RELEASE_FIELD_GATE_JSON)
        document["releaseFieldReplayGate"] = {
            "sourceReport": v2.repo(RELEASE_FIELD_GATE_JSON),
            "gateStatus": release_gate.get("gateStatus"),
            "standardsSigned": release_gate.get("standardsSigned"),
            "currentPublicEvidenceWithinSignedStandards": release_gate.get("currentPublicEvidenceWithinSignedStandards"),
            "releaseFieldReplayEvidenceStatus": release_gate.get("releaseFieldReplayEvidenceStatus"),
            "defaultOnReady": release_gate.get("defaultOnReady"),
        }
    document["gates"]["productDefaultChange"] = False
    document["gates"]["allNewAlgorithmsDefaultOff"] = True
    document["gates"]["noRawDatasetOrModelArtifactsInGit"] = True
    return document


def validate_document(document: dict[str, Any]) -> list[str]:
    errors = v2.validate_document(document)
    if document.get("schemaVersion") != "2026-05-01.detection-precision.v3":
        errors.append("schemaVersion must be v3")
    if document.get("gates", {}).get("productDefaultChange") is not False:
        errors.append("v3 must not change product defaults")
    if "real production-site sign-off" not in document.get("claimBoundary", ""):
        errors.append("claim boundary must explicitly reject real production-site sign-off")
    if document.get("datasetScope") != PLAN_DATASET_SCOPE:
        errors.append("dataset scope must match the 2026-05-01 six-dataset plan")
    release_gate = document.get("releaseFieldReplayGate") if isinstance(document.get("releaseFieldReplayGate"), dict) else {}
    if release_gate.get("gateStatus") != "standards-signed-replay-required":
        errors.append("release/field replay gate must be standards-signed and replay-required")
    if release_gate.get("defaultOnReady") is not False:
        errors.append("release/field replay gate must not mark default-on ready")
    return errors


def render_markdown(document: dict[str, Any]) -> str:
    body = v2.render_markdown(document).replace(
        "# Quality Flywheel Detection Precision v2",
        "# Quality Flywheel Detection Precision v3",
        1,
    )
    lines = body.rstrip().splitlines()
    lines.extend(
        [
            "",
            "## 2026-05-01 Dataset Scope",
            "",
            "| Dataset | Status |",
            "|---|---|",
            *[f"| `{dataset}` | included |" for dataset in document.get("datasetScope", [])],
            "",
            "## Default-Off Policy",
            "",
            "- Product defaults are unchanged.",
            "- Candidate profiles remain default-off/advisory until their per-dataset gates pass.",
            "- Anomaly FP and ORB runtime budgets are signed standards, but release/field replay evidence is still required.",
            "- Raw datasets and external model artifacts are not evidence files for git.",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build QualityFlywheel_detection_precision_v3 from public benchmark reports.")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    document = v2.read_json(OUTPUT_JSON) if args.validate_only else build_document()
    errors = validate_document(document)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 1

    if not args.validate_only:
        v2.write_json(OUTPUT_JSON, document)
        v2.write_text(OUTPUT_MD, render_markdown(document))

    print(f"QualityFlywheel detection precision v3 ready: output={v2.repo(OUTPUT_JSON)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
