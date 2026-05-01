from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any

import build_detection_precision_v2 as v2


OUTPUT_JSON = v2.REPORT_DIR / "QualityFlywheel_detection_precision_v3.json"
OUTPUT_MD = v2.REPORT_DIR / "QualityFlywheel_detection_precision_v3.md"


NEW_SOURCE_REPORTS = [
    v2.REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_clahe_response_stats_candidate.json",
    v2.REPORT_DIR / "AnomalyDetection_mvtec_ad_full_onnx_embedding.json",
    v2.REPORT_DIR / "AnomalyDetection_mvtec_loco_ad_advisory.json",
    v2.REPORT_DIR / "EdgeDetection_bsds500_recall_guard_candidate.json",
    v2.REPORT_DIR / "DeepLearning_coco_real_model_candidate_500.json",
]


def optional_repo(path: Path) -> str | None:
    return v2.repo(path) if path.exists() else None


def build_document() -> dict[str, Any]:
    document = v2.build_document()
    document["schemaVersion"] = "2026-04-30.detection-precision.v3"
    document["claimBoundary"] = (
        "Public benchmark and replay evidence only; no report in this flywheel represents real production-site sign-off."
    )
    document["sourceReports"] = [
        *document.get("sourceReports", []),
        *[value for value in (optional_repo(path) for path in NEW_SOURCE_REPORTS) if value],
    ]
    document["capabilityBackfillPlan"] = {
        "surfaceDefectDetection": {
            "candidateParameters": {
                "NormalizationMode": "ClaheLocalMean",
                "ComponentFilterMode": "ResponseStats",
                "ClaheClipLimit": 2.0,
                "ClaheTileGridSize": 8,
            },
            "promotionGate": "KSDD2 PixelF1 delta >= 0.03, FP/normal <= 0.06, low-contrast misses decline, fixed-FPR recall not lower.",
        },
        "anomalyDetection": {
            "featureExtractorId": "onnx_embedding",
            "embeddingModelId": "resnet18_avgpool_embedding_external_onnx",
            "promotionGate": "MVTec AD full ImageAUROC >= 0.90, PixelAUROC >= 0.85, zero processing failures; LOCO/AD2 advisory only.",
        },
        "deepLearning": {
            "modelId": "coco_yolo_external_onnx",
            "artifactPolicy": "YOLO11n ONNX stays under quality/public_datasets/models and is never committed.",
            "promotionGate": "500-case COCO AP50 >= 0.45, Precision@50 >= 0.45, Recall@50 >= 0.35, AnnotationSeeded=false.",
        },
        "edgeDetection": {
            "cannyCandidate": "RecallGuardPercentile",
            "onnxCandidate": "Method=OnnxEdge with EdgeModelPath/Id default-off.",
            "promotionGate": "Canny recall drop <= 0.01 on BSDS replay; ONNX edge needs BSDS500 BoundaryF1 >= 0.60 and BIPED test F1 above Canny baseline.",
        },
    }
    document["gates"]["productDefaultChange"] = False
    document["gates"]["allNewAlgorithmsDefaultOff"] = True
    document["gates"]["noRawDatasetOrModelArtifactsInGit"] = True
    return document


def validate_document(document: dict[str, Any]) -> list[str]:
    errors = v2.validate_document(document)
    if document.get("schemaVersion") != "2026-04-30.detection-precision.v3":
        errors.append("schemaVersion must be v3")
    if document.get("gates", {}).get("productDefaultChange") is not False:
        errors.append("v3 must not change product defaults")
    if "real production-site sign-off" not in document.get("claimBoundary", ""):
        errors.append("claim boundary must explicitly reject real production-site sign-off")
    return errors


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
        v2.write_text(OUTPUT_MD, v2.render_markdown(document))

    print(f"QualityFlywheel detection precision v3 ready: output={v2.repo(OUTPUT_JSON)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
