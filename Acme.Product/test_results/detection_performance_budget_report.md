# Detection Performance Budget Report

Generated (UTC): 2026-05-30T09:34:54.3359208Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.15 | 0.29 | 0.32 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 1.38 | 1.59 | 2.50 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 10.46 | 11.63 | 12.91 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.53 | 2.87 | 3.12 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.82 | 0.88 | 0.91 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.10 | 0.11 | 0.11 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.50 | 0.56 | 0.59 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.29 | 2.78 | 2.95 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.09 | 0.09 | 0.10 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.22 | 2.37 | 2.55 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.62 | 1.78 | 1.96 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 17.43 | 22.20 | 22.65 | PASS | Within budget. |
