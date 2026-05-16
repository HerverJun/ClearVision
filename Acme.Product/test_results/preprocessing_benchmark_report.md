# Preprocessing Benchmark Report

Generated (UTC): 2026-05-16T10:23:45.4478120Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 2.00 | 7.00 | 7.00 | 7.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 26.00 | 35.00 | 35.00 | 35.00 |
| ClaheEnhancement | 1920x1080 | 6 | 39.00 | 48.00 | 48.00 | 48.00 |
| ShadingCorrection | 1920x1080 | 6 | 49.17 | 70.00 | 70.00 | 70.00 |
| HistogramEqualization | 1920x1080 | 6 | 53.17 | 82.00 | 82.00 | 82.00 |
| BilateralFilter | 1920x1080 | 6 | 67.33 | 249.00 | 249.00 | 249.00 |
| FrameAveraging | 1920x1080 | 6 | 119.50 | 157.00 | 157.00 | 157.00 |
| MedianBlur | 4096x3072 | 3 | 14.00 | 29.00 | 29.00 | 29.00 |
| ClaheEnhancement | 4096x3072 | 3 | 61.33 | 66.00 | 66.00 | 66.00 |
| HistogramEqualization | 4096x3072 | 3 | 69.67 | 77.00 | 77.00 | 77.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 78.33 | 85.00 | 85.00 | 85.00 |
| ShadingCorrection | 4096x3072 | 3 | 160.00 | 176.00 | 176.00 | 176.00 |
| BilateralFilter | 4096x3072 | 3 | 167.00 | 178.00 | 178.00 | 178.00 |
| FrameAveraging | 4096x3072 | 3 | 375.00 | 505.00 | 505.00 | 505.00 |
| MedianBlur | native | 6 | 0.50 | 3.00 | 3.00 | 3.00 |
| AdaptiveThreshold | native | 6 | 1.00 | 5.00 | 5.00 | 5.00 |
| HistogramEqualization | native | 6 | 2.83 | 7.00 | 7.00 | 7.00 |
| ClaheEnhancement | native | 6 | 6.17 | 12.00 | 12.00 | 12.00 |
| FrameAveraging | native | 6 | 12.33 | 37.00 | 37.00 | 37.00 |
| ShadingCorrection | native | 6 | 19.00 | 61.00 | 61.00 | 61.00 |
| BilateralFilter | native | 6 | 19.33 | 24.00 | 24.00 | 24.00 |
