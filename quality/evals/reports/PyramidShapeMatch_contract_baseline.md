# PyramidShapeMatch Contract Baseline

GeneratedAtUtc: `2026-04-29T03:30:15.4505885+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 24 |
| Passed | 24 |
| Failed | 0 |
| Runtime ms | 106.031 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| PyramidShapeMatch | 24 | 24 | 0 | 4.418 | 47703 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Failure contract | 4 | 4 | 0 | 2.013 |
| Input formats | 1 | 1 | 0 | 7.98 |
| ShapeDescriptor mode | 5 | 5 | 0 | 2.674 |
| Template mode | 4 | 4 | 0 | 19.054 |
| Validation contract | 10 | 10 | 0 | 0.041 |

## Cases

| Case | Scenario | Passed | Runtime ms | IsMatch | Score | Count | Pos Error | Failure |
| --- | --- | --- | ---: | --- | ---: | ---: | ---: | --- |
| template_input_exact | Template mode | Yes | 54.384 | True | 100 | 1 | 8.944272 | - |
| template_path_exact | Template mode | Yes | 7.802 | True | 100 | 1 | 8.944272 | - |
| template_low_threshold_exact | Template mode | Yes | 7.356 | True | 100 | 1 | 8.246211 | - |
| template_max_matches_one | Template mode | Yes | 6.674 | True | 100 | 1 | 8.246211 | - |
| template_grayscale_inputs | Input formats | Yes | 7.98 | True | 100 | 1 | 8.246211 | - |
| template_blank_scene_no_match | Failure contract | Yes | 6.596 | False | 0 | 0 | 0 | - |
| template_blank_template_training_fails | Failure contract | Yes | 0.769 | False | 0 | 0 | 0 | - |
| template_missing_template_fails | Failure contract | Yes | 0.061 | False | 0 | 0 | 0 | - |
| shape_descriptor_input_exact | ShapeDescriptor mode | Yes | 9.732 | True | 97.052185 | 1 | 12.806248 | - |
| shape_descriptor_path_exact | ShapeDescriptor mode | Yes | 1.744 | True | 96.890221 | 1 | 12.806248 | - |
| shape_descriptor_hu_only | ShapeDescriptor mode | Yes | 0.725 | True | 100 | 1 | 12.806248 | - |
| shape_descriptor_fourier_only | ShapeDescriptor mode | Yes | 0.548 | True | 96.839348 | 1 | 12.806248 | - |
| shape_descriptor_scaled_area_rejects | ShapeDescriptor mode | Yes | 0.622 | False | 0 | 0 | 0 | - |
| shape_descriptor_blank_scene_no_match | Failure contract | Yes | 0.626 | False | 0 | 0 | 0 | - |
| validate_defaults | Validation contract | Yes | 0.297 | - | - | - | - | - |
| validate_min_score_low_invalid | Validation contract | Yes | 0.06 | - | - | - | - | - |
| validate_min_score_high_invalid | Validation contract | Yes | 0.006 | - | - | - | - | - |
| validate_pyramid_low_invalid | Validation contract | Yes | 0.006 | - | - | - | - | - |
| validate_pyramid_high_invalid | Validation contract | Yes | 0.005 | - | - | - | - | - |
| validate_num_features_low_invalid | Validation contract | Yes | 0.009 | - | - | - | - | - |
| validate_num_features_high_invalid | Validation contract | Yes | 0.006 | - | - | - | - | - |
| validate_spread_low_invalid | Validation contract | Yes | 0.007 | - | - | - | - | - |
| validate_angle_range_high_invalid | Validation contract | Yes | 0.008 | - | - | - | - | - |
| validate_angle_step_high_invalid | Validation contract | Yes | 0.008 | - | - | - | - | - |

## Notes

- This baseline uses deterministic synthetic asymmetric shapes.
- Template mode accepts either the current LINEMOD candidate point or the UI-drawn center as position-compatible evidence.
- ShapeDescriptor mode is evaluated against contour-center localization.
- The run locks output contract and validation behavior; it does not claim HPatches-style public benchmark coverage.
