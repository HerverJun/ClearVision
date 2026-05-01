# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:45:34.9382139+00:00`
Operator: `PlanarMatching`
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
| Mean position error px | 41.357 |
| P95 position error px | 88.113 |
| P95 corner error px | 10.31 |
| Mean inliers | 236.85 |
| Mean score | 0.1476 |
| Runtime ms | 1510.973 |
| Max features | 1600 |
| Min inliers | 6 |
| Match ratio | 0.75 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.2 |
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
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.274 | 1.093 | 1.752 | 0.1557 | 295/300 | 0.983 | 0.953 | 3.287 | 0.703 | 0 | True | 211.857 | Projected quadrilateral is invalid. | isMatch=False, score=0.156, inliers=295, totalMatches=300, error=66.274, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 51.921 | 1.268 | 2.206 | 0.1343 | 150/180 | 0.833 | 1.172 | 4.381 | 1.323 | 0 | True | 65.708 | Projected quadrilateral is invalid. | isMatch=False, score=0.134, inliers=150, totalMatches=180, error=51.921, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 1.367 | 2.543 | 5.536 | 0.134 | 157/175 | 0.897 | 1.188 | 4.065 | 0.659 | 1 | True | 58.968 | Projected quadrilateral is invalid. | isMatch=False, score=0.134, inliers=157, totalMatches=175, error=1.367, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | False | 0.791 | 1.398 | 1.971 | 0.1564 | 300/300 | 1 | 1.001 | 4.449 | 0.962 | 1 | True | 82.123 | Projected quadrilateral is invalid. | isMatch=False, score=0.156, inliers=300, totalMatches=300, error=0.791, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 25.077 | 0.711 | 1.546 | 0.1526 | 293/300 | 0.977 | 0.882 | 3.636 | 0.928 | 1 | True | 83.292 | Projected quadrilateral is invalid. | isMatch=False, score=0.153, inliers=293, totalMatches=300, error=25.077, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 65.001 | 2.705 | 4.524 | 0.1499 | 264/300 | 0.88 | 1.005 | 3.264 | 0.697 | 1 | True | 84.57 | Projected quadrilateral is invalid. | isMatch=False, score=0.15, inliers=264, totalMatches=300, error=65.001, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 117.416 | 1.87 | 3.566 | 0.148 | 273/277 | 0.986 | 1.213 | 4.879 | 1.23 | 1 | True | 64.202 | Projected quadrilateral is invalid. | isMatch=False, score=0.148, inliers=273, totalMatches=277, error=117.416, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 54.064 | 1.233 | 2 | 0.1295 | 34/42 | 0.81 | 0.983 | 3.744 | 0.785 | 1 | True | 35.893 | Projected quadrilateral is invalid. | isMatch=False, score=0.129, inliers=34, totalMatches=42, error=54.064, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.218 | 5.563 | 10.31 | 0.1551 | 293/300 | 0.977 | 0.885 | 3.305 | 1.02 | 0 | True | 67.434 | Projected quadrilateral is invalid. | isMatch=False, score=0.155, inliers=293, totalMatches=300, error=62.218, tolerance=35 |
| v_fest_1_2 | viewpoint | 1-2 | False | 88.113 | 1.524 | 2.128 | 0.1527 | 297/300 | 0.99 | 1.216 | 4.06 | 1.689 | 0 | True | 72.66 | Projected quadrilateral is invalid. | isMatch=False, score=0.153, inliers=297, totalMatches=300, error=88.113, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | False | 0.733 | 0.371 | 0.533 | 0.1583 | 269/300 | 0.897 | 0.832 | 3.576 | 0.943 | 1 | True | 63.276 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=269, totalMatches=300, error=0.733, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 45.467 | 1.354 | 2.56 | 0.1538 | 287/300 | 0.957 | 1.022 | 4.336 | 1.192 | 0 | True | 75.863 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=287, totalMatches=300, error=45.467, tolerance=35 |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 45.144 | 1.189 | 2.393 | 0.1397 | 106/110 | 0.964 | 1.007 | 3.58 | 0.918 | 0 | True | 40.093 | Projected quadrilateral is invalid. | isMatch=False, score=0.14, inliers=106, totalMatches=110, error=45.144, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 7.114 | 1.312 | 1.59 | 0.1583 | 292/300 | 0.973 | 0.628 | 2.783 | 1.012 | 1 | True | 69.085 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=292, totalMatches=300, error=7.114, tolerance=35 |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 19.053 | 8.875 | 16.468 | 0.123 | 105/135 | 0.778 | 1.209 | 4.387 | 1.072 | 1 | True | 79.257 | Projected quadrilateral is invalid. | isMatch=False, score=0.123, inliers=105, totalMatches=135, error=19.053, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 44.592 | 0.761 | 1.24 | 0.1542 | 298/300 | 0.993 | 1.01 | 3.92 | 1.381 | 0 | True | 74.845 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=298, totalMatches=300, error=44.592, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 33.173 | 0.718 | 0.994 | 0.1433 | 207/233 | 0.888 | 1.081 | 4.8 | 1.025 | 1 | True | 83.75 | Projected quadrilateral is invalid. | isMatch=False, score=0.143, inliers=207, totalMatches=233, error=33.173, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 48.019 | 2.538 | 6.079 | 0.1544 | 272/300 | 0.907 | 0.984 | 4.689 | 1.008 | 0 | True | 65.11 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=272, totalMatches=300, error=48.019, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.436 | 1.935 | 2.346 | 0.1432 | 245/251 | 0.976 | 1.255 | 4.095 | 1.043 | 1 | True | 72.228 | Projected quadrilateral is invalid. | isMatch=False, score=0.143, inliers=245, totalMatches=251, error=49.436, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 2.169 | 1.534 | 1.981 | 0.1548 | 300/300 | 1 | 0.984 | 4.967 | 1.011 | 1 | True | 60.759 | Projected quadrilateral is invalid. | isMatch=False, score=0.155, inliers=300, totalMatches=300, error=2.169, tolerance=35 |
