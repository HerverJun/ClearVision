# Circle Caliper Fit V2 Benchmark

> Date: 2026-07-04
> Contract: `caliper-circle-fit.v2`
> Command: `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" -FullyQualifiedName CircleCaliperFitV2BenchmarkTests -NoRestore -Verbosity normal`
> Environment: Microsoft Windows 10.0.22000; arch=X64; processors=20; serverGC=False
> Policy: baseline evidence only; no brittle CI millisecond threshold.

| Profile | Size | Calipers | Samples | Work units | p50 ms | p95 ms | Avg ms | Avg allocated bytes | Elapsed variance | Max allocated bytes |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| typical | 320x240 | 128 | 129 | 82560 | 20.154 | 40.869 | 23.394 | 2463978 | 53.575441 | 2464440 |
| upper-bounded | 320x240 | 256 | 1025 | 1312000 | 183.066 | 226.698 | 190.039 | 34160786 | 241.393076 | 34160944 |
| typical | 640x480 | 128 | 129 | 82560 | 11.356 | 12.635 | 11.623 | 2464019 | 0.395459 | 2464440 |
| upper-bounded | 640x480 | 256 | 1025 | 1312000 | 178.987 | 183.526 | 179.555 | 34306031 | 5.870546 | 34306136 |
| typical | 1920x1080 | 128 | 129 | 82560 | 11.366 | 13.412 | 11.815 | 2463862 | 0.895374 | 2464440 |
| upper-bounded | 1920x1080 | 256 | 1025 | 1312000 | 178.041 | 186.642 | 180.285 | 34305210 | 18.646193 | 34305368 |

| Budget case | Work units | Elapsed ms | Result |
|---|---:|---:|---|
| near-budget legal | 7864320 | 955.194 | PASS |
| over-budget rejected | 8847360 | 0.024 | InvalidInput |

Result: PASS. Typical allocation stayed below 8 MB and upper-bounded profile evidence stayed below the 64 MB guard. The over-budget request failed closed before collecting edge points.
