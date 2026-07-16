# Product Operator E2E Benchmark (after)

- Product SHA: `727414e2ca6bd5785aac3f2dbc68fb5b8badc369` (dirty=False)
- Product Infrastructure assembly SHA: `9e2b5d66da33bf3a695d9540f7b5adb5462c78a09e83a024fb1d306edce11274`
- Harness commit/program SHA: `727414e2ca6bd5785aac3f2dbc68fb5b8badc369` / `572dfc2826d0eb597839442c0b2b2e49040348287fc01174288e18e803a57b9f` (dirty=False)
- Dataset manifest/generated SHA: `a507e7345388017506dc60f544598721042a0cdaa12dd0f2402cb6236256eaeb` / `5d60098525547bf873b9e4618b3d7b5a08bf202164295dac9c2eff6f99c2507a`
- Claim boundary: Synthetic raster end-to-end product-operator evidence only. It is not E4, commercial-grade, field-accuracy, release-readiness, or production-site validation evidence.
- Managed allocation scope: GC.GetAllocatedBytesForCurrentThread on the benchmark thread only; it is not full-process allocation or native OpenCV allocation.
- Full-process resources: peak working set=117833728 B; private bytes=78864384 B

| Domain | Algorithm | Split | Cases | Bias | RMSE | P95 | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Managed alloc B/case |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Circle | LegacyDefault | validation | 24 | 0.096774 | 1.068459 | 2.151906 | 0.000000 | 0.000000 | 0.080295 | 7.475200 / 9.738900 | 1991587.633333 |
| Circle | LegacyDefault | test | 72 | 0.229830 | 1.175745 | 2.419451 | 0.000000 | 0.000000 | 0.033999 | 7.399900 / 9.886700 | 1990959.916667 |
| Line | L2Default | validation | 24 | 0.066164 | 0.185610 | 0.318032 | 0.000000 | 0.000000 | 0.000000 | 2.048800 / 3.956400 | 355361.091667 |
| Line | L2Default | test | 72 | 0.059528 | 0.497887 | 0.780994 | 0.000000 | 0.000000 | 0.000000 | 1.737400 / 3.380400 | 336889.580556 |
| Circle | WelschOptIn | validation | 24 | 0.006372 | 1.234893 | 2.669103 | 0.000000 | 0.000000 | 0.080295 | 7.357900 / 8.021200 | 2172152.291667 |
| Circle | WelschOptIn | test | 72 | 0.042047 | 1.272204 | 2.915767 | 0.013889 | 0.000000 | 0.033999 | 7.346500 / 8.136400 | 2164545.105556 |
| Line | WelschOptIn | validation | 24 | 0.044098 | 0.140368 | 0.282194 | 0.000000 | 0.000000 | 0.016963 | 1.838100 / 4.231600 | 521605.375000 |
| Line | WelschOptIn | test | 72 | 0.049977 | 0.492977 | 0.657435 | 0.000000 | 0.000000 | 0.023585 | 1.786400 / 4.109100 | 466151.675000 |

## Reproduction

`& "./scripts/run-operator-product-e2e-benchmark.ps1" -Profile acceptance -Label after -IncludeCandidates -ResultsDirectory ".tmp/operator-product-e2e"` 
