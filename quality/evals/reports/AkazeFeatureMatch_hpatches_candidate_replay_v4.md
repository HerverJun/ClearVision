# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:49:54.7666580+00:00`
Operator: `AkazeFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 13 |
| Failed | 7 |
| Pass rate | 0.65 |
| Mean position error px | 104.582 |
| P95 position error px | 378.274 |
| P95 corner error px | 382.627 |
| Mean inliers | 235.5 |
| Mean score | 0.5796 |
| Runtime ms | 1189.781 |
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
| Allow center-only projection | False |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_bologna_1_2 | illumination | 1-2 | True | 1.114 | 14.408 | 27.321 | 0.9156 | 16/18 | 0.889 | 0.449 | 0.974 | 1.013 | 2 | True | 97.745 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 1.08 | 7.616 | 12.387 | 0.8252 | 22/28 | 0.786 | 1.097 | 3.513 | 1.004 | 2 | True | 51.899 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.237 | 2.047 | 3.799 | 0.8724 | 258/312 | 0.827 | 0.624 | 6.399 | 1.003 | 2 | True | 40.063 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 376.317 | - | - | 0 | 244/248 | 0.984 | 0.258 | 2.634 | 1.001 | 1 | True | 56.372 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=244, totalMatches=248, error=376.317, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 3.378 | 14.832 | 26.343 | 0.8214 | 40/49 | 0.816 | 1.496 | 3.415 | 0.956 | 2 | True | 43.466 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 235.136 | - | - | 0 | 731/823 | 0.888 | 0.398 | 3.19 | 1.001 | 1 | True | 64.323 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=731, totalMatches=823, error=235.136, tolerance=35 |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.414 | 1.448 | 1.972 | 0.9389 | 310/327 | 0.948 | 0.626 | 4.999 | 0.999 | 2 | True | 69.534 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.317 | 4.704 | 9.247 | 0.7273 | 35/53 | 0.66 | 1.658 | 3.821 | 0.999 | 2 | True | 35.561 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.989 | 1.18 | 2.399 | 0.8661 | 174/194 | 0.897 | 1.49 | 5.419 | 1.003 | 2 | True | 68.462 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 230.107 | - | - | 0 | 0/2 | 0 | - | - | 0 | 0 | False | 52.617 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=2, error=230.107, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 2.049 | 1.93 | 2.446 | 0.9561 | 324/331 | 0.979 | 0.623 | 5.366 | 0.999 | 2 | True | 61.101 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 17.052 | 199.432 | 382.627 | 0.8784 | 18/21 | 0.857 | 0.829 | 2.586 | 1.088 | 2 | True | 61.829 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.356 | 2.662 | 7.129 | 0.9121 | 62/67 | 0.925 | 0.904 | 3.118 | 1.012 | 2 | True | 39.194 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 378.274 | - | - | 0 | 409/415 | 0.986 | 0.956 | 3.775 | 1.014 | 0 | True | 71.997 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=409, totalMatches=415, error=378.274, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.47 | 2.927 | 6.489 | 0.9427 | 45/46 | 0.978 | 0.874 | 2.362 | 0.615 | 2 | True | 34.515 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.081 | 0.65 | 0.771 | 0.9766 | 843/843 | 1 | 0.451 | 3.394 | 0.868 | 2 | True | 76.877 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 400.798 | - | - | 0 | 205/225 | 0.911 | 1.109 | 3.834 | 1.901 | 0 | True | 55.861 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=205, totalMatches=225, error=400.798, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 254.382 | - | - | 0 | 257/277 | 0.928 | 1.465 | 4.851 | 1.013 | 1 | True | 80.675 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=257, totalMatches=277, error=254.382, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 187.998 | - | - | 0 | 126/126 | 1 | 0.762 | 4.048 | 0.666 | 1 | True | 54.306 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=126, totalMatches=126, error=187.998, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.082 | 0.676 | 1.062 | 0.9583 | 591/594 | 0.995 | 0.751 | 5.069 | 1.095 | 2 | True | 73.384 | - | - |
