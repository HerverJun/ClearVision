# Operator Benchmark Report

Generated (UTC): 2026-07-03T13:50:20.4441450Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 3.00 | 6.00 | 6.00 | OK |
| Thresholding | 1920x1080 | 8 | 7.12 | 17.00 | 17.00 | OK |
| Filtering | 1920x1080 | 8 | 8.00 | 16.00 | 16.00 | OK |
| Thresholding | 4096x3072 | 5 | 14.60 | 21.00 | 21.00 | OK |
| Morphology | 4096x3072 | 5 | 15.40 | 17.00 | 17.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 15.50 | 21.00 | 21.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 18.12 | 24.00 | 24.00 | OK |
| Filtering | 4096x3072 | 5 | 20.80 | 44.00 | 44.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 21.75 | 26.00 | 26.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 43.00 | 49.00 | 49.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 63.20 | 69.00 | 69.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 108.00 | 118.00 | 118.00 | NeedOptimize |
