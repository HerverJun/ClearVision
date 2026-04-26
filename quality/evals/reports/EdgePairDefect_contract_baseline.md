# EdgePairDefect Contract Baseline

GeneratedAtUtc: `2026-04-26T08:17:33.7451398+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 27 |
| Passed | 27 |
| Failed | 0 |
| Runtime ms | 154.203 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Auto line detection | 2 | 2 | 0 | 3.800 |
| Edge method contract | 1 | 1 | 0 | 0.932 |
| Failure contract | 3 | 3 | 0 | 0.468 |
| Line input contract | 4 | 4 | 0 | 1.003 |
| Output contract | 1 | 1 | 0 | 0.906 |
| Private helper contract | 4 | 4 | 0 | 0.357 |
| Provided line geometry | 4 | 4 | 0 | 33.216 |
| Sampling contract | 2 | 2 | 0 | 1.175 |
| Tolerance contract | 2 | 2 | 0 | 0.767 |
| Validation contract | 4 | 4 | 0 | 0.293 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| provided_parallel_lines_zero_defects | Provided line geometry | True | 129.560 |  |
| provided_wide_pair_single_defect | Provided line geometry | True | 1.568 |  |
| provided_narrow_pair_single_defect | Provided line geometry | True | 0.887 |  |
| tolerance_boundary_is_not_defect | Tolerance contract | True | 0.809 |  |
| tolerance_exceeded_is_defect | Tolerance contract | True | 0.725 |  |
| high_sample_count_returns_requested_deviations | Sampling contract | True | 1.774 |  |
| min_sample_count_returns_requested_deviations | Sampling contract | True | 0.576 |  |
| diagonal_parallel_lines_zero_defects | Provided line geometry | True | 0.848 |  |
| sobel_provided_lines_zero_defects | Edge method contract | True | 0.932 |  |
| auto_detect_canny_pair_success | Auto line detection | True | 6.029 |  |
| auto_detect_sobel_pair_success | Auto line detection | True | 1.572 |  |
| auto_detect_blank_without_lines_fails | Failure contract | True | 0.803 |  |
| missing_image_fails | Failure contract | True | 0.273 |  |
| degenerate_line_fails | Failure contract | True | 0.328 |  |
| dict_start_end_line_parse | Line input contract | True | 0.860 |  |
| dict_x1_y1_line_parse | Line input contract | True | 0.686 |  |
| legacy_hashtable_line_parse | Line input contract | True | 2.359 |  |
| validate_defaults_valid | Validation contract | True | 0.404 |  |
| validate_negative_expected_invalid | Validation contract | True | 0.107 |  |
| validate_negative_tolerance_invalid | Validation contract | True | 0.043 |  |
| validate_bad_edge_method_invalid | Validation contract | True | 0.619 |  |
| build_edge_map_canny_nonzero | Private helper contract | True | 0.677 |  |
| build_edge_map_sobel_nonzero | Private helper contract | True | 0.573 |  |
| distance_point_to_line_horizontal | Private helper contract | True | 0.112 |  |
| angle_diff_wraps_180 | Private helper contract | True | 0.065 |  |
| try_parse_line_rejects_bad_dict | Line input contract | True | 0.108 |  |
| output_image_is_color_and_same_size | Output contract | True | 0.906 |  |

## Notes

- This is a synthetic contract baseline for edge-pair spacing inspection using generated line images and direct LineData inputs.
- It validates deviation sign/magnitude, tolerance boundaries, sample counts, Canny/Sobel edge maps, line input formats, auto-detection fallback, output image contract, and parameter failures.
- It does not claim field defect accuracy; real edge-pair robustness should be evaluated with production-like parts and optics.
