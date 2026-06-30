# Preprocessing Benchmark Report

Generated (UTC): 2026-05-30T16:16:18.2603447Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 2.50 | 7.00 | 7.00 | 7.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 17.67 | 32.00 | 32.00 | 32.00 |
| BilateralFilter | 1920x1080 | 6 | 37.67 | 59.00 | 59.00 | 59.00 |
| ClaheEnhancement | 1920x1080 | 6 | 48.00 | 66.00 | 66.00 | 66.00 |
| HistogramEqualization | 1920x1080 | 6 | 66.50 | 96.00 | 96.00 | 96.00 |
| ShadingCorrection | 1920x1080 | 6 | 93.17 | 152.00 | 152.00 | 152.00 |
| FrameAveraging | 1920x1080 | 6 | 135.67 | 178.00 | 178.00 | 178.00 |
| MedianBlur | 4096x3072 | 3 | 12.00 | 15.00 | 15.00 | 15.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 82.67 | 95.00 | 95.00 | 95.00 |
| ClaheEnhancement | 4096x3072 | 3 | 104.00 | 140.00 | 140.00 | 140.00 |
| HistogramEqualization | 4096x3072 | 3 | 203.67 | 342.00 | 342.00 | 342.00 |
| ShadingCorrection | 4096x3072 | 3 | 262.67 | 317.00 | 317.00 | 317.00 |
| FrameAveraging | 4096x3072 | 3 | 516.33 | 578.00 | 578.00 | 578.00 |
| BilateralFilter | 4096x3072 | 3 | 1531.00 | 4152.00 | 4152.00 | 4152.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 1.50 | 8.00 | 8.00 | 8.00 |
| FrameAveraging | native | 6 | 7.00 | 12.00 | 12.00 | 12.00 |
| HistogramEqualization | native | 6 | 7.67 | 26.00 | 26.00 | 26.00 |
| ClaheEnhancement | native | 6 | 9.17 | 24.00 | 24.00 | 24.00 |
| ShadingCorrection | native | 6 | 10.67 | 19.00 | 19.00 | 19.00 |
| BilateralFilter | native | 6 | 28.67 | 61.00 | 61.00 | 61.00 |
