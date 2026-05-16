# Operator Benchmark Report

Generated (UTC): 2026-05-16T07:21:17.7771443Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 3.00 | 4.00 | 4.00 | OK |
| Thresholding | 1920x1080 | 8 | 8.00 | 21.00 | 21.00 | OK |
| Filtering | 1920x1080 | 8 | 12.88 | 48.00 | 48.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 15.38 | 18.00 | 18.00 | OK |
| Morphology | 4096x3072 | 5 | 21.60 | 25.00 | 25.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 24.00 | 50.00 | 50.00 | OK |
| Thresholding | 4096x3072 | 5 | 25.60 | 36.00 | 36.00 | OK |
| Filtering | 4096x3072 | 5 | 41.80 | 81.00 | 81.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 61.00 | 78.00 | 78.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 78.62 | 161.00 | 161.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 187.20 | 343.00 | 343.00 | NeedOptimize |
| SharpnessEvaluation | 4096x3072 | 5 | 604.40 | 832.00 | 832.00 | NeedOptimize |
