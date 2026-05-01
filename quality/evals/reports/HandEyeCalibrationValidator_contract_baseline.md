# HandEyeCalibrationValidator Contract Baseline

GeneratedAtUtc: `2026-04-29T03:30:17.4269511+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 24 |
| Passed | 24 |
| Failed | 0 |
| Runtime ms | 40.995 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| HandEyeCalibrationValidator | 24 | 24 | 0 | 1.708 | 21871 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Failure contract | 8 | 8 | 0 | 0.363 |
| Good validation | 2 | 2 | 0 | 17.512 |
| Input parsing | 2 | 2 | 0 | 0.732 |
| Output bundle contract | 2 | 2 | 0 | 0.096 |
| Parameter parsing | 1 | 1 | 0 | 0.718 |
| Perturbation contract | 1 | 1 | 0 | 0.096 |
| Report contract | 2 | 2 | 0 | 0.1 |
| Suggestion contract | 2 | 2 | 0 | 0.102 |
| Validation contract | 4 | 4 | 0 | 0.049 |

## Cases

| Case | Scenario | Passed | Runtime ms | Quality | Mean Error | Failure |
| --- | --- | --- | ---: | --- | ---: | --- |
| eye_in_hand_good_matrix | Good validation | Yes | 34.49 | good | 1E-07 | - |
| eye_to_hand_good_matrix | Good validation | Yes | 0.534 | good | 8E-08 | - |
| eye_in_hand_json_pose_inputs | Input parsing | Yes | 1.264 | good | 1E-07 | - |
| eye_to_hand_json_pose_inputs | Input parsing | Yes | 0.201 | good | 8E-08 | - |
| custom_bundle_metadata_preserved | Output bundle contract | Yes | 0.099 | good | 1E-07 | - |
| html_report_contains_quality | Report contract | Yes | 0.104 | good | 1E-07 | - |
| suggested_validation_poses_parseable | Report contract | Yes | 0.095 | good | 1E-07 | - |
| good_quality_has_operational_suggestion | Suggestion contract | Yes | 0.104 | good | 8E-08 | - |
| low_sample_count_adds_suggestion | Suggestion contract | Yes | 0.099 | good | 9E-08 | - |
| perturbed_eye_to_hand_matrix_is_poor | Perturbation contract | Yes | 0.096 | poor | 0.00893879 | - |
| perturbed_bundle_marks_quality_rejected | Output bundle contract | Yes | 0.092 | poor | 0.00893879 | - |
| eye_in_hand_case_insensitive_type | Parameter parsing | Yes | 0.718 | good | 1E-07 | - |
| missing_calibration_data_fails | Failure contract | Yes | 0.098 | - | - | - |
| invalid_calibration_json_fails | Failure contract | Yes | 2.384 | - | - | - |
| wrong_calibration_kind_fails | Failure contract | Yes | 0.36 | - | - | - |
| missing_transform3d_fails | Failure contract | Yes | 0.027 | - | - | - |
| invalid_matrix_shape_fails | Failure contract | Yes | 0.021 | - | - | - |
| missing_robot_poses_fails | Failure contract | Yes | 0.004 | - | - | - |
| missing_board_poses_fails | Failure contract | Yes | 0.005 | - | - | - |
| pose_count_mismatch_fails | Failure contract | Yes | 0.004 | - | - | - |
| validate_eye_in_hand_valid | Validation contract | Yes | 0.116 | - | - | - |
| validate_eye_to_hand_valid | Validation contract | Yes | 0.005 | - | - | - |
| validate_bad_type_invalid | Validation contract | Yes | 0.069 | - | - | - |
| validate_trimmed_type_valid | Validation contract | Yes | 0.006 | - | - | - |

## Notes

- This baseline uses deterministic synthetic eye-in-hand and eye-to-hand pose sets.
- It validates good, perturbed, malformed input, output bundle, HTML report, suggestion, pose parsing, and parameter validation contracts.
- It is a validator contract baseline; the upstream hand-eye solver has separate unit coverage.
