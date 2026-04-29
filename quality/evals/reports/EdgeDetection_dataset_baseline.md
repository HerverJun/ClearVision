# EdgeDetection Dataset Benchmark Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-29T03:21:29.7823198+00:00`
Dataset: `BSDS-style semi-synthetic edge benchmark protocol bridge`
DatasetKind: `Tier A protocol bridge for public/BSDS-style edge-detection metrics; no external image pixels are stored.`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 36 |
| Passed | 36 |
| Failed | 0 |
| Total pixels | 612864 |
| Expected edge pixels | 23111 |
| Predicted edge pixels | 23111 |
| True positives | 23111 |
| False positives | 0 |
| False negatives | 0 |
| Precision | 1 |
| Recall | 1 |
| F1 | 1 |
| Mean boundary F1 | 1 |
| Boundary tolerance px | 1 |
| Runtime ms | 321.894 |

## Scenarios

| Scenario | Cases | Passed | Failed | Expected edges | Predicted edges | FP | FN | F1 | Boundary F1 | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| blurred_noise | 6 | 6 | 0 | 1831 | 1831 | 0 | 0 | 1 | 1 | 1.911 |
| color_input_edges | 6 | 6 | 0 | 3278 | 3278 | 0 | 0 | 1 | 1 | 1.78 |
| diagonal_edges | 6 | 6 | 0 | 4371 | 4371 | 0 | 0 | 1 | 1 | 1.811 |
| hard_step_shapes | 6 | 6 | 0 | 3235 | 3235 | 0 | 0 | 1 | 1 | 43.455 |
| low_contrast_auto_threshold | 6 | 6 | 0 | 1381 | 1381 | 0 | 0 | 1 | 1 | 2.763 |
| thin_lines | 6 | 6 | 0 | 9015 | 9015 | 0 | 0 | 1 | 1 | 1.928 |

## Failure Boundaries

- `hard_step_shapes` verifies canonical Canny step-edge extraction on rectangles, circles, and lines.
- `diagonal_edges` verifies diagonal and L2-gradient edge behavior.
- `thin_lines` verifies 1 px line structures and sparse edge maps.
- `low_contrast_auto_threshold` verifies auto-threshold median logic and low-contrast boundaries.
- `blurred_noise` verifies the Gaussian prefilter path under deterministic noise.
- `color_input_edges` verifies color input conversion into edge benchmark scoring.
- This bridge records BSDS-style edge benchmark metrics for the EdgeDetection Canny path; it is not field-image accuracy evidence.

## Cases

| Case | Scenario | Passed | Size | Source | Auto | Blur | Aperture | L2 | Thresholds | Expected | Predicted | FP | FN | F1 | Boundary F1 | Runtime ms | Failure |
| --- | --- | --- | --- | --- | --- | --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| EdgeDetection_hard_step_shapes_0000 | hard_step_shapes | True | 64x48 | grayscale | False | False | 3 | False | 35/120 | 214 | 214 | 0 | 0 | 1 | 1 | 249.833 | - |
| EdgeDetection_diagonal_edges_0000 | diagonal_edges | True | 64x48 | grayscale | False | False | 3 | True | 28/96 | 266 | 266 | 0 | 0 | 1 | 1 | 0.876 | - |
| EdgeDetection_thin_lines_0000 | thin_lines | True | 64x48 | grayscale | False | False | 3 | False | 24/82 | 475 | 475 | 0 | 0 | 1 | 1 | 0.769 | - |
| EdgeDetection_low_contrast_auto_threshold_0000 | low_contrast_auto_threshold | True | 64x48 | grayscale | True | True | 3 | False | 67.68/120.32 | 102 | 102 | 0 | 0 | 1 | 1 | 4.506 | - |
| EdgeDetection_blurred_noise_0000 | blurred_noise | True | 64x48 | grayscale | False | True | 3 | True | 45/130 | 138 | 138 | 0 | 0 | 1 | 1 | 0.913 | - |
| EdgeDetection_color_input_edges_0000 | color_input_edges | True | 64x48 | bgr | False | False | 3 | False | 42/136 | 215 | 215 | 0 | 0 | 1 | 1 | 0.789 | - |
| EdgeDetection_hard_step_shapes_0001 | hard_step_shapes | True | 96x64 | grayscale | False | False | 3 | False | 36/121 | 311 | 311 | 0 | 0 | 1 | 1 | 0.982 | - |
| EdgeDetection_diagonal_edges_0001 | diagonal_edges | True | 96x64 | grayscale | False | False | 3 | True | 29/97 | 398 | 398 | 0 | 0 | 1 | 1 | 0.859 | - |
| EdgeDetection_thin_lines_0001 | thin_lines | True | 96x64 | grayscale | False | False | 3 | False | 25/83 | 807 | 807 | 0 | 0 | 1 | 1 | 0.899 | - |
| EdgeDetection_low_contrast_auto_threshold_0001 | low_contrast_auto_threshold | True | 96x64 | grayscale | True | True | 3 | False | 63.92/124.08 | 142 | 142 | 0 | 0 | 1 | 1 | 0.933 | - |
| EdgeDetection_blurred_noise_0001 | blurred_noise | True | 96x64 | grayscale | False | True | 3 | False | 46/131 | 197 | 197 | 0 | 0 | 1 | 1 | 0.911 | - |
| EdgeDetection_color_input_edges_0001 | color_input_edges | True | 96x64 | bgr | False | False | 3 | False | 43/137 | 322 | 322 | 0 | 0 | 1 | 1 | 0.892 | - |
| EdgeDetection_hard_step_shapes_0002 | hard_step_shapes | True | 128x96 | grayscale | False | False | 3 | False | 37/122 | 502 | 502 | 0 | 0 | 1 | 1 | 1.452 | - |
| EdgeDetection_diagonal_edges_0002 | diagonal_edges | True | 128x96 | grayscale | False | False | 5 | True | 30/98 | 714 | 714 | 0 | 0 | 1 | 1 | 1.586 | - |
| EdgeDetection_thin_lines_0002 | thin_lines | True | 128x96 | grayscale | False | False | 3 | False | 26/84 | 1454 | 1454 | 0 | 0 | 1 | 1 | 1.593 | - |
| EdgeDetection_low_contrast_auto_threshold_0002 | low_contrast_auto_threshold | True | 128x96 | grayscale | True | True | 3 | False | 60.16/127.84 | 208 | 208 | 0 | 0 | 1 | 1 | 2.371 | - |
| EdgeDetection_blurred_noise_0002 | blurred_noise | True | 128x96 | grayscale | False | True | 3 | True | 47/132 | 273 | 273 | 0 | 0 | 1 | 1 | 1.704 | - |
| EdgeDetection_color_input_edges_0002 | color_input_edges | True | 128x96 | bgr | False | False | 3 | False | 44/138 | 508 | 508 | 0 | 0 | 1 | 1 | 1.722 | - |
| EdgeDetection_hard_step_shapes_0003 | hard_step_shapes | True | 160x120 | grayscale | False | False | 3 | False | 38/123 | 646 | 646 | 0 | 0 | 1 | 1 | 3.088 | - |
| EdgeDetection_diagonal_edges_0003 | diagonal_edges | True | 160x120 | grayscale | False | False | 3 | True | 31/99 | 777 | 777 | 0 | 0 | 1 | 1 | 2.101 | - |
| EdgeDetection_thin_lines_0003 | thin_lines | True | 160x120 | grayscale | False | False | 3 | False | 27/85 | 1795 | 1795 | 0 | 0 | 1 | 1 | 2.42 | - |
| EdgeDetection_low_contrast_auto_threshold_0003 | low_contrast_auto_threshold | True | 160x120 | grayscale | True | True | 3 | False | 67.68/120.32 | 270 | 270 | 0 | 0 | 1 | 1 | 2.517 | - |
| EdgeDetection_blurred_noise_0003 | blurred_noise | True | 160x120 | grayscale | False | True | 3 | False | 48/133 | 348 | 348 | 0 | 0 | 1 | 1 | 1.962 | - |
| EdgeDetection_color_input_edges_0003 | color_input_edges | True | 160x120 | bgr | False | False | 3 | False | 45/139 | 654 | 654 | 0 | 0 | 1 | 1 | 1.741 | - |
| EdgeDetection_hard_step_shapes_0004 | hard_step_shapes | True | 192x128 | grayscale | False | False | 3 | False | 39/124 | 709 | 709 | 0 | 0 | 1 | 1 | 2.175 | - |
| EdgeDetection_diagonal_edges_0004 | diagonal_edges | True | 192x128 | grayscale | False | False | 3 | True | 32/100 | 876 | 876 | 0 | 0 | 1 | 1 | 2.195 | - |
| EdgeDetection_thin_lines_0004 | thin_lines | True | 192x128 | grayscale | False | False | 3 | False | 28/86 | 2018 | 2018 | 0 | 0 | 1 | 1 | 2.553 | - |
| EdgeDetection_low_contrast_auto_threshold_0004 | low_contrast_auto_threshold | True | 192x128 | grayscale | True | True | 3 | False | 63.92/124.08 | 294 | 294 | 0 | 0 | 1 | 1 | 2.475 | - |
| EdgeDetection_blurred_noise_0004 | blurred_noise | True | 192x128 | grayscale | False | True | 3 | True | 49/134 | 390 | 390 | 0 | 0 | 1 | 1 | 2.422 | - |
| EdgeDetection_color_input_edges_0004 | color_input_edges | True | 192x128 | bgr | False | False | 3 | False | 46/140 | 724 | 724 | 0 | 0 | 1 | 1 | 2.411 | - |
| EdgeDetection_hard_step_shapes_0005 | hard_step_shapes | True | 256x144 | grayscale | False | False | 3 | False | 40/125 | 853 | 853 | 0 | 0 | 1 | 1 | 3.203 | - |
| EdgeDetection_diagonal_edges_0005 | diagonal_edges | True | 256x144 | grayscale | False | False | 5 | True | 33/101 | 1340 | 1340 | 0 | 0 | 1 | 1 | 3.249 | - |
| EdgeDetection_thin_lines_0005 | thin_lines | True | 256x144 | grayscale | False | False | 3 | False | 29/87 | 2466 | 2466 | 0 | 0 | 1 | 1 | 3.333 | - |
| EdgeDetection_low_contrast_auto_threshold_0005 | low_contrast_auto_threshold | True | 256x144 | grayscale | True | True | 3 | False | 60.16/127.84 | 365 | 365 | 0 | 0 | 1 | 1 | 3.776 | - |
| EdgeDetection_blurred_noise_0005 | blurred_noise | True | 256x144 | grayscale | False | True | 3 | False | 50/135 | 485 | 485 | 0 | 0 | 1 | 1 | 3.555 | - |
| EdgeDetection_color_input_edges_0005 | color_input_edges | True | 256x144 | bgr | False | False | 3 | False | 47/141 | 855 | 855 | 0 | 0 | 1 | 1 | 3.128 | - |
