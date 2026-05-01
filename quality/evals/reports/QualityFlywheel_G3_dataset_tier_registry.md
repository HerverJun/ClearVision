# Quality Flywheel G3 Dataset-Tier Registry

GeneratedAtUtc: `2026-04-29T00:00:00Z`

This registry records the frozen G3 operator set after closure. All 20 operators now have dataset-tier evidence via Tier A public/public-bridge evidence or Tier B semi-synthetic protocol evidence.

## Tier Definitions

| Tier | Definition |
|---|---|
| A | Public or licensed dataset evidence: fixed source/version, checksum or citation, deterministic split, metrics, and reproducible runner. |
| B | Semi-synthetic dataset evidence: generated or transformed samples with fixed seed/recipe, boundary cases, metrics, and manifest. |
| C | Dataset-adjacent smoke evidence: small curated fixture pack or field-substitute set with manifest and failure taxonomy; not counted as full dataset evidence until promoted. |

## Frozen 20 Closure

| # | Operator | Tier | Dataset Mode | Manifest | Status |
|---:|---|---|---|---|---|
| 1 | TemplateMatching | A | public-bridge | `quality/datasets/manifests/TemplateMatching_public_bridge_manifest.json` | promoted-closure |
| 2 | AnomalyDetection | A | public-dataset | `quality/datasets/manifests/AnomalyDetection_mvtec_lite_manifest.json` | promoted-closure |
| 3 | DeepLearning | A | coco-style-detection-protocol-bridge | `quality/datasets/manifests/DeepLearning_detection_dataset_manifest.json` | promoted-closure |
| 4 | SemanticSegmentation | A | voc-style-segmentation-protocol-bridge | `quality/datasets/manifests/SemanticSegmentation_dataset_manifest.json` | promoted-closure |
| 5 | EdgeDetection | A | bsds-style-edge-benchmark-protocol-bridge | `quality/datasets/manifests/EdgeDetection_dataset_manifest.json` | promoted-closure |
| 6 | ShapeMatching | B | semi-synthetic-geometric-scenes | `quality/datasets/manifests/ShapeMatching_dataset_manifest.json` | promoted-closure |
| 7 | GradientShapeMatch | B | semi-synthetic-template-scenes | `quality/datasets/manifests/GradientShapeMatch_dataset_manifest.json` | promoted-closure |
| 8 | PyramidShapeMatch | B | semi-synthetic-multiscale-scenes | `quality/datasets/manifests/PyramidShapeMatch_dataset_manifest.json` | promoted-closure |
| 9 | AkazeFeatureMatch | B | semi-synthetic-feature-scenes | `quality/datasets/manifests/AkazeFeatureMatch_dataset_manifest.json` | promoted-closure |
| 10 | OrbFeatureMatch | B | semi-synthetic-feature-scenes | `quality/datasets/manifests/OrbFeatureMatch_dataset_manifest.json` | promoted-closure |
| 11 | PlanarMatching | B | semi-synthetic-homography-scenes | `quality/datasets/manifests/PlanarMatching_dataset_manifest.json` | promoted-closure |
| 12 | LocalDeformableMatching | B | semi-synthetic-deformation-scenes | `quality/datasets/manifests/LocalDeformableMatching_dataset_manifest.json` | promoted-closure |
| 13 | CaliperTool | B | semi-synthetic-edge-caliper-scenes | `quality/datasets/manifests/CaliperTool_dataset_manifest.json` | promoted-closure |
| 14 | ArcCaliper | B | semi-synthetic-arc-edge-scenes | `quality/datasets/manifests/ArcCaliper_dataset_manifest.json` | promoted-closure |
| 15 | ContourDetection | B | semi-synthetic-contour-scenes | `quality/datasets/manifests/ContourDetection_dataset_manifest.json` | promoted-closure |
| 16 | BlobAnalysis | B | semi-synthetic-blob-scenes | `quality/datasets/manifests/BlobAnalysis_dataset_manifest.json` | promoted-closure |
| 17 | LineMeasurement | B | semi-synthetic-line-metrology-scenes | `quality/datasets/manifests/LineMeasurement_dataset_manifest.json` | promoted-closure |
| 18 | CircleMeasurement | B | semi-synthetic-circle-metrology-scenes | `quality/datasets/manifests/CircleMeasurement_dataset_manifest.json` | promoted-closure |
| 19 | GeometricFitting | B | semi-synthetic-fitting-scenes | `quality/datasets/manifests/GeometricFitting_dataset_manifest.json` | promoted-closure |
| 20 | SurfaceDefectDetection | B | semi-synthetic-surface-defect-scenes | `quality/datasets/manifests/SurfaceDefectDetection_dataset_manifest.json` | promoted-closure |

## Closure Reports

- `quality/evals/reports/QualityFlywheel_G3_dataset_closure_baseline.json`
- `quality/evals/reports/QualityFlywheel_G3_dataset_closure.md`
- `quality/evals/reports/QualityFlywheel_G3_dataset_batch1.md`
