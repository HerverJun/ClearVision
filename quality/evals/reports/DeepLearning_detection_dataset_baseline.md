# DeepLearning Detection Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-29T03:21:22.7700048+00:00`
Dataset: `COCO-style semi-synthetic detection protocol bridge`
DatasetKind: `Tier A protocol bridge for public/COCO-style object detection metrics; no external image pixels are stored.`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 36 |
| Passed | 36 |
| Failed | 0 |
| Ground truth boxes | 42 |
| True positives | 42 |
| False positives | 0 |
| False negatives | 0 |
| Precision@0.50 | 1 |
| Recall@0.50 | 1 |
| AP50 | 1 |
| Mean matched IoU | 0.9735 |
| Confidence threshold | 0.45 |
| NMS IoU threshold | 0.45 |
| Match IoU threshold | 0.5 |
| Runtime ms | 1057.938 |

## Scenarios

| Scenario | Cases | Passed | Failed | GT | Detections | TP | FP | FN | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| different_class_overlap | 6 | 6 | 0 | 12 | 12 | 12 | 0 | 0 | 20.505 |
| edge_clamp | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 18.665 |
| multi_class | 6 | 6 | 0 | 12 | 12 | 12 | 0 | 0 | 18.576 |
| negative_low_confidence | 6 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 18.018 |
| same_class_nms | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 18.091 |
| single_object | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 82.469 |

## Failure Boundaries

- `edge_clamp` verifies detections near image borders remain matchable after coordinate clamp.
- `same_class_nms` verifies duplicate same-class candidates are suppressed before dataset scoring.
- `different_class_overlap` verifies overlapping boxes with different class ids are not suppressed across classes.
- `negative_low_confidence` verifies below-threshold candidates do not become false positives.
- This bridge records COCO-style detection metrics for DeepLearning post-processing; it is not a claim of production model accuracy.

## Cases

| Case | Scenario | Passed | Size | GT | Detections | TP | FP | FN | Best IoU | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| DeepLearning_single_object_0000 | single_object | True | 320x240 | 1 | 1 | 1 | 0 | 0 | 0.919 | 399.717 | - |
| DeepLearning_multi_class_0000 | multi_class | True | 320x240 | 2 | 2 | 2 | 0 | 0 | 0.9584 | 20.794 | - |
| DeepLearning_edge_clamp_0000 | edge_clamp | True | 320x240 | 1 | 1 | 1 | 0 | 0 | 0.8763 | 18.017 | - |
| DeepLearning_same_class_nms_0000 | same_class_nms | True | 320x240 | 1 | 1 | 1 | 0 | 0 | 1 | 18.542 | - |
| DeepLearning_different_class_overlap_0000 | different_class_overlap | True | 320x240 | 2 | 2 | 2 | 0 | 0 | 0.9828 | 18.312 | - |
| DeepLearning_negative_low_confidence_0000 | negative_low_confidence | True | 320x240 | 0 | 0 | 0 | 0 | 0 | 0 | 16.914 | - |
| DeepLearning_single_object_0001 | single_object | True | 512x384 | 1 | 1 | 1 | 0 | 0 | 0.9484 | 18.717 | - |
| DeepLearning_multi_class_0001 | multi_class | True | 512x384 | 2 | 2 | 2 | 0 | 0 | 0.9738 | 17.593 | - |
| DeepLearning_edge_clamp_0001 | edge_clamp | True | 512x384 | 1 | 1 | 1 | 0 | 0 | 0.9206 | 18.644 | - |
| DeepLearning_same_class_nms_0001 | same_class_nms | True | 512x384 | 1 | 1 | 1 | 0 | 0 | 1 | 17.406 | - |
| DeepLearning_different_class_overlap_0001 | different_class_overlap | True | 512x384 | 2 | 2 | 2 | 0 | 0 | 0.9892 | 18.768 | - |
| DeepLearning_negative_low_confidence_0001 | negative_low_confidence | True | 512x384 | 0 | 0 | 0 | 0 | 0 | 0 | 17.979 | - |
| DeepLearning_single_object_0002 | single_object | True | 640x480 | 1 | 1 | 1 | 0 | 0 | 0.9584 | 18.962 | - |
| DeepLearning_multi_class_0002 | multi_class | True | 640x480 | 2 | 2 | 2 | 0 | 0 | 0.9789 | 17.735 | - |
| DeepLearning_edge_clamp_0002 | edge_clamp | True | 640x480 | 1 | 1 | 1 | 0 | 0 | 0.9359 | 18.689 | - |
| DeepLearning_same_class_nms_0002 | same_class_nms | True | 640x480 | 1 | 1 | 1 | 0 | 0 | 1 | 18.254 | - |
| DeepLearning_different_class_overlap_0002 | different_class_overlap | True | 640x480 | 2 | 2 | 2 | 0 | 0 | 0.9914 | 28.189 | - |
| DeepLearning_negative_low_confidence_0002 | negative_low_confidence | True | 640x480 | 0 | 0 | 0 | 0 | 0 | 0 | 27.622 | - |
| DeepLearning_single_object_0003 | single_object | True | 800x600 | 1 | 1 | 1 | 0 | 0 | 0.9666 | 29.115 | - |
| DeepLearning_multi_class_0003 | multi_class | True | 800x600 | 2 | 2 | 2 | 0 | 0 | 0.9831 | 29.309 | - |
| DeepLearning_edge_clamp_0003 | edge_clamp | True | 800x600 | 1 | 1 | 1 | 0 | 0 | 0.9483 | 29.122 | - |
| DeepLearning_same_class_nms_0003 | same_class_nms | True | 800x600 | 1 | 1 | 1 | 0 | 0 | 1 | 28.667 | - |
| DeepLearning_different_class_overlap_0003 | different_class_overlap | True | 800x600 | 2 | 2 | 2 | 0 | 0 | 0.9931 | 29.57 | - |
| DeepLearning_negative_low_confidence_0003 | negative_low_confidence | True | 800x600 | 0 | 0 | 0 | 0 | 0 | 0 | 20.228 | - |
| DeepLearning_single_object_0004 | single_object | True | 960x540 | 1 | 1 | 1 | 0 | 0 | 0.9671 | 13.365 | - |
| DeepLearning_multi_class_0004 | multi_class | True | 960x540 | 2 | 2 | 2 | 0 | 0 | 0.9834 | 12.117 | - |
| DeepLearning_edge_clamp_0004 | edge_clamp | True | 960x540 | 1 | 1 | 1 | 0 | 0 | 0.9519 | 12.941 | - |
| DeepLearning_same_class_nms_0004 | same_class_nms | True | 960x540 | 1 | 1 | 1 | 0 | 0 | 1 | 12.102 | - |
| DeepLearning_different_class_overlap_0004 | different_class_overlap | True | 960x540 | 2 | 2 | 2 | 0 | 0 | 0.9942 | 13.221 | - |
| DeepLearning_negative_low_confidence_0004 | negative_low_confidence | True | 960x540 | 0 | 0 | 0 | 0 | 0 | 0 | 11.916 | - |
| DeepLearning_single_object_0005 | single_object | True | 1280x720 | 1 | 1 | 1 | 0 | 0 | 0.9752 | 14.936 | - |
| DeepLearning_multi_class_0005 | multi_class | True | 1280x720 | 2 | 2 | 2 | 0 | 0 | 0.9875 | 13.908 | - |
| DeepLearning_edge_clamp_0005 | edge_clamp | True | 1280x720 | 1 | 1 | 1 | 0 | 0 | 0.9637 | 14.577 | - |
| DeepLearning_same_class_nms_0005 | same_class_nms | True | 1280x720 | 1 | 1 | 1 | 0 | 0 | 1 | 13.573 | - |
| DeepLearning_different_class_overlap_0005 | different_class_overlap | True | 1280x720 | 2 | 2 | 2 | 0 | 0 | 0.9957 | 14.97 | - |
| DeepLearning_negative_low_confidence_0005 | negative_low_confidence | True | 1280x720 | 0 | 0 | 0 | 0 | 0 | 0 | 13.447 | - |
