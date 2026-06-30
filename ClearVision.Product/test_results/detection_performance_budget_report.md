# Detection Performance Budget Report

Generated (UTC): 2026-06-28T15:42:51.0804900Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.14 | 0.15 | 0.16 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 1.92 | 2.03 | 2.94 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 12.31 | 14.16 | 14.49 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.86 | 3.05 | 3.50 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.99 | 1.12 | 1.91 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.13 | 0.14 | 0.15 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.57 | 0.59 | 0.60 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.57 | 2.87 | 2.92 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.12 | 0.13 | 0.14 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.37 | 2.52 | 2.55 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.73 | 1.84 | 1.85 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 9.17 | 9.60 | 10.69 | PASS | Within budget. |
