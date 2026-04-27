# Quality Flywheel G1/G2 Registry

GeneratedAtUtc: `2026-04-27T12:05:52+00:00`
SourceMatrix: `quality/evals/reports/operator_quality_matrix.md`

## Scope

Matrix evidence is split into HasContractTest, HasGoldenTest, HasDatasetEvidence, and HasFieldReplay. G1 counts any accepted evidence signal; G2 Core50 counts accepted contract/golden/dataset/field signal without drifting into new dataset runs.

## Status

- G1 current evidence signal: 155/155 operators.
- G1 remaining without signal: 0.
- G1 status: complete.
- G2 Core50 frozen: True (32 P2 + 18 P3).
- G2 current Core50 evidence signal: 50/50.
- G2 remaining Core50 without evidence signal: 0.
- G2 status: complete.
- P2 without evidence signal: 0.

## Evidence Layers

| Layer | Acceptance |
|---|---|
| contract | Happy path, missing input, parameter boundary, type/null boundary, and structured failure message. |
| golden | Behavior, geometry, protocol, or synthetic oracle with at least 20 cases, 0 failures, runtime, and memory. |
| dataset | Public dataset or licensed alternative/semi-synthetic tier with manifest, fixed version/seed, metrics, and failure boundaries. |
| field | Anonymized failure sample with manifest, minimal replay, triage labels, and regression conversion status. |

## P2 Residual Golden Plan

| Operator | Owner | Runner | Strategy |
|---|---|---|---|
| None | - | - | P2 golden residual is closed. |

## Frozen Core 50

| # | Operator | Priority | Contract | Golden | Dataset | Field | Cases | Evidence Layer | Owner |
|---:|---|---|---|---|---|---|---:|---|---|
| 1 | FFT1D | P2 | No | Yes | No | No | 117 | golden | Quality Flywheel Agent |
| 2 | FrequencyFilter | P2 | No | Yes | No | No | 117 | golden | Quality Flywheel Agent |
| 3 | InverseFFT1D | P2 | No | Yes | No | No | 117 | golden | Quality Flywheel Agent |
| 4 | RegionUnion | P2 | No | Yes | No | No | 100 | golden | Quality Flywheel Agent |
| 5 | AkazeFeatureMatch | P2 | Yes | No | No | No | 22 | contract | Quality Flywheel Agent |
| 6 | OrbFeatureMatch | P2 | Yes | No | No | No | 22 | contract | Quality Flywheel Agent |
| 7 | SemanticSegmentation | P2 | Yes | No | Yes | No | 36 | dataset | Quality Flywheel Agent |
| 8 | Undistort | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 9 | DualModalVoting | P2 | Yes | No | No | No | 31 | contract | Quality Flywheel Agent |
| 10 | CaliperTool | P2 | No | Yes | No | No | 117 | golden | Quality Flywheel Agent |
| 11 | EdgePairDefect | P2 | Yes | No | No | No | 27 | contract | Quality Flywheel Agent |
| 12 | FisheyeUndistort | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 13 | TemplateMatching | P2 | No | Yes | Yes | No | 117 | dataset+golden | Quality Flywheel Agent |
| 14 | AnomalyDetection | P2 | No | No | Yes | No | 120 | dataset | Quality Flywheel Agent |
| 15 | CalibrationLoader | P2 | No | Yes | No | No | 24 | golden | Calibration Evidence Agent |
| 16 | CameraCalibration | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 17 | CoordinateTransform | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 18 | DeepLearning | P2 | Yes | No | Yes | No | 46 | dataset | Quality Flywheel Agent |
| 19 | DetectionSequenceJudge | P2 | No | Yes | No | No | 24 | golden | AI/Rule Contract Agent |
| 20 | FisheyeCalibration | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 21 | GradientShapeMatch | P2 | No | Yes | No | No | 117 | golden | Quality Flywheel Agent |
| 22 | HandEyeCalibration | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 23 | HandEyeCalibrationValidator | P2 | Yes | No | No | No | 24 | contract | Quality Flywheel Agent |
| 24 | LocalDeformableMatching | P2 | No | Yes | No | No | 24 | golden | Matching Evidence Agent |
| 25 | NPointCalibration | P2 | No | Yes | No | No | 24 | golden | Calibration Evidence Agent |
| 26 | PixelToWorldTransform | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 27 | PlanarMatching | P2 | No | Yes | No | No | 24 | golden | Matching Evidence Agent |
| 28 | PyramidShapeMatch | P2 | Yes | No | No | No | 24 | contract | Quality Flywheel Agent |
| 29 | ShapeMatching | P2 | No | Yes | Yes | No | 36 | dataset+golden | Matching Evidence Agent |
| 30 | StereoCalibration | P2 | No | Yes | No | No | 24 | golden | Quality Flywheel Agent |
| 31 | SurfaceDefectDetection | P2 | No | Yes | No | No | 24 | golden | AI/Rule Contract Agent |
| 32 | TranslationRotationCalibration | P2 | No | Yes | No | No | 24 | golden | Calibration Evidence Agent |
| 33 | ArcCaliper | P3 | No | Yes | No | No | 31 | golden | Quality Flywheel Agent |
| 34 | RegionComplement | P3 | No | Yes | No | No | 100 | golden | Quality Flywheel Agent |
| 35 | RegionDifference | P3 | No | Yes | No | No | 100 | golden | Quality Flywheel Agent |
| 36 | RegionIntersection | P3 | No | Yes | No | No | 100 | golden | Quality Flywheel Agent |
| 37 | ImageDiff | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 38 | ImageSubtract | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 39 | AdaptiveThreshold | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 40 | EdgeDetection | P3 | No | Yes | Yes | No | 36 | dataset+golden | Quality Flywheel Agent |
| 41 | ContourDetection | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 42 | BlobAnalysis | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 43 | BlobLabeling | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 44 | LineMeasurement | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 45 | CircleMeasurement | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 46 | GeometricFitting | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 47 | PerspectiveTransform | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 48 | AffineTransform | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 49 | DistanceTransform | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |
| 50 | WidthMeasurement | P3 | No | Yes | No | No | 20 | golden | Quality Flywheel Agent |

## Visual 20 Candidate Pool

This is only a G3 candidate pool. It is recorded here so G2 selection does not drift into dataset-tier work.

```text
FFT1D
FrequencyFilter
InverseFFT1D
RegionUnion
AkazeFeatureMatch
OrbFeatureMatch
SemanticSegmentation
DualModalVoting
CaliperTool
EdgePairDefect
TemplateMatching
AnomalyDetection
DeepLearning
DetectionSequenceJudge
GradientShapeMatch
LocalDeformableMatching
PlanarMatching
PyramidShapeMatch
ShapeMatching
SurfaceDefectDetection
```
