# Operator Benchmark Report

Generated (UTC): 2026-05-30T09:34:01.2899466Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 4.00 | 6.00 | 6.00 | OK |
| Thresholding | 1920x1080 | 8 | 8.62 | 26.00 | 26.00 | OK |
| Filtering | 1920x1080 | 8 | 16.25 | 43.00 | 43.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 24.62 | 32.00 | 32.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 33.88 | 79.00 | 79.00 | OK |
| Filtering | 4096x3072 | 5 | 34.40 | 50.00 | 50.00 | OK |
| Thresholding | 4096x3072 | 5 | 39.00 | 96.00 | 96.00 | OK |
| Morphology | 4096x3072 | 5 | 49.20 | 97.00 | 97.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 51.75 | 94.00 | 94.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 72.40 | 95.00 | 95.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 91.00 | 143.00 | 143.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 221.20 | 246.00 | 246.00 | NeedOptimize |
