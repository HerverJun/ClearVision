# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:17:14.8875446+00:00`
Operator: `PlanarMatching`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 80 |
| Passed | 60 |
| Failed | 20 |
| Pass rate | 0.75 |
| Mean position error px | 25022.379 |
| P95 position error px | 101.637 |
| P95 corner error px | 35.478 |
| Mean inliers | 184.088 |
| Mean score | 0.6762 |
| Runtime ms | 4408.856 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.304 | 0.518 | 0.902 | 0.8021 | 291/300 | 0.97 | 0.293 | 3.431 | 1.001 | 4 | True | 208.299 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 9.197 | 12.946 | 30.608 | 0.7466 | 22/22 | 1 | 0.639 | 4.295 | 0.97 | 2 | True | 47.773 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.588 | 0.928 | 1.329 | 0.754 | 108/112 | 0.964 | 0.634 | 3.46 | 1.002 | 3 | True | 33.07 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.1 | 0.583 | 0.759 | 0.8222 | 300/300 | 1 | 0.124 | 2.783 | 0.695 | 4 | True | 45.824 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.524 | 0.803 | 1.59 | 0.7855 | 275/282 | 0.975 | 0.59 | 3.512 | 1.001 | 3 | True | 58.194 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.38 | 0.824 | 1.258 | 0.7744 | 218/220 | 0.991 | 0.667 | 3.292 | 0.696 | 3 | True | 58.476 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.17 | 1.06 | 1.468 | 0.7618 | 172/173 | 0.994 | 0.747 | 3.924 | 0.996 | 4 | True | 57.888 | - | - |
| i_castle_1_2 | illumination | 1-2 | False | 1000000 | - | - | 0.3033 | 0/4 | 0 | 0 | 0 | 0 | 0 | False | 51.461 | Insufficient feature matches (4 < 6). | isMatch=False, score=0.303, inliers=0, totalMatches=4, error=1000000, tolerance=35 |
| i_chestnuts_1_2 | illumination | 1-2 | True | 3.498 | 4.659 | 9.249 | 0.7394 | 37/38 | 0.974 | 0.404 | 1.895 | 0.993 | 2 | True | 58.441 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8282 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 63.298 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 17.93 | 24.784 | 46.512 | 0.7177 | 13/14 | 0.929 | 0.49 | 1.237 | 1.045 | 2 | True | 52.856 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.233 | 0.615 | 1.085 | 0.7522 | 82/83 | 0.988 | 0.526 | 2.646 | 1.001 | 4 | True | 54.635 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 1.674 | 2.163 | 3.292 | 0.735 | 29/29 | 1 | 0.782 | 3.085 | 0.695 | 2 | True | 64.436 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.186 | 0.277 | 0.458 | 0.7823 | 206/211 | 0.976 | 0.307 | 4.638 | 1 | 4 | True | 52.227 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.019 | 0.13 | 0.186 | 0.8178 | 300/300 | 1 | 0.173 | 3.493 | 1 | 4 | True | 46.808 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.908 | 1.594 | 2.17 | 0.7933 | 300/300 | 1 | 0.631 | 4.367 | 1.002 | 2 | True | 45.356 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.671 | 0.896 | 1.507 | 0.7575 | 46/46 | 1 | 0.369 | 2.188 | 1 | 3 | True | 39.06 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.018 | 0.043 | 0.061 | 0.8297 | 300/300 | 1 | 0.023 | 1.184 | 1 | 4 | True | 36.05 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.018 | 0.131 | 0.194 | 0.821 | 300/300 | 1 | 0.074 | 2.928 | 0.999 | 4 | True | 46.478 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 2.836 | 4.272 | 7.918 | 0.7346 | 47/49 | 0.959 | 0.406 | 1.881 | 1.016 | 2 | True | 53.407 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.025 | 0.074 | 0.132 | 0.8154 | 299/300 | 0.997 | 0.109 | 2.964 | 1 | 4 | True | 53.913 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.202 | 0.598 | 0.827 | 0.7575 | 127/128 | 0.992 | 0.622 | 3.82 | 0.999 | 4 | True | 52.618 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 15.496 | 23.663 | 35.478 | 0.6329 | 14/18 | 0.778 | 1.392 | 3.871 | 0.817 | 2 | True | 49.012 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8276 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 48.069 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.019 | 0.033 | 0.074 | 0.8242 | 300/300 | 1 | 0.013 | 0.997 | 1 | 4 | True | 66.669 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 1.043 | 1.328 | 3.205 | 0.1767 | 300/300 | 1 | 0.663 | 2.844 | 1.001 | 1 | True | 47.16 | Projected quadrilateral is invalid. | isMatch=False, score=0.177, inliers=300, totalMatches=300, error=1.043, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 4.459 | 5.307 | 9.862 | 0.6954 | 23/25 | 0.92 | 1.139 | 2.751 | 0.693 | 3 | True | 40.873 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 2.124 | 2.805 | 5.242 | 0.6202 | 55/82 | 0.671 | 1.014 | 3.159 | 0.699 | 2 | True | 60.029 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 1.767 | 1.849 | 2.08 | 0.7955 | 296/300 | 0.987 | 0.643 | 3.548 | 1.001 | 2 | True | 60.264 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.287 | 0.418 | 0.68 | 0.8104 | 300/300 | 1 | 0.34 | 3.617 | 1 | 4 | True | 53.576 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.444 | 0.533 | 1.133 | 0.8049 | 296/300 | 0.987 | 0.261 | 3.561 | 1 | 4 | True | 62.772 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.287 | 0.474 | 0.651 | 0.8247 | 300/300 | 1 | 0.026 | 1.174 | 0.999 | 4 | True | 52.02 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 0.732 | 1.3 | 1.87 | 0.7276 | 77/80 | 0.963 | 0.954 | 3.865 | 0.999 | 3 | True | 59.19 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.69 | 1.084 | 1.643 | 0.7234 | 66/71 | 0.93 | 0.795 | 3.875 | 1.001 | 3 | True | 29.902 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 1.741 | 1.869 | 3.65 | 0.723 | 59/60 | 0.983 | 1.145 | 3.066 | 0.691 | 4 | True | 66.992 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.201 | 0.284 | 0.403 | 0.7683 | 172/176 | 0.977 | 0.576 | 4.893 | 0.999 | 4 | True | 39.126 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.008 | 0.02 | 0.039 | 0.8253 | 300/300 | 1 | 0.007 | 0.995 | 1 | 4 | True | 44.353 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.929 | 1.085 | 3.499 | 0.6182 | 22/31 | 0.71 | 1.186 | 3.686 | 0.83 | 3 | True | 62.297 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.813 | 2.318 | 3.759 | 0.7632 | 181/182 | 0.995 | 0.9 | 4.987 | 0.69 | 4 | True | 44.772 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 1000000 | - | - | 0.316 | 0/3 | 0 | 0 | 0 | 0 | 0 | False | 34.473 | Insufficient feature matches (3 < 6). | isMatch=False, score=0.316, inliers=0, totalMatches=3, error=1000000, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.068 | 0.166 | 0.308 | 0.8209 | 300/300 | 1 | 0.178 | 3.555 | 0.999 | 4 | True | 39.282 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.135 | 0.415 | 0.59 | 0.7848 | 229/229 | 1 | 0.543 | 4.804 | 0.999 | 4 | True | 59.435 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.048 | 0.09 | 0.123 | 0.8119 | 296/300 | 0.987 | 0.128 | 3.512 | 1 | 4 | True | 63.611 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 2.008 | 2.171 | 2.687 | 0.7474 | 102/105 | 0.971 | 0.684 | 3.43 | 0.693 | 2 | True | 53.702 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.182 | 0.286 | 0.719 | 0.8007 | 281/281 | 1 | 0.459 | 3.646 | 1.001 | 4 | True | 41.598 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.557 | 1.249 | 2.036 | 0.7939 | 299/300 | 0.997 | 0.639 | 5.021 | 0.996 | 4 | True | 46.787 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.111 | 0.163 | 0.254 | 0.8122 | 300/300 | 1 | 0.251 | 4.156 | 1 | 4 | True | 55.738 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.718 | 1.058 | 1.922 | 0.8139 | 300/300 | 1 | 0.379 | 2.694 | 0.693 | 4 | True | 47.103 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 1.741 | 1.869 | 3.65 | 0.723 | 59/60 | 0.983 | 1.145 | 3.066 | 0.691 | 4 | True | 67.075 | - | - |
| i_tools_1_2 | illumination | 1-2 | False | 101.637 | 111.49 | 261.373 | 0.6299 | 7/10 | 0.7 | 0.465 | 1.263 | 0.806 | 2 | True | 53.494 | - | isMatch=True, score=0.63, inliers=7, totalMatches=10, error=101.637, tolerance=35 |
| i_toy_1_2 | illumination | 1-2 | True | 0.25 | 0.724 | 1.245 | 0.819 | 300/300 | 1 | 0.127 | 2.777 | 1.002 | 3 | True | 39.764 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 1.256 | 2.309 | 5.053 | 0.7756 | 103/103 | 1 | 0.777 | 4.891 | 0.689 | 4 | True | 29.621 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.475 | 0.683 | 1.487 | 0.8022 | 300/300 | 1 | 0.532 | 3.552 | 1.002 | 3 | True | 55.085 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 3.805 | 4.967 | 11.701 | 0.7158 | 25/26 | 0.962 | 0.889 | 3.509 | 1.019 | 2 | True | 41.511 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8273 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 53.625 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.091 | 0.245 | 0.361 | 0.7883 | 283/300 | 0.943 | 0.36 | 3.79 | 0.999 | 4 | True | 58.254 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.118 | 0.359 | 0.405 | 0.7861 | 274/276 | 0.993 | 0.713 | 3.675 | 0.999 | 4 | True | 52.807 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.018 | 1.546 | 2.011 | 0.1654 | 273/288 | 0.948 | 1.108 | 4.491 | 0.839 | 0 | True | 52.739 | Projected quadrilateral is invalid. | isMatch=False, score=0.165, inliers=273, totalMatches=288, error=66.018, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | False | 44.549 | 2.356 | 5.117 | 0.7693 | 148/149 | 0.993 | 0.915 | 3.537 | 0.509 | 2 | True | 28.828 | - | isMatch=True, score=0.769, inliers=148, totalMatches=149, error=44.549, tolerance=35 |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 14.844 | 0.847 | 1.286 | 0.7896 | 295/300 | 0.983 | 0.784 | 3.044 | 0.716 | 2 | True | 72.046 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | False | 97.094 | 0.955 | 2.256 | 0.7428 | 133/137 | 0.971 | 0.937 | 4.336 | 0.731 | 4 | True | 53.943 | - | isMatch=True, score=0.743, inliers=133, totalMatches=137, error=97.094, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 52.733 | 3.35 | 8.995 | 0.1335 | 117/131 | 0.893 | 1.188 | 4.45 | 1.317 | 0 | True | 54.47 | Projected quadrilateral is invalid. | isMatch=False, score=0.134, inliers=117, totalMatches=131, error=52.733, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 80.985 | 6.397 | 7.34 | 0.1318 | 102/136 | 0.75 | 1.186 | 4.43 | 0.84 | 1 | True | 75.788 | Projected quadrilateral is invalid. | isMatch=False, score=0.132, inliers=102, totalMatches=136, error=80.985, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 0.342 | 1.481 | 3.413 | 0.1359 | 129/141 | 0.915 | 1.1 | 3.711 | 0.662 | 1 | True | 48.366 | Projected quadrilateral is invalid. | isMatch=False, score=0.136, inliers=129, totalMatches=141, error=0.342, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | False | 68.838 | 0.443 | 0.923 | 0.7894 | 300/300 | 1 | 0.896 | 3.212 | 0.904 | 2 | True | 56.043 | - | isMatch=True, score=0.789, inliers=300, totalMatches=300, error=68.838, tolerance=35 |
| v_beyus_1_2 | viewpoint | 1-2 | True | 32.759 | 2.592 | 3.01 | 0.693 | 52/57 | 0.912 | 1.298 | 3.631 | 0.47 | 3 | True | 48.768 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.479 | 0.644 | 1.23 | 0.7523 | 155/160 | 0.969 | 0.855 | 2.183 | 0.32 | 4 | True | 53.251 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | False | 35.076 | 1.044 | 1.344 | 0.7729 | 264/267 | 0.989 | 0.954 | 4.47 | 0.547 | 3 | True | 58.39 | - | isMatch=True, score=0.773, inliers=264, totalMatches=267, error=35.076, tolerance=35 |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 15.141 | 0.847 | 1.815 | 0.7429 | 81/82 | 0.988 | 0.816 | 2.707 | 0.285 | 4 | True | 56.712 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | False | 70.36 | 1.642 | 4.531 | 0.7319 | 257/294 | 0.874 | 1.082 | 3.412 | 0.718 | 3 | True | 65.159 | - | isMatch=True, score=0.732, inliers=257, totalMatches=294, error=70.36, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | False | 0.696 | 1.4 | 1.829 | 0.1699 | 300/300 | 1 | 0.993 | 4.459 | 0.962 | 1 | True | 73.374 | Projected quadrilateral is invalid. | isMatch=False, score=0.17, inliers=300, totalMatches=300, error=0.696, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 25.256 | 0.953 | 1.736 | 0.1654 | 285/300 | 0.95 | 0.94 | 4.21 | 0.928 | 1 | True | 69.211 | Projected quadrilateral is invalid. | isMatch=False, score=0.165, inliers=285, totalMatches=300, error=25.256, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 64.732 | 3.802 | 9.966 | 0.1589 | 226/278 | 0.813 | 0.99 | 4.441 | 0.692 | 1 | True | 76.403 | Projected quadrilateral is invalid. | isMatch=False, score=0.159, inliers=226, totalMatches=278, error=64.732, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 28.448 | 0.722 | 1.177 | 0.7325 | 99/100 | 0.99 | 1.223 | 5.601 | 0.826 | 4 | True | 54.532 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 12.315 | 0.921 | 1.969 | 0.7867 | 299/300 | 0.997 | 0.952 | 4.508 | 0.914 | 4 | True | 70.03 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 667.398 | 548.741 | 1549.725 | 0.5902 | 13/21 | 0.619 | 1.008 | 2.233 | 3.728 | 2 | True | 72.949 | - | isMatch=True, score=0.59, inliers=13, totalMatches=21, error=667.398, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 118.03 | 5.664 | 9.281 | 0.1483 | 207/209 | 0.99 | 1.197 | 5.393 | 1.228 | 1 | True | 52.919 | Projected quadrilateral is invalid. | isMatch=False, score=0.148, inliers=207, totalMatches=209, error=118.03, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 4.474 | 0.753 | 1.273 | 0.7751 | 285/300 | 0.95 | 0.799 | 3.514 | 0.724 | 4 | True | 59.729 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | False | 51.966 | 3.657 | 7.893 | 0.7307 | 82/83 | 0.988 | 1.128 | 3.916 | 0.582 | 2 | True | 58.311 | - | isMatch=True, score=0.731, inliers=82, totalMatches=83, error=51.966, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 54.064 | 1.233 | 2 | 0.1295 | 34/42 | 0.81 | 0.983 | 3.744 | 0.785 | 1 | True | 36.286 | Projected quadrilateral is invalid. | isMatch=False, score=0.129, inliers=34, totalMatches=42, error=54.064, tolerance=35 |
