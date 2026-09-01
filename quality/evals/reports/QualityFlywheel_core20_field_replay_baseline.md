# Quality Flywheel Field Replay Drill

DrillId: `2026-04-core20-proof-v1`
GeneratedAtUtc: `2026-04-29T00:00:00Z`
Manifest: `quality/field_replay/manifests/core20_field_replay_manifest.json`

## Summary

- Operators covered: 20
- Samples replayed: 400
- Reproducible rate: 90.00%
- Regressionized rate: 72.22%
- Privacy/raw-path leaks: 0/0
- Drill passed: Yes

## Operators

| Operator | Samples | Reproducible | Regressionized | Replay Tier | Labels |
|---|---:|---:|---:|---|---|
| TemplateMatching | 20 | 18 | 13 | field-substitute | core20, proof, public-bridge |
| AnomalyDetection | 20 | 18 | 13 | field-substitute | core20, proof, public-dataset |
| DeepLearning | 20 | 18 | 13 | field-substitute | core20, proof, coco-style-detection-protocol-bridge |
| SemanticSegmentation | 20 | 18 | 13 | field-substitute | core20, proof, voc-style-segmentation-protocol-bridge |
| EdgeDetection | 20 | 18 | 13 | field-substitute | core20, proof, bsds-style-edge-benchmark-protocol-bridge |
| ShapeMatching | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-geometric-scenes |
| GradientShapeMatch | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-template-scenes |
| PyramidShapeMatch | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-multiscale-scenes |
| AkazeFeatureMatch | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-feature-scenes |
| OrbFeatureMatch | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-feature-scenes |
| PlanarMatching | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-homography-scenes |
| LocalDeformableMatching | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-deformation-scenes |
| CaliperTool | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-edge-caliper-scenes |
| ArcCaliper | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-arc-edge-scenes |
| ContourDetection | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-contour-scenes |
| BlobAnalysis | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-blob-scenes |
| LineMeasurement | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-line-metrology-scenes |
| CircleMeasurement | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-circle-metrology-scenes |
| GeometricFitting | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-fitting-scenes |
| SurfaceDefectDetection | 20 | 18 | 13 | field-substitute | core20, proof, semi-synthetic-surface-defect-scenes |

## Gate Interpretation

- P0/P1 triage SLA is represented by manifest metadata and validated during replay manifest checks.
- Samples in this seed set are anonymized field-substitute records; raw customer paths are forbidden.
- A drill can pass only when reproducible rate and regressionization rate meet the manifest policy.
