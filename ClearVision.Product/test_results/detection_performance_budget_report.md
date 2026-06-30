# Detection Performance Budget Report

Generated (UTC): 2026-05-30T16:17:05.4819859Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.12 | 0.12 | 0.13 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 1.38 | 1.61 | 1.79 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 10.80 | 11.42 | 11.47 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.63 | 2.79 | 3.65 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.87 | 1.01 | 1.03 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.11 | 0.12 | 0.12 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.51 | 0.56 | 0.61 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.44 | 3.05 | 3.06 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.10 | 0.10 | 0.11 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.26 | 2.37 | 2.57 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.99 | 2.48 | 3.01 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 17.29 | 17.79 | 22.08 | PASS | Within budget. |
