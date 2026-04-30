# Measurement Geometry Oracle Report

GeneratedAtUtc: `2026-04-30T02:25:55.9063691+00:00`
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
| Regression cases | 0 |
| P95 pixel error px | 0.6406 |
| P95 angle error deg | 0.0094 |
| Runtime ms | 2746.439 |

## Operators

| Operator | Cases | Boundary | Passed | Failed | Pass rate | P95 pixel error | P95 angle error | Avg runtime ms | Accepted |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ArcCaliper | 300 | 40 | 300 | 0 | 1 | 0.5997 | 0 | 4.162 | True |
| CaliperTool | 300 | 40 | 300 | 0 | 1 | 0.0255 | 0 | 1.269 | True |
| CircleMeasurement | 300 | 40 | 300 | 0 | 1 | 0.3143 | 0 | 0.175 | True |
| GeometricFitting | 300 | 40 | 300 | 0 | 1 | 0.7443 | 0.0094 | 2.149 | True |
| LineMeasurement | 300 | 40 | 300 | 0 | 1 | 0.0045 | 0.0001 | 1.399 | True |

## Failed Cases

| Case | Operator | Scenario | Pixel error | Angle error | Expected | Actual | Failure |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| - | - | - | - | - | - | - | - |
