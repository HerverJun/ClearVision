# Operator Performance Benchmark

Lightweight benchmark entry for representative ClearVision operators.

## Runner

```powershell
dotnet run --project quality/tools/OperatorPerformanceBenchmarkRunner/OperatorPerformanceBenchmarkRunner.csproj -- `
  --mode smoke `
  --output artifacts/operator-performance-benchmark.json `
  --report artifacts/operator-performance-benchmark.md
```

## Coverage

| Operator | Scenario | Purpose |
| --- | --- | --- |
| MeanFilter | 640x480 synthetic texture, kernel 5 | Preprocess image throughput smoke baseline |
| CaliperTool | Horizontal edge-pair synthetic target | Measurement path and edge-pair extraction latency |
| EdgeDetection | 640x480 synthetic scene with auto threshold | Edge extraction and threshold path latency |
| TranslationRotationCalibration | 20 synthetic point pairs, SVD | Calibration/geometry solve latency |

## Modes

`smoke` is intended for CI and uses short warmup/measured loops. `local` raises iteration counts and enables the CaliperTool subpixel path. For longer trend runs, pin the machine profile, run from a release build, and archive both JSON and Markdown artifacts with the commit SHA.
