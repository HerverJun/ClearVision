# Preprocessing Benchmark Report

Generated (UTC): 2026-06-28T15:41:57.5990561Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 5.33 | 12.00 | 12.00 | 12.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 18.33 | 39.00 | 39.00 | 39.00 |
| HistogramEqualization | 1920x1080 | 6 | 36.50 | 53.00 | 53.00 | 53.00 |
| ClaheEnhancement | 1920x1080 | 6 | 37.00 | 58.00 | 58.00 | 58.00 |
| ShadingCorrection | 1920x1080 | 6 | 52.00 | 71.00 | 71.00 | 71.00 |
| BilateralFilter | 1920x1080 | 6 | 60.00 | 210.00 | 210.00 | 210.00 |
| FrameAveraging | 1920x1080 | 6 | 85.67 | 99.00 | 99.00 | 99.00 |
| MedianBlur | 4096x3072 | 3 | 8.67 | 12.00 | 12.00 | 12.00 |
| ClaheEnhancement | 4096x3072 | 3 | 79.67 | 87.00 | 87.00 | 87.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 133.67 | 187.00 | 187.00 | 187.00 |
| BilateralFilter | 4096x3072 | 3 | 180.00 | 188.00 | 188.00 | 188.00 |
| HistogramEqualization | 4096x3072 | 3 | 195.33 | 218.00 | 218.00 | 218.00 |
| ShadingCorrection | 4096x3072 | 3 | 203.67 | 221.00 | 221.00 | 221.00 |
| FrameAveraging | 4096x3072 | 3 | 421.33 | 527.00 | 527.00 | 527.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 5.33 | 23.00 | 23.00 | 23.00 |
| HistogramEqualization | native | 6 | 8.17 | 15.00 | 15.00 | 15.00 |
| ClaheEnhancement | native | 6 | 10.33 | 25.00 | 25.00 | 25.00 |
| ShadingCorrection | native | 6 | 13.67 | 23.00 | 23.00 | 23.00 |
| FrameAveraging | native | 6 | 14.33 | 21.00 | 21.00 | 21.00 |
| BilateralFilter | native | 6 | 19.17 | 23.00 | 23.00 | 23.00 |
