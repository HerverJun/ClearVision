# DeepLearning Provider Inference Baseline

GeneratedAtUtc: `2026-04-28T16:05:32.8845552+00:00`

## Summary

| Cases | Passed | Failed | Available providers |
| ---: | ---: | ---: | --- |
| 1 | 1 | 0 | CPUExecutionProvider |

## Cases

| Case | Required | RequestedProvider | ActiveProvider | FallbackToCpu | RealOnnxInference | Passed | Latency ms | Provider failure |
| --- | --- | --- | --- | --- | --- | --- | ---: | --- |
| cpu_smoke_identity | True | CPUExecutionProvider | CPUExecutionProvider | False | True | True | 50.256 | :  |

GPU/CUDA/TensorRT cases are optional/manual. Missing providers are reported as optional evidence gaps, not as enabled GPU inference.
