# HandEyeCalibrationValidator Contract Baseline

GeneratedAtUtc: `2026-04-26T13:09:28.2840723+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 24 |
| Passed | 24 |
| Failed | 0 |
| Runtime ms | 42.986 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| HandEyeCalibrationValidator | 24 | 24 | 0 | 1.791 | 23921 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Failure contract | 8 | 8 | 0 | 0.355 |
| Good validation | 2 | 2 | 0 | 18.354 |
| Input parsing | 2 | 2 | 0 | 0.71 |
| Output bundle contract | 2 | 2 | 0 | 0.102 |
| Parameter parsing | 1 | 1 | 0 | 1.02 |
| Perturbation contract | 1 | 1 | 0 | 0.166 |
| Report contract | 2 | 2 | 0 | 0.097 |
| Suggestion contract | 2 | 2 | 0 | 0.116 |
| Validation contract | 4 | 4 | 0 | 0.05 |

## Cases

| Case | Scenario | Passed | Runtime ms | Quality | Mean Error | Failure |
| --- | --- | --- | ---: | --- | ---: | --- |
| eye_in_hand_good_matrix | Good validation | Yes | 36.209 | good | 1E-07 | - |
| eye_to_hand_good_matrix | Good validation | Yes | 0.499 | good | 8E-08 | - |
| eye_in_hand_json_pose_inputs | Input parsing | Yes | 1.221 | good | 1E-07 | - |
| eye_to_hand_json_pose_inputs | Input parsing | Yes | 0.2 | good | 8E-08 | - |
| custom_bundle_metadata_preserved | Output bundle contract | Yes | 0.095 | good | 1E-07 | - |
| html_report_contains_quality | Report contract | Yes | 0.101 | good | 1E-07 | - |
| suggested_validation_poses_parseable | Report contract | Yes | 0.093 | good | 1E-07 | - |
| good_quality_has_operational_suggestion | Suggestion contract | Yes | 0.119 | good | 8E-08 | - |
| low_sample_count_adds_suggestion | Suggestion contract | Yes | 0.113 | good | 9E-08 | - |
| perturbed_eye_to_hand_matrix_is_poor | Perturbation contract | Yes | 0.166 | poor | 0.00893879 | - |
| perturbed_bundle_marks_quality_rejected | Output bundle contract | Yes | 0.108 | poor | 0.00893879 | - |
| eye_in_hand_case_insensitive_type | Parameter parsing | Yes | 1.02 | good | 1E-07 | - |
| missing_calibration_data_fails | Failure contract | Yes | 0.106 | - | - | - |
| invalid_calibration_json_fails | Failure contract | Yes | 2.333 | - | - | - |
| wrong_calibration_kind_fails | Failure contract | Yes | 0.347 | - | - | - |
| missing_transform3d_fails | Failure contract | Yes | 0.024 | - | - | - |
| invalid_matrix_shape_fails | Failure contract | Yes | 0.019 | - | - | - |
| missing_robot_poses_fails | Failure contract | Yes | 0.003 | - | - | - |
| missing_board_poses_fails | Failure contract | Yes | 0.004 | - | - | - |
| pose_count_mismatch_fails | Failure contract | Yes | 0.004 | - | - | - |
| validate_eye_in_hand_valid | Validation contract | Yes | 0.118 | - | - | - |
| validate_eye_to_hand_valid | Validation contract | Yes | 0.005 | - | - | - |
| validate_bad_type_invalid | Validation contract | Yes | 0.073 | - | - | - |
| validate_trimmed_type_valid | Validation contract | Yes | 0.006 | - | - | - |

## Notes

- This baseline uses deterministic synthetic eye-in-hand and eye-to-hand pose sets.
- It validates good, perturbed, malformed input, output bundle, HTML report, suggestion, pose parsing, and parameter validation contracts.
- It is a validator contract baseline; the upstream hand-eye solver has separate unit coverage.
