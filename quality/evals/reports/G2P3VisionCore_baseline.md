# G2 P3 Vision Core Baseline

GeneratedAtUtc: `2026-04-27T09:25:55.3494725+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 280 |
| Passed | 280 |
| Failed | 0 |
| Runtime ms | 408.258 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| AdaptiveThreshold | 20 | 20 | 0 | 0.833 | 71807 |
| AffineTransform | 20 | 20 | 0 | 0.228 | 8394 |
| BlobAnalysis | 20 | 20 | 0 | 1.055 | 28675 |
| BlobLabeling | 20 | 20 | 0 | 0.448 | 15121 |
| CircleMeasurement | 20 | 20 | 0 | 0.346 | 10655 |
| ContourDetection | 20 | 20 | 0 | 4.791 | 14834 |
| DistanceTransform | 20 | 20 | 0 | 0.663 | 11744 |
| EdgeDetection | 20 | 20 | 0 | 0.44 | 9662 |
| GeometricFitting | 20 | 20 | 0 | 1.222 | 68641 |
| ImageDiff | 20 | 20 | 0 | 1.484 | 7455 |
| ImageSubtract | 20 | 20 | 0 | 0.921 | 11034 |
| LineMeasurement | 20 | 20 | 0 | 1.958 | 155806 |
| PerspectiveTransform | 20 | 20 | 0 | 0.394 | 10712 |
| WidthMeasurement | 20 | 20 | 0 | 5.631 | 894005 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Affine matrix oracle | 12 | 12 | 0 | 0.186 |
| Circle fit oracle | 16 | 16 | 0 | 0.337 |
| Circle fitting oracle | 6 | 6 | 0 | 1.099 |
| Connected component oracle | 16 | 16 | 0 | 0.49 |
| Contour count oracle | 16 | 16 | 0 | 5.942 |
| Distance limit contract | 1 | 1 | 0 | 1.095 |
| Ellipse fitting oracle | 4 | 4 | 0 | 1.143 |
| Failure contract | 27 | 27 | 0 | 0.751 |
| Line fitting oracle | 6 | 6 | 0 | 2.05 |
| Line geometry oracle | 16 | 16 | 0 | 2.369 |
| Manual line width oracle | 16 | 16 | 0 | 6.975 |
| OpenCV Canny oracle | 16 | 16 | 0 | 0.506 |
| OpenCV adaptive threshold oracle | 16 | 16 | 0 | 0.996 |
| OpenCV distance transform oracle | 16 | 16 | 0 | 0.694 |
| Pixel difference oracle | 16 | 16 | 0 | 1.776 |
| Point set transform oracle | 16 | 16 | 0 | 0.434 |
| Provided blob label oracle | 16 | 16 | 0 | 0.512 |
| Subtraction statistics oracle | 16 | 16 | 0 | 1.13 |
| Three-point transform oracle | 4 | 4 | 0 | 0.356 |
| Validation contract | 28 | 28 | 0 | 0.185 |
