# Operator Benchmark Report

Generated (UTC): 2026-08-03T17:29:44.8876367Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 3.25 | 6.00 | 6.00 | OK |
| Filtering | 1920x1080 | 8 | 3.62 | 10.00 | 10.00 | OK |
| Thresholding | 1920x1080 | 8 | 9.38 | 14.00 | 14.00 | OK |
| Thresholding | 4096x3072 | 5 | 13.00 | 14.00 | 14.00 | OK |
| Filtering | 4096x3072 | 5 | 13.80 | 17.00 | 17.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 17.25 | 24.00 | 24.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 20.50 | 27.00 | 27.00 | OK |
| Morphology | 4096x3072 | 5 | 21.80 | 24.00 | 24.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 24.00 | 32.00 | 32.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 61.00 | 66.00 | 66.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 69.80 | 81.00 | 81.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 130.00 | 138.00 | 138.00 | NeedOptimize |
