# SemanticSegmentation Contract Baseline

GeneratedAtUtc: `2026-04-29T03:30:07.2993732+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 27 |
| Passed | 27 |
| Failed | 0 |
| Runtime ms | 199.814 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Class-name contract | 4 | 4 | 0 | 0.832 |
| End-to-end identity model | 5 | 5 | 0 | 36.667 |
| Failure contract | 3 | 3 | 0 | 0.419 |
| Model catalog contract | 2 | 2 | 0 | 4.344 |
| Parser contract | 4 | 4 | 0 | 0.167 |
| Preprocess contract | 4 | 4 | 0 | 0.439 |
| Validation contract | 4 | 4 | 0 | 0.173 |
| Visualization contract | 1 | 1 | 0 | 0.091 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| identity_direct_class_map_exact | End-to-end identity model | True | 175.353 |  |
| identity_output_types_and_size | End-to-end identity model | True | 2.159 |  |
| identity_present_classes_and_count | End-to-end identity model | True | 2.164 |  |
| identity_class_masks_exact | End-to-end identity model | True | 1.349 |  |
| identity_colored_map_matches_palette | End-to-end identity model | True | 2.309 |  |
| catalog_resolves_model_defaults | Model catalog contract | True | 7.943 |  |
| validate_catalog_defaults_valid | Model catalog contract | True | 0.744 |  |
| validate_missing_model_invalid | Validation contract | True | 0.368 |  |
| validate_bad_input_size_invalid | Validation contract | True | 0.118 |  |
| validate_zero_std_invalid | Validation contract | True | 0.110 |  |
| validate_bad_execution_provider_invalid | Validation contract | True | 0.097 |  |
| execute_missing_image_fails | Failure contract | True | 0.306 |  |
| execute_missing_model_fails | Failure contract | True | 0.406 |  |
| execute_bad_mean_fails | Failure contract | True | 0.546 |  |
| parse_size_accepts_trimmed_pair | Parser contract | True | 0.220 |  |
| parse_size_rejects_zero | Parser contract | True | 0.151 |  |
| parse_float_triplet_accepts_three_values | Parser contract | True | 0.216 |  |
| parse_float_triplet_rejects_two_values | Parser contract | True | 0.081 |  |
| class_names_json_expands_missing | Class-name contract | True | 0.563 |  |
| class_names_comma_truncates_extra | Class-name contract | True | 0.268 |  |
| class_names_empty_fallback | Class-name contract | True | 0.124 |  |
| class_names_bad_json_fails | Class-name contract | True | 2.373 |  |
| preprocess_rgb_channel_order | Preprocess contract | True | 1.170 |  |
| preprocess_bgr_channel_order | Preprocess contract | True | 0.306 |  |
| preprocess_unit_range_mean_std | Preprocess contract | True | 0.136 |  |
| preprocess_grayscale_promotes_to_three_channels | Preprocess contract | True | 0.143 |  |
| palette_color_is_stable_per_class | Visualization contract | True | 0.091 |  |

## Notes

- This is a contract baseline using the repo-local identity 2x2 ONNX segmentation model plus direct private-helper contract checks.
- It validates class-map argmax behavior, mask generation, palette mapping, model catalog resolution, parser validation, failure paths, and preprocessing channel/range contracts.
- It does not claim real segmentation accuracy; dataset quality should be evaluated separately with a public or field segmentation dataset.
