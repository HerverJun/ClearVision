# Preprocessing Benchmark Report

Generated (UTC): 2026-08-03T17:29:36.7159349Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 2.83 | 8.00 | 8.00 | 8.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 20.00 | 31.00 | 31.00 | 31.00 |
| BilateralFilter | 1920x1080 | 6 | 25.50 | 29.00 | 29.00 | 29.00 |
| ClaheEnhancement | 1920x1080 | 6 | 29.17 | 36.00 | 36.00 | 36.00 |
| HistogramEqualization | 1920x1080 | 6 | 55.33 | 88.00 | 88.00 | 88.00 |
| FrameAveraging | 1920x1080 | 6 | 59.33 | 73.00 | 73.00 | 73.00 |
| ShadingCorrection | 1920x1080 | 6 | 67.83 | 90.00 | 90.00 | 90.00 |
| MedianBlur | 4096x3072 | 3 | 12.00 | 13.00 | 13.00 | 13.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 111.67 | 123.00 | 123.00 | 123.00 |
| FrameAveraging | 4096x3072 | 3 | 115.67 | 156.00 | 156.00 | 156.00 |
| BilateralFilter | 4096x3072 | 3 | 123.00 | 128.00 | 128.00 | 128.00 |
| HistogramEqualization | 4096x3072 | 3 | 143.33 | 171.00 | 171.00 | 171.00 |
| ClaheEnhancement | 4096x3072 | 3 | 155.33 | 234.00 | 234.00 | 234.00 |
| ShadingCorrection | 4096x3072 | 3 | 215.33 | 230.00 | 230.00 | 230.00 |
| MedianBlur | native | 6 | 1.17 | 6.00 | 6.00 | 6.00 |
| AdaptiveThreshold | native | 6 | 2.17 | 13.00 | 13.00 | 13.00 |
| FrameAveraging | native | 6 | 5.17 | 14.00 | 14.00 | 14.00 |
| HistogramEqualization | native | 6 | 9.33 | 16.00 | 16.00 | 16.00 |
| ShadingCorrection | native | 6 | 13.00 | 21.00 | 21.00 | 21.00 |
| BilateralFilter | native | 6 | 20.00 | 25.00 | 25.00 | 25.00 |
| ClaheEnhancement | native | 6 | 25.33 | 101.00 | 101.00 | 101.00 |
