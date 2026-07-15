# Stage 4 Operator Performance Evidence

- Label: before
- Source SHA: `6c0fa1f029362239c6c5f252152bec44b3fb773c`
- Generated UTC: 2026-07-15T11:45:27.9939825+00:00
- Environment: DESKTOP-TRGEMQT; Microsoft Windows NT 10.0.22000.0; .NET 8.0.19; CPU logical cores 20; Server GC False
- Warmup / measured iterations: 5 / 50
- Method: Inputs are created before timing. Each case is warmed up, then measured sequentially. P50/P95 use nearest-rank sorted samples. Allocations use process-wide GC.GetTotalAllocatedBytes(true). Memory is sampled before output disposal.

| Case | Input | P50 ms | P95 ms | Alloc/iter bytes | Managed peak delta | Working-set peak delta | Core calls | Evidence | Passed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| translation_rotation_none_40_points | 40 point pairs | 0.5834 | 0.6667 | 92343 | 4804504 | 5746688 | 1 | source-observed-legacy-path | True |
| euclidean_cluster_6000_materialized | 6000 XYZRGB points; 3 x 2000 | 31.4114 | 45.7601 | 30802799 | 14960096 | 9908224 | 2 | source-observed-legacy-path | True |
