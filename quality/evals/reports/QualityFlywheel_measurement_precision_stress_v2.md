# Measurement Geometry Oracle Report

GeneratedAtUtc: `2026-04-30T07:09:59.5623005+00:00`
Accepted: `True`

## Claim Boundary

- This report is semisynthetic stress evidence for measurement-operator precision and robustness.
- It is not real production-site validation or sign-off.
- Stress samples cover blur, noise, low contrast, occlusion, polarity flip, subpixel offset, outlier contour, and weak edge cases.

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 600 |
| Passed | 598 |
| Failed | 2 |
| Pass rate | 0.9967 |
| Boundary/failure-oriented cases | 600 |
| Stress cases | 600 |
| Regression cases | 0 |
| P95 pixel error px | 0.6533 |
| P95 angle error deg | 0.0116 |
| Mean uncertainty px | 0.1425 |
| Outlier rate | 0.0067 |
| Runtime ms | 1509.629 |

## Operators

| Operator | Cases | Stress | Passed | Failed | Pass rate | P95 pixel error | P95 angle error | Mean uncertainty | Outlier rate | Avg runtime ms | Accepted |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ArcCaliper | 120 | 120 | 120 | 0 | 1 | 0.5996 | 0 | 0.0469 | 0 | 4.21 | True |
| CaliperTool | 120 | 120 | 120 | 0 | 1 | 0.0276 | 0 | 0.0138 | 0 | 1.678 | True |
| CircleMeasurement | 120 | 120 | 120 | 0 | 1 | 0.4883 | 0 | 0.5 | 0 | 0.239 | True |
| GeometricFitting | 120 | 120 | 118 | 2 | 0.9833 | 0.8681 | 0.0157 | 0.125 | 0.0333 | 4.928 | True |
| LineMeasurement | 120 | 120 | 120 | 0 | 1 | 0.0056 | 0.0071 | 0.0268 | 0 | 1.525 | True |

## Failed Cases

| Case | Operator | Scenario | Pixel error | Angle error | Edge count | Uncertainty | Outliers | Taxonomy | Failure |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| GeometricFitting_Circle_stress_v2_027 | GeometricFitting | occlusion | 2.2564 | - | 1674 | 0.125 | 1162 | pair-distance-outlier, occluded-edge, outlier-contour | CircleError=2.256 |
| GeometricFitting_Circle_stress_v2_051 | GeometricFitting | occlusion | 4.4387 | - | 1526 | 0.125 | 1153 | pair-distance-outlier, occluded-edge, outlier-contour | CircleError=4.439 |
