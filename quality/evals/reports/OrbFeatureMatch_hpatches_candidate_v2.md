# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-29T14:28:55.2260683+00:00`
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
| Mean position error px | 55.895 |
| P95 position error px | 358.14 |
| Mean inliers | 190 |
| Mean score | 0.734 |
| Runtime ms | 552.392 |
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

## Cases

| Case | Type | Pair | Passed | Error px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_autannes_1_2 | illumination | 1-2 | True | 1.077 | 0.9776 | 28/28 | 1 | 0.432 | 4.272 | 0.979 | 2 | True | 182.548 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.214 | 0.96 | 109/110 | 0.991 | 0.675 | 3.411 | 1.001 | 2 | True | 10.922 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.144 | 0.987 | 608/614 | 0.99 | 0.146 | 4.197 | 1.001 | 2 | True | 17.107 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 4.858 | 0.9756 | 9/9 | 1 | 0.471 | 1.746 | 0.944 | 2 | True | 18.433 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 1.864 | 0.9221 | 34/36 | 0.944 | 0.912 | 3.367 | 1.017 | 2 | True | 35.836 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.439 | 0.9343 | 287/306 | 0.938 | 0.608 | 4.313 | 1.009 | 2 | True | 17.472 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.879 | 0.629 | 7/19 | 0.368 | 0.455 | 0.723 | 0.956 | 2 | True | 23.393 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 358.14 | 0 | 510/594 | 0.859 | 0.935 | 4.189 | 0.996 | 1 | True | 16.109 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=510, totalMatches=594, error=358.14, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 1.123 | 0.8895 | 19/21 | 0.905 | 1.12 | 2.691 | 1.003 | 2 | True | 14.12 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.692 | 0.7547 | 53/77 | 0.688 | 1.425 | 3.588 | 1.003 | 2 | True | 20.089 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.025 | 0.9518 | 547/550 | 0.995 | 0.871 | 7.016 | 1.001 | 2 | True | 20.578 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.287 | 0.9435 | 89/91 | 0.978 | 0.857 | 3.804 | 1 | 2 | True | 21.883 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | 0 | 0/1 | 0 | - | - | 0 | 0 | False | 14.299 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=1, error=241.446, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 1.927 | 0.9394 | 120/125 | 0.96 | 0.745 | 4.045 | 1.001 | 2 | True | 20.328 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.131 | 0.9656 | 351/353 | 0.994 | 0.604 | 4.847 | 1.002 | 2 | True | 21.62 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.211 | 0.9626 | 21/21 | 1 | 0.72 | 2.23 | 1.01 | 2 | True | 16.601 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 391.906 | 0 | 275/279 | 0.986 | 1.076 | 3.704 | 1.014 | 0 | True | 20.964 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=275, totalMatches=279, error=391.906, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.059 | 0.9367 | 223/227 | 0.982 | 1.033 | 4.117 | 0.62 | 2 | True | 11.776 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.075 | 0.9495 | 405/406 | 0.998 | 0.948 | 6.293 | 0.868 | 2 | True | 28.205 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 108.405 | 0 | 105/110 | 0.955 | 1.344 | 4.216 | 1.904 | 0 | True | 20.109 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=105, totalMatches=110, error=108.405, tolerance=35 |
