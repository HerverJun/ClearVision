# Quality Flywheel G1/G2 Registry

GeneratedAtUtc: `2026-04-26T16:47:20+00:00`
SourceMatrix: `quality/evals/reports/operator_quality_matrix.md`

## Scope

Current matrix has HasGoldenTest only; G1 treats existing golden/contract baselines as contract evidence until HasContractTest is split out.

## Status

- G1 current signal: 63/155 operators.
- G1 remaining without signal: 92.
- G2 Core50 frozen: True (32 P2 + 18 P3).
- G2 current Core50 signal: 36/50.
- G2 remaining Core50 without golden signal: 14.
- P2 without golden evidence: 0.

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

| # | Operator | Priority | Has Golden | Cases | Evidence Layer | Owner |
|---:|---|---|---|---:|---|---|
| 1 | FFT1D | P2 | Yes | 117 | golden-or-contract | Quality Flywheel Agent |
| 2 | FrequencyFilter | P2 | Yes | 117 | golden-or-contract | Quality Flywheel Agent |
| 3 | InverseFFT1D | P2 | Yes | 117 | golden-or-contract | Quality Flywheel Agent |
| 4 | RegionUnion | P2 | Yes | 100 | golden-or-contract | Quality Flywheel Agent |
| 5 | AkazeFeatureMatch | P2 | Yes | 22 | golden-or-contract | Quality Flywheel Agent |
| 6 | OrbFeatureMatch | P2 | Yes | 22 | golden-or-contract | Quality Flywheel Agent |
| 7 | SemanticSegmentation | P2 | Yes | 27 | golden-or-contract | Quality Flywheel Agent |
| 8 | Undistort | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 9 | DualModalVoting | P2 | Yes | 31 | golden-or-contract | Quality Flywheel Agent |
| 10 | CaliperTool | P2 | Yes | 117 | golden-or-contract | Quality Flywheel Agent |
| 11 | EdgePairDefect | P2 | Yes | 27 | golden-or-contract | Quality Flywheel Agent |
| 12 | FisheyeUndistort | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 13 | TemplateMatching | P2 | Yes | 141 | dataset+golden | Quality Flywheel Agent |
| 14 | AnomalyDetection | P2 | Yes | 120 | dataset+golden | Quality Flywheel Agent |
| 15 | CalibrationLoader | P2 | Yes | 24 | golden-or-contract | Calibration Evidence Agent |
| 16 | CameraCalibration | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 17 | CoordinateTransform | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 18 | DeepLearning | P2 | Yes | 46 | golden-or-contract | Quality Flywheel Agent |
| 19 | DetectionSequenceJudge | P2 | Yes | 24 | golden-or-contract | AI/Rule Contract Agent |
| 20 | FisheyeCalibration | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 21 | GradientShapeMatch | P2 | Yes | 117 | golden-or-contract | Quality Flywheel Agent |
| 22 | HandEyeCalibration | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 23 | HandEyeCalibrationValidator | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 24 | LocalDeformableMatching | P2 | Yes | 24 | golden-or-contract | Matching Evidence Agent |
| 25 | NPointCalibration | P2 | Yes | 24 | golden-or-contract | Calibration Evidence Agent |
| 26 | PixelToWorldTransform | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 27 | PlanarMatching | P2 | Yes | 24 | golden-or-contract | Matching Evidence Agent |
| 28 | PyramidShapeMatch | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 29 | ShapeMatching | P2 | Yes | 24 | golden-or-contract | Matching Evidence Agent |
| 30 | StereoCalibration | P2 | Yes | 24 | golden-or-contract | Quality Flywheel Agent |
| 31 | SurfaceDefectDetection | P2 | Yes | 24 | golden-or-contract | AI/Rule Contract Agent |
| 32 | TranslationRotationCalibration | P2 | Yes | 24 | golden-or-contract | Calibration Evidence Agent |
| 33 | ArcCaliper | P3 | Yes | 31 | golden-or-contract | Quality Flywheel Agent |
| 34 | RegionComplement | P3 | Yes | 100 | golden-or-contract | Quality Flywheel Agent |
| 35 | RegionDifference | P3 | Yes | 100 | golden-or-contract | Quality Flywheel Agent |
| 36 | RegionIntersection | P3 | Yes | 100 | golden-or-contract | Quality Flywheel Agent |
| 37 | ImageDiff | P3 | No | 0 | planned | Quality Flywheel Agent |
| 38 | ImageSubtract | P3 | No | 0 | planned | Quality Flywheel Agent |
| 39 | AdaptiveThreshold | P3 | No | 0 | planned | Quality Flywheel Agent |
| 40 | EdgeDetection | P3 | No | 0 | planned | Quality Flywheel Agent |
| 41 | ContourDetection | P3 | No | 0 | planned | Quality Flywheel Agent |
| 42 | BlobAnalysis | P3 | No | 0 | planned | Quality Flywheel Agent |
| 43 | BlobLabeling | P3 | No | 0 | planned | Quality Flywheel Agent |
| 44 | LineMeasurement | P3 | No | 0 | planned | Quality Flywheel Agent |
| 45 | CircleMeasurement | P3 | No | 0 | planned | Quality Flywheel Agent |
| 46 | GeometricFitting | P3 | No | 0 | planned | Quality Flywheel Agent |
| 47 | PerspectiveTransform | P3 | No | 0 | planned | Quality Flywheel Agent |
| 48 | AffineTransform | P3 | No | 0 | planned | Quality Flywheel Agent |
| 49 | DistanceTransform | P3 | No | 0 | planned | Quality Flywheel Agent |
| 50 | WidthMeasurement | P3 | No | 0 | planned | Quality Flywheel Agent |

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
