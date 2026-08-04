# Operator Benchmark Report

Generated (UTC): 2026-08-04T02:12:33.1998186Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| Morphology | 1920x1080 | 8 | 5.75 | 27.00 | 27.00 | OK |
| Thresholding | 1920x1080 | 8 | 45.50 | 188.00 | 188.00 | OK |
| EdgeDetection | 1920x1080 | 8 | 76.38 | 200.00 | 200.00 | OK |
| Filtering | 4096x3072 | 5 | 100.60 | 178.00 | 178.00 | NeedOptimize |
| SharpnessEvaluation | 1920x1080 | 8 | 113.62 | 173.00 | 173.00 | NeedOptimize |
| Thresholding | 4096x3072 | 5 | 114.80 | 184.00 | 184.00 | NeedOptimize |
| BlobAnalysis | 1920x1080 | 8 | 121.25 | 342.00 | 342.00 | NeedOptimize |
| Morphology | 4096x3072 | 5 | 167.20 | 236.00 | 236.00 | NeedOptimize |
| Filtering | 1920x1080 | 8 | 201.88 | 1191.00 | 1191.00 | NeedOptimize |
| EdgeDetection | 4096x3072 | 5 | 355.60 | 642.00 | 642.00 | NeedOptimize |
| BlobAnalysis | 4096x3072 | 5 | 412.60 | 444.00 | 444.00 | NeedOptimize |
| SharpnessEvaluation | 4096x3072 | 5 | 1908.80 | 2826.00 | 2826.00 | NeedOptimize |
