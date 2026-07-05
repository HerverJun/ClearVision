# Operator Benchmark Report

Generated (UTC): 2026-07-05T03:53:59.5720688Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 4.88 | 19.00 | 19.00 | OK |
| Filtering | 1920x1080 | 8 | 6.88 | 14.00 | 14.00 | OK |
| Thresholding | 1920x1080 | 8 | 6.88 | 15.00 | 15.00 | OK |
| Filtering | 4096x3072 | 5 | 14.00 | 37.00 | 37.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 22.38 | 36.00 | 36.00 | OK |
| Thresholding | 4096x3072 | 5 | 23.60 | 37.00 | 37.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 25.62 | 42.00 | 42.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 26.00 | 32.00 | 32.00 | OK |
| Morphology | 4096x3072 | 5 | 40.60 | 55.00 | 55.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 92.40 | 97.00 | 97.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 98.40 | 181.00 | 181.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 138.00 | 169.00 | 169.00 | NeedOptimize |
