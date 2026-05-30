# Operator Benchmark Report

Generated (UTC): 2026-05-30T16:16:13.9356519Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Filtering | 1920x1080 | 8 | 6.50 | 18.00 | 18.00 | OK |
| Morphology | 1920x1080 | 8 | 6.50 | 31.00 | 31.00 | OK |
| Thresholding | 1920x1080 | 8 | 9.38 | 14.00 | 14.00 | OK |
| Filtering | 4096x3072 | 5 | 17.60 | 38.00 | 38.00 | OK |
| Morphology | 4096x3072 | 5 | 19.20 | 20.00 | 20.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 21.00 | 57.00 | 57.00 | OK |
| Thresholding | 4096x3072 | 5 | 29.40 | 58.00 | 58.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 37.50 | 58.00 | 58.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 63.75 | 198.00 | 198.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 81.20 | 117.00 | 117.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 144.00 | 167.00 | 167.00 | NeedOptimize |
| SharpnessEvaluation | 4096x3072 | 5 | 371.20 | 472.00 | 472.00 | NeedOptimize |
