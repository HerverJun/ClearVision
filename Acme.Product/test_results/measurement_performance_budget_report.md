# Measurement Performance Budget Report

Generated (UTC): 2026-05-16T05:57:26.6708834Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.13 | 0.16 | 0.18 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 6.53 | 7.13 | 7.53 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 39.01 | 39.70 | 40.28 | PASS | Within budget. |
| ColorMeasurement | 20 | 1.50 | 30 | 64.15 | 65.80 | 67.54 | FAIL | p95 65.80ms exceeded allowed 30.00ms. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.51 | 2.78 | 3.28 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 3.54 | 3.79 | 3.91 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.03 | 0.03 | 0.03 | PASS | Within budget. |
| GeometricFitting | 35 | 1.50 | 52.5 | 3.52 | 3.75 | 4.26 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.51 | 0.55 | 0.55 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.03 | 0.03 | 0.04 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 7.15 | 7.64 | 7.68 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.10 | 0.10 | 0.11 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 22.17 | 22.68 | 22.80 | FAIL | p95 22.68ms exceeded allowed 15.00ms. |
| PointLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.67 | 1.88 | 2.03 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 89.41 | 90.49 | 90.71 | FAIL | p95 90.49ms exceeded allowed 45.00ms. |
