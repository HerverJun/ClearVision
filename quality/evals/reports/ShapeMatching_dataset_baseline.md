# ShapeMatching Geometric Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-29T03:21:34.8356248+00:00`
Dataset: `Semi-synthetic geometric shape matching scene protocol`
DatasetKind: `Tier B semi-synthetic geometric scenes with fixed seed, pose labels, multi-target labels, and no-match negatives.`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 36 |
| Passed | 36 |
| Failed | 0 |
| Ground truth poses | 36 |
| Predicted poses | 36 |
| True positives | 36 |
| False positives | 0 |
| False negatives | 0 |
| Precision | 1 |
| Recall | 1 |
| F1 | 1 |
| Mean position error px | 0.025 |
| Mean angle error deg | 0.056 |
| Mean scale error | 0 |
| Min score | 0.951 |
| Mean score | 0.9979 |
| Position tolerance px | 8 |
| Angle tolerance deg | 6 |
| Scale tolerance | 0.16 |
| Runtime ms | 1658.717 |

## Scenarios

| Scenario | Cases | Passed | Failed | GT | Pred | TP | FP | FN | F1 | Pos err px | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| blank_negative | 6 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 32.45 |
| direct_pose | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.001 | 12.593 |
| multi_target | 6 | 6 | 0 | 12 | 12 | 12 | 0 | 0 | 1 | 0.001 | 3.373 |
| rotated_pose | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.001 | 49.677 |
| scaled_pose | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.149 | 175.874 |
| top_left_origin | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0 | 2.485 |

## Failure Boundaries

- `direct_pose` verifies exact translation pose recovery for fixed-size templates.
- `rotated_pose` verifies rotation search over positive angle transforms.
- `scaled_pose` verifies scale search and mixed rotation/scale transforms.
- `multi_target` verifies MaxMatches and non-maximum suppression for two same-pose targets.
- `top_left_origin` verifies reference-origin reporting when OriginMode is TopLeft.
- `blank_negative` verifies empty scenes reject with zero matches and structured no-match reason.
- This bridge records semi-synthetic geometric-scene metrics for the ShapeMatching rotation-scale template path; it is not field-image accuracy evidence.

## Cases

| Case | Scenario | Passed | Size | GT | Pred | TP | FP | FN | F1 | Pos err px | Angle err | Scale err | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| ShapeMatching_direct_pose_0000 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 61.189 | - |
| ShapeMatching_rotated_pose_0000 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 92.615 | - |
| ShapeMatching_scaled_pose_0000 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.22 | 2 | 0 | 282.272 | - |
| ShapeMatching_multi_target_0000 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 4.234 | - |
| ShapeMatching_top_left_origin_0000 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 3.003 | - |
| ShapeMatching_blank_negative_0000 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 27.706 | - |
| ShapeMatching_direct_pose_0001 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.885 | - |
| ShapeMatching_rotated_pose_0001 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.003 | 0 | 0 | 31.244 | - |
| ShapeMatching_scaled_pose_0001 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.171 | 0 | 0 | 66.428 | - |
| ShapeMatching_multi_target_0001 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.405 | - |
| ShapeMatching_top_left_origin_0001 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.492 | - |
| ShapeMatching_blank_negative_0001 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 24.029 | - |
| ShapeMatching_direct_pose_0002 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.498 | - |
| ShapeMatching_rotated_pose_0002 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.001 | 0 | 0 | 31.698 | - |
| ShapeMatching_scaled_pose_0002 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.256 | 0 | 0 | 81.543 | - |
| ShapeMatching_multi_target_0002 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.331 | - |
| ShapeMatching_top_left_origin_0002 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0.002 | 0 | 0 | 2.493 | - |
| ShapeMatching_blank_negative_0002 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 46.095 | - |
| ShapeMatching_direct_pose_0003 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0.003 | 0 | 0 | 3.462 | - |
| ShapeMatching_rotated_pose_0003 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 69.937 | - |
| ShapeMatching_scaled_pose_0003 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.148 | 0 | 0 | 237.224 | - |
| ShapeMatching_multi_target_0003 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0 | 0 | 0 | 4.286 | - |
| ShapeMatching_top_left_origin_0003 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 3.18 | - |
| ShapeMatching_blank_negative_0003 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 36.539 | - |
| ShapeMatching_direct_pose_0004 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 3.501 | - |
| ShapeMatching_rotated_pose_0004 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.001 | 0 | 0 | 54.629 | - |
| ShapeMatching_scaled_pose_0004 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 280.537 | - |
| ShapeMatching_multi_target_0004 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0 | 0 | 0 | 2.532 | - |
| ShapeMatching_top_left_origin_0004 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1.758 | - |
| ShapeMatching_blank_negative_0004 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 30.553 | - |
| ShapeMatching_direct_pose_0005 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.02 | - |
| ShapeMatching_rotated_pose_0005 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.002 | 0 | 0 | 17.941 | - |
| ShapeMatching_scaled_pose_0005 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.099 | 0 | 0 | 107.242 | - |
| ShapeMatching_multi_target_0005 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0 | 0 | 0 | 2.452 | - |
| ShapeMatching_top_left_origin_0005 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1.984 | - |
| ShapeMatching_blank_negative_0005 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 29.78 | - |
