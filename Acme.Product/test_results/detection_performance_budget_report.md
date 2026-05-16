# Detection Performance Budget Report

Generated (UTC): 2026-05-16T10:15:35.6612104Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.40 | 0.80 | 0.81 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 2.26 | 2.74 | 6.12 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 14.60 | 19.20 | 22.40 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.67 | 3.26 | 3.36 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 1.05 | 1.27 | 1.79 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.11 | 0.13 | 0.13 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.66 | 0.77 | 0.78 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.02 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 2.48 | 3.16 | 3.38 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.80 | 0.91 | 0.99 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.35 | 2.63 | 2.66 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.01 | 0.02 | 0.02 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 2.08 | 2.33 | 2.43 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 17.32 | 18.29 | 19.33 | PASS | Within budget. |
