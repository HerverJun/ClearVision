# Detection Performance Budget Report

Generated (UTC): 2026-07-03T13:51:35.0231461Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.16 | 0.16 | 0.17 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 2.26 | 2.77 | 4.32 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 13.48 | 19.13 | 19.91 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.92 | 3.15 | 3.78 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.99 | 1.11 | 2.03 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.13 | 0.15 | 0.15 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.59 | 0.70 | 0.71 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.68 | 2.90 | 2.97 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.13 | 0.14 | 0.15 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.39 | 2.57 | 2.60 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.96 | 2.04 | 2.06 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 9.65 | 10.41 | 10.94 | PASS | Within budget. |
