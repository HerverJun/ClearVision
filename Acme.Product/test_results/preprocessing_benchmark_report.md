# Preprocessing Benchmark Report

Generated (UTC): 2026-05-05T02:33:05.2977138Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 3.00 | 6.00 | 6.00 | 6.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 12.00 | 16.00 | 16.00 | 16.00 |
| HistogramEqualization | 1920x1080 | 6 | 22.00 | 26.00 | 26.00 | 26.00 |
| ClaheEnhancement | 1920x1080 | 6 | 22.17 | 27.00 | 27.00 | 27.00 |
| ShadingCorrection | 1920x1080 | 6 | 34.50 | 41.00 | 41.00 | 41.00 |
| FrameAveraging | 1920x1080 | 6 | 84.67 | 95.00 | 95.00 | 95.00 |
| BilateralFilter | 1920x1080 | 6 | 135.50 | 277.00 | 277.00 | 277.00 |
| MedianBlur | 4096x3072 | 3 | 7.33 | 8.00 | 8.00 | 8.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 40.33 | 42.00 | 42.00 | 42.00 |
| HistogramEqualization | 4096x3072 | 3 | 51.00 | 56.00 | 56.00 | 56.00 |
| ClaheEnhancement | 4096x3072 | 3 | 63.00 | 102.00 | 102.00 | 102.00 |
| BilateralFilter | 4096x3072 | 3 | 113.00 | 115.00 | 115.00 | 115.00 |
| ShadingCorrection | 4096x3072 | 3 | 146.67 | 150.00 | 150.00 | 150.00 |
| FrameAveraging | 4096x3072 | 3 | 232.67 | 279.00 | 279.00 | 279.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 0.50 | 3.00 | 3.00 | 3.00 |
| HistogramEqualization | native | 6 | 5.17 | 9.00 | 9.00 | 9.00 |
| ClaheEnhancement | native | 6 | 7.17 | 12.00 | 12.00 | 12.00 |
| ShadingCorrection | native | 6 | 8.17 | 15.00 | 15.00 | 15.00 |
| FrameAveraging | native | 6 | 12.00 | 26.00 | 26.00 | 26.00 |
| BilateralFilter | native | 6 | 17.50 | 26.00 | 26.00 | 26.00 |
