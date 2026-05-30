# Preprocessing Benchmark Report

Generated (UTC): 2026-05-30T09:34:04.0630254Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 8.33 | 34.00 | 34.00 | 34.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 17.67 | 30.00 | 30.00 | 30.00 |
| HistogramEqualization | 1920x1080 | 6 | 30.67 | 40.00 | 40.00 | 40.00 |
| ClaheEnhancement | 1920x1080 | 6 | 33.33 | 36.00 | 36.00 | 36.00 |
| BilateralFilter | 1920x1080 | 6 | 60.17 | 106.00 | 106.00 | 106.00 |
| ShadingCorrection | 1920x1080 | 6 | 73.33 | 146.00 | 146.00 | 146.00 |
| FrameAveraging | 1920x1080 | 6 | 138.67 | 239.00 | 239.00 | 239.00 |
| MedianBlur | 4096x3072 | 3 | 12.00 | 17.00 | 17.00 | 17.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 73.67 | 85.00 | 85.00 | 85.00 |
| HistogramEqualization | 4096x3072 | 3 | 93.33 | 95.00 | 95.00 | 95.00 |
| ClaheEnhancement | 4096x3072 | 3 | 176.33 | 309.00 | 309.00 | 309.00 |
| ShadingCorrection | 4096x3072 | 3 | 213.00 | 225.00 | 225.00 | 225.00 |
| FrameAveraging | 4096x3072 | 3 | 387.33 | 463.00 | 463.00 | 463.00 |
| BilateralFilter | 4096x3072 | 3 | 687.67 | 1696.00 | 1696.00 | 1696.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| AdaptiveThreshold | native | 6 | 0.67 | 1.00 | 1.00 | 1.00 |
| HistogramEqualization | native | 6 | 3.00 | 8.00 | 8.00 | 8.00 |
| ClaheEnhancement | native | 6 | 5.17 | 12.00 | 12.00 | 12.00 |
| FrameAveraging | native | 6 | 6.50 | 10.00 | 10.00 | 10.00 |
| ShadingCorrection | native | 6 | 20.17 | 35.00 | 35.00 | 35.00 |
| BilateralFilter | native | 6 | 49.00 | 171.00 | 171.00 | 171.00 |
