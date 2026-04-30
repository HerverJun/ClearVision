# Quality Flywheel Algorithm A/B Replay Report

GeneratedAtUtc: `2026-04-29T16:13:33+00:00`
Accepted: `Yes`

## Summary

- Operators: 10
- Replay cases compared: 183
- Executed candidate cases: 80
- Control cases: 103
- Fixed cases: 29
- Regressed cases: 0
- Matching viewpoint cases: 11
- Matching viewpoint fixed: 5
- Surface defect replay cases: 20
- Surface defect improved cases: 10
- Surface defect regressed cases: 0
- Surface defect worse metric cases: 8
- Anomaly detection replay cases: 20
- Anomaly detection improved cases: 14
- Anomaly detection regressed cases: 0
- Anomaly detection worse metric cases: 0
- Anomaly detection image-correct cases: 5
- Anomaly detection detected anomaly cases: 5

## Operators

| Operator | Dataset | Status | Replay | Old Pass | New Pass | Fixed | Regressed | Worse metric | Candidate |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| AkazeFeatureMatch | hpatches | candidate-executed | 20 | 0.0 | 0.65 | 13 | 0 | 0 | quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_replay_v4.json |
| AnomalyDetection | mvtec_ad_lite | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 0 | quality/evals/reports/AnomalyDetection_mvtec_candidate_replay_v1.json |
| CameraCalibration | opencv_calibration_samples | unchanged-baseline-control | 3 | 1.0 | 1.0 | 0 | 0 | 0 | same-as-old-control |
| DeepLearning | coco2017 | unchanged-baseline-control | 20 | 0.65 | 0.65 | 0 | 0 | 0 | same-as-old-control |
| EdgeDetection | bsds500 | unchanged-baseline-control | 20 | 1.0 | 1.0 | 0 | 0 | 0 | same-as-old-control |
| OrbFeatureMatch | hpatches | candidate-executed | 20 | 0.0 | 0.8 | 16 | 0 | 1 | quality/evals/reports/OrbFeatureMatch_hpatches_candidate_replay_v4.json |
| SemanticSegmentation | voc-style-protocol-bridge | unchanged-baseline-control | 20 | 1.0 | 1.0 | 0 | 0 | 0 | same-as-old-control |
| ShapeMatching | semisynthetic-geometric-shape-scenes | unchanged-baseline-control | 20 | 1.0 | 1.0 | 0 | 0 | 0 | same-as-old-control |
| SurfaceDefectDetection | kolektorsdd2 | candidate-executed | 20 | 1.0 | 1.0 | 0 | 0 | 8 | quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_candidate_replay_v1.json |
| TemplateMatching | hpatches-style-homography-bridge | unchanged-baseline-control | 20 | 1.0 | 1.0 | 0 | 0 | 0 | same-as-old-control |

## Matching Viewpoint Focus

| Operator | Case | Status | New pass | Old error | New error | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---|---|
| AkazeFeatureMatch | v_abstract_1_2 | unchanged | False | 378.274 | 378.274 | 0.986 | 0.956 | 1.014 | 0 | True | Projected quadrilateral is invalid. |
| AkazeFeatureMatch | v_astronautis_1_2 | unchanged | False | 400.798 | 400.798 | 0.911 | 1.109 | 1.901 | 0 | True | Projected quadrilateral is invalid. |
| AkazeFeatureMatch | v_azzola_1_2 | unchanged | False | 254.382 | 254.382 | 0.928 | 1.465 | 1.013 | 1 | True | Projected quadrilateral is invalid. |
| AkazeFeatureMatch | v_bark_1_2 | unchanged | False | 187.998 | 187.998 | 1 | 0.762 | 0.666 | 1 | True | Projected quadrilateral is invalid. |
| OrbFeatureMatch | v_abstract_1_2 | improved | False | 391.906 | 279.156 | 0.973 | 1.04 | 1.014 | 0 | True | Projected quadrilateral is invalid. |
| OrbFeatureMatch | v_astronautis_1_2 | worse-metric | False | 108.405 | 307.23 | 1 | 1.493 | 1.899 | 0 | True | Projected quadrilateral is invalid. |
| AkazeFeatureMatch | v_adam_1_2 | fixed | True | 230.146 | 0.47 | 0.978 | 0.874 | 0.615 | 2 | True | - |
| AkazeFeatureMatch | v_apprentices_1_2 | fixed | True | 127.844 | 0.081 | 1 | 0.451 | 0.868 | 2 | True | - |
| AkazeFeatureMatch | v_bees_1_2 | fixed | True | 134.503 | 0.082 | 0.995 | 0.751 | 1.095 | 2 | True | - |
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
| grid/bent/003 | improved | True | False | 0 | 0.3 | False | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/004 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/006 | improved | True | True | 0 | 0.541 | True | - |
| grid/bent/007 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | improved | True | False | 0 | 0.034 | False | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/010 | improved | True | True | 0 | 0.385 | True | - |
| grid/bent/011 | improved | True | False | 0 | 0.155 | False | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/broken/000 | improved | True | False | 0 | 0.222 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/002 | improved | True | False | 0 | 0.015 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/004 | improved | True | False | 0 | 0.195 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/005 | improved | True | False | 0 | 0.035 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/006 | improved | True | False | 0 | 0.124 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/007 | improved | True | False | 0 | 0.254 | False | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/008 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/009 | improved | True | True | 0 | 0.398 | True | - |
| grid/broken/010 | unchanged | True | False | 0 | 0 | False | anomaly_miss, zero_score_anomaly, defect_broken |

## Policy

This report is algorithm A/B evidence over public and semisynthetic replay seeds, not real field sign-off.
