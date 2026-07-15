# Stage 4 Before / After Performance Comparison

- Same machine/input protocol: 5 warmup + 50 measured iterations, nearest-rank P50/P95.
- Managed allocation: `GC.GetTotalAllocatedBytes(true)` delta per measured iteration.
- Memory: managed heap and process working-set sampled before output disposal.

| Case | Before P50/P95 ms | After P50/P95 ms | P50/P95 change | Allocation before/after | Core calls |
| --- | ---: | ---: | ---: | ---: | ---: |
| translation_rotation_none_40_points | 0.5834/0.6667 | 0.5945/0.6853 | -1.90% / -2.79% | 92343 / 97102 | 1 -> 1 |
| euclidean_cluster_6000_materialized | 31.4114/45.7601 | 23.0928/28.7586 | 26.48% / 37.15% | 30802799 / 16268534 | 2 -> 1 |

## Indices-only after case

- P50/P95: 14.4536/16.3940 ms
- Allocation/iteration: 14538994 bytes
- Managed/working-set peak delta: 15146280/7847936 bytes
- Core invocations: 1 (runtime-output)

## Robust calibration quality

| Scenario | Mode | Transform error | Inlier RMS | Outliers |
| --- | --- | ---: | ---: | ---: |
| no_noise | None | 3.55283564945118E-15 | 3.32325934484418E-15 | 0 |
| no_noise | Ransac | 3.55283564945118E-15 | 3.32325934484418E-15 | 0 |
| no_noise | Huber | 3.55283564945118E-15 | 3.32325934484418E-15 | 0 |
| single_outlier | None | 5.480327357624509 | 11.120414560219428 | 0 |
| single_outlier | Ransac | 3.55283564945118E-15 | 3.40959214617482E-15 | 1 |
| single_outlier | Huber | 0.018340318725132067 | 0.013319245357240406 | 1 |
| multiple_outliers | None | 10.502424103746236 | 31.250739123003875 | 0 |
| multiple_outliers | Ransac | 1.46482794945365E-14 | 1.23590794522585E-14 | 3 |
| multiple_outliers | Huber | 0.017103068706382625 | 0.008964739953329666 | 3 |
