# DeepLearning Contract Baseline

GeneratedAtUtc: `2026-07-05T05:26:53.1150434+00:00`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 26 |
| Passed | 26 |
| Failed | 0 |
| Runtime ms | 69.855 |

## Scenarios

| Scenario | Cases | Passed | Failed | Avg ms |
| --- | ---: | ---: | ---: | ---: |
| Label contract | 4 | 4 | 0 | 0.534 |
| NMS contract | 5 | 5 | 0 | 0.638 |
| Output contract | 2 | 2 | 0 | 0.146 |
| Output tensor selection | 3 | 3 | 0 | 1.584 |
| Preprocess contract | 2 | 2 | 0 | 9.306 |
| Target class contract | 2 | 2 | 0 | 5.962 |
| Visualization contract | 1 | 1 | 0 | 0.335 |
| YOLO output parsing | 5 | 5 | 0 | 5.057 |
| YOLO version detection | 2 | 2 | 0 | 1.666 |

## Cases

| Case | Scenario | Passed | Runtime ms | Failure |
| --- | --- | --- | ---: | --- |
| yolov8_standard_box_mapping | YOLO output parsing | True | 21.684 |  |
| yolov8_transposed_box_mapping | YOLO output parsing | True | 1.927 |  |
| yolov8_coordinate_clamp | YOLO output parsing | True | 0.245 |  |
| yolov5_standard_objectness_product | YOLO output parsing | True | 0.864 |  |
| yolov5_transposed_objectness_product | YOLO output parsing | True | 0.564 |  |
| auto_detect_yolov8_custom_labels | YOLO version detection | True | 2.983 |  |
| auto_detect_yolov5_custom_labels | YOLO version detection | True | 0.348 |  |
| select_detection_output_known_label_count | Output tensor selection | True | 1.180 |  |
| select_detection_output_rank3_heuristic | Output tensor selection | True | 0.360 |  |
| select_detection_output_fail_closed | Output tensor selection | True | 3.212 |  |
| nms_same_class_suppresses_overlap | NMS contract | True | 1.831 |  |
| nms_different_class_keeps_overlap | NMS contract | True | 0.520 |  |
| nms_iou_threshold_low_suppresses | NMS contract | True | 0.301 |  |
| nms_iou_threshold_high_keeps | NMS contract | True | 0.259 |  |
| nms_invalid_box_discarded | NMS contract | True | 0.279 |  |
| target_classes_numeric_filter | Target class contract | True | 2.055 |  |
| target_classes_named_parse | Target class contract | True | 9.870 |  |
| label_contract_match_valid | Label contract | True | 0.880 |  |
| label_contract_metadata_overrides_external | Label contract | True | 0.576 |  |
| label_contract_missing_fails | Label contract | True | 0.332 |  |
| visualization_nms_when_internal_disabled | Visualization contract | True | 0.335 |  |
| statistics_label_object_mode | Output contract | True | 0.153 |  |
| statistics_label_defect_mode | Output contract | True | 0.138 |  |
| preprocess_grayscale_to_chw_rgb | Preprocess contract | True | 17.259 |  |
| preprocess_float_unit_range | Preprocess contract | True | 1.353 |  |
| class_name_fallback_is_class_id | Label contract | True | 0.347 |  |

## Notes

- This is a contract baseline using controlled fake YOLO tensors and direct DeepLearningOperator post-processing paths.
- It validates output tensor layout parsing, coordinate mapping, configurable NMS, same-class NMS isolation, TargetClasses parsing, label-contract failures, preprocessing, and output text contracts.
- It does not claim model accuracy; real model quality should be evaluated separately with a public or field dataset.
