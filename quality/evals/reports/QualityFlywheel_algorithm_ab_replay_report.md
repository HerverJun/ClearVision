# Quality Flywheel Algorithm A/B Replay Report

GeneratedAtUtc: `2026-04-30T12:54:35+00:00`
Accepted: `Yes`

## Summary

- Operators: 10
- Replay cases compared: 183
- Executed candidate cases: 160
- Control cases: 23
- Fixed cases: 37
- Regressed cases: 0
- Matching viewpoint cases: 11
- Matching viewpoint fixed: 10
- Surface defect replay cases: 20
- Surface defect improved cases: 10
- Surface defect regressed cases: 0
- Surface defect worse metric cases: 8
- Anomaly detection replay cases: 20
- Anomaly detection improved cases: 14
- Anomaly detection regressed cases: 0
- Anomaly detection worse metric cases: 0
- Anomaly detection image-correct cases: 11
- Anomaly detection detected anomaly cases: 11
- EdgeDetection replay cases: 20
- EdgeDetection improved cases: 12
- EdgeDetection regressed cases: 0
- EdgeDetection worse metric cases: 8
- EdgeDetection worse taxonomy: {'boundary_f1_drop_gt_0_01': 4, 'boundary_recall_drop': 8, 'large_recall_drop': 6, 'low_absolute_recall': 4, 'minor_boundary_f1_drop': 4, 'precision_drop': 7, 'precision_gain_recall_tradeoff': 1, 'reduced_edge_density': 8}
- SemanticSegmentation replay cases: 20
- SemanticSegmentation improved cases: 0
- SemanticSegmentation regressed cases: 0
- SemanticSegmentation worse metric cases: 0
- CameraCalibration replay cases: 3
- CameraCalibration executed cases: 0
- CameraCalibration regressed cases: 0
- CameraCalibration worse metric cases: 0
- TemplateMatching replay cases: 20
- TemplateMatching improved cases: 0
- TemplateMatching regressed cases: 0
- TemplateMatching worse metric cases: 0
- ShapeMatching replay cases: 20
- ShapeMatching improved cases: 0
- ShapeMatching regressed cases: 0
- ShapeMatching worse metric cases: 0
- DeepLearning replay cases: 20
- DeepLearning real-model candidate cases: 0
- DeepLearning processing-error cases: 0

## Operators

| Operator | Dataset | Status | Replay | Old Pass | New Pass | Fixed | Regressed | Worse metric | Candidate |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| AkazeFeatureMatch | hpatches | candidate-executed | 20 | 0.0 | 0.95 | 19 | 0 | 0 | quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_replay_center_only_v1.json |
| AnomalyDetection | mvtec_ad_lite | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 0 | quality/evals/reports/AnomalyDetection_mvtec_candidate_replay_v2.json |
| CameraCalibration | opencv_calibration_samples | unchanged-baseline-control | 3 | 1.0 | 1.0 | 0 | 0 | 0 | same-as-old-control |
| DeepLearning | coco2017 | unchanged-baseline-control | 20 | 0.0 | 0.0 | 0 | 0 | 0 | same-as-old-control |
| EdgeDetection | bsds500 | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 8 | quality/evals/reports/EdgeDetection_bsds500_candidate_replay_v1.json |
| OrbFeatureMatch | hpatches | candidate-executed | 20 | 0.0 | 0.9 | 18 | 0 | 1 | quality/evals/reports/OrbFeatureMatch_hpatches_candidate_replay_center_only_v1.json |
| SemanticSegmentation | voc-style-protocol-bridge | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 0 | quality/evals/reports/SemanticSegmentation_dataset_candidate_replay_v1.json |
| ShapeMatching | semisynthetic-geometric-shape-scenes | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 0 | quality/evals/reports/ShapeMatching_geometric_dataset_candidate_replay_v1.json |
| SurfaceDefectDetection | kolektorsdd2 | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 8 | quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_candidate_replay_v1.json |
| TemplateMatching | hpatches-style-homography-bridge | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 0 | quality/evals/reports/TemplateMatching_public_bridge_candidate_replay_v1.json |

## Matching Viewpoint Focus

| Operator | Case | Status | New pass | Old error | New error | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---|---|
| OrbFeatureMatch | v_astronautis_1_2 | worse-metric | False | 108.405 | 307.23 | 1 | 1.493 | 1.899 | 0 | True | Projected quadrilateral is invalid. |
| AkazeFeatureMatch | v_abstract_1_2 | fixed | True | 378.274 | 0.31 | 0.986 | 0.956 | 1.014 | 0 | True | - |
| AkazeFeatureMatch | v_adam_1_2 | fixed | True | 230.146 | 0.47 | 0.978 | 0.874 | 0.615 | 2 | True | - |
| AkazeFeatureMatch | v_apprentices_1_2 | fixed | True | 127.844 | 0.081 | 1 | 0.451 | 0.868 | 2 | True | - |
| AkazeFeatureMatch | v_astronautis_1_2 | fixed | True | 400.798 | 0.231 | 0.911 | 1.109 | 1.901 | 0 | True | - |
| AkazeFeatureMatch | v_azzola_1_2 | fixed | True | 254.382 | 0.286 | 0.928 | 1.465 | 1.013 | 1 | True | - |
| AkazeFeatureMatch | v_bark_1_2 | fixed | True | 187.998 | 0.129 | 1 | 0.762 | 0.666 | 1 | True | - |
| AkazeFeatureMatch | v_bees_1_2 | fixed | True | 134.503 | 0.082 | 0.995 | 0.751 | 1.095 | 2 | True | - |
| OrbFeatureMatch | v_abstract_1_2 | fixed | True | 391.906 | 0.045 | 0.973 | 1.04 | 1.014 | 0 | True | - |
| OrbFeatureMatch | v_adam_1_2 | fixed | True | 166.291 | 0.151 | 0.991 | 1.061 | 0.619 | 2 | True | - |
| OrbFeatureMatch | v_apprentices_1_2 | fixed | True | 172.499 | 0.088 | 1 | 0.955 | 0.868 | 2 | True | - |

## Surface Defect Focus

| Case | Status | Is defect | Predicted | Old F1 | New F1 | Old FP | New FP | Old FN | New FN | Taxonomy |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---|
| 20017 | worse-metric | False | True | 0 | 0 | 17 | 18 | 0 | 0 | texture_noise_false_positive |
| 20021 | worse-metric | False | True | 0 | 0 | 34 | 36 | 0 | 0 | texture_noise_false_positive |
| 20023 | worse-metric | False | True | 0 | 0 | 182 | 184 | 0 | 0 | texture_noise_false_positive |
| 20054 | worse-metric | False | True | 0 | 0 | 120 | 192 | 0 | 0 | oversegmentation_false_positive |
| 20083 | worse-metric | False | True | 0 | 0 | 13 | 16 | 0 | 0 | texture_noise_false_positive |
| 20091 | worse-metric | False | True | 0 | 0 | 88 | 107 | 0 | 0 | texture_noise_false_positive |
| 20111 | worse-metric | False | True | 0 | 0 | 95 | 104 | 0 | 0 | texture_noise_false_positive |
| 20113 | worse-metric | False | True | 0 | 0 | 12 | 18 | 0 | 0 | texture_noise_false_positive |
| 20006 | improved | False | False | 0 | 1 | 11 | 0 | 0 | 0 | - |
| 20008 | improved | False | False | 0 | 1 | 10 | 0 | 0 | 0 | - |
| 20015 | improved | False | False | 0 | 1 | 11 | 0 | 0 | 0 | - |
| 20018 | improved | False | False | 0 | 1 | 13 | 0 | 0 | 0 | - |
| 20027 | improved | False | False | 0 | 1 | 24 | 0 | 0 | 0 | - |
| 20068 | unchanged | True | False | 0 | 0 | 0 | 0 | 369 | 369 | low_contrast_defect_miss |
| 20080 | improved | False | False | 0 | 1 | 9 | 0 | 0 | 0 | - |
| 20105 | improved | False | False | 0 | 1 | 12 | 0 | 0 | 0 | - |
| 20109 | improved | False | False | 0 | 1 | 10 | 0 | 0 | 0 | - |
| 20112 | improved | False | False | 0 | 1 | 20 | 0 | 0 | 0 | - |
| 20134 | improved | False | False | 0 | 1 | 9 | 0 | 0 | 0 | - |
| 20139 | unchanged | True | False | 0 | 0 | 0 | 0 | 98 | 98 | low_contrast_defect_miss |

## Anomaly Detection Focus

| Case | Status | Is anomaly | Predicted | Old score | New score | Image correct | Taxonomy |
|---|---|---|---|---:|---:|---|---|
| grid/bent/000 | improved | True | True | 0 | 0.398 | True | - |
| grid/bent/001 | improved | True | True | 0 | 0.756 | True | - |
| grid/bent/003 | improved | True | True | 0 | 0.3 | True | - |
| grid/bent/004 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/006 | improved | True | True | 0 | 0.541 | True | - |
| grid/bent/007 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | improved | True | False | 0 | 0.034 | False | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/010 | improved | True | True | 0 | 0.385 | True | - |
| grid/bent/011 | improved | True | True | 0 | 0.155 | True | - |
| grid/broken/000 | improved | True | True | 0 | 0.222 | True | - |
| grid/broken/002 | improved | True | False | 0 | 0.015 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/004 | improved | True | True | 0 | 0.195 | True | - |
| grid/broken/005 | improved | True | False | 0 | 0.035 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/006 | improved | True | True | 0 | 0.124 | True | - |
| grid/broken/007 | improved | True | True | 0 | 0.254 | True | - |
| grid/broken/008 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/009 | improved | True | True | 0 | 0.398 | True | - |
| grid/broken/010 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_broken |

## EdgeDetection BSDS500 Focus

| Case | Status | Old F1 | New F1 | Old recall | New recall | Old precision | New precision | Thresholds | Predicted px | Taxonomy |
|---|---|---:|---:|---:|---:|---:|---:|---|---:|---|
| 109055 | worse-metric | 0.243 | 0.233 | 0.541 | 0.473 | 0.157 | 0.154 | 50/150 | 14681 | reduced_edge_density, boundary_recall_drop, large_recall_drop, precision_drop, boundary_f1_drop_gt_0_01, low_absolute_recall |
| 141012 | worse-metric | 0.189 | 0.178 | 0.852 | 0.745 | 0.106 | 0.101 | 50/150 | 29773 | reduced_edge_density, boundary_recall_drop, large_recall_drop, precision_drop, boundary_f1_drop_gt_0_01 |
| 159022 | worse-metric | 0.323 | 0.322 | 0.472 | 0.413 | 0.245 | 0.264 | 50/150 | 6072 | reduced_edge_density, boundary_recall_drop, large_recall_drop, precision_gain_recall_tradeoff, minor_boundary_f1_drop, low_absolute_recall |
| 160067 | worse-metric | 0.303 | 0.293 | 0.801 | 0.71 | 0.187 | 0.185 | 50/150 | 13702 | reduced_edge_density, boundary_recall_drop, large_recall_drop, precision_drop, minor_boundary_f1_drop |
| 202000 | worse-metric | 0.244 | 0.228 | 0.396 | 0.353 | 0.176 | 0.168 | 50/150 | 13331 | reduced_edge_density, boundary_recall_drop, precision_drop, boundary_f1_drop_gt_0_01, low_absolute_recall |
| 302022 | worse-metric | 0.219 | 0.2 | 0.585 | 0.492 | 0.135 | 0.126 | 50/150 | 14110 | reduced_edge_density, boundary_recall_drop, large_recall_drop, precision_drop, boundary_f1_drop_gt_0_01, low_absolute_recall |
| 48017 | worse-metric | 0.284 | 0.278 | 0.733 | 0.676 | 0.176 | 0.175 | 50/150 | 13997 | reduced_edge_density, boundary_recall_drop, large_recall_drop, precision_drop, minor_boundary_f1_drop |
| 97010 | worse-metric | 0.287 | 0.282 | 0.745 | 0.718 | 0.178 | 0.176 | 50/150 | 19609 | reduced_edge_density, boundary_recall_drop, precision_drop, minor_boundary_f1_drop |
| 101027 | improved | 0.321 | 0.329 | 0.716 | 0.659 | 0.207 | 0.219 | 50/150 | 14605 | - |
| 103006 | improved | 0.31 | 0.335 | 0.743 | 0.705 | 0.196 | 0.22 | 50/150 | 19608 | - |
| 108069 | improved | 0.17 | 0.172 | 0.882 | 0.741 | 0.094 | 0.097 | 50/150 | 17261 | - |
| 164046 | improved | 0.314 | 0.444 | 0.855 | 0.793 | 0.192 | 0.309 | 50/150 | 6405 | - |
| 196088 | improved | 0.337 | 0.339 | 0.882 | 0.827 | 0.208 | 0.213 | 50/150 | 31749 | - |
| 223060 | improved | 0.324 | 0.328 | 0.847 | 0.793 | 0.2 | 0.207 | 50/150 | 18349 | - |
| 232076 | improved | 0.24 | 0.279 | 0.542 | 0.516 | 0.154 | 0.192 | 50/150 | 11961 | - |
| 306052 | improved | 0.272 | 0.312 | 0.671 | 0.575 | 0.171 | 0.215 | 50/150 | 9546 | - |
| 326085 | improved | 0.252 | 0.273 | 0.846 | 0.77 | 0.148 | 0.166 | 50/150 | 17338 | - |
| 33044 | improved | 0.33 | 0.371 | 0.881 | 0.849 | 0.203 | 0.237 | 50/150 | 17809 | - |
| 41096 | improved | 0.242 | 0.265 | 0.823 | 0.81 | 0.142 | 0.159 | 50/150 | 13980 | - |
| 49024 | improved | 0.23 | 0.271 | 0.26 | 0.249 | 0.207 | 0.297 | 50/150 | 2712 | - |

## SemanticSegmentation Focus

| Case | Status | Old mIoU | New mIoU | Old boundary IoU | New boundary IoU | Input | Classes |
|---|---|---:|---:|---:|---:|---|---|
| SemanticSegmentation_class_absent_0000 | unchanged | 1 | 1 | 1 | 1 | 64x48 / RGB | background, surface |
| SemanticSegmentation_class_absent_0001 | unchanged | 1 | 1 | 1 | 1 | 96x64 / BGR | background, surface |
| SemanticSegmentation_class_absent_0002 | unchanged | 1 | 1 | 1 | 1 | 64x64 / RGB | background, surface |
| SemanticSegmentation_multi_class_regions_0000 | unchanged | 1 | 1 | 1 | 1 | 64x48 / RGB | background, surface, scratch, contaminant |
| SemanticSegmentation_multi_class_regions_0001 | unchanged | 1 | 1 | 1 | 1 | 96x64 / BGR | background, surface, scratch, contaminant |
| SemanticSegmentation_multi_class_regions_0002 | unchanged | 1 | 1 | 1 | 1 | 64x64 / RGB | background, surface, scratch, contaminant |
| SemanticSegmentation_multi_class_regions_0003 | unchanged | 1 | 1 | 1 | 1 | 96x48 / BGR | background, surface, scratch, contaminant |
| SemanticSegmentation_nested_regions_0000 | unchanged | 1 | 1 | 1 | 1 | 64x48 / RGB | background, surface, scratch, contaminant |
| SemanticSegmentation_nested_regions_0001 | unchanged | 1 | 1 | 1 | 1 | 96x64 / BGR | background, surface, scratch, contaminant |
| SemanticSegmentation_nested_regions_0002 | unchanged | 1 | 1 | 1 | 1 | 64x64 / RGB | background, surface, scratch, contaminant |
| SemanticSegmentation_single_region_0000 | unchanged | 1 | 1 | 1 | 1 | 64x48 / RGB | background, surface |
| SemanticSegmentation_single_region_0001 | unchanged | 1 | 1 | 1 | 1 | 96x64 / BGR | background, surface |
| SemanticSegmentation_single_region_0002 | unchanged | 1 | 1 | 1 | 1 | 64x64 / RGB | background, surface |
| SemanticSegmentation_single_region_0003 | unchanged | 1 | 1 | 1 | 1 | 96x48 / BGR | background, surface |
| SemanticSegmentation_small_object_0000 | unchanged | 1 | 1 | 1 | 1 | 64x48 / RGB | background, surface, contaminant |
| SemanticSegmentation_small_object_0001 | unchanged | 1 | 1 | 1 | 1 | 96x64 / BGR | background, surface, contaminant |
| SemanticSegmentation_small_object_0002 | unchanged | 1 | 1 | 1 | 1 | 64x64 / RGB | background, surface, contaminant |
| SemanticSegmentation_thin_boundary_0000 | unchanged | 1 | 1 | 1 | 1 | 64x48 / RGB | background, surface, scratch, contaminant |
| SemanticSegmentation_thin_boundary_0001 | unchanged | 1 | 1 | 1 | 1 | 96x64 / BGR | background, surface, scratch, contaminant |
| SemanticSegmentation_thin_boundary_0002 | unchanged | 1 | 1 | 1 | 1 | 64x64 / RGB | background, surface, scratch, contaminant |

## TemplateMatching Homography Bridge Focus

| Case | Status | Sequence | Template | Old error | New error | Old norm score | New norm score |
|---|---|---|---|---:|---:|---:|---:|
| TemplateMatching_homography_perspective_0000 | unchanged | homography_perspective | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_perspective_0001 | unchanged | homography_perspective | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_shear_0000 | unchanged | homography_shear | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_shear_0001 | unchanged | homography_shear | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_shear_0002 | unchanged | homography_shear | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_shear_0003 | unchanged | homography_shear | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_shear_0004 | unchanged | homography_shear | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_homography_shear_0005 | unchanged | homography_shear | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_illumination_translation_0000 | unchanged | illumination_translation | Source | 0 | 0 | 1 | 1 |
| TemplateMatching_illumination_translation_0001 | unchanged | illumination_translation | Source | 0 | 0 | 1 | 1 |
| TemplateMatching_illumination_translation_0002 | unchanged | illumination_translation | Source | 0 | 0 | 1 | 1 |
| TemplateMatching_illumination_translation_0003 | unchanged | illumination_translation | Source | 0 | 0 | 1 | 1 |
| TemplateMatching_illumination_translation_0004 | unchanged | illumination_translation | Source | 0 | 0 | 1 | 1 |
| TemplateMatching_illumination_translation_0005 | unchanged | illumination_translation | Source | 0 | 0 | 1 | 1 |
| TemplateMatching_viewpoint_translation_0000 | unchanged | viewpoint_translation | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_viewpoint_translation_0001 | unchanged | viewpoint_translation | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_viewpoint_translation_0002 | unchanged | viewpoint_translation | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_viewpoint_translation_0003 | unchanged | viewpoint_translation | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_viewpoint_translation_0004 | unchanged | viewpoint_translation | WarpedScene | 0 | 0 | 1 | 1 |
| TemplateMatching_viewpoint_translation_0005 | unchanged | viewpoint_translation | WarpedScene | 0 | 0 | 1 | 1 |

## ShapeMatching Geometric Dataset Focus

| Case | Status | Scenario | Old F1 | New F1 | Old pos err | New pos err | GT | Pred | FP | FN |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ShapeMatching_direct_pose_0000 | unchanged | direct_pose | 1 | 1 | 0 | 0 | 1 | 1 | 0 | 0 |
| ShapeMatching_direct_pose_0001 | unchanged | direct_pose | 1 | 1 | 0 | 0 | 1 | 1 | 0 | 0 |
| ShapeMatching_direct_pose_0003 | unchanged | direct_pose | 1 | 1 | 0.003 | 0.003 | 1 | 1 | 0 | 0 |
| ShapeMatching_direct_pose_0005 | unchanged | direct_pose | 1 | 1 | 0 | 0 | 1 | 1 | 0 | 0 |
| ShapeMatching_multi_target_0000 | unchanged | multi_target | 1 | 1 | 0.001 | 0.001 | 2 | 2 | 0 | 0 |
| ShapeMatching_multi_target_0001 | unchanged | multi_target | 1 | 1 | 0.001 | 0.001 | 2 | 2 | 0 | 0 |
| ShapeMatching_multi_target_0002 | unchanged | multi_target | 1 | 1 | 0.001 | 0.001 | 2 | 2 | 0 | 0 |
| ShapeMatching_rotated_pose_0001 | unchanged | rotated_pose | 1 | 1 | 0.003 | 0.003 | 1 | 1 | 0 | 0 |
| ShapeMatching_rotated_pose_0002 | unchanged | rotated_pose | 1 | 1 | 0.001 | 0.001 | 1 | 1 | 0 | 0 |
| ShapeMatching_rotated_pose_0003 | unchanged | rotated_pose | 1 | 1 | 0 | 0 | 1 | 1 | 0 | 0 |
| ShapeMatching_rotated_pose_0004 | unchanged | rotated_pose | 1 | 1 | 0.001 | 0.001 | 1 | 1 | 0 | 0 |
| ShapeMatching_rotated_pose_0005 | unchanged | rotated_pose | 1 | 1 | 0.002 | 0.002 | 1 | 1 | 0 | 0 |
| ShapeMatching_scaled_pose_0000 | unchanged | scaled_pose | 1 | 1 | 0.22 | 0.22 | 1 | 1 | 0 | 0 |
| ShapeMatching_scaled_pose_0001 | unchanged | scaled_pose | 1 | 1 | 0.171 | 0.171 | 1 | 1 | 0 | 0 |
| ShapeMatching_scaled_pose_0002 | unchanged | scaled_pose | 1 | 1 | 0.256 | 0.256 | 1 | 1 | 0 | 0 |
| ShapeMatching_scaled_pose_0003 | unchanged | scaled_pose | 1 | 1 | 0.148 | 0.148 | 1 | 1 | 0 | 0 |
| ShapeMatching_scaled_pose_0005 | unchanged | scaled_pose | 1 | 1 | 0.099 | 0.099 | 1 | 1 | 0 | 0 |
| ShapeMatching_top_left_origin_0002 | unchanged | top_left_origin | 1 | 1 | 0.002 | 0.002 | 1 | 1 | 0 | 0 |
| ShapeMatching_top_left_origin_0003 | unchanged | top_left_origin | 1 | 1 | 0 | 0 | 1 | 1 | 0 | 0 |
| ShapeMatching_top_left_origin_0004 | unchanged | top_left_origin | 1 | 1 | 0 | 0 | 1 | 1 | 0 | 0 |

## CameraCalibration Focus

| Case | Status | New pass | Accepted | Old RMS | New RMS | Old max error | New max error | Old detected | New detected | Total | Failure reason |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| opencv_calibration_left_camera | unchanged | True | False | 0.338 | 0.338 | 0.458 | 0.458 | 12 | 12 | 13 | - |
| opencv_calibration_right_camera | unchanged | True | False | 0.368 | 0.368 | 0.47 | 0.47 | 13 | 13 | 13 | - |
| opencv_calibration_stereo_rig | unchanged | True | False | 0 | 0 | 0 | 0 | 13 | 13 | 13 | - |

## DeepLearning Real Model Focus

| Case | Status | Execution | Old pass | New pass | New detections | TP | FP | FN | Processing error | Output shape |
|---|---|---|---|---|---:|---:|---:|---:|---|---|
| coco2017_val_139 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 2 | - | - |
| coco2017_val_724 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 1 | - | - |
| coco2017_val_785 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 1 | - | - |
| coco2017_val_872 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 2 | - | - |
| coco2017_val_885 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 8 | - | - |
| coco2017_val_1000 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 12 | - | - |
| coco2017_val_1268 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 4 | - | - |
| coco2017_val_1296 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 2 | - | - |
| coco2017_val_1353 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 6 | - | - |
| coco2017_val_1490 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 1 | - | - |
| coco2017_val_1532 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 7 | - | - |
| coco2017_val_1584 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 11 | - | - |
| coco2017_val_1761 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 5 | - | - |
| coco2017_val_2006 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 3 | - | - |
| coco2017_val_2153 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 4 | - | - |
| coco2017_val_2261 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 1 | - | - |
| coco2017_val_2299 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 13 | - | - |
| coco2017_val_2431 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 2 | - | - |
| coco2017_val_2473 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 4 | - | - |
| coco2017_val_2532 | unchanged | unchanged-baseline-control | False | False | 1 | 0 | 1 | 1 | - | - |

## Policy

This report is algorithm A/B evidence over public and semisynthetic replay seeds, not real field sign-off.

DeepLearning real-model candidates are ONNX Runtime outputs with AnnotationSeeded=false; do not compare annotation-seeded proof as model accuracy.
