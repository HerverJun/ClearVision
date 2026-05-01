# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:26:31.2221710+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 15 |
| Failed | 5 |
| Pass rate | 0.75 |
| Mean position error px | 52.715 |
| P95 position error px | 279.156 |
| P95 corner error px | 16.101 |
| Mean inliers | 274.35 |
| Mean score | 0.7032 |
| Runtime ms | 601.724 |
| Max features | 2000 |
| Min inliers | 6 |
| Match ratio | 0.7 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.25 |
| Detector type | ORB |
| Score threshold | 0.5 |
| Multi-scale | True |
| Scale range | 0.2 |
| ORB FAST threshold | 16 |
| ORB edge threshold | 10 |
| AKAZE detector threshold | 0.001 |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_autannes_1_2 | illumination | 1-2 | True | 0.155 | 0.76 | 1.061 | 0.9786 | 85/86 | 0.988 | 0.29 | 3.521 | 1 | 3 | True | 171.407 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.211 | 0.955 | 1.539 | 0.971 | 134/135 | 0.993 | 0.481 | 3.501 | 0.998 | 4 | True | 14.297 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.184 | 0.306 | 0.589 | 0.9899 | 801/806 | 0.994 | 0.129 | 4.185 | 0.999 | 4 | True | 20.976 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.78 | 8.45 | 16.101 | 0.9189 | 10/11 | 0.909 | 0.599 | 1.566 | 0.97 | 3 | True | 23.077 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.122 | 1.528 | 4.577 | 0.9652 | 36/36 | 1 | 0.67 | 2.831 | 1.004 | 3 | True | 37 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.051 | 0.269 | 0.329 | 0.975 | 432/433 | 0.998 | 0.458 | 4.103 | 1 | 4 | True | 19.311 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.985 | 2.338 | 3.017 | 0.8576 | 23/28 | 0.821 | 0.852 | 1.778 | 0.993 | 3 | True | 21.788 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 750/862 | 0.87 | 0.872 | 5.033 | 1.001 | 1 | True | 20.245 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=750, totalMatches=862, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 0.755 | 4.435 | 6.515 | 0.8992 | 17/18 | 0.944 | 1.354 | 3.188 | 1.002 | 2 | True | 17.99 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.815 | 1.097 | 1.571 | 0.7946 | 72/97 | 0.742 | 1.227 | 2.977 | 0.999 | 2 | True | 23.291 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 196.939 | - | - | 0 | 840/840 | 1 | 0.83 | 4.475 | 1.001 | 1 | True | 24.988 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=840, totalMatches=840, error=196.939, tolerance=35 |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.246 | 1.116 | 1.476 | 0.9496 | 148/150 | 0.987 | 0.831 | 4.97 | 1 | 3 | True | 25.977 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 0.5 | - | - | 0 | 0/0 | 0 | - | - | 0 | 0 | False | 16.798 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=0, error=0.5, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 1.889 | 2.058 | 2.364 | 0.9443 | 202/210 | 0.962 | 0.671 | 3.697 | 0.999 | 2 | True | 23.284 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.119 | 0.361 | 0.81 | 0.9724 | 566/568 | 0.996 | 0.495 | 3.875 | 1.001 | 4 | True | 24.14 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.569 | 3.573 | 9.428 | 0.9694 | 19/19 | 1 | 0.591 | 1.976 | 1.017 | 2 | True | 20.146 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156 | - | - | 0 | 330/366 | 0.902 | 1.025 | 4.861 | 1.014 | 0 | True | 24.199 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=330, totalMatches=366, error=279.156, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.197 | 2.438 | 3.988 | 0.9292 | 313/323 | 0.969 | 1.037 | 4.032 | 0.619 | 2 | True | 16.466 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.111 | 0.864 | 1.185 | 0.9493 | 582/584 | 0.997 | 0.941 | 4.054 | 0.868 | 2 | True | 31.587 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.23 | - | - | 0 | 127/134 | 0.948 | 1.39 | 5.076 | 1.895 | 0 | True | 24.757 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=127, totalMatches=134, error=307.23, tolerance=35 |
