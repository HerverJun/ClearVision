# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:56:53.1930466+00:00`
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
| Mean position error px | 41.718 |
| P95 position error px | 87.944 |
| P95 corner error px | 4.328 |
| Mean inliers | 253.2 |
| Mean score | 0.7931 |
| Runtime ms | 4260.074 |
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
| Allow center-only projection | True |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.459 | 0.942 | 1.228 | 0.848 | 299/300 | 0.997 | 0.681 | 4.312 | 0.704 | 0 | True | 272.253 | - | isMatch=True, score=0.848, inliers=299, totalMatches=300, error=66.459, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 52.419 | 1.308 | 2.223 | 0.7889 | 213/215 | 0.991 | 0.851 | 3.239 | 1.321 | 0 | True | 201.535 | - | isMatch=True, score=0.789, inliers=213, totalMatches=215, error=52.419, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.955 | 0.409 | 0.515 | 0.8126 | 300/300 | 1 | 0.788 | 4.659 | 1.041 | 1 | True | 172.112 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | True | 0.65 | 0.574 | 0.933 | 0.7892 | 300/300 | 1 | 0.553 | 2.184 | 0.541 | 1 | True | 266.13 | - | - |
| v_bricks_1_2 | viewpoint | 1-2 | True | 25.093 | 0.551 | 0.839 | 0.7975 | 300/300 | 1 | 0.297 | 1.176 | 0.929 | 1 | True | 244.584 | - | - |
| v_busstop_1_2 | viewpoint | 1-2 | False | 65.668 | 1.821 | 2.411 | 0.7831 | 299/300 | 0.997 | 0.574 | 2.953 | 0.696 | 1 | True | 244.353 | - | isMatch=True, score=0.783, inliers=299, totalMatches=300, error=65.668, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 118.723 | 2.761 | 4.328 | 0.7447 | 267/300 | 0.89 | 1.009 | 4.459 | 1.227 | 1 | True | 215.533 | - | isMatch=True, score=0.745, inliers=267, totalMatches=300, error=118.723, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 53.857 | 1.303 | 2.106 | 0.8144 | 80/81 | 0.988 | 1.112 | 3.415 | 0.934 | 1 | True | 173.84 | - | isMatch=True, score=0.814, inliers=80, totalMatches=81, error=53.857, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.456 | 1.931 | 3.956 | 0.788 | 297/300 | 0.99 | 0.519 | 2.309 | 0.846 | 0 | True | 232.177 | - | isMatch=True, score=0.788, inliers=297, totalMatches=300, error=62.456, tolerance=35 |
| v_fest_1_2 | viewpoint | 1-2 | False | 87.944 | 1.918 | 2.742 | 0.7886 | 298/300 | 0.993 | 0.589 | 3.91 | 0.948 | 0 | True | 223.794 | - | isMatch=True, score=0.789, inliers=298, totalMatches=300, error=87.944, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | True | 0.74 | 0.393 | 0.576 | 0.8427 | 299/300 | 0.997 | 0.51 | 4.055 | 0.63 | 1 | True | 184.348 | - | - |
| v_london_1_2 | viewpoint | 1-2 | False | 45.222 | 1.069 | 1.695 | 0.7981 | 300/300 | 1 | 0.304 | 1.807 | 0.83 | 0 | True | 209.184 | - | isMatch=True, score=0.798, inliers=300, totalMatches=300, error=45.222, tolerance=35 |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 45.926 | 1.193 | 1.625 | 0.7726 | 34/34 | 1 | 1.396 | 4.244 | 0.918 | 0 | True | 181.248 | - | isMatch=True, score=0.773, inliers=34, totalMatches=34, error=45.926, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | True | 6.505 | 0.257 | 0.394 | 0.8151 | 300/300 | 1 | 0.223 | 0.997 | 1.012 | 1 | True | 209.984 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | True | 23.468 | 3.785 | 6.433 | 0.7268 | 139/146 | 0.952 | 0.898 | 4.299 | 0.606 | 1 | True | 225.336 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 45.145 | 0.575 | 0.713 | 0.7963 | 300/300 | 1 | 0.337 | 2.604 | 0.926 | 0 | True | 201.409 | - | isMatch=True, score=0.796, inliers=300, totalMatches=300, error=45.145, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | True | 32.841 | 0.478 | 0.751 | 0.7827 | 295/300 | 0.983 | 0.567 | 3.247 | 0.829 | 1 | True | 221.952 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 47.512 | 1.097 | 1.601 | 0.8007 | 300/300 | 1 | 0.595 | 3.306 | 0.697 | 0 | True | 198.139 | - | isMatch=True, score=0.801, inliers=300, totalMatches=300, error=47.512, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.778 | 1.104 | 1.544 | 0.7829 | 300/300 | 1 | 0.842 | 3.791 | 0.847 | 1 | True | 206.397 | - | isMatch=True, score=0.783, inliers=300, totalMatches=300, error=49.778, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | True | 3.007 | 2.534 | 4.064 | 0.79 | 144/148 | 0.973 | 0.881 | 4.08 | 1.014 | 1 | True | 175.766 | - | - |
