# Detection Performance Budget Report

Generated (UTC): 2026-05-02T12:29:39.3227184Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.12 | 0.13 | 0.14 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 1.35 | 1.51 | 1.74 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 10.74 | 11.21 | 13.21 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.63 | 3.11 | 3.20 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.86 | 0.96 | 0.96 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.11 | 0.14 | 0.15 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.53 | 0.58 | 0.58 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.43 | 2.93 | 3.49 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.09 | 0.10 | 0.11 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 6.91 | 7.52 | 7.98 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.68 | 2.03 | 2.08 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 16.77 | 17.44 | 17.54 | PASS | Within budget. |
