# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:56:56.3240940+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 18 |
| Failed | 2 |
| Pass rate | 0.9 |
| Mean position error px | 22.802 |
| P95 position error px | 143.408 |
| P95 corner error px | 9.096 |
| Mean inliers | 331.75 |
| Mean score | 0.844 |
| Runtime ms | 670.838 |
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
| Allow center-only projection | True |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| v_abstract_1_2 | viewpoint | 1-2 | True | 0.045 | 0.743 | 1.203 | 0.9465 | 356/366 | 0.973 | 1.04 | 4.893 | 1.014 | 0 | True | 161.929 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.23 | - | - | 0 | 134/134 | 1 | 1.493 | 5.211 | 1.899 | 0 | True | 25.399 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=134, totalMatches=134, error=307.23, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.671 | 2.034 | 4.346 | 0.9025 | 208/229 | 0.908 | 1.271 | 4.133 | 0.662 | 1 | True | 24.269 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | True | 0.166 | 1.812 | 1.945 | 0.9277 | 330/350 | 0.943 | 1.103 | 5.03 | 0.78 | 1 | True | 30.855 | - | - |
| v_bricks_1_2 | viewpoint | 1-2 | True | 0.019 | 0.586 | 1.542 | 0.9293 | 339/362 | 0.936 | 0.966 | 5.277 | 1.123 | 1 | True | 33.5 | - | - |
| v_busstop_1_2 | viewpoint | 1-2 | True | 0.738 | 2.182 | 3.508 | 0.9592 | 325/325 | 1 | 1.101 | 4.159 | 0.844 | 1 | True | 34.08 | - | - |
| v_churchill_1_2 | viewpoint | 1-2 | False | 143.408 | - | - | 0 | 207/218 | 0.95 | 1.307 | 5.162 | 1.773 | 1 | True | 23.4 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=207, totalMatches=218, error=143.408, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | True | 0.443 | 1.601 | 3.486 | 0.9392 | 35/36 | 0.972 | 1.229 | 3.993 | 1.132 | 1 | True | 16.367 | - | - |
| v_courses_1_2 | viewpoint | 1-2 | True | 0.352 | 3.319 | 6.162 | 0.9632 | 569/570 | 0.998 | 0.967 | 4.029 | 1.022 | 0 | True | 26.577 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | True | 0.279 | 1.444 | 2.746 | 0.8757 | 296/348 | 0.851 | 1.138 | 5.6 | 1.369 | 0 | True | 28.396 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | True | 0.24 | 0.313 | 0.631 | 0.9653 | 637/637 | 1 | 0.937 | 4.884 | 0.764 | 1 | True | 21.671 | - | - |
| v_london_1_2 | viewpoint | 1-2 | True | 0.08 | 0.552 | 0.775 | 0.9606 | 471/471 | 1 | 1.065 | 4.403 | 1.193 | 0 | True | 29.147 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | True | 0.309 | 0.931 | 1.393 | 0.9356 | 110/115 | 0.957 | 1.093 | 4.371 | 1.115 | 0 | True | 21.902 | - | - |
| v_strand_1_2 | viewpoint | 1-2 | True | 0.183 | 1.003 | 1.311 | 0.9678 | 1017/1017 | 1 | 0.871 | 5.044 | 1.012 | 1 | True | 25.964 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | True | 0.666 | 6.097 | 9.096 | 0.9267 | 90/95 | 0.947 | 1.198 | 4.539 | 0.87 | 1 | True | 29.733 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | True | 0.202 | 0.892 | 1.163 | 0.9601 | 500/507 | 0.986 | 0.872 | 4.259 | 1.12 | 0 | True | 28.694 | - | - |
| v_vitro_1_2 | viewpoint | 1-2 | True | 0.164 | 0.843 | 1.724 | 0.9208 | 181/196 | 0.923 | 1.002 | 4.49 | 0.83 | 1 | True | 30.878 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | True | 0.353 | 3.124 | 5.474 | 0.9097 | 388/430 | 0.902 | 0.988 | 8.913 | 1.008 | 0 | True | 26.785 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | True | 0.298 | 0.882 | 2.11 | 0.9407 | 147/153 | 0.961 | 1.02 | 4.308 | 0.845 | 1 | True | 28.005 | - | - |
| v_yuri_1_2 | viewpoint | 1-2 | True | 0.204 | 1.123 | 2.227 | 0.9486 | 295/303 | 0.974 | 0.996 | 4.948 | 1.009 | 1 | True | 23.287 | - | - |
