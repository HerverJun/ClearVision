# ShapeMatching Geometric Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-27T12:04:39.1371181+00:00`
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
| Runtime ms | 1804.38 |

## Scenarios

| Scenario | Cases | Passed | Failed | GT | Pred | TP | FP | FN | F1 | Pos err px | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| blank_negative | 6 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 38.144 |
| direct_pose | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.001 | 14.962 |
| multi_target | 6 | 6 | 0 | 12 | 12 | 12 | 0 | 0 | 1 | 0.001 | 3.484 |
| rotated_pose | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.001 | 58.252 |
| scaled_pose | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.149 | 183.252 |
| top_left_origin | 6 | 6 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0 | 2.636 |

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
| ShapeMatching_direct_pose_0000 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 75.129 | - |
| ShapeMatching_rotated_pose_0000 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 129.281 | - |
| ShapeMatching_scaled_pose_0000 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.22 | 2 | 0 | 257.987 | - |
| ShapeMatching_multi_target_0000 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.791 | - |
| ShapeMatching_top_left_origin_0000 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 3.363 | - |
| ShapeMatching_blank_negative_0000 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 29.394 | - |
| ShapeMatching_direct_pose_0001 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.69 | - |
| ShapeMatching_rotated_pose_0001 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.003 | 0 | 0 | 33.095 | - |
| ShapeMatching_scaled_pose_0001 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.171 | 0 | 0 | 70.359 | - |
| ShapeMatching_multi_target_0001 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.502 | - |
| ShapeMatching_top_left_origin_0001 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.429 | - |
| ShapeMatching_blank_negative_0001 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 27.928 | - |
| ShapeMatching_direct_pose_0002 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.631 | - |
| ShapeMatching_rotated_pose_0002 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.001 | 0 | 0 | 32.941 | - |
| ShapeMatching_scaled_pose_0002 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.256 | 0 | 0 | 84.954 | - |
| ShapeMatching_multi_target_0002 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.515 | - |
| ShapeMatching_top_left_origin_0002 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0.002 | 0 | 0 | 2.642 | - |
| ShapeMatching_blank_negative_0002 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 32.993 | - |
| ShapeMatching_direct_pose_0003 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0.003 | 0 | 0 | 3.84 | - |
| ShapeMatching_rotated_pose_0003 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 74.729 | - |
| ShapeMatching_scaled_pose_0003 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.148 | 0 | 0 | 256.135 | - |
| ShapeMatching_multi_target_0003 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0 | 0 | 0 | 4.457 | - |
| ShapeMatching_top_left_origin_0003 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 3.352 | - |
| ShapeMatching_blank_negative_0003 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 41.8 | - |
| ShapeMatching_direct_pose_0004 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 3.389 | - |
| ShapeMatching_rotated_pose_0004 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.001 | 0 | 0 | 55.188 | - |
| ShapeMatching_scaled_pose_0004 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 301.997 | - |
| ShapeMatching_multi_target_0004 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0 | 0 | 0 | 2.832 | - |
| ShapeMatching_top_left_origin_0004 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1.87 | - |
| ShapeMatching_blank_negative_0004 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 64.143 | - |
| ShapeMatching_direct_pose_0005 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.096 | - |
| ShapeMatching_rotated_pose_0005 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.002 | 0 | 0 | 24.278 | - |
| ShapeMatching_scaled_pose_0005 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.099 | 0 | 0 | 128.08 | - |
| ShapeMatching_multi_target_0005 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0 | 0 | 0 | 2.807 | - |
| ShapeMatching_top_left_origin_0005 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.158 | - |
| ShapeMatching_blank_negative_0005 | blank_negative | True | 240x220 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 32.605 | - |
