# Quality Flywheel G3 Dataset Batch 1

GeneratedAtUtc: `2026-04-27T12:08:00Z`

## Scope

This report starts the long-running G3 lane and records the first promoted dataset evidence. It promotes the two operators that already had dataset-kind baselines into the standard manifest/report shape, then tracks follow-up Tier A protocol bridges as they are completed.

## Promoted

| Operator | Tier | Manifest | Baseline | Report | Status |
|---|---|---|---|---|---|
| TemplateMatching | A | `quality/datasets/manifests/TemplateMatching_public_bridge_manifest.json` | `quality/evals/reports/TemplateMatching_public_bridge_baseline.json` | `quality/evals/reports/TemplateMatching_public_bridge_baseline.md` | promoted |
| AnomalyDetection | A | `quality/datasets/manifests/AnomalyDetection_mvtec_lite_manifest.json` | `quality/evals/reports/AnomalyDetection_mvtec_baseline.json` | `quality/evals/reports/AnomalyDetection_mvtec_baseline.md` | promoted |

## Next Dataset Candidates

| Operator | Required next artifact | Notes |
|---|---|---|
| GradientShapeMatch | Tier B dataset manifest + template-scene runner | Next frozen G3 matching operator after ShapeMatching promotion. |

## Promoted In Follow-Up

| Operator | Tier | Manifest | Baseline | Report | Status |
|---|---|---|---|---|---|
| DeepLearning | A | `quality/datasets/manifests/DeepLearning_detection_dataset_manifest.json` | `quality/evals/reports/DeepLearning_detection_dataset_baseline.json` | `quality/evals/reports/DeepLearning_detection_dataset_baseline.md` | promoted |
| SemanticSegmentation | A | `quality/datasets/manifests/SemanticSegmentation_dataset_manifest.json` | `quality/evals/reports/SemanticSegmentation_dataset_baseline.json` | `quality/evals/reports/SemanticSegmentation_dataset_baseline.md` | promoted |
| EdgeDetection | A | `quality/datasets/manifests/EdgeDetection_dataset_manifest.json` | `quality/evals/reports/EdgeDetection_dataset_baseline.json` | `quality/evals/reports/EdgeDetection_dataset_baseline.md` | promoted |
| ShapeMatching | B | `quality/datasets/manifests/ShapeMatching_dataset_manifest.json` | `quality/evals/reports/ShapeMatching_dataset_baseline.json` | `quality/evals/reports/ShapeMatching_dataset_baseline.md` | promoted |

DeepLearning uses a COCO-style semi-synthetic detection protocol bridge. It records AP50, Precision@0.50, Recall@0.50, false positives, false negatives, edge-clamp behavior, same-class NMS, different-class overlap, and negative low-confidence boundaries. It is dataset-protocol evidence for the DeepLearning post-processing path, not production model accuracy evidence.

SemanticSegmentation uses a VOC-style semi-synthetic segmentation protocol bridge. It records PixelAccuracy, MeanIoU, MeanDice, MeanBoundaryIoU, per-class mask accounting, palette stability, class-absent behavior, small-object behavior, and thin-boundary behavior. It is dataset-protocol evidence for the SemanticSegmentation preprocessing, mask, and visualization paths, not production model accuracy evidence.

EdgeDetection uses a BSDS-style semi-synthetic edge benchmark protocol bridge. It records Precision, Recall, F1, one-pixel tolerance BoundaryF1, false-positive pixels, false-negative pixels, auto-threshold behavior, Gaussian-prefilter behavior, thin-line behavior, low-contrast behavior, and color-input conversion. It is dataset-protocol evidence for the EdgeDetection Canny path, not field-image accuracy evidence.

ShapeMatching uses a fixed semi-synthetic geometric-scene protocol. It records Precision, Recall, F1, mean position error, mean angle error, mean scale error, score floor, multi-target behavior, reference-origin behavior, and blank-scene rejection. It is dataset-protocol evidence for the ShapeMatching rotation-scale template path, not field-image accuracy evidence.

## Governance

- Dataset work is routed through `quality/evals/suites/dataset_heavy_suite.json`.
- Quick local checks are routed through `quality/evals/suites/quick_contract_suite.json`.
- Core synthetic/protocol baselines are routed through `quality/evals/suites/golden_core50_suite.json`.
- Planned G3 entries are visible in the heavy suite but are not runnable until they have a manifest, runner, metrics, and failure/boundary report.
