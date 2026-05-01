# G2 P3 Vision Core Baseline

EvidenceKind: `golden`
GeneratedAtUtc: `2026-04-29T03:29:57.3419805+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 280 |
| Passed | 280 |
| Failed | 0 |
| Runtime ms | 429.97 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| AdaptiveThreshold | 20 | 20 | 0 | 0.797 | 75427 |
| AffineTransform | 20 | 20 | 0 | 0.221 | 8394 |
| BlobAnalysis | 20 | 20 | 0 | 1.07 | 26215 |
| BlobLabeling | 20 | 20 | 0 | 0.447 | 15121 |
| CircleMeasurement | 20 | 20 | 0 | 0.42 | 10655 |
| ContourDetection | 20 | 20 | 0 | 5.504 | 14834 |
| DistanceTransform | 20 | 20 | 0 | 0.628 | 11744 |
| EdgeDetection | 20 | 20 | 0 | 0.399 | 9662 |
| GeometricFitting | 20 | 20 | 0 | 1.218 | 68641 |
| ImageDiff | 20 | 20 | 0 | 1.425 | 7455 |
| ImageSubtract | 20 | 20 | 0 | 0.851 | 10536 |
| LineMeasurement | 20 | 20 | 0 | 2.38 | 155806 |
| PerspectiveTransform | 20 | 20 | 0 | 0.367 | 10712 |
| WidthMeasurement | 20 | 20 | 0 | 5.771 | 894005 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Affine matrix oracle | 12 | 12 | 0 | 0.186 |
| Circle fit oracle | 16 | 16 | 0 | 0.413 |
| Circle fitting oracle | 6 | 6 | 0 | 1.078 |
| Connected component oracle | 16 | 16 | 0 | 0.549 |
| Contour count oracle | 16 | 16 | 0 | 6.839 |
| Distance limit contract | 1 | 1 | 0 | 0.817 |
| Ellipse fitting oracle | 4 | 4 | 0 | 1.132 |
| Failure contract | 27 | 27 | 0 | 0.734 |
| Line fitting oracle | 6 | 6 | 0 | 2.078 |
| Line geometry oracle | 16 | 16 | 0 | 2.896 |
| Manual line width oracle | 16 | 16 | 0 | 7.132 |
| OpenCV Canny oracle | 16 | 16 | 0 | 0.457 |
| OpenCV adaptive threshold oracle | 16 | 16 | 0 | 0.954 |
| OpenCV distance transform oracle | 16 | 16 | 0 | 0.681 |
| Pixel difference oracle | 16 | 16 | 0 | 1.708 |
| Point set transform oracle | 16 | 16 | 0 | 0.404 |
| Provided blob label oracle | 16 | 16 | 0 | 0.51 |
| Subtraction statistics oracle | 16 | 16 | 0 | 1.043 |
| Three-point transform oracle | 4 | 4 | 0 | 0.329 |
| Validation contract | 28 | 28 | 0 | 0.177 |
