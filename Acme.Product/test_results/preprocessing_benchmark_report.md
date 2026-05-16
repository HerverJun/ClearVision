# Preprocessing Benchmark Report

Generated (UTC): 2026-05-16T07:21:19.1559871Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 1.17 | 2.00 | 2.00 | 2.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 15.83 | 31.00 | 31.00 | 31.00 |
| ShadingCorrection | 1920x1080 | 6 | 46.67 | 69.00 | 69.00 | 69.00 |
| ClaheEnhancement | 1920x1080 | 6 | 57.17 | 108.00 | 108.00 | 108.00 |
| HistogramEqualization | 1920x1080 | 6 | 62.67 | 123.00 | 123.00 | 123.00 |
| FrameAveraging | 1920x1080 | 6 | 95.17 | 133.00 | 133.00 | 133.00 |
| BilateralFilter | 1920x1080 | 6 | 104.17 | 265.00 | 265.00 | 265.00 |
| MedianBlur | 4096x3072 | 3 | 6.33 | 7.00 | 7.00 | 7.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 63.33 | 70.00 | 70.00 | 70.00 |
| BilateralFilter | 4096x3072 | 3 | 222.00 | 250.00 | 250.00 | 250.00 |
| ShadingCorrection | 4096x3072 | 3 | 236.33 | 360.00 | 360.00 | 360.00 |
| HistogramEqualization | 4096x3072 | 3 | 257.67 | 276.00 | 276.00 | 276.00 |
| ClaheEnhancement | 4096x3072 | 3 | 267.00 | 362.00 | 362.00 | 362.00 |
| FrameAveraging | 4096x3072 | 3 | 1763.00 | 2380.00 | 2380.00 | 2380.00 |
| MedianBlur | native | 6 | 0.33 | 1.00 | 1.00 | 1.00 |
| AdaptiveThreshold | native | 6 | 0.67 | 3.00 | 3.00 | 3.00 |
| ClaheEnhancement | native | 6 | 2.67 | 4.00 | 4.00 | 4.00 |
| FrameAveraging | native | 6 | 3.50 | 4.00 | 4.00 | 4.00 |
| HistogramEqualization | native | 6 | 7.83 | 35.00 | 35.00 | 35.00 |
| ShadingCorrection | native | 6 | 11.83 | 16.00 | 16.00 | 16.00 |
| BilateralFilter | native | 6 | 41.67 | 101.00 | 101.00 | 101.00 |
