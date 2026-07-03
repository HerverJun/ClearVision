# Preprocessing Benchmark Report

Generated (UTC): 2026-07-03T13:50:17.7408372Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 6.33 | 15.00 | 15.00 | 15.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 28.33 | 42.00 | 42.00 | 42.00 |
| BilateralFilter | 1920x1080 | 6 | 31.33 | 39.00 | 39.00 | 39.00 |
| ClaheEnhancement | 1920x1080 | 6 | 41.00 | 49.00 | 49.00 | 49.00 |
| HistogramEqualization | 1920x1080 | 6 | 54.17 | 73.00 | 73.00 | 73.00 |
| ShadingCorrection | 1920x1080 | 6 | 79.17 | 102.00 | 102.00 | 102.00 |
| FrameAveraging | 1920x1080 | 6 | 155.00 | 202.00 | 202.00 | 202.00 |
| MedianBlur | 4096x3072 | 3 | 14.00 | 18.00 | 18.00 | 18.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 104.33 | 111.00 | 111.00 | 111.00 |
| HistogramEqualization | 4096x3072 | 3 | 132.33 | 175.00 | 175.00 | 175.00 |
| BilateralFilter | 4096x3072 | 3 | 141.67 | 149.00 | 149.00 | 149.00 |
| ClaheEnhancement | 4096x3072 | 3 | 180.33 | 237.00 | 237.00 | 237.00 |
| ShadingCorrection | 4096x3072 | 3 | 233.33 | 271.00 | 271.00 | 271.00 |
| FrameAveraging | 4096x3072 | 3 | 409.33 | 495.00 | 495.00 | 495.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 1.00 | 5.00 | 5.00 | 5.00 |
| ClaheEnhancement | native | 6 | 4.67 | 11.00 | 11.00 | 11.00 |
| HistogramEqualization | native | 6 | 6.17 | 14.00 | 14.00 | 14.00 |
| FrameAveraging | native | 6 | 10.17 | 11.00 | 11.00 | 11.00 |
| ShadingCorrection | native | 6 | 12.17 | 22.00 | 22.00 | 22.00 |
| BilateralFilter | native | 6 | 18.50 | 23.00 | 23.00 | 23.00 |
