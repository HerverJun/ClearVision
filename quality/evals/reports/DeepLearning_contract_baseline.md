# DeepLearning Contract Baseline

GeneratedAtUtc: `2026-04-26T07:59:32.6175120+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 26 |
| Passed | 26 |
| Failed | 0 |
| Runtime ms | 52.544 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Label contract | 4 | 4 | 0 | 0.429 |
| NMS contract | 5 | 5 | 0 | 0.375 |
| Output contract | 2 | 2 | 0 | 0.130 |
| Output tensor selection | 3 | 3 | 0 | 1.210 |
| Preprocess contract | 2 | 2 | 0 | 6.588 |
| Target class contract | 2 | 2 | 0 | 4.390 |
| Visualization contract | 1 | 1 | 0 | 0.287 |
| YOLO output parsing | 5 | 5 | 0 | 4.048 |
| YOLO version detection | 2 | 2 | 0 | 1.291 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| yolov8_standard_box_mapping | YOLO output parsing | True | 16.939 |  |
| yolov8_transposed_box_mapping | YOLO output parsing | True | 1.716 |  |
| yolov8_coordinate_clamp | YOLO output parsing | True | 0.202 |  |
| yolov5_standard_objectness_product | YOLO output parsing | True | 0.878 |  |
| yolov5_transposed_objectness_product | YOLO output parsing | True | 0.507 |  |
| auto_detect_yolov8_custom_labels | YOLO version detection | True | 2.237 |  |
| auto_detect_yolov5_custom_labels | YOLO version detection | True | 0.345 |  |
| select_detection_output_known_label_count | Output tensor selection | True | 0.973 |  |
| select_detection_output_rank3_heuristic | Output tensor selection | True | 0.433 |  |
| select_detection_output_fail_closed | Output tensor selection | True | 2.223 |  |
| nms_same_class_suppresses_overlap | NMS contract | True | 1.264 |  |
| nms_different_class_keeps_overlap | NMS contract | True | 0.264 |  |
| nms_iou_threshold_low_suppresses | NMS contract | True | 0.134 |  |
| nms_iou_threshold_high_keeps | NMS contract | True | 0.107 |  |
| nms_invalid_box_discarded | NMS contract | True | 0.106 |  |
| target_classes_numeric_filter | Target class contract | True | 1.218 |  |
| target_classes_named_parse | Target class contract | True | 7.562 |  |
| label_contract_match_valid | Label contract | True | 0.825 |  |
| label_contract_mismatch_fails | Label contract | True | 0.469 |  |
| label_contract_missing_fails | Label contract | True | 0.254 |  |
| visualization_nms_when_internal_disabled | Visualization contract | True | 0.287 |  |
| statistics_label_object_mode | Output contract | True | 0.136 |  |
| statistics_label_defect_mode | Output contract | True | 0.123 |  |
| preprocess_grayscale_to_chw_rgb | Preprocess contract | True | 12.086 |  |
| preprocess_float_unit_range | Preprocess contract | True | 1.089 |  |
| class_name_fallback_is_class_id | Label contract | True | 0.167 |  |

## Notes

- This is a contract baseline using controlled fake YOLO tensors and direct DeepLearningOperator post-processing paths.
- It validates output tensor layout parsing, coordinate mapping, configurable NMS, same-class NMS isolation, TargetClasses parsing, label-contract failures, preprocessing, and output text contracts.
- It does not claim model accuracy; real model quality should be evaluated separately with a public or field dataset.
