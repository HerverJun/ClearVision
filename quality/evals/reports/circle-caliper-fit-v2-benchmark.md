# Circle Caliper Fit V2 Benchmark

> Date: 2026-07-03
> Contract: `caliper-circle-fit.v2`
> Command: `./scripts/run-dotnet-test-serial.ps1 -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" -FullyQualifiedName CircleCaliperFitV2BenchmarkTests -Verbosity normal`
> Policy: baseline evidence only; no brittle CI millisecond threshold.

| Size | Calipers | Avg ms | Avg allocated bytes | Elapsed variance | Max allocated bytes |
|---|---:|---:|---:|---:|---:|
| 320x240 | 128 | 18.690 | 2442173 | 3.534174 | 2442688 |
| 640x480 | 128 | 18.853 | 2442173 | 8.633057 | 2442688 |
| 1920x1080 | 128 | 16.241 | 2442173 | 18.278504 | 2442688 |

Result: PASS. Managed allocation stayed flat across image sizes because the kernel samples bounded caliper profiles instead of allocating per-pixel image-size buffers.
