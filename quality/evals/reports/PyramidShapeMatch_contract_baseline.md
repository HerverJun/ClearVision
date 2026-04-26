# PyramidShapeMatch Contract Baseline

GeneratedAtUtc: `2026-04-26T12:52:32.2435434+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 24 |
| Passed | 24 |
| Failed | 0 |
| Runtime ms | 106.382 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| PyramidShapeMatch | 24 | 24 | 0 | 4.433 | 45643 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Failure contract | 4 | 4 | 0 | 1.952 |
| Input formats | 1 | 1 | 0 | 6.422 |
| ShapeDescriptor mode | 5 | 5 | 0 | 2.703 |
| Template mode | 4 | 4 | 0 | 19.583 |
| Validation contract | 10 | 10 | 0 | 0.031 |

## Cases

| Case | Scenario | Passed | Runtime ms | IsMatch | Score | Count | Pos Error | Failure |
| --- | --- | --- | ---: | --- | ---: | ---: | ---: | --- |
| template_input_exact | Template mode | Yes | 55.718 | True | 100 | 1 | 8.944272 | - |
| template_path_exact | Template mode | Yes | 8.609 | True | 100 | 1 | 8.944272 | - |
| template_low_threshold_exact | Template mode | Yes | 6.917 | True | 100 | 1 | 8.246211 | - |
| template_max_matches_one | Template mode | Yes | 7.089 | True | 100 | 1 | 8.246211 | - |
| template_grayscale_inputs | Input formats | Yes | 6.422 | True | 100 | 1 | 8.246211 | - |
| template_blank_scene_no_match | Failure contract | Yes | 6.578 | False | 0 | 0 | 0 | - |
| template_blank_template_training_fails | Failure contract | Yes | 0.629 | False | 0 | 0 | 0 | - |
| template_missing_template_fails | Failure contract | Yes | 0.053 | False | 0 | 0 | 0 | - |
| shape_descriptor_input_exact | ShapeDescriptor mode | Yes | 10.169 | True | 97.052185 | 1 | 12.806248 | - |
| shape_descriptor_path_exact | ShapeDescriptor mode | Yes | 1.117 | True | 96.890221 | 1 | 12.806248 | - |
| shape_descriptor_hu_only | ShapeDescriptor mode | Yes | 0.821 | True | 100 | 1 | 12.806248 | - |
| shape_descriptor_fourier_only | ShapeDescriptor mode | Yes | 0.743 | True | 96.839348 | 1 | 12.806248 | - |
| shape_descriptor_scaled_area_rejects | ShapeDescriptor mode | Yes | 0.664 | False | 0 | 0 | 0 | - |
| shape_descriptor_blank_scene_no_match | Failure contract | Yes | 0.546 | False | 0 | 0 | 0 | - |
| validate_defaults | Validation contract | Yes | 0.201 | - | - | - | - | - |
| validate_min_score_low_invalid | Validation contract | Yes | 0.053 | - | - | - | - | - |
| validate_min_score_high_invalid | Validation contract | Yes | 0.006 | - | - | - | - | - |
| validate_pyramid_low_invalid | Validation contract | Yes | 0.008 | - | - | - | - | - |
| validate_pyramid_high_invalid | Validation contract | Yes | 0.005 | - | - | - | - | - |
| validate_num_features_low_invalid | Validation contract | Yes | 0.006 | - | - | - | - | - |
| validate_num_features_high_invalid | Validation contract | Yes | 0.006 | - | - | - | - | - |
| validate_spread_low_invalid | Validation contract | Yes | 0.007 | - | - | - | - | - |
| validate_angle_range_high_invalid | Validation contract | Yes | 0.007 | - | - | - | - | - |
| validate_angle_step_high_invalid | Validation contract | Yes | 0.008 | - | - | - | - | - |

## Notes

- This baseline uses deterministic synthetic asymmetric shapes.
- Template mode accepts either the current LINEMOD candidate point or the UI-drawn center as position-compatible evidence.
- ShapeDescriptor mode is evaluated against contour-center localization.
- The run locks output contract and validation behavior; it does not claim HPatches-style public benchmark coverage.
