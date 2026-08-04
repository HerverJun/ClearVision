# Measurement Performance Budget Report

Generated (UTC): 2026-08-03T13:27:47.6263214Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.19 | 0.27 | 0.29 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 1.99 | 2.56 | 2.59 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 15.41 | 17.79 | 21.12 | PASS | Within budget. |
| ColorMeasurement | 22.5 | 1.50 | 33.75 | 18.86 | 35.42 | 36.56 | FAIL | p95 35.42ms exceeded allowed 33.75ms. |
| ContourMeasurement | 40 | 1.50 | 60 | 3.53 | 4.34 | 5.90 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 1.06 | 1.19 | 1.20 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| GeometricFitting | 35 | 1.50 | 52.5 | 5.18 | 5.79 | 6.03 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.80 | 0.90 | 1.00 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.02 | 0.02 | 0.03 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 3.85 | 4.39 | 4.48 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.15 | 0.20 | 0.24 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 3.09 | 3.88 | 3.93 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 2.61 | 3.03 | 3.12 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 12.03 | 14.15 | 14.57 | PASS | Within budget. |
