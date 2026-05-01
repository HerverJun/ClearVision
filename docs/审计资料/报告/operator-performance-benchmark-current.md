# Operator Performance Benchmark Report

- Generated UTC: 2026-04-29T01:09:07.8697545+00:00
- Mode: smoke
- Warmup iterations: 1
- Measured iterations: 3
- Cases: 4/4 passed
- Total runtime: 13.486 ms
- Total allocated bytes: 749176

## Operator Summary

| Operator | Cases | Passed | Failed | Mean ms | P95 ms | Alloc/iter bytes | Scenarios |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CaliperTool | 1 | 1 | 0 | 1.445 | 1.555 | 162512 | measurement |
| EdgeDetection | 1 | 1 | 0 | 1.818 | 1.846 | 13200 | edge |
| MeanFilter | 1 | 1 | 0 | 1.012 | 1.404 | 3245 | preprocess |
| TranslationRotationCalibration | 1 | 1 | 0 | 0.220 | 0.285 | 70768 | calibration_geometry |

## Cases

| Case | Operator | Scenario | Mean ms | Min ms | Max ms | P95 ms | Alloc/iter bytes | Passed | Error |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| mean_filter_640x480_k5 | MeanFilter | preprocess | 1.012 | 0.815 | 1.404 | 1.404 | 3245 | True |  |
| caliper_tool_horizontal_edge_pair | CaliperTool | measurement | 1.445 | 1.351 | 1.555 | 1.555 | 162512 | True |  |
| edge_detection_640x480_auto_threshold | EdgeDetection | edge | 1.818 | 1.797 | 1.846 | 1.846 | 13200 | True |  |
| translation_rotation_calibration_20_points_svd | TranslationRotationCalibration | calibration_geometry | 0.220 | 0.182 | 0.285 | 0.285 | 70768 | True |  |
