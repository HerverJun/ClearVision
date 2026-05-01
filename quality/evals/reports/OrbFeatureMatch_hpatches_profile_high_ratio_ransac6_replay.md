# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:26:40.2925150+00:00`
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
| Mean position error px | 45.662 |
| P95 position error px | 259.288 |
| P95 corner error px | 9.804 |
| Mean inliers | 349.1 |
| Mean score | 0.7314 |
| Runtime ms | 605.555 |
| Max features | 2000 |
| Min inliers | 6 |
| Match ratio | 0.78 |
| RANSAC threshold px | 6 |
| Min inlier ratio | 0.2 |
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
| i_autannes_1_2 | illumination | 1-2 | True | 0.482 | 1.487 | 2.26 | 0.9746 | 92/93 | 0.989 | 0.451 | 5.168 | 1 | 3 | True | 170.736 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.097 | 1.609 | 2.182 | 0.9431 | 190/200 | 0.95 | 0.681 | 4.534 | 0.995 | 3 | True | 14.74 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.214 | 0.464 | 0.809 | 0.9885 | 907/913 | 0.993 | 0.183 | 4.588 | 0.999 | 4 | True | 20.547 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.196 | 4.957 | 6.696 | 0.8708 | 17/20 | 0.85 | 1.081 | 2.779 | 0.986 | 2 | True | 23.869 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.713 | 3.092 | 9.804 | 0.8907 | 63/72 | 0.875 | 0.939 | 7.574 | 1.011 | 2 | True | 37.456 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.084 | 0.266 | 0.396 | 0.9711 | 564/567 | 0.995 | 0.602 | 5.236 | 1 | 4 | True | 19.694 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.553 | 2.524 | 3.984 | 0.8315 | 41/53 | 0.774 | 1.019 | 6.099 | 0.994 | 3 | True | 22.044 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 970/975 | 0.995 | 0.92 | 5.849 | 1.001 | 1 | True | 20.352 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=970, totalMatches=975, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 1.784 | 2.45 | 3.159 | 0.7983 | 33/44 | 0.75 | 1.486 | 3.776 | 0.994 | 3 | True | 18.047 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.847 | 1.193 | 1.694 | 0.7734 | 111/161 | 0.689 | 1.292 | 3.337 | 0.999 | 3 | True | 23.592 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.016 | 1.886 | 2.03 | 0.938 | 931/973 | 0.957 | 0.887 | 7.035 | 1.001 | 2 | True | 25.886 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.173 | 1.211 | 1.389 | 0.9433 | 216/222 | 0.973 | 0.968 | 6.025 | 0.999 | 3 | True | 25.912 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | - | - | 0 | 4/6 | 0.667 | 0 | 0 | 0 | 4 | True | 16.867 | Insufficient inliers (4 < 6). | isMatch=False, score=0, inliers=4, totalMatches=6, error=241.446, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 1.917 | 2.227 | 2.312 | 0.9148 | 236/262 | 0.901 | 0.709 | 3.732 | 1 | 2 | True | 23.088 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.118 | 0.435 | 0.743 | 0.9731 | 688/691 | 0.996 | 0.567 | 4.927 | 1.002 | 4 | True | 24.569 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.08 | 3.77 | 8.241 | 0.9175 | 40/43 | 0.93 | 1.022 | 5.153 | 1.014 | 2 | True | 20.175 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 85.606 | - | - | 0 | 500/523 | 0.956 | 1.07 | 7.068 | 1.014 | 0 | True | 24.23 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=500, totalMatches=523, error=85.606, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.181 | 1.259 | 1.5 | 0.9448 | 437/441 | 0.991 | 1.163 | 5.788 | 0.62 | 2 | True | 16.777 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.069 | 0.685 | 0.881 | 0.9555 | 724/726 | 0.997 | 0.995 | 5.458 | 0.868 | 2 | True | 31.877 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 313.373 | - | - | 0 | 218/221 | 0.986 | 1.465 | 5.356 | 1.9 | 0 | True | 25.097 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=218, totalMatches=221, error=313.373, tolerance=35 |
