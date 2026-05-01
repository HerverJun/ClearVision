# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T11:24:30.5327678+00:00`
Operator: `AkazeFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 19 |
| Failed | 1 |
| Pass rate | 0.95 |
| Mean position error px | 13.101 |
| P95 position error px | 17.052 |
| P95 corner error px | 382.627 |
| Mean inliers | 235.5 |
| Mean score | 0.8584 |
| Runtime ms | 1467.697 |
| Max features | 1200 |
| Min inliers | 6 |
| Match ratio | 0.75 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.25 |
| Detector type | ORB |
| Score threshold | 0.5 |
| Multi-scale | True |
| Scale range | 0.2 |
| ORB FAST threshold | 20 |
| ORB edge threshold | 15 |
| AKAZE detector threshold | 0.001 |
| Allow center-only projection | True |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_bologna_1_2 | illumination | 1-2 | True | 1.114 | 14.408 | 27.321 | 0.9156 | 16/18 | 0.889 | 0.449 | 0.974 | 1.013 | 2 | True | 183.079 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 1.08 | 7.616 | 12.387 | 0.8252 | 22/28 | 0.786 | 1.097 | 3.513 | 1.004 | 2 | True | 64.695 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.237 | 2.047 | 3.799 | 0.8724 | 258/312 | 0.827 | 0.624 | 6.399 | 1.003 | 2 | True | 51.149 | - | - |
| i_leuven_1_2 | illumination | 1-2 | True | 0.319 | 1.389 | 3.353 | 0.9777 | 244/248 | 0.984 | 0.258 | 2.634 | 1.001 | 1 | True | 71.186 | - | - |
| i_lionday_1_2 | illumination | 1-2 | True | 3.378 | 14.832 | 26.343 | 0.8214 | 40/49 | 0.816 | 1.496 | 3.415 | 0.956 | 2 | True | 53.346 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.011 | 1.704 | 2.117 | 0.9179 | 731/823 | 0.888 | 0.398 | 3.19 | 1.001 | 1 | True | 78.682 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.414 | 1.448 | 1.972 | 0.9389 | 310/327 | 0.948 | 0.626 | 4.999 | 0.999 | 2 | True | 87.562 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.317 | 4.704 | 9.247 | 0.7273 | 35/53 | 0.66 | 1.658 | 3.821 | 0.999 | 2 | True | 41.215 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.989 | 1.18 | 2.399 | 0.8661 | 174/194 | 0.897 | 1.49 | 5.419 | 1.003 | 2 | True | 80.338 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 230.107 | - | - | 0 | 0/2 | 0 | - | - | 0 | 0 | False | 60.501 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=2, error=230.107, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 2.049 | 1.93 | 2.446 | 0.9561 | 324/331 | 0.979 | 0.623 | 5.366 | 0.999 | 2 | True | 68.74 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 17.052 | 199.432 | 382.627 | 0.8784 | 18/21 | 0.857 | 0.829 | 2.586 | 1.088 | 2 | True | 76.17 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.356 | 2.662 | 7.129 | 0.9121 | 62/67 | 0.925 | 0.904 | 3.118 | 1.012 | 2 | True | 47.058 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | True | 0.31 | 0.65 | 0.876 | 0.9425 | 409/415 | 0.986 | 0.956 | 3.775 | 1.014 | 0 | True | 73.734 | - | - |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.47 | 2.927 | 6.489 | 0.9427 | 45/46 | 0.978 | 0.874 | 2.362 | 0.615 | 2 | True | 37.64 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.081 | 0.65 | 0.771 | 0.9766 | 843/843 | 1 | 0.451 | 3.394 | 0.868 | 2 | True | 96.666 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | True | 0.231 | 3.856 | 6.868 | 0.8936 | 205/225 | 0.911 | 1.109 | 3.834 | 1.901 | 0 | True | 76.101 | - | - |
| v_azzola_1_2 | viewpoint | 1-2 | True | 0.286 | 3.941 | 5.446 | 0.8843 | 257/277 | 0.928 | 1.465 | 4.851 | 1.013 | 1 | True | 91.504 | - | - |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.129 | 0.722 | 1.112 | 0.9605 | 126/126 | 1 | 0.762 | 4.048 | 0.666 | 1 | True | 56.349 | - | - |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.082 | 0.676 | 1.062 | 0.9583 | 591/594 | 0.995 | 0.751 | 5.069 | 1.095 | 2 | True | 71.982 | - | - |
