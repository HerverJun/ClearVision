# DeepLearning Provider Inference Baseline

EvidencePurpose: `InferenceSmokeOnly`
GeneratedAtUtc: `2026-09-01T05:11:27.7895243+00:00`
Git SHA / dirty: `376174d830621d284c0d5da0b40a9b6c219a9150` / `True`
Model content SHA256: `4013a532ea9eb05dd86508e56b3839f9e8f581f76d1e86bc6f61c2f42227343e`
Tool / environment: `DeepLearningProviderInferenceRunner/2026-09-01.wave3b` / `Microsoft Windows 10.0.26200; X64; .NET 8.0.26`

## Summary

| Cases | Passed | Failed | Available providers |
| ---: | ---: | ---: | --- |
| 3 | 1 | 0 | CPUExecutionProvider |

## Cases

| Case | Required | RequestedProvider | ActiveProvider | ProfileStatus | FallbackToCpu | RealOnnxInference | Passed | Latency ms | Provider failure |
| --- | --- | --- | --- | --- | --- | --- | --- | ---: | --- |
| cpu_smoke_identity | True | CPUExecutionProvider | CPUExecutionProvider | smoke-validated | False | True | True | 68.558 | :  |
| cuda_smoke_identity_optional | False | CUDAExecutionProvider | NotRun | unvalidated | False | False | False | 0 | :  |
| tensorrt_smoke_identity_optional | False | TensorrtExecutionProvider | NotRun | unvalidated | False | False | False | 0 | :  |

GPU/CUDA/TensorRT cases are optional/manual. Missing providers are reported as optional evidence gaps, not as enabled GPU inference.
