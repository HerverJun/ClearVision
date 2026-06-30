# Detection Performance Budget Report

Generated (UTC): 2026-06-16T01:39:52.5499143Z
Gate Profile: standard
Warmup Iterations: 5
Measured Iterations: 24
Budget Scale: 1.50

| Operator | Budget (ms) | Scale | Allowed P95 (ms) | Mean (ms) | P95 (ms) | P99 (ms) | Status | Notes |
|---|---:|---:|---:|---:|---:|---:|---|---|
| AngleMeasurement | 10 | 1.50 | 15 | 0.14 | 0.18 | 0.22 | PASS | Within budget. |
| CaliperTool | 50 | 1.50 | 75 | 2.17 | 2.48 | 3.25 | PASS | Within budget. |
| CircleMeasurement | 30 | 1.50 | 45 | 12.54 | 14.90 | 15.54 | PASS | Within budget. |
| ContourMeasurement | 40 | 1.50 | 60 | 2.75 | 2.99 | 3.64 | PASS | Within budget. |
| GapMeasurement | 30 | 1.50 | 45 | 1.12 | 1.28 | 1.30 | PASS | Within budget. |
| GeoMeasurement | 20 | 1.50 | 30 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| GeometricTolerance | 20 | 1.50 | 30 | 0.16 | 0.22 | 0.27 | PASS | Within budget. |
| HistogramAnalysis | 10 | 1.50 | 15 | 0.60 | 0.67 | 0.70 | PASS | Within budget. |
| LineLineDistance | 10 | 1.50 | 15 | 0.01 | 0.01 | 0.01 | PASS | Within budget. |
| LineMeasurement | 20 | 1.50 | 30 | 3.42 | 4.37 | 5.17 | PASS | Within budget. |
| MeasureDistance | 10 | 1.50 | 15 | 0.13 | 0.15 | 0.16 | PASS | Within budget. |
| PixelStatistics | 10 | 1.50 | 15 | 2.81 | 4.46 | 4.48 | PASS | Within budget. |
| PointLineDistance | 10 | 1.50 | 15 | 0.00 | 0.00 | 0.01 | PASS | Within budget. |
| SharpnessEvaluation | 15 | 1.50 | 22.5 | 1.75 | 2.01 | 2.32 | PASS | Within budget. |
| WidthMeasurement | 30 | 1.50 | 45 | 19.11 | 20.08 | 20.23 | PASS | Within budget. |
