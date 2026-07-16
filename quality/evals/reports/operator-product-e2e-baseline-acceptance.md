# Product Operator E2E Benchmark (baseline)

- Product SHA: `ce266626e0bec0a8cd4a68c11b176df95e8cb482` (dirty=False)
- Product Infrastructure assembly SHA: `9c1e4b708a4c184e1dbf16ac0bbfdb75ae96b94d54acf4c83716ee8f1797c626`
- Harness commit/program SHA: `727414e2ca6bd5785aac3f2dbc68fb5b8badc369` / `572dfc2826d0eb597839442c0b2b2e49040348287fc01174288e18e803a57b9f` (dirty=False)
- Dataset manifest/generated SHA: `a507e7345388017506dc60f544598721042a0cdaa12dd0f2402cb6236256eaeb` / `5d60098525547bf873b9e4618b3d7b5a08bf202164295dac9c2eff6f99c2507a`
- Claim boundary: Synthetic raster end-to-end product-operator evidence only. It is not E4, commercial-grade, field-accuracy, release-readiness, or production-site validation evidence.
- Managed allocation scope: GC.GetAllocatedBytesForCurrentThread on the benchmark thread only; it is not full-process allocation or native OpenCV allocation.
- Full-process resources: peak working set=117391360 B; private bytes=68894720 B

| Domain | Algorithm | Split | Cases | Bias | RMSE | P95 | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Managed alloc B/case |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Circle | LegacyDefault | validation | 24 | 0.096774 | 1.068459 | 2.151906 | 0.000000 | 0.000000 | 0.080295 | 7.334000 / 8.105100 | 1981638.975000 |
| Circle | LegacyDefault | test | 72 | 0.229830 | 1.175745 | 2.419451 | 0.000000 | 0.000000 | 0.033999 | 7.307900 / 8.197200 | 1980956.636111 |
| Line | L2Default | validation | 24 | 0.066164 | 0.185610 | 0.318032 | 0.000000 | 0.000000 | 0.000000 | 1.920500 / 3.619000 | 349217.216667 |
| Line | L2Default | test | 72 | 0.059528 | 0.497887 | 0.780994 | 0.000000 | 0.000000 | 0.000000 | 1.959200 / 3.717100 | 330720.083333 |

## Reproduction

`& "./scripts/reproduce-operator-precision-baseline.ps1" -Profile acceptance -ResultsDirectory ".tmp/operator-product-e2e"`
