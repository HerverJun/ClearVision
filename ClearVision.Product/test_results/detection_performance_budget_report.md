# Detection Performance Budget Report

Generated (UTC): 2026-07-03T15:35:13.8517399Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.17 | 0.26 | 0.27 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 2.08 | 2.20 | 2.92 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 12.78 | 14.75 | 15.21 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.91 | 3.02 | 3.86 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.95 | 1.03 | 1.04 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.13 | 0.15 | 0.15 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.59 | 0.70 | 0.73 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.79 | 3.32 | 3.90 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.13 | 0.16 | 0.23 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.48 | 2.62 | 2.73 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.80 | 1.92 | 1.95 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 9.65 | 10.39 | 10.41 | PASS | Within budget. |
