# Preprocessing Benchmark Report

Generated (UTC): 2026-06-16T01:38:40.1088820Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 1.67 | 3.00 | 3.00 | 3.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 41.17 | 74.00 | 74.00 | 74.00 |
| BilateralFilter | 1920x1080 | 6 | 51.33 | 78.00 | 78.00 | 78.00 |
| HistogramEqualization | 1920x1080 | 6 | 88.00 | 101.00 | 101.00 | 101.00 |
| ClaheEnhancement | 1920x1080 | 6 | 110.33 | 183.00 | 183.00 | 183.00 |
| ShadingCorrection | 1920x1080 | 6 | 121.83 | 145.00 | 145.00 | 145.00 |
| FrameAveraging | 1920x1080 | 6 | 268.33 | 356.00 | 356.00 | 356.00 |
| MedianBlur | 4096x3072 | 3 | 34.67 | 42.00 | 42.00 | 42.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 187.00 | 195.00 | 195.00 | 195.00 |
| ClaheEnhancement | 4096x3072 | 3 | 248.67 | 417.00 | 417.00 | 417.00 |
| HistogramEqualization | 4096x3072 | 3 | 272.00 | 298.00 | 298.00 | 298.00 |
| BilateralFilter | 4096x3072 | 3 | 273.33 | 310.00 | 310.00 | 310.00 |
| ShadingCorrection | 4096x3072 | 3 | 496.00 | 522.00 | 522.00 | 522.00 |
| FrameAveraging | 4096x3072 | 3 | 823.33 | 971.00 | 971.00 | 971.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| ClaheEnhancement | native | 6 | 3.33 | 10.00 | 10.00 | 10.00 |
| AdaptiveThreshold | native | 6 | 3.50 | 19.00 | 19.00 | 19.00 |
| HistogramEqualization | native | 6 | 4.17 | 18.00 | 18.00 | 18.00 |
| ShadingCorrection | native | 6 | 13.00 | 21.00 | 21.00 | 21.00 |
| FrameAveraging | native | 6 | 17.50 | 37.00 | 37.00 | 37.00 |
| BilateralFilter | native | 6 | 20.00 | 24.00 | 24.00 | 24.00 |
