# Operator Benchmark Report

Generated (UTC): 2026-05-02T12:26:28.3230301Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 4.12 | 6.00 | 6.00 | OK |
| Thresholding | 1920x1080 | 8 | 7.50 | 22.00 | 22.00 | OK |
| Filtering | 4096x3072 | 5 | 15.60 | 20.00 | 20.00 | OK |
| Filtering | 1920x1080 | 8 | 17.25 | 78.00 | 78.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 18.00 | 25.00 | 25.00 | OK |
| Morphology | 4096x3072 | 5 | 20.60 | 23.00 | 23.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 22.38 | 25.00 | 25.00 | OK |
| Thresholding | 4096x3072 | 5 | 23.40 | 29.00 | 29.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 26.50 | 49.00 | 49.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 69.00 | 75.00 | 75.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 71.80 | 75.00 | 75.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 118.00 | 126.00 | 126.00 | NeedOptimize |
