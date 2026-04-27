# DeepLearning Detection Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-27T11:19:44.1823679+00:00`
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
| Runtime ms | 1074.771 |

## Scenarios

| Scenario | Cases | Passed | Failed | GT | Detections | TP | FP | FN | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| different_class_overlap | 6 | 6 | 0 | 12 | 12 | 12 | 0 | 0 | 19.839 |
| edge_clamp | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 19.794 |
| multi_class | 6 | 6 | 0 | 12 | 12 | 12 | 0 | 0 | 19.385 |
| negative_low_confidence | 6 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 17.426 |
| same_class_nms | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 20.19 |
| single_object | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 82.494 |

## Failure Boundaries

- `edge_clamp` verifies detections near image borders remain matchable after coordinate clamp.
- `same_class_nms` verifies duplicate same-class candidates are suppressed before dataset scoring.
- `different_class_overlap` verifies overlapping boxes with different class ids are not suppressed across classes.
- `negative_low_confidence` verifies below-threshold candidates do not become false positives.
- This bridge records COCO-style detection metrics for DeepLearning post-processing; it is not a claim of production model accuracy.

## Cases

| Case | Scenario | Passed | Size | GT | Detections | TP | FP | FN | Best IoU | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| DeepLearning_single_object_0000 | single_object | True | 320x240 | 1 | 1 | 1 | 0 | 0 | 0.919 | 396.452 | - |
| DeepLearning_multi_class_0000 | multi_class | True | 320x240 | 2 | 2 | 2 | 0 | 0 | 0.9584 | 21.594 | - |
| DeepLearning_edge_clamp_0000 | edge_clamp | True | 320x240 | 1 | 1 | 1 | 0 | 0 | 0.8763 | 19.284 | - |
| DeepLearning_same_class_nms_0000 | same_class_nms | True | 320x240 | 1 | 1 | 1 | 0 | 0 | 1 | 18.853 | - |
| DeepLearning_different_class_overlap_0000 | different_class_overlap | True | 320x240 | 2 | 2 | 2 | 0 | 0 | 0.9828 | 19.218 | - |
| DeepLearning_negative_low_confidence_0000 | negative_low_confidence | True | 320x240 | 0 | 0 | 0 | 0 | 0 | 0 | 18.043 | - |
| DeepLearning_single_object_0001 | single_object | True | 512x384 | 1 | 1 | 1 | 0 | 0 | 0.9484 | 19.244 | - |
| DeepLearning_multi_class_0001 | multi_class | True | 512x384 | 2 | 2 | 2 | 0 | 0 | 0.9738 | 19.122 | - |
| DeepLearning_edge_clamp_0001 | edge_clamp | True | 512x384 | 1 | 1 | 1 | 0 | 0 | 0.9206 | 19.638 | - |
| DeepLearning_same_class_nms_0001 | same_class_nms | True | 512x384 | 1 | 1 | 1 | 0 | 0 | 1 | 18.19 | - |
| DeepLearning_different_class_overlap_0001 | different_class_overlap | True | 512x384 | 2 | 2 | 2 | 0 | 0 | 0.9892 | 20.187 | - |
| DeepLearning_negative_low_confidence_0001 | negative_low_confidence | True | 512x384 | 0 | 0 | 0 | 0 | 0 | 0 | 18.214 | - |
| DeepLearning_single_object_0002 | single_object | True | 640x480 | 1 | 1 | 1 | 0 | 0 | 0.9584 | 20.434 | - |
| DeepLearning_multi_class_0002 | multi_class | True | 640x480 | 2 | 2 | 2 | 0 | 0 | 0.9789 | 18.972 | - |
| DeepLearning_edge_clamp_0002 | edge_clamp | True | 640x480 | 1 | 1 | 1 | 0 | 0 | 0.9359 | 19.679 | - |
| DeepLearning_same_class_nms_0002 | same_class_nms | True | 640x480 | 1 | 1 | 1 | 0 | 0 | 1 | 27.58 | - |
| DeepLearning_different_class_overlap_0002 | different_class_overlap | True | 640x480 | 2 | 2 | 2 | 0 | 0 | 0.9914 | 29.779 | - |
| DeepLearning_negative_low_confidence_0002 | negative_low_confidence | True | 640x480 | 0 | 0 | 0 | 0 | 0 | 0 | 28.842 | - |
| DeepLearning_single_object_0003 | single_object | True | 800x600 | 1 | 1 | 1 | 0 | 0 | 0.9666 | 30.125 | - |
| DeepLearning_multi_class_0003 | multi_class | True | 800x600 | 2 | 2 | 2 | 0 | 0 | 0.9831 | 29.455 | - |
| DeepLearning_edge_clamp_0003 | edge_clamp | True | 800x600 | 1 | 1 | 1 | 0 | 0 | 0.9483 | 30.861 | - |
| DeepLearning_same_class_nms_0003 | same_class_nms | True | 800x600 | 1 | 1 | 1 | 0 | 0 | 1 | 29.27 | - |
| DeepLearning_different_class_overlap_0003 | different_class_overlap | True | 800x600 | 2 | 2 | 2 | 0 | 0 | 0.9931 | 19.744 | - |
| DeepLearning_negative_low_confidence_0003 | negative_low_confidence | True | 800x600 | 0 | 0 | 0 | 0 | 0 | 0 | 12.464 | - |
| DeepLearning_single_object_0004 | single_object | True | 960x540 | 1 | 1 | 1 | 0 | 0 | 0.9671 | 13.641 | - |
| DeepLearning_multi_class_0004 | multi_class | True | 960x540 | 2 | 2 | 2 | 0 | 0 | 0.9834 | 13.021 | - |
| DeepLearning_edge_clamp_0004 | edge_clamp | True | 960x540 | 1 | 1 | 1 | 0 | 0 | 0.9519 | 13.85 | - |
| DeepLearning_same_class_nms_0004 | same_class_nms | True | 960x540 | 1 | 1 | 1 | 0 | 0 | 1 | 12.942 | - |
| DeepLearning_different_class_overlap_0004 | different_class_overlap | True | 960x540 | 2 | 2 | 2 | 0 | 0 | 0.9942 | 14.318 | - |
| DeepLearning_negative_low_confidence_0004 | negative_low_confidence | True | 960x540 | 0 | 0 | 0 | 0 | 0 | 0 | 12.568 | - |
| DeepLearning_single_object_0005 | single_object | True | 1280x720 | 1 | 1 | 1 | 0 | 0 | 0.9752 | 15.067 | - |
| DeepLearning_multi_class_0005 | multi_class | True | 1280x720 | 2 | 2 | 2 | 0 | 0 | 0.9875 | 14.146 | - |
| DeepLearning_edge_clamp_0005 | edge_clamp | True | 1280x720 | 1 | 1 | 1 | 0 | 0 | 0.9637 | 15.455 | - |
| DeepLearning_same_class_nms_0005 | same_class_nms | True | 1280x720 | 1 | 1 | 1 | 0 | 0 | 1 | 14.308 | - |
| DeepLearning_different_class_overlap_0005 | different_class_overlap | True | 1280x720 | 2 | 2 | 2 | 0 | 0 | 0.9957 | 15.788 | - |
| DeepLearning_negative_low_confidence_0005 | negative_low_confidence | True | 1280x720 | 0 | 0 | 0 | 0 | 0 | 0 | 14.423 | - |
