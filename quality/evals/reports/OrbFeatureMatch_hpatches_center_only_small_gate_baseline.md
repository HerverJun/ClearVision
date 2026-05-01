# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:45:38.0556400+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 0 |
| Failed | 20 |
| Pass rate | 0 |
| Mean position error px | 206.301 |
| P95 position error px | 307.23 |
| P95 corner error px | 1000000 |
| Mean inliers | 331.75 |
| Mean score | 0 |
| Runtime ms | 669.699 |
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
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156 | - | - | 0 | 356/366 | 0.973 | 1.04 | 4.893 | 1.014 | 0 | True | 161.724 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=356, totalMatches=366, error=279.156, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.23 | - | - | 0 | 134/134 | 1 | 1.493 | 5.211 | 1.899 | 0 | True | 24.93 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=134, totalMatches=134, error=307.23, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 260.984 | - | - | 0 | 208/229 | 0.908 | 1.271 | 4.133 | 0.662 | 1 | True | 24.491 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=208, totalMatches=229, error=260.984, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | False | 206 | - | - | 0 | 330/350 | 0.943 | 1.103 | 5.03 | 0.78 | 1 | True | 31.378 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=330, totalMatches=350, error=206, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 217.228 | - | - | 0 | 339/362 | 0.936 | 0.966 | 5.277 | 1.123 | 1 | True | 31.806 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=339, totalMatches=362, error=217.228, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 227.419 | - | - | 0 | 325/325 | 1 | 1.101 | 4.159 | 0.844 | 1 | True | 34.05 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=325, totalMatches=325, error=227.419, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 143.408 | - | - | 0 | 207/218 | 0.95 | 1.307 | 5.162 | 1.773 | 1 | True | 23.294 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=207, totalMatches=218, error=143.408, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.875 | - | - | 0 | 35/36 | 0.972 | 1.229 | 3.993 | 1.132 | 1 | True | 16.845 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=35, totalMatches=36, error=243.875, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 119.45 | - | - | 0 | 569/570 | 0.998 | 0.967 | 4.029 | 1.022 | 0 | True | 25.902 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=569, totalMatches=570, error=119.45, tolerance=35 |
| v_fest_1_2 | viewpoint | 1-2 | False | 157.731 | - | - | 0 | 296/348 | 0.851 | 1.138 | 5.6 | 1.369 | 0 | True | 27.733 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=296, totalMatches=348, error=157.731, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | False | 132.914 | - | - | 0 | 637/637 | 1 | 0.937 | 4.884 | 0.764 | 1 | True | 22.519 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=637, totalMatches=637, error=132.914, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 248.894 | - | - | 0 | 471/471 | 1 | 1.065 | 4.403 | 1.193 | 0 | True | 30.123 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=471, totalMatches=471, error=248.894, tolerance=35 |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 267.972 | - | - | 0 | 110/115 | 0.957 | 1.093 | 4.371 | 1.115 | 0 | True | 21.586 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=110, totalMatches=115, error=267.972, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 19.469 | - | - | 0 | 1017/1017 | 1 | 0.871 | 5.044 | 1.012 | 1 | True | 26.579 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1017, totalMatches=1017, error=19.469, tolerance=35 |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 189.489 | - | - | 0 | 90/95 | 0.947 | 1.198 | 4.539 | 0.87 | 1 | True | 30.03 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=90, totalMatches=95, error=189.489, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 177.838 | - | - | 0 | 500/507 | 0.986 | 0.872 | 4.259 | 1.12 | 0 | True | 28.643 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=500, totalMatches=507, error=177.838, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 73.318 | - | - | 0 | 181/196 | 0.923 | 1.002 | 4.49 | 0.83 | 1 | True | 30.276 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=181, totalMatches=196, error=73.318, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 358.203 | - | - | 0 | 388/430 | 0.902 | 0.988 | 8.913 | 1.008 | 0 | True | 26.482 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=388, totalMatches=430, error=358.203, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 219.078 | - | - | 0 | 147/153 | 0.961 | 1.02 | 4.308 | 0.845 | 1 | True | 27.802 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=147, totalMatches=153, error=219.078, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 276.367 | - | - | 0 | 295/303 | 0.974 | 0.996 | 4.948 | 1.009 | 1 | True | 23.506 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=295, totalMatches=303, error=276.367, tolerance=35 |
