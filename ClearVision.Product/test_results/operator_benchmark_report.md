# Operator Benchmark Report

Generated (UTC): 2026-06-16T01:38:43.6861473Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Filtering | 1920x1080 | 8 | 5.75 | 11.00 | 11.00 | OK |
| Morphology | 1920x1080 | 8 | 10.25 | 20.00 | 20.00 | OK |
| Thresholding | 1920x1080 | 8 | 15.38 | 24.00 | 24.00 | OK |
| Filtering | 4096x3072 | 5 | 26.40 | 40.00 | 40.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 35.25 | 45.00 | 45.00 | OK |
| Morphology | 4096x3072 | 5 | 49.80 | 54.00 | 54.00 | OK |
| SharpnessEvaluation | 1920x1080 | 8 | 51.25 | 66.00 | 66.00 | OK |
| Thresholding | 4096x3072 | 5 | 53.40 | 69.00 | 69.00 | OK |
| BlobAnalysis | 1920x1080 | 8 | 69.25 | 86.00 | 86.00 | OK |
| EdgeDetection | 4096x3072 | 5 | 130.00 | 154.00 | 154.00 | NeedOptimize |
| BlobAnalysis | 4096x3072 | 5 | 135.80 | 197.00 | 197.00 | NeedOptimize |
| SharpnessEvaluation | 4096x3072 | 5 | 224.00 | 236.00 | 236.00 | NeedOptimize |
