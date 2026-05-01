# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:56:55.1722398+00:00`
Operator: `PlanarMatching`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 8 |
| Failed | 12 |
| Pass rate | 0.4 |
| Mean position error px | 41.627 |
| P95 position error px | 88.227 |
| P95 corner error px | 9.617 |
| Mean inliers | 241.9 |
| Mean score | 0.7568 |
| Runtime ms | 1511.347 |
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
| Allow center-only projection | True |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.199 | 1.26 | 1.583 | 0.7679 | 299/300 | 0.997 | 1.077 | 4.43 | 0.837 | 0 | True | 211.144 | - | isMatch=True, score=0.768, inliers=299, totalMatches=300, error=66.199, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 55.926 | 7.333 | 13.66 | 0.7363 | 167/170 | 0.982 | 1.19 | 4.284 | 1.567 | 0 | True | 67.033 | - | isMatch=True, score=0.736, inliers=167, totalMatches=170, error=55.926, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.242 | 1.342 | 3.44 | 0.7407 | 159/162 | 0.981 | 1.025 | 3.499 | 0.461 | 1 | True | 58.862 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | True | 0.791 | 1.398 | 1.971 | 0.7727 | 300/300 | 1 | 1.001 | 4.449 | 0.962 | 1 | True | 82.332 | - | - |
| v_bricks_1_2 | viewpoint | 1-2 | True | 25.112 | 0.595 | 0.905 | 0.7655 | 296/300 | 0.987 | 0.903 | 2.969 | 1.126 | 1 | True | 84.154 | - | - |
| v_busstop_1_2 | viewpoint | 1-2 | False | 65.451 | 2.51 | 4.216 | 0.7633 | 299/300 | 0.997 | 1.014 | 3.474 | 0.585 | 1 | True | 85.772 | - | isMatch=True, score=0.763, inliers=299, totalMatches=300, error=65.451, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 117.416 | 1.87 | 3.566 | 0.7519 | 273/277 | 0.986 | 1.213 | 4.879 | 1.23 | 1 | True | 63.841 | - | isMatch=True, score=0.752, inliers=273, totalMatches=277, error=117.416, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 53.481 | 1.324 | 2.003 | 0.7241 | 38/40 | 0.95 | 1.062 | 2.833 | 0.935 | 1 | True | 35.203 | - | isMatch=True, score=0.724, inliers=38, totalMatches=40, error=53.481, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.619 | 1.88 | 3.847 | 0.7735 | 299/300 | 0.997 | 0.883 | 5.981 | 0.711 | 0 | True | 67.547 | - | isMatch=True, score=0.774, inliers=299, totalMatches=300, error=62.619, tolerance=35 |
| v_fest_1_2 | viewpoint | 1-2 | False | 88.227 | 1.534 | 3.782 | 0.758 | 290/300 | 0.967 | 0.942 | 4.33 | 0.949 | 0 | True | 73.056 | - | isMatch=True, score=0.758, inliers=290, totalMatches=300, error=88.227, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | True | 1.102 | 0.495 | 0.885 | 0.7789 | 300/300 | 1 | 0.838 | 3.446 | 0.632 | 1 | True | 60 | - | - |
| v_london_1_2 | viewpoint | 1-2 | False | 45.712 | 0.752 | 0.979 | 0.7671 | 299/300 | 0.997 | 1.027 | 5.934 | 0.986 | 0 | True | 74.409 | - | isMatch=True, score=0.767, inliers=299, totalMatches=300, error=45.712, tolerance=35 |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 44.946 | 1.207 | 2.166 | 0.7461 | 99/102 | 0.971 | 0.935 | 2.997 | 0.773 | 0 | True | 41.204 | - | isMatch=True, score=0.746, inliers=99, totalMatches=102, error=44.946, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | True | 6.623 | 0.723 | 1.062 | 0.7882 | 300/300 | 1 | 0.582 | 4.46 | 0.703 | 1 | True | 69.002 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | True | 21.429 | 6.617 | 9.617 | 0.72 | 123/131 | 0.939 | 0.913 | 3.2 | 0.722 | 1 | True | 78.97 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 44.556 | 0.563 | 0.797 | 0.777 | 298/300 | 0.993 | 0.714 | 3.498 | 0.778 | 0 | True | 75.031 | - | isMatch=True, score=0.777, inliers=298, totalMatches=300, error=44.556, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | True | 33.737 | 1.44 | 1.851 | 0.7313 | 196/211 | 0.929 | 0.993 | 3.712 | 0.687 | 1 | True | 84.056 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 47.759 | 2.442 | 4.643 | 0.7563 | 289/300 | 0.963 | 1.034 | 4.652 | 0.827 | 0 | True | 67.699 | - | isMatch=True, score=0.756, inliers=289, totalMatches=300, error=47.759, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.036 | 2.019 | 2.744 | 0.7459 | 214/220 | 0.973 | 0.984 | 3.72 | 0.587 | 1 | True | 72.241 | - | isMatch=True, score=0.746, inliers=214, totalMatches=220, error=49.036, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | True | 2.169 | 1.534 | 1.981 | 0.7717 | 300/300 | 1 | 0.984 | 4.967 | 1.011 | 1 | True | 59.791 | - | - |
