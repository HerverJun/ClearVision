# Measurement Geometry Oracle Report

GeneratedAtUtc: `2026-04-30T07:10:20.4366436+00:00`
Accepted: `True`

## Claim Boundary

- This report is semisynthetic geometry-oracle evidence for measurement operators.
- It is not real production-site validation or sign-off.
- Boundary samples are stress cases over blur, noise, contrast, partial edges, polarity, subpixel offset, outliers, and occlusion.

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 1500 |
| Passed | 1500 |
| Failed | 0 |
| Pass rate | 1 |
| Boundary/failure-oriented cases | 200 |
| Stress cases | 200 |
| Regression cases | 0 |
| P95 pixel error px | 0.6406 |
| P95 angle error deg | 0.0094 |
| Mean uncertainty px | 0.1379 |
| Outlier rate | 0.0007 |
| Runtime ms | 2540.256 |

## Operators

| Operator | Cases | Stress | Passed | Failed | Pass rate | P95 pixel error | P95 angle error | Mean uncertainty | Outlier rate | Avg runtime ms | Accepted |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ArcCaliper | 300 | 40 | 300 | 0 | 1 | 0.5997 | 0 | 0.0487 | 0 | 3.807 | True |
| CaliperTool | 300 | 40 | 300 | 0 | 1 | 0.0254 | 0 | 0.0118 | 0 | 1.205 | True |
| CircleMeasurement | 300 | 40 | 300 | 0 | 1 | 0.3143 | 0 | 0.5 | 0 | 0.181 | True |
| GeometricFitting | 300 | 40 | 300 | 0 | 1 | 0.7443 | 0.0094 | 0.125 | 0.0033 | 1.924 | True |
| LineMeasurement | 300 | 40 | 300 | 0 | 1 | 0.0045 | 0.0001 | 0.0041 | 0 | 1.35 | True |

## Failed Cases

| Case | Operator | Scenario | Pixel error | Angle error | Edge count | Uncertainty | Outliers | Taxonomy | Failure |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| - | - | - | - | - | - | - | - | - | - |
