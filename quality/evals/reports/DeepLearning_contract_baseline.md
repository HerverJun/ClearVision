# DeepLearning Contract Baseline

GeneratedAtUtc: `2026-04-29T03:30:05.2538970+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 26 |
| Passed | 26 |
| Failed | 0 |
| Runtime ms | 114.769 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Label contract | 4 | 4 | 0 | 0.435 |
| NMS contract | 5 | 5 | 0 | 0.366 |
| Output contract | 2 | 2 | 0 | 0.129 |
| Output tensor selection | 3 | 3 | 0 | 1.129 |
| Preprocess contract | 2 | 2 | 0 | 6.210 |
| Target class contract | 2 | 2 | 0 | 4.365 |
| Visualization contract | 1 | 1 | 0 | 0.282 |
| YOLO output parsing | 5 | 5 | 0 | 16.674 |
| YOLO version detection | 2 | 2 | 0 | 1.376 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| yolov8_standard_box_mapping | YOLO output parsing | True | 80.386 |  |
| yolov8_transposed_box_mapping | YOLO output parsing | True | 1.566 |  |
| yolov8_coordinate_clamp | YOLO output parsing | True | 0.185 |  |
| yolov5_standard_objectness_product | YOLO output parsing | True | 0.774 |  |
| yolov5_transposed_objectness_product | YOLO output parsing | True | 0.460 |  |
| auto_detect_yolov8_custom_labels | YOLO version detection | True | 2.529 |  |
| auto_detect_yolov5_custom_labels | YOLO version detection | True | 0.223 |  |
| select_detection_output_known_label_count | Output tensor selection | True | 0.990 |  |
| select_detection_output_rank3_heuristic | Output tensor selection | True | 0.293 |  |
| select_detection_output_fail_closed | Output tensor selection | True | 2.104 |  |
| nms_same_class_suppresses_overlap | NMS contract | True | 1.228 |  |
| nms_different_class_keeps_overlap | NMS contract | True | 0.253 |  |
| nms_iou_threshold_low_suppresses | NMS contract | True | 0.126 |  |
| nms_iou_threshold_high_keeps | NMS contract | True | 0.111 |  |
| nms_invalid_box_discarded | NMS contract | True | 0.113 |  |
| target_classes_numeric_filter | Target class contract | True | 1.079 |  |
| target_classes_named_parse | Target class contract | True | 7.650 |  |
| label_contract_match_valid | Label contract | True | 0.842 |  |
| label_contract_mismatch_fails | Label contract | True | 0.491 |  |
| label_contract_missing_fails | Label contract | True | 0.245 |  |
| visualization_nms_when_internal_disabled | Visualization contract | True | 0.282 |  |
| statistics_label_object_mode | Output contract | True | 0.137 |  |
| statistics_label_defect_mode | Output contract | True | 0.121 |  |
| preprocess_grayscale_to_chw_rgb | Preprocess contract | True | 11.371 |  |
| preprocess_float_unit_range | Preprocess contract | True | 1.048 |  |
| class_name_fallback_is_class_id | Label contract | True | 0.162 |  |

## Notes

- This is a contract baseline using controlled fake YOLO tensors and direct DeepLearningOperator post-processing paths.
- It validates output tensor layout parsing, coordinate mapping, configurable NMS, same-class NMS isolation, TargetClasses parsing, label-contract failures, preprocessing, and output text contracts.
- It does not claim model accuracy; real model quality should be evaluated separately with a public or field dataset.
