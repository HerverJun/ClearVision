# DeepLearning Runtime Benchmark

GeneratedAtUtc: `2026-04-26T15:30:45.1735996+00:00`
Scope: `preprocess+YOLO postprocess benchmark; ONNX provider availability/fallback metadata is recorded without model inference`

## Provider

| Metric | Value |
| --- | --- |
| Requested provider | GPU |
| Active provider | CPUExecutionProvider |
| Fallback to CPU | True |
| Available providers | AzureExecutionProvider, CPUExecutionProvider |

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Runtime ms | 753.676 |
| Memory bytes | 718330568 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg total ms | Avg preprocess ms | Avg postprocess ms | Avg detections |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1080p_cpu_preprocess_postprocess | 6 | 6 | 0 | 27.836 | 20.291 | 4.385 | 4 |
| 4k_cpu_preprocess_postprocess | 4 | 4 | 0 | 29.85 | 22.324 | 3.009 | 4 |
| batch_pressure_1080p_x4 | 5 | 5 | 0 | 78.424 | 66.371 | 10.931 | 16 |
| gpu_cpu_fallback_contract | 5 | 5 | 0 | 15.028 | 11.72 | 2.775 | 4 |

## Cases

| Case | Scenario | Passed | Size | Batch | Total ms | Pre ms | Post ms | Detections | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1080p_cpu_preprocess_postprocess_0000 | 1080p_cpu_preprocess_postprocess | True | 1920x1080 | 1 | 52.368 | 27.521 | 11.385 | 4 | - |
| 1080p_cpu_preprocess_postprocess_0001 | 1080p_cpu_preprocess_postprocess | True | 1920x1080 | 1 | 22.853 | 18.656 | 3.089 | 4 | - |
| 1080p_cpu_preprocess_postprocess_0002 | 1080p_cpu_preprocess_postprocess | True | 1920x1080 | 1 | 22.231 | 18.223 | 2.935 | 4 | - |
| 1080p_cpu_preprocess_postprocess_0003 | 1080p_cpu_preprocess_postprocess | True | 1920x1080 | 1 | 23.519 | 19.386 | 3.029 | 4 | - |
| 1080p_cpu_preprocess_postprocess_0004 | 1080p_cpu_preprocess_postprocess | True | 1920x1080 | 1 | 22.951 | 18.733 | 3.088 | 4 | - |
| 1080p_cpu_preprocess_postprocess_0005 | 1080p_cpu_preprocess_postprocess | True | 1920x1080 | 1 | 23.091 | 19.228 | 2.782 | 4 | - |
| 4k_cpu_preprocess_postprocess_0000 | 4k_cpu_preprocess_postprocess | True | 3840x2160 | 1 | 29.235 | 21.988 | 3.124 | 4 | - |
| 4k_cpu_preprocess_postprocess_0001 | 4k_cpu_preprocess_postprocess | True | 3840x2160 | 1 | 30.394 | 22.942 | 2.864 | 4 | - |
| 4k_cpu_preprocess_postprocess_0002 | 4k_cpu_preprocess_postprocess | True | 3840x2160 | 1 | 30.205 | 22.255 | 3.32 | 4 | - |
| 4k_cpu_preprocess_postprocess_0003 | 4k_cpu_preprocess_postprocess | True | 3840x2160 | 1 | 29.568 | 22.111 | 2.728 | 4 | - |
| batch_pressure_1080p_x4_0000 | batch_pressure_1080p_x4 | True | 1920x1080 | 4 | 120.223 | 106.539 | 12.597 | 16 | - |
| batch_pressure_1080p_x4_0001 | batch_pressure_1080p_x4 | True | 1920x1080 | 4 | 68.328 | 56.962 | 10.274 | 16 | - |
| batch_pressure_1080p_x4_0002 | batch_pressure_1080p_x4 | True | 1920x1080 | 4 | 69.372 | 57.576 | 10.705 | 16 | - |
| batch_pressure_1080p_x4_0003 | batch_pressure_1080p_x4 | True | 1920x1080 | 4 | 66.786 | 55.322 | 10.352 | 16 | - |
| batch_pressure_1080p_x4_0004 | batch_pressure_1080p_x4 | True | 1920x1080 | 4 | 67.412 | 55.456 | 10.726 | 16 | - |
| gpu_cpu_fallback_contract_0000 | gpu_cpu_fallback_contract | True | 1280x720 | 1 | 14.808 | 11.602 | 2.63 | 4 | - |
| gpu_cpu_fallback_contract_0001 | gpu_cpu_fallback_contract | True | 1280x720 | 1 | 15.621 | 12.23 | 2.884 | 4 | - |
| gpu_cpu_fallback_contract_0002 | gpu_cpu_fallback_contract | True | 1280x720 | 1 | 14.497 | 11.471 | 2.529 | 4 | - |
| gpu_cpu_fallback_contract_0003 | gpu_cpu_fallback_contract | True | 1280x720 | 1 | 15.496 | 12.05 | 2.878 | 4 | - |
| gpu_cpu_fallback_contract_0004 | gpu_cpu_fallback_contract | True | 1280x720 | 1 | 14.718 | 11.245 | 2.954 | 4 | - |

## Notes

- This benchmark uses deterministic generated images and controlled YOLOv8 tensors.
- It measures DeepLearningOperator preprocessing and YOLO post-processing paths, not model accuracy.
- GPU/CPU fallback is recorded from ONNX Runtime provider availability because no production ONNX model is required for this contract benchmark.
