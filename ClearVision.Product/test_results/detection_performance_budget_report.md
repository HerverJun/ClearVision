# Detection Performance Budget Report

Generated (UTC): 2026-08-03T13:27:43.1268320Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.21 | 0.26 | 0.29 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 2.27 | 2.74 | 2.74 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 15.22 | 18.10 | 18.10 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 3.65 | 4.11 | 5.03 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 1.19 | 1.46 | 2.23 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.02 | 0.06 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.20 | 0.22 | 0.23 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.78 | 1.05 | 1.06 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 3.93 | 4.79 | 5.09 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.16 | 0.21 | 0.25 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 3.13 | 3.84 | 3.97 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 2.49 | 2.99 | 3.19 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 10.87 | 12.55 | 13.05 | PASS | Within budget. |
