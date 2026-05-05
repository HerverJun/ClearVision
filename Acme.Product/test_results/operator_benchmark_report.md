# Operator Benchmark Report

Generated (UTC): 2026-05-05T02:33:03.1739160Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 3.25 | 8.00 | 8.00 | OK |
| Filtering | 1920x1080 | 8 | 3.75 | 6.00 | 6.00 | OK |
| Thresholding | 1920x1080 | 8 | 4.50 | 8.00 | 8.00 | OK |
| Filtering | 4096x3072 | 5 | 5.40 | 7.00 | 7.00 | OK |
| Thresholding | 4096x3072 | 5 | 10.20 | 13.00 | 13.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 10.25 | 15.00 | 15.00 | OK |
| Morphology | 4096x3072 | 5 | 18.20 | 21.00 | 21.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 18.75 | 38.00 | 38.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 30.88 | 45.00 | 45.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 52.20 | 84.00 | 84.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 59.40 | 64.00 | 64.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 147.00 | 170.00 | 170.00 | NeedOptimize |
