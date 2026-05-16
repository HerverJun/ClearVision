# Detection Performance Budget Report

Generated (UTC): 2026-05-16T04:25:27.9030347Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.13 | 0.13 | 0.14 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 6.48 | 6.73 | 7.17 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 41.74 | 43.73 | 45.71 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 3.61 | 4.44 | 4.50 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 4.68 | 5.37 | 5.69 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.02 | 0.03 | 0.03 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.19 | 0.32 | 0.37 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.73 | 0.99 | 0.99 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.03 | 0.03 | 0.04 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 8.78 | 12.85 | 13.40 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.13 | 0.16 | 0.32 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 23.54 | 27.55 | 29.97 | FAIL | p95 27.55ms exceeded allowed 15.00ms. |
| PointLineDistance | 10 | 1.50 | 15 | 0.03 | 0.01 | 0.57 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.76 | 2.16 | 2.23 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 94.40 | 99.60 | 100.00 | FAIL | p95 99.60ms exceeded allowed 45.00ms. |
