# Detection Performance Budget Report

Generated (UTC): 2026-08-04T02:17:33.1827208Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.38 | 0.42 | 0.47 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 5.03 | 20.40 | 28.27 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 31.30 | 40.24 | 45.69 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 5.94 | 6.37 | 7.83 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 2.08 | 2.57 | 3.01 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.18 | 0.24 | 0.27 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.77 | 1.09 | 1.09 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.02 | 0.02 | 0.02 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 4.97 | 6.36 | 9.11 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.37 | 0.50 | 0.55 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 4.49 | 5.02 | 5.22 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 3.84 | 4.30 | 5.26 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 19.46 | 22.19 | 22.62 | PASS | Within budget. |
