# Preprocessing Benchmark Report

Generated (UTC): 2026-07-05T03:54:00.1605278Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 1.00 | 1.00 | 1.00 | 1.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 20.67 | 34.00 | 34.00 | 34.00 |
| FrameAveraging | 1920x1080 | 6 | 40.67 | 62.00 | 62.00 | 62.00 |
| HistogramEqualization | 1920x1080 | 6 | 46.00 | 71.00 | 71.00 | 71.00 |
| ShadingCorrection | 1920x1080 | 6 | 63.83 | 76.00 | 76.00 | 76.00 |
| ClaheEnhancement | 1920x1080 | 6 | 99.67 | 125.00 | 125.00 | 125.00 |
| BilateralFilter | 1920x1080 | 6 | 146.00 | 333.00 | 333.00 | 333.00 |
| MedianBlur | 4096x3072 | 3 | 10.67 | 16.00 | 16.00 | 16.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 52.67 | 55.00 | 55.00 | 55.00 |
| HistogramEqualization | 4096x3072 | 3 | 63.33 | 66.00 | 66.00 | 66.00 |
| FrameAveraging | 4096x3072 | 3 | 71.67 | 74.00 | 74.00 | 74.00 |
| BilateralFilter | 4096x3072 | 3 | 137.33 | 146.00 | 146.00 | 146.00 |
| ShadingCorrection | 4096x3072 | 3 | 147.00 | 162.00 | 162.00 | 162.00 |
| ClaheEnhancement | 4096x3072 | 3 | 166.00 | 194.00 | 194.00 | 194.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| FrameAveraging | native | 6 | 2.00 | 8.00 | 8.00 | 8.00 |
| ClaheEnhancement | native | 6 | 8.83 | 22.00 | 22.00 | 22.00 |
| HistogramEqualization | native | 6 | 9.67 | 22.00 | 22.00 | 22.00 |
| ShadingCorrection | native | 6 | 13.17 | 26.00 | 26.00 | 26.00 |
| BilateralFilter | native | 6 | 35.33 | 70.00 | 70.00 | 70.00 |
