# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:56:58.2818072+00:00`
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
| Mean position error px | 9.874 |
| P95 position error px | 0.989 |
| P95 corner error px | 43.012 |
| Mean inliers | 385.45 |
| Mean score | 0.8874 |
| Runtime ms | 1457.437 |
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
| v_abstract_1_2 | viewpoint | 1-2 | True | 0.31 | 0.65 | 0.876 | 0.9425 | 409/415 | 0.986 | 0.956 | 3.775 | 1.014 | 0 | True | 127.801 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | True | 0.231 | 3.856 | 6.868 | 0.8936 | 205/225 | 0.911 | 1.109 | 3.834 | 1.901 | 0 | True | 56.365 | - | - |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.129 | 0.722 | 1.112 | 0.9605 | 126/126 | 1 | 0.762 | 4.048 | 0.666 | 1 | True | 55.029 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | True | 0.081 | 0.741 | 0.963 | 0.952 | 431/433 | 0.995 | 0.878 | 3.815 | 0.779 | 1 | True | 86.588 | - | - |
| v_bricks_1_2 | viewpoint | 1-2 | True | 0.07 | 0.17 | 0.241 | 0.9632 | 761/787 | 0.967 | 0.358 | 1.977 | 1.124 | 1 | True | 75.249 | - | - |
| v_busstop_1_2 | viewpoint | 1-2 | True | 0.989 | 1.767 | 2.286 | 0.9213 | 473/515 | 0.918 | 0.652 | 2.848 | 0.842 | 1 | True | 82.065 | - | - |
| v_churchill_1_2 | viewpoint | 1-2 | False | 192.562 | - | - | 0 | 241/296 | 0.814 | 1.279 | 4.987 | 1.773 | 1 | True | 66.576 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=241, totalMatches=296, error=192.562, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | True | 0.281 | 1.62 | 4.2 | 0.8919 | 92/102 | 0.902 | 1.046 | 3.622 | 1.138 | 1 | True | 59.435 | - | - |
| v_courses_1_2 | viewpoint | 1-2 | True | 0.31 | 2.725 | 5.667 | 0.9567 | 539/544 | 0.991 | 0.738 | 5.089 | 1.022 | 0 | True | 79.719 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | True | 0.348 | 1.318 | 1.758 | 0.9506 | 469/474 | 0.989 | 0.84 | 4.495 | 1.368 | 0 | True | 72.073 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | True | 0.05 | 0.135 | 0.274 | 0.9473 | 347/356 | 0.975 | 0.748 | 4.118 | 0.763 | 1 | True | 61.174 | - | - |
| v_london_1_2 | viewpoint | 1-2 | True | 0.05 | 0.176 | 0.302 | 0.9591 | 512/525 | 0.975 | 0.527 | 4.491 | 1.192 | 0 | True | 71.308 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | True | 0.553 | 19.735 | 43.012 | 0.8616 | 40/47 | 0.851 | 1.089 | 3.101 | 1.084 | 1 | True | 59.534 | - | - |
| v_strand_1_2 | viewpoint | 1-2 | True | 0.063 | 0.318 | 0.501 | 0.9842 | 961/965 | 0.996 | 0.261 | 4.324 | 1.012 | 1 | True | 72.855 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | True | 0.495 | 4.718 | 8.412 | 0.9278 | 200/212 | 0.943 | 0.791 | 2.82 | 0.876 | 1 | True | 79.069 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | True | 0.062 | 0.225 | 0.349 | 0.971 | 474/478 | 0.992 | 0.471 | 3.932 | 1.12 | 0 | True | 71.3 | - | - |
| v_vitro_1_2 | viewpoint | 1-2 | True | 0.213 | 0.553 | 0.769 | 0.9016 | 340/387 | 0.879 | 0.609 | 3.278 | 0.829 | 1 | True | 79.846 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | True | 0.192 | 0.787 | 1.317 | 0.9499 | 548/568 | 0.965 | 0.592 | 4.761 | 1.005 | 0 | True | 67.485 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | True | 0.415 | 1.563 | 2.353 | 0.8992 | 403/451 | 0.894 | 0.815 | 3.777 | 0.846 | 1 | True | 70.481 | - | - |
| v_yuri_1_2 | viewpoint | 1-2 | True | 0.083 | 3.507 | 5.447 | 0.9132 | 138/148 | 0.932 | 0.957 | 4.484 | 1.016 | 2 | True | 63.485 | - | - |
