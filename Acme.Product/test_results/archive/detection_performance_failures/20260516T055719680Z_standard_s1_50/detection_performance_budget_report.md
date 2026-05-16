# Detection Performance Budget Report

Generated (UTC): 2026-05-16T05:57:19.6809426Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.14 | 0.26 | 0.30 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 6.79 | 8.35 | 9.44 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 38.99 | 39.98 | 40.29 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.55 | 2.93 | 3.32 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 3.55 | 3.93 | 4.14 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.02 | 0.02 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.11 | 0.12 | 0.13 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.54 | 0.59 | 0.63 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.03 | 0.03 | 0.03 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 7.10 | 7.49 | 7.57 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.10 | 0.11 | 0.11 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 22.10 | 22.57 | 22.89 | FAIL | p95 22.57ms exceeded allowed 15.00ms. |
| PointLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.60 | 1.71 | 1.75 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 89.23 | 90.07 | 90.10 | FAIL | p95 90.07ms exceeded allowed 45.00ms. |
