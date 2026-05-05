# Detection Performance Budget Report

Generated (UTC): 2026-05-05T02:35:51.4964648Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.12 | 0.13 | 0.13 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 1.29 | 1.36 | 1.38 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 9.82 | 10.17 | 10.17 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.46 | 2.55 | 3.12 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 0.83 | 0.95 | 0.96 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.10 | 0.11 | 0.14 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.50 | 0.53 | 0.62 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.24 | 2.41 | 2.47 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.10 | 0.11 | 0.15 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 6.48 | 7.08 | 7.26 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.60 | 1.73 | 1.74 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 16.26 | 17.23 | 17.33 | PASS | Within budget. |
