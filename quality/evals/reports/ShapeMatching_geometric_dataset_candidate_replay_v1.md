# ShapeMatching Geometric Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T02:39:47.4970051+00:00`
Dataset: `Semi-synthetic geometric shape matching scene protocol`
DatasetKind: `Tier B semi-synthetic geometric scenes with fixed seed, pose labels, multi-target labels, and no-match negatives.`
CandidateVersion: `v1`
Profile: `geometric_dataset_bridge_v1`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Ground truth poses | 23 |
| Predicted poses | 23 |
| True positives | 23 |
| False positives | 0 |
| False negatives | 0 |
| Precision | 1 |
| Recall | 1 |
| F1 | 1 |
| Mean position error px | 0.04 |
| Mean angle error deg | 0.087 |
| Mean scale error | 0 |
| Min score | 0.951 |
| Mean score | 0.9967 |
| Position tolerance px | 8 |
| Angle tolerance deg | 6 |
| Scale tolerance | 0.16 |
| Runtime ms | 1056.649 |

## Scenarios

| Scenario | Cases | Passed | Failed | GT | Pred | TP | FP | FN | F1 | Pos err px | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| direct_pose | 4 | 4 | 0 | 4 | 4 | 4 | 0 | 0 | 1 | 0.001 | 17.19 |
| multi_target | 3 | 3 | 0 | 6 | 6 | 6 | 0 | 0 | 1 | 0.001 | 3.267 |
| rotated_pose | 5 | 5 | 0 | 5 | 5 | 5 | 0 | 0 | 1 | 0.001 | 26.74 |
| scaled_pose | 5 | 5 | 0 | 5 | 5 | 5 | 0 | 0 | 1 | 0.179 | 167.272 |
| top_left_origin | 3 | 3 | 0 | 3 | 3 | 3 | 0 | 0 | 1 | 0.001 | 2.677 |

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
| ShapeMatching_direct_pose_0000 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 62.336 | - |
| ShapeMatching_scaled_pose_0000 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.22 | 2 | 0 | 449.348 | - |
| ShapeMatching_multi_target_0000 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.573 | - |
| ShapeMatching_direct_pose_0001 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.213 | - |
| ShapeMatching_rotated_pose_0001 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.003 | 0 | 0 | 30.881 | - |
| ShapeMatching_scaled_pose_0001 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.171 | 0 | 0 | 64.763 | - |
| ShapeMatching_multi_target_0001 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.095 | - |
| ShapeMatching_rotated_pose_0002 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.001 | 0 | 0 | 28.303 | - |
| ShapeMatching_scaled_pose_0002 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.256 | 0 | 0 | 74.348 | - |
| ShapeMatching_multi_target_0002 | multi_target | True | 260x250 | 2 | 2 | 2 | 0 | 0 | 1 | 0.001 | 0 | 0 | 3.132 | - |
| ShapeMatching_top_left_origin_0002 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0.002 | 0 | 0 | 3.03 | - |
| ShapeMatching_direct_pose_0003 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0.003 | 0 | 0 | 2.242 | - |
| ShapeMatching_rotated_pose_0003 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 24.531 | - |
| ShapeMatching_scaled_pose_0003 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.148 | 0 | 0 | 99.54 | - |
| ShapeMatching_top_left_origin_0003 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.521 | - |
| ShapeMatching_rotated_pose_0004 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.001 | 0 | 0 | 25.317 | - |
| ShapeMatching_top_left_origin_0004 | top_left_origin | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 2.479 | - |
| ShapeMatching_direct_pose_0005 | direct_pose | True | 240x220 | 1 | 1 | 1 | 0 | 0 | 1 | 0 | 0 | 0 | 1.968 | - |
| ShapeMatching_rotated_pose_0005 | rotated_pose | True | 280x260 | 1 | 1 | 1 | 0 | 0 | 1 | 0.002 | 0 | 0 | 24.667 | - |
| ShapeMatching_scaled_pose_0005 | scaled_pose | True | 300x280 | 1 | 1 | 1 | 0 | 0 | 1 | 0.099 | 0 | 0 | 148.362 | - |
