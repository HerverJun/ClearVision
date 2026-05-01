# Quality Flywheel Core20 Proof Baseline

GeneratedAtUtc: `2026-04-29T00:00:00Z`

## Summary

- Operators: 20
- Accepted proof operators: 0
- Blocked missing field data: 20
- Legacy baseline count: 20
- Field replay samples tracked: 400
- Privacy/raw-path leaks: 0/0
- Proof gate passed: No

## Operators

| Operator | Proof Status | Primary Metric | Train | Val | Test | Accepted | Industrial Status |
|---|---|---|---:|---:|---:|---|---|
| TemplateMatching | blocked-missing-field-data | HomographyPassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| AnomalyDetection | blocked-missing-field-data | ImageAuroc | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| DeepLearning | blocked-missing-field-data | AP50 | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| SemanticSegmentation | blocked-missing-field-data | MeanIoU | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| EdgeDetection | blocked-missing-field-data | BoundaryF1 | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| ShapeMatching | blocked-missing-field-data | F1 | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| GradientShapeMatch | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| PyramidShapeMatch | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| AkazeFeatureMatch | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| OrbFeatureMatch | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| PlanarMatching | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| LocalDeformableMatching | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| CaliperTool | blocked-missing-field-data | WidthErrorPx | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| ArcCaliper | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| ContourDetection | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| BlobAnalysis | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| LineMeasurement | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| CircleMeasurement | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| GeometricFitting | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |
| SurfaceDefectDetection | blocked-missing-field-data | PassRate | 0 | 0 | 0 | False | field proof pending; real industrial validation is not complete |

## Gate Interpretation

- `accepted=false` is intentional while field data is missing; this prevents legacy baselines from being silently promoted.
- Each row already has manifest, split, thresholds, failure taxonomy, privacy checks, and replay governance attached.
- Populate split case ids from approved de-identified samples, then replace blocked status with executed/promoted proof results.
