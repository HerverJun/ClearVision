# Feature Match Contract Baseline

GeneratedAtUtc: `2026-04-26T12:44:43.3566447+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 44 |
| Passed | 44 |
| Failed | 0 |
| Runtime ms | 423.143 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| AkazeFeatureMatch | 22 | 22 | 0 | 11.666 | 53554 |
| OrbFeatureMatch | 22 | 22 | 0 | 7.568 | 152028 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Failure contract | 8 | 8 | 0 | 4.754 |
| Input formats | 4 | 4 | 0 | 8.82 |
| Matcher options | 6 | 6 | 0 | 9.465 |
| Origin contract | 4 | 4 | 0 | 9.748 |
| Positive localization | 2 | 2 | 0 | 72.254 |
| Scale and rotation | 6 | 6 | 0 | 9.429 |
| Template source | 4 | 4 | 0 | 13.135 |
| Validation contract | 10 | 10 | 0 | 0.043 |

## Cases

| Case | Operator | Scenario | Passed | Runtime ms | IsMatch | Inliers | Score | Position Error | Failure |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | --- |
| AkazeFeatureMatch_translation_center_input | AkazeFeatureMatch | Positive localization | Yes | 41.51 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_translation_topleft_origin | AkazeFeatureMatch | Origin contract | Yes | 14.761 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_translation_custom_origin | AkazeFeatureMatch | Origin contract | Yes | 13.754 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_template_path_center | AkazeFeatureMatch | Template source | Yes | 25.431 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_template_path_cache_repeat | AkazeFeatureMatch | Template source | Yes | 14.778 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_symmetry_disabled_translation | AkazeFeatureMatch | Matcher options | Yes | 13.74 | True | 83 | 0.987 | 0.039 | - |
| AkazeFeatureMatch_min_match_count_four | AkazeFeatureMatch | Matcher options | Yes | 13.971 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_max_features_low_boundary | AkazeFeatureMatch | Matcher options | Yes | 17.168 | True | 74 | 1 | 0 | - |
| AkazeFeatureMatch_scaled_up | AkazeFeatureMatch | Scale and rotation | Yes | 15.283 | True | 58 | 0.972 | 0.136 | - |
| AkazeFeatureMatch_scaled_down | AkazeFeatureMatch | Scale and rotation | Yes | 13.446 | True | 41 | 0.975 | 0.106 | - |
| AkazeFeatureMatch_rotated_small_angle | AkazeFeatureMatch | Scale and rotation | Yes | 13.138 | True | 62 | 0.992 | 0.018 | - |
| AkazeFeatureMatch_grayscale_inputs | AkazeFeatureMatch | Input formats | Yes | 12.861 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_color_scene_grayscale_template | AkazeFeatureMatch | Input formats | Yes | 12.803 | True | 82 | 1 | 0 | - |
| AkazeFeatureMatch_blank_scene_no_features | AkazeFeatureMatch | Failure contract | Yes | 10.809 | False | 0 | 0 | 0 | - |
| AkazeFeatureMatch_blank_template_no_features | AkazeFeatureMatch | Failure contract | Yes | 10.295 | False | 0 | 0 | 0 | - |
| AkazeFeatureMatch_missing_template_source | AkazeFeatureMatch | Failure contract | Yes | 12.509 | False | 0 | 0 | 0 | - |
| AkazeFeatureMatch_operator_failure_without_image | AkazeFeatureMatch | Failure contract | Yes | 0.166 | False | 0 | 0 | 0 | - |
| AkazeFeatureMatch_validate_defaults | AkazeFeatureMatch | Validation contract | Yes | 0.156 | - | - | - | - | - |
| AkazeFeatureMatch_validate_min_match_low_invalid | AkazeFeatureMatch | Validation contract | Yes | 0.052 | - | - | - | - | - |
| AkazeFeatureMatch_validate_min_match_high_invalid | AkazeFeatureMatch | Validation contract | Yes | 0.006 | - | - | - | - | - |
| AkazeFeatureMatch_validate_threshold_low_invalid | AkazeFeatureMatch | Validation contract | Yes | 0.011 | - | - | - | - | - |
| AkazeFeatureMatch_validate_threshold_high_invalid | AkazeFeatureMatch | Validation contract | Yes | 0.004 | - | - | - | - | - |
| OrbFeatureMatch_translation_center_input | OrbFeatureMatch | Positive localization | Yes | 102.998 | True | 394 | 0.965 | 0.128 | - |
| OrbFeatureMatch_translation_topleft_origin | OrbFeatureMatch | Origin contract | Yes | 5.407 | True | 375 | 0.955 | 0.885 | - |
| OrbFeatureMatch_translation_custom_origin | OrbFeatureMatch | Origin contract | Yes | 5.07 | True | 381 | 0.964 | 0.108 | - |
| OrbFeatureMatch_template_path_center | OrbFeatureMatch | Template source | Yes | 6.156 | True | 393 | 0.944 | 0.074 | - |
| OrbFeatureMatch_template_path_cache_repeat | OrbFeatureMatch | Template source | Yes | 6.173 | True | 393 | 0.944 | 0.074 | - |
| OrbFeatureMatch_symmetry_disabled_translation | OrbFeatureMatch | Matcher options | Yes | 4.346 | True | 400 | 0.94 | 0.179 | - |
| OrbFeatureMatch_min_match_count_four | OrbFeatureMatch | Matcher options | Yes | 5.096 | True | 372 | 0.927 | 0.128 | - |
| OrbFeatureMatch_max_features_low_boundary | OrbFeatureMatch | Matcher options | Yes | 2.468 | True | 41 | 0.958 | 0.366 | - |
| OrbFeatureMatch_scaled_up | OrbFeatureMatch | Scale and rotation | Yes | 4.914 | True | 120 | 0.908 | 0.115 | - |
| OrbFeatureMatch_scaled_down | OrbFeatureMatch | Scale and rotation | Yes | 4.86 | True | 175 | 0.944 | 0.395 | - |
| OrbFeatureMatch_rotated_small_angle | OrbFeatureMatch | Scale and rotation | Yes | 4.932 | True | 250 | 0.882 | 0.13 | - |
| OrbFeatureMatch_grayscale_inputs | OrbFeatureMatch | Input formats | Yes | 4.747 | True | 411 | 0.966 | 0.129 | - |
| OrbFeatureMatch_color_scene_grayscale_template | OrbFeatureMatch | Input formats | Yes | 4.871 | True | 415 | 0.969 | 0.154 | - |
| OrbFeatureMatch_blank_scene_no_features | OrbFeatureMatch | Failure contract | Yes | 0.922 | False | 0 | 0 | 0 | - |
| OrbFeatureMatch_blank_template_no_features | OrbFeatureMatch | Failure contract | Yes | 1.383 | False | 0 | 0 | 0 | - |
| OrbFeatureMatch_missing_template_source | OrbFeatureMatch | Failure contract | Yes | 1.938 | False | 0 | 0 | 0 | - |
| OrbFeatureMatch_operator_failure_without_image | OrbFeatureMatch | Failure contract | Yes | 0.011 | False | 0 | 0 | 0 | - |
| OrbFeatureMatch_validate_defaults | OrbFeatureMatch | Validation contract | Yes | 0.178 | - | - | - | - | - |
| OrbFeatureMatch_validate_min_match_low_invalid | OrbFeatureMatch | Validation contract | Yes | 0.006 | - | - | - | - | - |
| OrbFeatureMatch_validate_min_match_high_invalid | OrbFeatureMatch | Validation contract | Yes | 0.005 | - | - | - | - | - |
| OrbFeatureMatch_validate_scale_factor_low_invalid | OrbFeatureMatch | Validation contract | Yes | 0.006 | - | - | - | - | - |
| OrbFeatureMatch_validate_max_features_high_invalid | OrbFeatureMatch | Validation contract | Yes | 0.004 | - | - | - | - | - |

## Notes

- This baseline uses deterministic synthetic textured templates and transformed scenes.
- It validates AKAZE and ORB execution contracts: template input/path, origin modes, matcher options, score ranges, homography-gated positions, and failure/validation behavior.
- It is contract evidence, not a public-image benchmark.
