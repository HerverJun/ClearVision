# Operator Benchmark Report

Generated (UTC): 2026-05-16T10:23:41.0233127Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 4.50 | 13.00 | 13.00 | OK |
| Thresholding | 1920x1080 | 8 | 7.50 | 15.00 | 15.00 | OK |
| Filtering | 1920x1080 | 8 | 8.12 | 14.00 | 14.00 | OK |
| Thresholding | 4096x3072 | 5 | 16.20 | 30.00 | 30.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 16.62 | 22.00 | 22.00 | OK |
| Filtering | 4096x3072 | 5 | 23.20 | 45.00 | 45.00 | OK |
| Morphology | 4096x3072 | 5 | 25.40 | 47.00 | 47.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 30.62 | 52.00 | 52.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 44.75 | 65.00 | 65.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 92.20 | 114.00 | 114.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 109.60 | 128.00 | 128.00 | NeedOptimize |
| SharpnessEvaluation | 4096x3072 | 5 | 185.80 | 219.00 | 219.00 | NeedOptimize |
