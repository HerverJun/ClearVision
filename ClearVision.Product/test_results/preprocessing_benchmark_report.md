# Preprocessing Benchmark Report

Generated (UTC): 2026-07-03T15:34:00.5796663Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 5.33 | 22.00 | 22.00 | 22.00 |
| ClaheEnhancement | 1920x1080 | 6 | 41.17 | 47.00 | 47.00 | 47.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 49.00 | 98.00 | 98.00 | 98.00 |
| HistogramEqualization | 1920x1080 | 6 | 50.17 | 70.00 | 70.00 | 70.00 |
| ShadingCorrection | 1920x1080 | 6 | 81.33 | 105.00 | 105.00 | 105.00 |
| BilateralFilter | 1920x1080 | 6 | 117.83 | 221.00 | 221.00 | 221.00 |
| FrameAveraging | 1920x1080 | 6 | 137.83 | 180.00 | 180.00 | 180.00 |
| MedianBlur | 4096x3072 | 3 | 11.00 | 14.00 | 14.00 | 14.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 71.67 | 76.00 | 76.00 | 76.00 |
| HistogramEqualization | 4096x3072 | 3 | 92.67 | 103.00 | 103.00 | 103.00 |
| ClaheEnhancement | 4096x3072 | 3 | 101.67 | 127.00 | 127.00 | 127.00 |
| ShadingCorrection | 4096x3072 | 3 | 187.67 | 193.00 | 193.00 | 193.00 |
| FrameAveraging | 4096x3072 | 3 | 299.00 | 331.00 | 331.00 | 331.00 |
| BilateralFilter | 4096x3072 | 3 | 596.00 | 1390.00 | 1390.00 | 1390.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| ClaheEnhancement | native | 6 | 11.83 | 26.00 | 26.00 | 26.00 |
| ShadingCorrection | native | 6 | 14.33 | 23.00 | 23.00 | 23.00 |
| FrameAveraging | native | 6 | 19.33 | 44.00 | 44.00 | 44.00 |
| BilateralFilter | native | 6 | 21.67 | 27.00 | 27.00 | 27.00 |
| HistogramEqualization | native | 6 | 33.00 | 115.00 | 115.00 | 115.00 |
