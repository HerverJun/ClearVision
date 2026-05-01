# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:49:56.9720344+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 16 |
| Failed | 4 |
| Pass rate | 0.8 |
| Mean position error px | 42.957 |
| P95 position error px | 279.156 |
| P95 corner error px | 16.101 |
| Mean inliers | 277.3 |
| Mean score | 0.7596 |
| Runtime ms | 615.503 |
| Max features | 2000 |
| Min inliers | 6 |
| Match ratio | 0.7 |
| RANSAC threshold px | 7 |
| Min inlier ratio | 0.25 |
| Detector type | ORB |
| Score threshold | 0.5 |
| Multi-scale | True |
| Scale range | 0.2 |
| ORB FAST threshold | 16 |
| ORB edge threshold | 10 |
| AKAZE detector threshold | 0.001 |
| Allow center-only projection | False |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_autannes_1_2 | illumination | 1-2 | True | 0.091 | 0.639 | 0.814 | 0.9879 | 86/86 | 1 | 0.326 | 5.08 | 1 | 4 | True | 170.085 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.206 | 0.942 | 1.509 | 0.9823 | 135/135 | 1 | 0.479 | 3.502 | 0.998 | 4 | True | 14.596 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.077 | 0.219 | 0.44 | 0.9926 | 802/806 | 0.995 | 0.126 | 6.817 | 1 | 4 | True | 22.779 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.78 | 8.45 | 16.101 | 0.9278 | 10/11 | 0.909 | 0.599 | 1.566 | 0.97 | 3 | True | 23.607 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.122 | 1.528 | 4.577 | 0.9752 | 36/36 | 1 | 0.67 | 2.831 | 1.004 | 3 | True | 38.488 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.049 | 0.261 | 0.314 | 0.9828 | 433/433 | 1 | 0.465 | 5.089 | 1 | 4 | True | 19.993 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.985 | 2.338 | 3.017 | 0.8702 | 23/28 | 0.821 | 0.852 | 1.778 | 0.993 | 3 | True | 21.869 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 810/862 | 0.94 | 0.874 | 5.043 | 1.001 | 1 | True | 21.797 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=810, totalMatches=862, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 0.755 | 4.435 | 6.515 | 0.9193 | 17/18 | 0.944 | 1.354 | 3.188 | 1.002 | 2 | True | 17.975 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.815 | 1.097 | 1.571 | 0.8128 | 72/97 | 0.742 | 1.227 | 2.977 | 0.999 | 2 | True | 23.771 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.02 | 1.817 | 2.174 | 0.9358 | 788/840 | 0.938 | 0.815 | 4.404 | 1.001 | 2 | True | 25.606 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.244 | 1.108 | 1.404 | 0.965 | 149/150 | 0.993 | 0.847 | 4.952 | 1 | 3 | True | 25.889 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 0.5 | - | - | 0 | 0/0 | 0 | - | - | 0 | 0 | False | 16.391 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=0, error=0.5, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 1.887 | 2.047 | 2.317 | 0.961 | 205/210 | 0.976 | 0.698 | 4.088 | 0.999 | 2 | True | 23.474 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.12 | 0.369 | 0.819 | 0.9803 | 567/568 | 0.998 | 0.506 | 4.918 | 1.001 | 4 | True | 24.358 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.569 | 3.573 | 9.428 | 0.9781 | 19/19 | 1 | 0.591 | 1.976 | 1.017 | 2 | True | 19.894 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156 | - | - | 0 | 356/366 | 0.973 | 1.04 | 4.893 | 1.014 | 0 | True | 25.484 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=356, totalMatches=366, error=279.156, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.151 | 2.268 | 3.519 | 0.9556 | 320/323 | 0.991 | 1.061 | 5.572 | 0.619 | 2 | True | 21.176 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.088 | 0.842 | 1.186 | 0.9646 | 584/584 | 1 | 0.955 | 5.454 | 0.868 | 2 | True | 32.927 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.23 | - | - | 0 | 134/134 | 1 | 1.493 | 5.211 | 1.899 | 0 | True | 25.344 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=134, totalMatches=134, error=307.23, tolerance=35 |
