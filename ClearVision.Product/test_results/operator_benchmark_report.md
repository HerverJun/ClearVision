# Operator Benchmark Report

Generated (UTC): 2026-06-28T15:41:58.8880094Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 2.75 | 7.00 | 7.00 | OK |
| Filtering | 1920x1080 | 8 | 7.50 | 17.00 | 17.00 | OK |
| Thresholding | 1920x1080 | 8 | 10.12 | 18.00 | 18.00 | OK |
| Filtering | 4096x3072 | 5 | 13.20 | 19.00 | 19.00 | OK |
| Morphology | 4096x3072 | 5 | 28.60 | 49.00 | 49.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 28.75 | 38.00 | 38.00 | OK |
| Thresholding | 4096x3072 | 5 | 30.20 | 41.00 | 41.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 33.88 | 88.00 | 88.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 37.38 | 48.00 | 48.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 93.80 | 155.00 | 155.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 96.00 | 130.00 | 130.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 189.80 | 202.00 | 202.00 | NeedOptimize |
