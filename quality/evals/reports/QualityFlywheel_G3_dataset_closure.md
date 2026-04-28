# Quality Flywheel G3 Dataset Closure

GeneratedAtUtc: `2026-04-29T00:00:00Z`

## Summary

- Frozen operators closed: 20/20
- Tier A operators: 5
- Tier B operators: 15
- Dataset/protocol cases counted: 793
- Failed cases: 0
- Closure rule: every frozen operator has a manifest, source baseline, metric threshold, and failure/boundary taxonomy.

## Operator Evidence

| # | Operator | Tier | Dataset Mode | Cases | Failed | Manifest | Source Baseline | Primary Gate |
|---:|---|---|---|---:|---:|---|---|---|
| 1 | TemplateMatching | A | public-bridge | 24 | 0 | `quality/datasets/manifests/TemplateMatching_public_bridge_manifest.json` | `quality/evals/reports/TemplateMatching_public_bridge_baseline.json` | HomographyPassRate |
| 2 | AnomalyDetection | A | public-dataset | 120 | 0 | `quality/datasets/manifests/AnomalyDetection_mvtec_lite_manifest.json` | `quality/evals/reports/AnomalyDetection_mvtec_baseline.json` | ImageAuroc |
| 3 | DeepLearning | A | coco-style-detection-protocol-bridge | 36 | 0 | `quality/datasets/manifests/DeepLearning_detection_dataset_manifest.json` | `quality/evals/reports/DeepLearning_detection_dataset_baseline.json` | AP50 |
| 4 | SemanticSegmentation | A | voc-style-segmentation-protocol-bridge | 36 | 0 | `quality/datasets/manifests/SemanticSegmentation_dataset_manifest.json` | `quality/evals/reports/SemanticSegmentation_dataset_baseline.json` | MeanIoU |
| 5 | EdgeDetection | A | bsds-style-edge-benchmark-protocol-bridge | 36 | 0 | `quality/datasets/manifests/EdgeDetection_dataset_manifest.json` | `quality/evals/reports/EdgeDetection_dataset_baseline.json` | BoundaryF1 |
| 6 | ShapeMatching | B | semi-synthetic-geometric-scenes | 36 | 0 | `quality/datasets/manifests/ShapeMatching_dataset_manifest.json` | `quality/evals/reports/ShapeMatching_dataset_baseline.json` | F1 |
| 7 | GradientShapeMatch | B | semi-synthetic-template-scenes | 117 | 0 | `quality/datasets/manifests/GradientShapeMatch_dataset_manifest.json` | `quality/evals/reports/GradientShapeMatch_baseline.json` | PassRate |
| 8 | PyramidShapeMatch | B | semi-synthetic-multiscale-scenes | 24 | 0 | `quality/datasets/manifests/PyramidShapeMatch_dataset_manifest.json` | `quality/evals/reports/PyramidShapeMatch_contract_baseline.json` | PassRate |
| 9 | AkazeFeatureMatch | B | semi-synthetic-feature-scenes | 22 | 0 | `quality/datasets/manifests/AkazeFeatureMatch_dataset_manifest.json` | `quality/evals/reports/FeatureMatch_contract_baseline.json` | PassRate |
| 10 | OrbFeatureMatch | B | semi-synthetic-feature-scenes | 22 | 0 | `quality/datasets/manifests/OrbFeatureMatch_dataset_manifest.json` | `quality/evals/reports/FeatureMatch_contract_baseline.json` | PassRate |
| 11 | PlanarMatching | B | semi-synthetic-homography-scenes | 24 | 0 | `quality/datasets/manifests/PlanarMatching_dataset_manifest.json` | `quality/evals/reports/P2MatchingResidual_baseline.json` | PassRate |
| 12 | LocalDeformableMatching | B | semi-synthetic-deformation-scenes | 24 | 0 | `quality/datasets/manifests/LocalDeformableMatching_dataset_manifest.json` | `quality/evals/reports/P2MatchingResidual_baseline.json` | PassRate |
| 13 | CaliperTool | B | semi-synthetic-edge-caliper-scenes | 117 | 0 | `quality/datasets/manifests/CaliperTool_dataset_manifest.json` | `quality/evals/reports/CaliperTool_baseline.json` | WidthErrorPx |
| 14 | ArcCaliper | B | semi-synthetic-arc-edge-scenes | 31 | 0 | `quality/datasets/manifests/ArcCaliper_dataset_manifest.json` | `quality/evals/reports/ArcCaliper_baseline.json` | PassRate |
| 15 | ContourDetection | B | semi-synthetic-contour-scenes | 20 | 0 | `quality/datasets/manifests/ContourDetection_dataset_manifest.json` | `quality/evals/reports/G2P3VisionCore_baseline.json` | PassRate |
| 16 | BlobAnalysis | B | semi-synthetic-blob-scenes | 20 | 0 | `quality/datasets/manifests/BlobAnalysis_dataset_manifest.json` | `quality/evals/reports/G2P3VisionCore_baseline.json` | PassRate |
| 17 | LineMeasurement | B | semi-synthetic-line-metrology-scenes | 20 | 0 | `quality/datasets/manifests/LineMeasurement_dataset_manifest.json` | `quality/evals/reports/G2P3VisionCore_baseline.json` | PassRate |
| 18 | CircleMeasurement | B | semi-synthetic-circle-metrology-scenes | 20 | 0 | `quality/datasets/manifests/CircleMeasurement_dataset_manifest.json` | `quality/evals/reports/G2P3VisionCore_baseline.json` | PassRate |
| 19 | GeometricFitting | B | semi-synthetic-fitting-scenes | 20 | 0 | `quality/datasets/manifests/GeometricFitting_dataset_manifest.json` | `quality/evals/reports/G2P3VisionCore_baseline.json` | PassRate |
| 20 | SurfaceDefectDetection | B | semi-synthetic-surface-defect-scenes | 24 | 0 | `quality/datasets/manifests/SurfaceDefectDetection_dataset_manifest.json` | `quality/evals/reports/P2InspectionResidual_baseline.json` | PassRate |

## Failure And Boundary Index

| Operator | Boundaries |
|---|---|
| TemplateMatching | homography, rotation, scale, low-texture, negative-scene |
| AnomalyDetection | good, defective, mask-present, mask-absent |
| DeepLearning | edge-clamp, same-class-nms, different-class-overlap, negative-low-confidence |
| SemanticSegmentation | multi-class, thin-boundary, small-object, class-absent, nested-region |
| EdgeDetection | hard-step, diagonal, thin-line, low-contrast, blurred-noise, color-input |
| ShapeMatching | direct-pose, rotated-pose, scaled-pose, multi-target, blank-negative |
| GradientShapeMatch | translation, rotation, low-contrast, strong-background, partial-occlusion |
| PyramidShapeMatch | template-mode, shape-descriptor-mode, max-matches, blank-scene, invalid-template |
| AkazeFeatureMatch | template-path, translation, scale, rotation, symmetry, low-feature |
| OrbFeatureMatch | template-path, translation, scale, rotation, symmetry, low-feature |
| PlanarMatching | planar-homography, missing-template, low-match, invalid-threshold |
| LocalDeformableMatching | local-warp, pyramid-validation, occlusion, deformation-limit |
| CaliperTool | horizontal, vertical, blurred-edge, strong-noise, wrong-polarity |
| ArcCaliper | positive-polarity, negative-polarity, wraparound, zero-span, low-texture |
| ContourDetection | single-contour, nested-contour, touching-contour, noise, empty |
| BlobAnalysis | small-blob, large-blob, touching-blob, hole, empty |
| LineMeasurement | horizontal, vertical, diagonal, short-line, no-line |
| CircleMeasurement | single-circle, partial-circle, small-radius, large-radius, no-circle |
| GeometricFitting | line-fit, circle-fit, rectangle-fit, outliers, degenerate |
| SurfaceDefectDetection | scratch, spot, low-contrast, reference-diff, clean-negative |

## Notes

- Tier A rows point to public or public-bridge protocol evidence already routed through the heavy dataset suite.
- Tier B rows promote fixed semi-synthetic protocol baselines into dataset-tier evidence by adding manifests, metric thresholds, and failure boundary taxonomy.
- This file is intentionally compact; detailed per-case evidence remains in each source baseline.
