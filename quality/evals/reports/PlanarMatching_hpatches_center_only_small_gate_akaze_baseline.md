# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:44:16.9222463+00:00`
Operator: `PlanarMatching`
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
| Mean position error px | 41.63 |
| P95 position error px | 87.944 |
| P95 corner error px | 5.806 |
| Mean inliers | 251.8 |
| Mean score | 0.2002 |
| Runtime ms | 4295.483 |
| Max features | 1600 |
| Min inliers | 6 |
| Match ratio | 0.75 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.2 |
| Detector type | AKAZE |
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
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.459 | 0.942 | 1.228 | 0.2221 | 299/300 | 0.997 | 0.681 | 4.312 | 0.704 | 0 | True | 277.413 | Projected quadrilateral is invalid. | isMatch=False, score=0.222, inliers=299, totalMatches=300, error=66.459, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 53.155 | 2.264 | 3.735 | 0.1755 | 194/230 | 0.843 | 1.013 | 6.417 | 1.57 | 0 | True | 210.867 | Projected quadrilateral is invalid. | isMatch=False, score=0.175, inliers=194, totalMatches=230, error=53.155, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 0.955 | 0.409 | 0.515 | 0.1892 | 300/300 | 1 | 0.788 | 4.659 | 1.041 | 1 | True | 172.399 | Projected quadrilateral is invalid. | isMatch=False, score=0.189, inliers=300, totalMatches=300, error=0.955, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | False | 0.344 | 0.929 | 1.109 | 0.1606 | 290/300 | 0.967 | 0.549 | 2.33 | 0.962 | 1 | True | 263.91 | Projected quadrilateral is invalid. | isMatch=False, score=0.161, inliers=290, totalMatches=300, error=0.344, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 25.093 | 0.551 | 0.839 | 0.1575 | 300/300 | 1 | 0.297 | 1.176 | 0.929 | 1 | True | 244.071 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=300, totalMatches=300, error=25.093, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 64.686 | 2.919 | 3.993 | 0.1538 | 299/300 | 0.997 | 0.606 | 2.817 | 0.843 | 1 | True | 247.274 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=299, totalMatches=300, error=64.686, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 118.723 | 2.761 | 4.328 | 0.168 | 267/300 | 0.89 | 1.009 | 4.459 | 1.227 | 1 | True | 216.814 | Projected quadrilateral is invalid. | isMatch=False, score=0.168, inliers=267, totalMatches=300, error=118.723, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 55.015 | 1.52 | 2.469 | 0.2127 | 66/78 | 0.846 | 1.2 | 4.59 | 0.788 | 1 | True | 175.584 | Projected quadrilateral is invalid. | isMatch=False, score=0.213, inliers=66, totalMatches=78, error=55.015, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.456 | 1.931 | 3.956 | 0.159 | 297/300 | 0.99 | 0.519 | 2.309 | 0.846 | 0 | True | 231.31 | Projected quadrilateral is invalid. | isMatch=False, score=0.159, inliers=297, totalMatches=300, error=62.456, tolerance=35 |
| v_fest_1_2 | viewpoint | 1-2 | False | 87.944 | 1.918 | 2.742 | 0.1608 | 298/300 | 0.993 | 0.589 | 3.91 | 0.948 | 0 | True | 228.032 | Projected quadrilateral is invalid. | isMatch=False, score=0.161, inliers=298, totalMatches=300, error=87.944, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | False | 0.527 | 0.748 | 1.865 | 0.2257 | 280/300 | 0.933 | 0.593 | 3.75 | 0.529 | 1 | True | 187.477 | Projected quadrilateral is invalid. | isMatch=False, score=0.226, inliers=280, totalMatches=300, error=0.527, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 45.222 | 1.069 | 1.695 | 0.1583 | 300/300 | 1 | 0.304 | 1.807 | 0.83 | 0 | True | 210.868 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=300, totalMatches=300, error=45.222, tolerance=35 |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 46.419 | 2.794 | 5.806 | 0.1845 | 39/47 | 0.83 | 1.088 | 3.362 | 1.113 | 0 | True | 184.971 | Projected quadrilateral is invalid. | isMatch=False, score=0.184, inliers=39, totalMatches=47, error=46.419, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 6.505 | 0.257 | 0.394 | 0.1726 | 300/300 | 1 | 0.223 | 0.997 | 1.012 | 1 | True | 207.86 | Projected quadrilateral is invalid. | isMatch=False, score=0.173, inliers=300, totalMatches=300, error=6.505, tolerance=35 |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 22.468 | 4.997 | 7.811 | 0.1477 | 253/300 | 0.843 | 0.929 | 4.416 | 1.079 | 1 | True | 226.772 | Projected quadrilateral is invalid. | isMatch=False, score=0.148, inliers=253, totalMatches=300, error=22.468, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 45.145 | 0.575 | 0.713 | 0.1577 | 300/300 | 1 | 0.337 | 2.604 | 0.926 | 0 | True | 201.86 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=300, totalMatches=300, error=45.145, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 32.932 | 0.69 | 0.94 | 0.158 | 286/300 | 0.953 | 0.625 | 3.443 | 1.023 | 1 | True | 219.45 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=286, totalMatches=300, error=32.932, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 47.512 | 1.097 | 1.601 | 0.1708 | 300/300 | 1 | 0.595 | 3.306 | 0.697 | 0 | True | 198.159 | Projected quadrilateral is invalid. | isMatch=False, score=0.171, inliers=300, totalMatches=300, error=47.512, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.778 | 1.104 | 1.544 | 0.1613 | 300/300 | 1 | 0.842 | 3.791 | 0.847 | 1 | True | 206.046 | Projected quadrilateral is invalid. | isMatch=False, score=0.161, inliers=300, totalMatches=300, error=49.778, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | True | 1.266 | 2.283 | 3.365 | 0.7089 | 68/79 | 0.861 | 1.078 | 5.443 | 1.244 | 2 | True | 184.346 | - | - |
