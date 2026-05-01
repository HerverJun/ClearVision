# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:45:41.1087931+00:00`
Operator: `AkazeFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 1 |
| Failed | 19 |
| Pass rate | 0.05 |
| Mean position error px | 216.392 |
| P95 position error px | 378.274 |
| P95 corner error px | 5.447 |
| Mean inliers | 385.45 |
| Mean score | 0.0457 |
| Runtime ms | 1444.01 |
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
| v_abstract_1_2 | viewpoint | 1-2 | False | 378.274 | - | - | 0 | 409/415 | 0.986 | 0.956 | 3.775 | 1.014 | 0 | True | 127.269 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=409, totalMatches=415, error=378.274, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 400.798 | - | - | 0 | 205/225 | 0.911 | 1.109 | 3.834 | 1.901 | 0 | True | 56.803 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=205, totalMatches=225, error=400.798, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 187.998 | - | - | 0 | 126/126 | 1 | 0.762 | 4.048 | 0.666 | 1 | True | 53.96 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=126, totalMatches=126, error=187.998, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | False | 196.692 | - | - | 0 | 431/433 | 0.995 | 0.878 | 3.815 | 0.779 | 1 | True | 85.696 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=431, totalMatches=433, error=196.692, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 221.668 | - | - | 0 | 761/787 | 0.967 | 0.358 | 1.977 | 1.124 | 1 | True | 72.116 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=761, totalMatches=787, error=221.668, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 150.087 | - | - | 0 | 473/515 | 0.918 | 0.652 | 2.848 | 0.842 | 1 | True | 80.386 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=473, totalMatches=515, error=150.087, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 192.562 | - | - | 0 | 241/296 | 0.814 | 1.279 | 4.987 | 1.773 | 1 | True | 65.241 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=241, totalMatches=296, error=192.562, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 318.251 | - | - | 0 | 92/102 | 0.902 | 1.046 | 3.622 | 1.138 | 1 | True | 57.649 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=92, totalMatches=102, error=318.251, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 152.575 | - | - | 0 | 539/544 | 0.991 | 0.738 | 5.089 | 1.022 | 0 | True | 76.165 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=539, totalMatches=544, error=152.575, tolerance=35 |
| v_fest_1_2 | viewpoint | 1-2 | False | 270.342 | - | - | 0 | 469/474 | 0.989 | 0.84 | 4.495 | 1.368 | 0 | True | 76.229 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=469, totalMatches=474, error=270.342, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | False | 202.934 | - | - | 0 | 347/356 | 0.975 | 0.748 | 4.118 | 0.763 | 1 | True | 60.39 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=347, totalMatches=356, error=202.934, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 137.909 | - | - | 0 | 512/525 | 0.975 | 0.527 | 4.491 | 1.192 | 0 | True | 72.046 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=512, totalMatches=525, error=137.909, tolerance=35 |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 58.531 | - | - | 0 | 40/47 | 0.851 | 1.089 | 3.101 | 1.084 | 1 | True | 61.661 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=40, totalMatches=47, error=58.531, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 233 | - | - | 0 | 961/965 | 0.996 | 0.261 | 4.324 | 1.012 | 1 | True | 67.99 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=961, totalMatches=965, error=233, tolerance=35 |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 321.632 | - | - | 0 | 200/212 | 0.943 | 0.791 | 2.82 | 0.876 | 1 | True | 77.554 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=200, totalMatches=212, error=321.632, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 129.671 | - | - | 0 | 474/478 | 0.992 | 0.471 | 3.932 | 1.12 | 0 | True | 70.773 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=474, totalMatches=478, error=129.671, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 171.697 | - | - | 0 | 340/387 | 0.879 | 0.609 | 3.278 | 0.829 | 1 | True | 79.835 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=340, totalMatches=387, error=171.697, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 332.042 | - | - | 0 | 548/568 | 0.965 | 0.592 | 4.761 | 1.005 | 0 | True | 68.614 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=548, totalMatches=568, error=332.042, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 271.102 | - | - | 0 | 403/451 | 0.894 | 0.815 | 3.777 | 0.846 | 1 | True | 68.69 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=403, totalMatches=451, error=271.102, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | True | 0.083 | 3.507 | 5.447 | 0.9132 | 138/148 | 0.932 | 0.957 | 4.484 | 1.016 | 2 | True | 64.943 | - | - |
