# Operator Benchmark Report

Generated (UTC): 2026-07-03T15:34:00.0348880Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Filtering | 1920x1080 | 8 | 3.88 | 9.00 | 9.00 | OK |
| Morphology | 1920x1080 | 8 | 4.00 | 9.00 | 9.00 | OK |
| Thresholding | 1920x1080 | 8 | 14.50 | 48.00 | 48.00 | OK |
| Filtering | 4096x3072 | 5 | 15.20 | 48.00 | 48.00 | OK |
| Thresholding | 4096x3072 | 5 | 16.40 | 24.00 | 24.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 20.62 | 33.00 | 33.00 | OK |
| Morphology | 4096x3072 | 5 | 21.40 | 30.00 | 30.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 22.62 | 34.00 | 34.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 26.50 | 30.00 | 30.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 51.00 | 60.00 | 60.00 | OK |
| BlobAnalysis | 4096x3072 | 5 | 75.60 | 83.00 | 83.00 | OK |
| SharpnessEvaluation | 4096x3072 | 5 | 134.40 | 151.00 | 151.00 | NeedOptimize |
