# Stage 4 Operator Performance Evidence

- Label: after-acceptance
- Source SHA: `5667bfbe75bebe491f1d4c533e93faf78f0f43c7`
- Generated UTC: 2026-07-15T12:44:40.3673297+00:00
- Environment: DESKTOP-TRGEMQT; Microsoft Windows NT 10.0.22000.0; .NET 8.0.19; CPU logical cores 20; Server GC False
- Warmup / measured iterations: 10 / 100
- Method: Inputs are created before timing. Each case is warmed up, then measured sequentially. P50/P95 use nearest-rank sorted samples. Allocations use process-wide GC.GetTotalAllocatedBytes(true). Memory is sampled before output disposal.

| Case | Input | P50 ms | P95 ms | Alloc/iter bytes | Managed peak delta | Working-set peak delta | Core calls | Evidence | Passed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| translation_rotation_none_40_points | 40 point pairs | 0.6972 | 0.8710 | 97082 | 10488792 | 12554240 | 1 | source-observed-legacy-path | True |
| euclidean_cluster_6000_materialized | 6000 XYZRGB points; 3 x 2000 | 15.7485 | 23.6856 | 16269164 | 14759880 | 6627328 | 1 | runtime-output | True |
| euclidean_cluster_6000_indices_only | 6000 XYZRGB points; 3 x 2000 | 14.1049 | 16.3487 | 14539010 | 15122008 | 6840320 | 1 | runtime-output | True |

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
