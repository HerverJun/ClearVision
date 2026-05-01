# C-Level Golden Runner Report

GeneratedAtUtc: 2026-04-29T03:29:19.6514604+00:00
CasesRoot: `embedded synthetic C-level cases`

## Summary

Cases: 66
Passed: 66
Failed: 0

## Operators

| Operator | Cases | Passed | Failed | Avg Runtime Ms | Max Runtime Ms | Avg Allocation Bytes |
|---|---:|---:|---:|---:|---:|---:|
| Comment | 22 | 22 | 0 | 1.001 | 17.433 | 35545 |
| ContourExtrema | 22 | 22 | 0 | 0.253 | 1.196 | 8815 |
| PhaseClosure | 22 | 22 | 0 | 1.433 | 4.545 | 279578 |

## Scenario Results

| Case | Operator | Scenario | Passed | Runtime Ms | Error |
|---|---|---|---|---:|---|
| ContourExtrema_quad_horizontal_0000 | ContourExtrema | quad_horizontal | Yes | 0.19 | - |
| ContourExtrema_quad_vertical_0001 | ContourExtrema | quad_vertical | Yes | 0.243 | - |
| ContourExtrema_quad_distance_0002 | ContourExtrema | quad_distance | Yes | 0.257 | - |
| ContourExtrema_negative_horizontal_0003 | ContourExtrema | negative_horizontal | Yes | 0.124 | - |
| ContourExtrema_negative_vertical_0004 | ContourExtrema | negative_vertical | Yes | 0.118 | - |
| ContourExtrema_negative_distance_0005 | ContourExtrema | negative_distance | Yes | 0.123 | - |
| ContourExtrema_collinear_horizontal_0006 | ContourExtrema | collinear_horizontal | Yes | 0.111 | - |
| ContourExtrema_collinear_vertical_0007 | ContourExtrema | collinear_vertical | Yes | 1.196 | - |
| ContourExtrema_collinear_distance_0008 | ContourExtrema | collinear_distance | Yes | 0.131 | - |
| ContourExtrema_duplicate_extreme_horizontal_0009 | ContourExtrema | duplicate_extreme_horizontal | Yes | 0.119 | - |
| ContourExtrema_duplicate_extreme_vertical_0010 | ContourExtrema | duplicate_extreme_vertical | Yes | 0.114 | - |
| ContourExtrema_duplicate_extreme_distance_0011 | ContourExtrema | duplicate_extreme_distance | Yes | 0.114 | - |
| ContourExtrema_slanted_horizontal_0012 | ContourExtrema | slanted_horizontal | Yes | 0.127 | - |
| ContourExtrema_slanted_vertical_0013 | ContourExtrema | slanted_vertical | Yes | 0.152 | - |
| ContourExtrema_slanted_distance_0014 | ContourExtrema | slanted_distance | Yes | 0.184 | - |
| ContourExtrema_single_point_horizontal_0015 | ContourExtrema | single_point_horizontal | Yes | 0.283 | - |
| ContourExtrema_single_point_vertical_0016 | ContourExtrema | single_point_vertical | Yes | 0.158 | - |
| ContourExtrema_single_point_distance_0017 | ContourExtrema | single_point_distance | Yes | 0.137 | - |
| ContourExtrema_point_array_horizontal_0018 | ContourExtrema | point_array_horizontal | Yes | 1.108 | - |
| ContourExtrema_unknown_direction_fallback_0019 | ContourExtrema | unknown_direction_fallback | Yes | 0.245 | - |
| ContourExtrema_empty_contour_0020 | ContourExtrema | empty_contour | Yes | 0.281 | - |
| ContourExtrema_distance_missing_ref_0021 | ContourExtrema | distance_missing_reference | Yes | 0.055 | - |
| PhaseClosure_ramp_32_0000 | PhaseClosure | ramp_32 | Yes | 0.852 | - |
| PhaseClosure_ramp_48_0001 | PhaseClosure | ramp_48 | Yes | 1.29 | - |
| PhaseClosure_ramp_wide_0002 | PhaseClosure | ramp_wide | Yes | 2.327 | - |
| PhaseClosure_ramp_tall_0003 | PhaseClosure | ramp_tall | Yes | 1.479 | - |
| PhaseClosure_ramp_gentle_0004 | PhaseClosure | ramp_gentle | Yes | 1.636 | - |
| PhaseClosure_ramp_x_only_0005 | PhaseClosure | ramp_x_only | Yes | 1.11 | - |
| PhaseClosure_ramp_y_only_0006 | PhaseClosure | ramp_y_only | Yes | 1.214 | - |
| PhaseClosure_ramp_offset_0007 | PhaseClosure | ramp_offset | Yes | 1.102 | - |
| PhaseClosure_quality_centered_0008 | PhaseClosure | quality_centered | Yes | 4.545 | - |
| PhaseClosure_quality_rect_0009 | PhaseClosure | quality_rect | Yes | 2.357 | - |
| PhaseClosure_quality_gentle_0010 | PhaseClosure | quality_gentle | Yes | 1.406 | - |
| PhaseClosure_quality_x_only_0011 | PhaseClosure | quality_x_only | Yes | 1.342 | - |
| PhaseClosure_floodfill_centered_0012 | PhaseClosure | floodfill_centered | Yes | 2.183 | - |
| PhaseClosure_floodfill_offset_0013 | PhaseClosure | floodfill_offset | Yes | 1.342 | - |
| PhaseClosure_floodfill_discontinuity_0014 | PhaseClosure | floodfill_discontinuity | Yes | 1.622 | - |
| PhaseClosure_itoh_discontinuity_0015 | PhaseClosure | itoh_discontinuity | Yes | 1.159 | - |
| PhaseClosure_uniform_zero_0016 | PhaseClosure | uniform_zero | Yes | 0.689 | - |
| PhaseClosure_uniform_positive_0017 | PhaseClosure | uniform_positive | Yes | 1.659 | - |
| PhaseClosure_uniform_negative_0018 | PhaseClosure | uniform_negative | Yes | 1.085 | - |
| PhaseClosure_wavelength_scaled_0019 | PhaseClosure | wavelength_scaled | Yes | 0.944 | - |
| PhaseClosure_bad_quality_size_0020 | PhaseClosure | bad_quality_size | Yes | 0.188 | - |
| PhaseClosure_missing_image_0021 | PhaseClosure | missing_image | Yes | 0.004 | - |
| Comment_missing_input_default_text_0000 | Comment | missing_input_default_text | Yes | 0.015 | - |
| Comment_string_payload_0001 | Comment | string_payload | Yes | 17.433 | - |
| Comment_int_payload_0002 | Comment | int_payload | Yes | 0.06 | - |
| Comment_double_payload_0003 | Comment | double_payload | Yes | 0.026 | - |
| Comment_bool_payload_0004 | Comment | bool_payload | Yes | 0.023 | - |
| Comment_dictionary_payload_0005 | Comment | dictionary_payload | Yes | 0.034 | - |
| Comment_list_payload_0006 | Comment | list_payload | Yes | 0.038 | - |
| Comment_byte_array_payload_0007 | Comment | byte_array_payload | Yes | 0.088 | - |
| Comment_empty_text_0008 | Comment | empty_text | Yes | 0.02 | - |
| Comment_long_valid_text_0009 | Comment | long_valid_text | Yes | 0.344 | - |
| Comment_numeric_text_param_0010 | Comment | numeric_text_param | Yes | 1.911 | - |
| Comment_bool_text_param_0011 | Comment | bool_text_param | Yes | 0.708 | - |
| Comment_image_payload_small_0012 | Comment | image_payload_small | Yes | 0.143 | - |
| Comment_image_payload_gray_0013 | Comment | image_payload_gray | Yes | 0.06 | - |
| Comment_large_scalar_payload_0014 | Comment | large_scalar_payload | Yes | 0.029 | - |
| Comment_zero_payload_0015 | Comment | zero_payload | Yes | 0.025 | - |
| Comment_negative_payload_0016 | Comment | negative_payload | Yes | 0.024 | - |
| Comment_decimal_payload_0017 | Comment | decimal_payload | Yes | 0.057 | - |
| Comment_object_payload_0018 | Comment | object_payload | Yes | 0.065 | - |
| Comment_max_minus_one_text_0019 | Comment | max_minus_one_text | Yes | 0.261 | - |
| Comment_too_long_text_0020 | Comment | too_long_text | Yes | 0.503 | - |
| Comment_much_too_long_text_0021 | Comment | much_too_long_text | Yes | 0.162 | - |
