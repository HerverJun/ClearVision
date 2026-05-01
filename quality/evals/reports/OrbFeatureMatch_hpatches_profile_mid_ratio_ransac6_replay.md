# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:26:35.7474136+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 15 |
| Failed | 5 |
| Pass rate | 0.75 |
| Mean position error px | 65.197 |
| P95 position error px | 279.156 |
| P95 corner error px | 12.134 |
| Mean inliers | 307.65 |
| Mean score | 0.7001 |
| Runtime ms | 595.507 |
| Max features | 2000 |
| Min inliers | 6 |
| Match ratio | 0.74 |
| RANSAC threshold px | 6 |
| Min inlier ratio | 0.22 |
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
| i_autannes_1_2 | illumination | 1-2 | True | 0.202 | 0.894 | 1.47 | 0.9835 | 89/89 | 1 | 0.382 | 5.126 | 1.001 | 3 | True | 161.273 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.205 | 0.932 | 1.202 | 0.9715 | 167/168 | 0.994 | 0.585 | 3.462 | 0.998 | 4 | True | 14.56 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.266 | 0.602 | 1.114 | 0.9891 | 852/858 | 0.993 | 0.163 | 4.585 | 0.998 | 4 | True | 20.517 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 1.498 | 5.86 | 10.598 | 0.9659 | 14/14 | 1 | 0.789 | 1.829 | 0.987 | 2 | True | 22.93 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.057 | 1.942 | 4.095 | 0.9596 | 48/49 | 0.98 | 0.674 | 2.902 | 1.004 | 2 | True | 36.91 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.049 | 0.217 | 0.255 | 0.978 | 495/495 | 1 | 0.509 | 5.25 | 1 | 4 | True | 19.846 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.832 | 1.467 | 2.277 | 0.812 | 29/39 | 0.744 | 1.086 | 6.197 | 0.997 | 3 | True | 21.304 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 766/923 | 0.83 | 0.886 | 4.946 | 0.997 | 1 | True | 20.437 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=766, totalMatches=923, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 0.894 | 6.126 | 12.134 | 0.868 | 26/30 | 0.867 | 1.358 | 3.354 | 1.009 | 2 | True | 18.577 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.681 | 3.194 | 7.571 | 0.7753 | 84/123 | 0.683 | 1.165 | 2.919 | 0.998 | 2 | True | 23.48 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 196.939 | - | - | 0 | 900/903 | 0.997 | 0.851 | 4.465 | 1.001 | 1 | True | 25.254 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=900, totalMatches=903, error=196.939, tolerance=35 |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.436 | 1.194 | 1.92 | 0.9497 | 184/188 | 0.979 | 0.892 | 6.819 | 1 | 2 | True | 26.151 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | - | - | 0 | 0/1 | 0 | - | - | 0 | 0 | False | 16.937 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=1, error=241.446, tolerance=35 |
| i_santuario_1_2 | illumination | 1-2 | True | 1.873 | 2.003 | 2.326 | 0.9607 | 236/240 | 0.983 | 0.698 | 4.1 | 0.999 | 2 | True | 23.473 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.102 | 0.439 | 0.88 | 0.9764 | 627/628 | 0.998 | 0.527 | 4.938 | 1.001 | 4 | True | 24.752 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.645 | 4.084 | 10.896 | 0.9104 | 28/31 | 0.903 | 0.841 | 2.423 | 1.018 | 2 | True | 19.764 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156 | - | - | 0 | 430/442 | 0.973 | 1.073 | 4.936 | 1.014 | 0 | True | 25.323 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=430, totalMatches=442, error=279.156, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.217 | 1.635 | 2.543 | 0.9443 | 372/377 | 0.987 | 1.121 | 5.79 | 0.62 | 2 | True | 17.559 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.084 | 0.804 | 1.173 | 0.957 | 647/649 | 0.997 | 0.956 | 5.468 | 0.868 | 2 | True | 31.986 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 317.071 | - | - | 0 | 159/173 | 0.919 | 1.399 | 5.202 | 1.903 | 0 | True | 24.474 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=159, totalMatches=173, error=317.071, tolerance=35 |
