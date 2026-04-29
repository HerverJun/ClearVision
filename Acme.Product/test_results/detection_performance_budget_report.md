# Detection Performance Budget Report

Generated (UTC): 2026-04-29T08:42:13.0810205Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.33 | 0.64 | 0.71 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 2.06 | 2.28 | 2.60 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 12.92 | 17.59 | 19.98 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.41 | 2.66 | 2.77 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.87 | 0.91 | 0.94 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.10 | 0.10 | 0.13 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.58 | 0.61 | 0.62 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.44 | 3.22 | 3.22 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.58 | 0.62 | 0.62 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 10.05 | 10.58 | 10.61 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.59 | 1.76 | 1.83 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 16.00 | 16.57 | 16.67 | PASS | Within budget. |
