# Quality Flywheel G3 Dataset-Tier Registry

GeneratedAtUtc: `2026-04-27T09:55:00Z`

This freezes the G3 candidate list and records the first promoted existing dataset evidence. It does not start new dataset scoring and does not move G1/G2 gates.

## Tier Definitions

| Tier | Definition |
|---|---|
| A | Public or licensed dataset evidence: fixed source/version, checksum or citation, deterministic split, metrics, and reproducible runner. |
| B | Semi-synthetic dataset evidence: generated or transformed samples with fixed seed/recipe, boundary cases, metrics, and manifest. |
| C | Dataset-adjacent smoke evidence: small curated fixture pack or field-substitute set with manifest and failure taxonomy; not counted as full dataset evidence until promoted. |

## Frozen 20

| # | Operator | Tier | Dataset Mode | Existing Seed |
|---:|---|---|---|---|
| 1 | TemplateMatching | A | public-bridge | `quality/datasets/manifests/TemplateMatching_public_bridge_manifest.json` |
| 2 | AnomalyDetection | A | public-dataset | `quality/datasets/manifests/AnomalyDetection_mvtec_lite_manifest.json` |
| 3 | DeepLearning | A | coco-style-detection-protocol-bridge | `quality/datasets/manifests/DeepLearning_detection_dataset_manifest.json` |
| 4 | SemanticSegmentation | A | voc-style-segmentation-protocol-bridge | `quality/datasets/manifests/SemanticSegmentation_dataset_manifest.json` |
| 5 | EdgeDetection | A | bsds-style-edge-benchmark-protocol-bridge | `quality/datasets/manifests/EdgeDetection_dataset_manifest.json` |
| 6 | ShapeMatching | B | semi-synthetic-geometric-scenes | `quality/datasets/manifests/ShapeMatching_dataset_manifest.json` |
| 7 | GradientShapeMatch | B | semi-synthetic-template-scenes | - |
| 8 | PyramidShapeMatch | B | semi-synthetic-multiscale-scenes | - |
| 9 | AkazeFeatureMatch | B | semi-synthetic-feature-scenes | - |
| 10 | OrbFeatureMatch | B | semi-synthetic-feature-scenes | - |
| 11 | PlanarMatching | B | semi-synthetic-homography-scenes | - |
| 12 | LocalDeformableMatching | B | semi-synthetic-deformation-scenes | - |
| 13 | CaliperTool | B | semi-synthetic-edge-caliper-scenes | - |
| 14 | ArcCaliper | B | semi-synthetic-arc-edge-scenes | - |
| 15 | ContourDetection | B | semi-synthetic-contour-scenes | - |
| 16 | BlobAnalysis | B | semi-synthetic-blob-scenes | - |
| 17 | LineMeasurement | B | semi-synthetic-line-metrology-scenes | - |
| 18 | CircleMeasurement | B | semi-synthetic-circle-metrology-scenes | - |
| 19 | GeometricFitting | B | semi-synthetic-fitting-scenes | - |
| 20 | SurfaceDefectDetection | B | semi-synthetic-surface-defect-scenes | - |

## Execution Policy

- Current round: promote existing dataset-kind baselines into standard manifest/report form; do not run new dataset scoring.
- Promotion gate: dataset evidence requires manifest, runner, metrics, and failure/boundary report.
- Matrix mapping: only completed dataset reports should set `EvidenceKind=dataset` in baseline JSON.
- Manifest template: `quality/datasets/QualityFlywheel_dataset_manifest_template.json`.

## Suite Routing

| Lane | Manifest |
|---|---|
| quick | `quality/evals/suites/quick_contract_suite.json` |
| golden | `quality/evals/suites/golden_core50_suite.json` |
| dataset | `quality/evals/suites/dataset_heavy_suite.json` |

## Batch Reports

- `quality/evals/reports/QualityFlywheel_G3_dataset_batch1.md`
