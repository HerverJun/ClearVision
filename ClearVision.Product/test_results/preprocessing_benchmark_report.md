# Preprocessing Benchmark Report

Generated (UTC): 2026-08-04T02:11:24.3949021Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Max (ms) |
|---|---|---:|---:|---:|---:|---:|
| MedianBlur | 1920x1080 | 6 | 2.33 | 3.00 | 3.00 | 3.00 |
| FrameAveraging | 1920x1080 | 6 | 53.33 | 95.00 | 95.00 | 95.00 |
| AdaptiveThreshold | 1920x1080 | 6 | 84.50 | 185.00 | 185.00 | 185.00 |
| ClaheEnhancement | 1920x1080 | 6 | 160.33 | 284.00 | 284.00 | 284.00 |
| BilateralFilter | 1920x1080 | 6 | 160.50 | 266.00 | 266.00 | 266.00 |
| HistogramEqualization | 1920x1080 | 6 | 167.33 | 361.00 | 361.00 | 361.00 |
| ShadingCorrection | 1920x1080 | 6 | 479.33 | 1021.00 | 1021.00 | 1021.00 |
| MedianBlur | 4096x3072 | 3 | 80.00 | 205.00 | 205.00 | 205.00 |
| FrameAveraging | 4096x3072 | 3 | 308.00 | 354.00 | 354.00 | 354.00 |
| AdaptiveThreshold | 4096x3072 | 3 | 389.00 | 447.00 | 447.00 | 447.00 |
| ClaheEnhancement | 4096x3072 | 3 | 446.67 | 502.00 | 502.00 | 502.00 |
| HistogramEqualization | 4096x3072 | 3 | 473.00 | 491.00 | 491.00 | 491.00 |
| BilateralFilter | 4096x3072 | 3 | 557.67 | 591.00 | 591.00 | 591.00 |
| ShadingCorrection | 4096x3072 | 3 | 918.00 | 1025.00 | 1025.00 | 1025.00 |
| MedianBlur | native | 6 | 0.00 | 0.00 | 0.00 | 0.00 |
| FrameAveraging | native | 6 | 1.17 | 2.00 | 2.00 | 2.00 |
| HistogramEqualization | native | 6 | 3.00 | 3.00 | 3.00 | 3.00 |
| AdaptiveThreshold | native | 6 | 40.67 | 241.00 | 241.00 | 241.00 |
| ClaheEnhancement | native | 6 | 43.17 | 236.00 | 236.00 | 236.00 |
| BilateralFilter | native | 6 | 138.83 | 472.00 | 472.00 | 472.00 |
| ShadingCorrection | native | 6 | 203.50 | 757.00 | 757.00 | 757.00 |
