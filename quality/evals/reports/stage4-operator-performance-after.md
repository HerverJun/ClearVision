# Stage 4 Operator Performance Evidence

- Label: after
- Source SHA: `5667bfbe75bebe491f1d4c533e93faf78f0f43c7`
- Generated UTC: 2026-07-15T12:44:20.0925460+00:00
- Environment: DESKTOP-TRGEMQT; Microsoft Windows NT 10.0.22000.0; .NET 8.0.19; CPU logical cores 20; Server GC False
- Warmup / measured iterations: 5 / 50
- Method: Inputs are created before timing. Each case is warmed up, then measured sequentially. P50/P95 use nearest-rank sorted samples. Allocations use process-wide GC.GetTotalAllocatedBytes(true). Memory is sampled before output disposal.

| Case | Input | P50 ms | P95 ms | Alloc/iter bytes | Managed peak delta | Working-set peak delta | Core calls | Evidence | Passed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| translation_rotation_none_40_points | 40 point pairs | 0.5945 | 0.6853 | 97102 | 5238312 | 5595136 | 1 | source-observed-legacy-path | True |
| euclidean_cluster_6000_materialized | 6000 XYZRGB points; 3 x 2000 | 23.0928 | 28.7586 | 16268534 | 14433320 | 5881856 | 1 | runtime-output | True |
| euclidean_cluster_6000_indices_only | 6000 XYZRGB points; 3 x 2000 | 14.4536 | 16.3940 | 14538994 | 15146280 | 7847936 | 1 | runtime-output | True |

## Robust calibration quality

| Scenario | Mode | Transform error | Inlier RMS | Outliers | Passed | Error |
| --- | --- | ---: | ---: | ---: | --- | --- |
| no_noise | None | 3.55284E-15 | 3.32326E-15 | 0 | True |  |
| no_noise | Ransac | 3.55284E-15 | 3.32326E-15 | 0 | True |  |
| no_noise | Huber | 3.55284E-15 | 3.32326E-15 | 0 | True |  |
| single_outlier | None | 5.48033 | 11.1204 | 0 | True |  |
| single_outlier | Ransac | 3.55284E-15 | 3.40959E-15 | 1 | True |  |
| single_outlier | Huber | 0.0183403 | 0.0133192 | 1 | True |  |
| multiple_outliers | None | 10.5024 | 31.2507 | 0 | True |  |
| multiple_outliers | Ransac | 1.46483E-14 | 1.23591E-14 | 3 | True |  |
| multiple_outliers | Huber | 0.0171031 | 0.00896474 | 3 | True |  |
