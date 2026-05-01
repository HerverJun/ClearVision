# SurfaceDefectDetection KolektorSDD2 Failure Taxonomy v1

GeneratedAtUtc: `2026-05-01T03:24:21+00:00`

## Summary

| Taxonomy | Count |
|---|---:|
| low_contrast_defect_miss | 15 |
| mask_overgrowth_false_positive | 1 |
| oversegmentation_false_positive | 2 |
| small_defect_miss | 2 |
| texture_noise_false_positive | 123 |
| undersegmentation_false_negative | 46 |

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
| 20564 | True | False | 0.0000 | 0 | 107 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20640 | True | False | 0.0000 | 0 | 194 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20667 | True | False | 0.0000 | 0 | 133 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20671 | True | False | 0.0000 | 0 | 2968 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20744 | True | False | 0.0000 | 0 | 452 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20821 | True | False | 0.0000 | 0 | 188 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20895 | True | False | 0.0000 | 0 | 246 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20969 | True | False | 0.0000 | 0 | 579 | low_contrast_defect_miss | Compare local background kernel sizes and response normalization for low-contrast positives. |
| 20942 | True | True | 0.3005 | 111 | 24 | mask_overgrowth_false_positive | Tighten cleanup or postprocess boundaries; prefer shape filters over global threshold increases. |
| 20054 | False | True | 0.0000 | 120 | 0 | oversegmentation_false_positive | Tune morphology and max-area handling; inspect whether broad response bands should be suppressed. |
| 20462 | False | True | 0.0000 | 83 | 0 | oversegmentation_false_positive | Tune morphology and max-area handling; inspect whether broad response bands should be suppressed. |
| 20236 | True | False | 0.0000 | 0 | 64 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20259 | True | False | 0.0000 | 0 | 55 | small_defect_miss | Use lower local-contrast floor on validation positives and protect with replay false-positive gate. |
| 20006 | False | True | 0.0000 | 11 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20008 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20015 | False | True | 0.0000 | 11 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20017 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20018 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20021 | False | True | 0.0000 | 34 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20023 | False | True | 0.0000 | 182 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20027 | False | True | 0.0000 | 24 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20080 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20083 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20091 | False | True | 0.0000 | 88 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20105 | False | True | 0.0000 | 12 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20109 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20111 | False | True | 0.0000 | 95 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20112 | False | True | 0.0000 | 20 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20113 | False | True | 0.0000 | 12 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20134 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20156 | False | True | 0.0000 | 8 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20160 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20162 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20163 | False | True | 0.0000 | 99 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20171 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20183 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20187 | False | True | 0.0000 | 45 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20192 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20198 | False | True | 0.0000 | 38 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20201 | False | True | 0.0000 | 25 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20214 | False | True | 0.0000 | 15 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20216 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20229 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20233 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20235 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20237 | False | True | 0.0000 | 11 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20239 | False | True | 0.0000 | 28 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20241 | False | True | 0.0000 | 11 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20257 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20262 | False | True | 0.0000 | 18 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20263 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20265 | False | True | 0.0000 | 25 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20267 | False | True | 0.0000 | 23 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20269 | False | True | 0.0000 | 14 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20273 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20292 | False | True | 0.0000 | 31 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20302 | False | True | 0.0000 | 14 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20304 | False | True | 0.0000 | 26 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20310 | False | True | 0.0000 | 14 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20312 | False | True | 0.0000 | 37 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20322 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20340 | False | True | 0.0000 | 20 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20353 | False | True | 0.0000 | 12 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20354 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20366 | False | True | 0.0000 | 13 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20368 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20372 | False | True | 0.0000 | 17 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20401 | False | True | 0.0000 | 24 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20405 | False | True | 0.0000 | 9 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20411 | False | True | 0.0000 | 11 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20415 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20435 | False | True | 0.0000 | 12 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
| 20437 | False | True | 0.0000 | 10 | 0 | texture_noise_false_positive | Raise area/noise guard or add connected-component shape filtering while preserving defect recall. |
