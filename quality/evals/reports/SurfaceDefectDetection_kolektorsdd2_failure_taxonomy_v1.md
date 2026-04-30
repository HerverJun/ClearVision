# SurfaceDefectDetection KolektorSDD2 Failure Taxonomy v1

GeneratedAtUtc: `2026-04-29T15:39:12+00:00`

## Summary

| Taxonomy | Count |
|---|---:|
| low_contrast_defect_miss | 19 |
| mask_overgrowth_false_positive | 1 |
| oversegmentation_false_positive | 1 |
| small_defect_miss | 7 |
| texture_noise_false_positive | 45 |
| undersegmentation_false_negative | 37 |

## Cases

| Case | Is defect | Predicted | Pixel F1 | FP px | FN px | Taxonomy | Next action |
|---|---|---|---:|---:|---:|---|---|
| 20068 | True | False | 0.0000 | 0 | 369 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20139 | True | False | 0.0000 | 0 | 98 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20141 | True | False | 0.0000 | 0 | 546 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20159 | True | False | 0.0000 | 0 | 120 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20281 | True | False | 0.0000 | 0 | 331 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20426 | True | False | 0.0000 | 0 | 108 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20439 | True | False | 0.0000 | 0 | 1617 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20523 | True | False | 0.0000 | 0 | 155 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20564 | True | False | 0.0000 | 0 | 107 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20625 | True | False | 0.0000 | 0 | 516 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20632 | True | False | 0.0000 | 0 | 148 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20640 | True | False | 0.0000 | 0 | 194 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20667 | True | False | 0.0000 | 0 | 133 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20671 | True | False | 0.0000 | 0 | 2968 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20744 | True | False | 0.0000 | 0 | 452 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20772 | True | False | 0.0000 | 0 | 680 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20821 | True | False | 0.0000 | 0 | 188 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20895 | True | False | 0.0000 | 0 | 246 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20969 | True | False | 0.0000 | 0 | 579 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20942 | True | True | 0.2956 | 120 | 23 | mask_overgrowth_false_positive | Tighten cleanup or postprocess boundaries; prefer shape filters over global threshold increases. |
| 20054 | False | True | 0.0000 | 192 | 0 | oversegmentation_false_positive | Tune morphology and max-area handling; inspect whether broad response bands should be suppressed. |
| 20228 | True | False | 0.0000 | 0 | 14 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20236 | True | False | 0.0000 | 0 | 64 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20259 | True | False | 0.0000 | 0 | 55 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20544 | True | False | 0.0000 | 0 | 31 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20754 | True | False | 0.0000 | 0 | 74 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20801 | True | False | 0.0000 | 0 | 70 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20816 | True | False | 0.0000 | 0 | 30 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20017 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20021 | False | True | 0.0000 | 36 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20023 | False | True | 0.0000 | 184 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20083 | False | True | 0.0000 | 16 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20091 | False | True | 0.0000 | 107 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20111 | False | True | 0.0000 | 104 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20113 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20160 | False | True | 0.0000 | 20 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20163 | False | True | 0.0000 | 94 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20171 | False | True | 0.0000 | 24 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20187 | False | True | 0.0000 | 52 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20214 | False | True | 0.0000 | 23 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20216 | False | True | 0.0000 | 31 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20239 | False | True | 0.0000 | 19 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20265 | False | True | 0.0000 | 19 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20292 | False | True | 0.0000 | 42 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20302 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20354 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20401 | False | True | 0.0000 | 15 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20462 | False | True | 0.0000 | 76 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20475 | False | True | 0.0000 | 27 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20510 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20548 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20549 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20573 | False | True | 0.0000 | 26 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20606 | False | True | 0.0000 | 24 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20614 | False | True | 0.0000 | 19 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20630 | False | True | 0.0000 | 19 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20658 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20673 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20674 | False | True | 0.0000 | 20 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20696 | False | True | 0.0000 | 24 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20700 | False | True | 0.0000 | 19 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20711 | False | True | 0.0000 | 15 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20719 | False | True | 0.0000 | 16 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20743 | False | True | 0.0000 | 15 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20756 | False | True | 0.0000 | 63 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20769 | False | True | 0.0000 | 41 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20793 | False | True | 0.0000 | 20 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20799 | False | True | 0.0000 | 26 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20843 | False | True | 0.0000 | 16 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20879 | False | True | 0.0000 | 15 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20934 | False | True | 0.0000 | 24 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20936 | False | True | 0.0000 | 16 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20999 | False | True | 0.0000 | 23 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20095 | True | True | 0.0826 | 0 | 689 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
| 20118 | True | True | 0.0579 | 0 | 813 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
| 20121 | True | True | 0.2005 | 0 | 933 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
| 20155 | True | True | 0.2256 | 6 | 145 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
| 20158 | True | True | 0.0675 | 26 | 2655 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
| 20172 | True | True | 0.1053 | 0 | 357 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
| 20193 | True | True | 0.0145 | 0 | 6945 | undersegmentation_false_negative | Relax threshold or morphology only for validation positives with stable false-positive budget. |
