# C-Level Golden Runner Report

GeneratedAtUtc: 2026-04-24T15:14:34.6234679+00:00
CasesRoot: `embedded synthetic C-level cases`

## Summary

Cases: 66
Passed: 66
Failed: 0

## Operators

| Operator | Cases | Passed | Failed | Avg Runtime Ms | Max Runtime Ms | Avg Allocation Bytes |
|---|---:|---:|---:|---:|---:|---:|
| Comment | 22 | 22 | 0 | 1.176 | 21.034 | 35571 |
| ContourExtrema | 22 | 22 | 0 | 0.244 | 1.171 | 8815 |
| PhaseClosure | 22 | 22 | 0 | 1.429 | 4.59 | 279578 |

## Scenario Results

| Case | Operator | Scenario | Passed | Runtime Ms | Error |
|---|---|---|---|---:|---|
| ContourExtrema_quad_horizontal_0000 | ContourExtrema | quad_horizontal | Yes | 0.198 | - |
| ContourExtrema_quad_vertical_0001 | ContourExtrema | quad_vertical | Yes | 0.273 | - |
| ContourExtrema_quad_distance_0002 | ContourExtrema | quad_distance | Yes | 0.312 | - |
| ContourExtrema_negative_horizontal_0003 | ContourExtrema | negative_horizontal | Yes | 0.137 | - |
| ContourExtrema_negative_vertical_0004 | ContourExtrema | negative_vertical | Yes | 0.122 | - |
| ContourExtrema_negative_distance_0005 | ContourExtrema | negative_distance | Yes | 0.119 | - |
| ContourExtrema_collinear_horizontal_0006 | ContourExtrema | collinear_horizontal | Yes | 0.115 | - |
| ContourExtrema_collinear_vertical_0007 | ContourExtrema | collinear_vertical | Yes | 1.026 | - |
| ContourExtrema_collinear_distance_0008 | ContourExtrema | collinear_distance | Yes | 0.135 | - |
| ContourExtrema_duplicate_extreme_horizontal_0009 | ContourExtrema | duplicate_extreme_horizontal | Yes | 0.13 | - |
| ContourExtrema_duplicate_extreme_vertical_0010 | ContourExtrema | duplicate_extreme_vertical | Yes | 0.128 | - |
| ContourExtrema_duplicate_extreme_distance_0011 | ContourExtrema | duplicate_extreme_distance | Yes | 0.137 | - |
| ContourExtrema_slanted_horizontal_0012 | ContourExtrema | slanted_horizontal | Yes | 0.12 | - |
| ContourExtrema_slanted_vertical_0013 | ContourExtrema | slanted_vertical | Yes | 0.114 | - |
| ContourExtrema_slanted_distance_0014 | ContourExtrema | slanted_distance | Yes | 0.116 | - |
| ContourExtrema_single_point_horizontal_0015 | ContourExtrema | single_point_horizontal | Yes | 0.239 | - |
| ContourExtrema_single_point_vertical_0016 | ContourExtrema | single_point_vertical | Yes | 0.118 | - |
| ContourExtrema_single_point_distance_0017 | ContourExtrema | single_point_distance | Yes | 0.11 | - |
| ContourExtrema_point_array_horizontal_0018 | ContourExtrema | point_array_horizontal | Yes | 1.171 | - |
| ContourExtrema_unknown_direction_fallback_0019 | ContourExtrema | unknown_direction_fallback | Yes | 0.221 | - |
| ContourExtrema_empty_contour_0020 | ContourExtrema | empty_contour | Yes | 0.283 | - |
| ContourExtrema_distance_missing_ref_0021 | ContourExtrema | distance_missing_reference | Yes | 0.053 | - |
| PhaseClosure_ramp_32_0000 | PhaseClosure | ramp_32 | Yes | 0.858 | - |
| PhaseClosure_ramp_48_0001 | PhaseClosure | ramp_48 | Yes | 1.303 | - |
| PhaseClosure_ramp_wide_0002 | PhaseClosure | ramp_wide | Yes | 2.327 | - |
| PhaseClosure_ramp_tall_0003 | PhaseClosure | ramp_tall | Yes | 1.394 | - |
| PhaseClosure_ramp_gentle_0004 | PhaseClosure | ramp_gentle | Yes | 1.629 | - |
| PhaseClosure_ramp_x_only_0005 | PhaseClosure | ramp_x_only | Yes | 1.02 | - |
| PhaseClosure_ramp_y_only_0006 | PhaseClosure | ramp_y_only | Yes | 1.127 | - |
| PhaseClosure_ramp_offset_0007 | PhaseClosure | ramp_offset | Yes | 1.113 | - |
| PhaseClosure_quality_centered_0008 | PhaseClosure | quality_centered | Yes | 4.59 | - |
| PhaseClosure_quality_rect_0009 | PhaseClosure | quality_rect | Yes | 2.377 | - |
| PhaseClosure_quality_gentle_0010 | PhaseClosure | quality_gentle | Yes | 1.44 | - |
| PhaseClosure_quality_x_only_0011 | PhaseClosure | quality_x_only | Yes | 1.367 | - |
| PhaseClosure_floodfill_centered_0012 | PhaseClosure | floodfill_centered | Yes | 2.117 | - |
| PhaseClosure_floodfill_offset_0013 | PhaseClosure | floodfill_offset | Yes | 1.338 | - |
| PhaseClosure_floodfill_discontinuity_0014 | PhaseClosure | floodfill_discontinuity | Yes | 1.625 | - |
| PhaseClosure_itoh_discontinuity_0015 | PhaseClosure | itoh_discontinuity | Yes | 1.182 | - |
| PhaseClosure_uniform_zero_0016 | PhaseClosure | uniform_zero | Yes | 0.647 | - |
| PhaseClosure_uniform_positive_0017 | PhaseClosure | uniform_positive | Yes | 1.759 | - |
| PhaseClosure_uniform_negative_0018 | PhaseClosure | uniform_negative | Yes | 1.104 | - |
| PhaseClosure_wavelength_scaled_0019 | PhaseClosure | wavelength_scaled | Yes | 0.923 | - |
| PhaseClosure_bad_quality_size_0020 | PhaseClosure | bad_quality_size | Yes | 0.188 | - |
| PhaseClosure_missing_image_0021 | PhaseClosure | missing_image | Yes | 0.004 | - |
| Comment_missing_input_default_text_0000 | Comment | missing_input_default_text | Yes | 0.016 | - |
| Comment_string_payload_0001 | Comment | string_payload | Yes | 21.034 | - |
| Comment_int_payload_0002 | Comment | int_payload | Yes | 0.061 | - |
| Comment_double_payload_0003 | Comment | double_payload | Yes | 0.03 | - |
| Comment_bool_payload_0004 | Comment | bool_payload | Yes | 0.023 | - |
| Comment_dictionary_payload_0005 | Comment | dictionary_payload | Yes | 0.038 | - |
| Comment_list_payload_0006 | Comment | list_payload | Yes | 0.04 | - |
| Comment_byte_array_payload_0007 | Comment | byte_array_payload | Yes | 0.098 | - |
| Comment_empty_text_0008 | Comment | empty_text | Yes | 0.027 | - |
| Comment_long_valid_text_0009 | Comment | long_valid_text | Yes | 0.353 | - |
| Comment_numeric_text_param_0010 | Comment | numeric_text_param | Yes | 2.168 | - |
| Comment_bool_text_param_0011 | Comment | bool_text_param | Yes | 0.743 | - |
| Comment_image_payload_small_0012 | Comment | image_payload_small | Yes | 0.144 | - |
| Comment_image_payload_gray_0013 | Comment | image_payload_gray | Yes | 0.063 | - |
| Comment_large_scalar_payload_0014 | Comment | large_scalar_payload | Yes | 0.03 | - |
| Comment_zero_payload_0015 | Comment | zero_payload | Yes | 0.026 | - |
| Comment_negative_payload_0016 | Comment | negative_payload | Yes | 0.022 | - |
| Comment_decimal_payload_0017 | Comment | decimal_payload | Yes | 0.056 | - |
| Comment_object_payload_0018 | Comment | object_payload | Yes | 0.066 | - |
| Comment_max_minus_one_text_0019 | Comment | max_minus_one_text | Yes | 0.204 | - |
| Comment_too_long_text_0020 | Comment | too_long_text | Yes | 0.458 | - |
| Comment_much_too_long_text_0021 | Comment | much_too_long_text | Yes | 0.162 | - |
