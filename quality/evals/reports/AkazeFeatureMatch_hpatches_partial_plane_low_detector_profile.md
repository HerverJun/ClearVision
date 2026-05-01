# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:25:35.3741023+00:00`
Operator: `AkazeFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 116 |
| Passed | 89 |
| Failed | 27 |
| Pass rate | 0.7672 |
| Mean position error px | 55.952 |
| P95 position error px | 317.262 |
| P95 corner error px | 5.208 |
| Mean inliers | 646.81 |
| Mean score | 0.7223 |
| Runtime ms | 7505.647 |
| Max features | 2000 |
| Min inliers | 4 |
| Match ratio | 0.88 |
| RANSAC threshold px | 10 |
| Min inlier ratio | 0.1 |
| Detector type | ORB |
| Score threshold | 0.5 |
| Multi-scale | True |
| Scale range | 0.2 |
| ORB FAST threshold | 20 |
| ORB edge threshold | 15 |
| AKAZE detector threshold | 0.0005 |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.345 | 0.543 | 0.928 | 0.9784 | 516/523 | 0.987 | 0.549 | 7.732 | 1 | 4 | True | 107.688 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.11 | 0.527 | 0.757 | 0.9763 | 199/203 | 0.98 | 0.495 | 4.078 | 1.001 | 4 | True | 60.335 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.601 | 10.022 | 20.392 | 0.9362 | 68/73 | 0.932 | 1.009 | 5.511 | 1.01 | 2 | True | 38.091 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.299 | 1.594 | 3.342 | 0.9749 | 427/433 | 0.986 | 0.673 | 9.634 | 0.996 | 4 | True | 64.49 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.643 | 0.486 | 0.898 | 0.9593 | 998/1026 | 0.973 | 0.99 | 8.595 | 0.999 | 4 | True | 81.57 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.582 | 0.811 | 1.269 | 0.9514 | 885/917 | 0.965 | 1.133 | 10.607 | 1.001 | 3 | True | 67.37 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.588 | 0.571 | 0.719 | 0.9398 | 587/628 | 0.935 | 0.937 | 10.03 | 0.999 | 4 | True | 71.626 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 0.432 | 1.598 | 3.29 | 0.744 | 96/160 | 0.6 | 1.39 | 7.36 | 1.002 | 3 | True | 51.372 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.238 | 0.404 | 0.511 | 0.9657 | 914/943 | 0.969 | 0.672 | 9.551 | 0.999 | 4 | True | 77.197 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.013 | 0.045 | 0.132 | 0.9956 | 1731/1731 | 1 | 0.169 | 4.575 | 1 | 4 | True | 78.094 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.155 | 4.568 | 10.014 | 0.7765 | 51/79 | 0.646 | 1.102 | 4.156 | 0.994 | 3 | True | 66.101 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.152 | 0.673 | 0.928 | 0.943 | 384/409 | 0.939 | 0.902 | 6.817 | 1 | 4 | True | 66.132 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.579 | 0.656 | 1.312 | 0.8963 | 135/156 | 0.865 | 1.144 | 6.938 | 1.001 | 3 | True | 75.031 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.076 | 0.071 | 0.193 | 0.974 | 579/593 | 0.976 | 0.502 | 8.228 | 1 | 4 | True | 64.435 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.294 | 0.248 | 0.299 | 0.9938 | 844/845 | 0.999 | 0.212 | 5.753 | 1 | 4 | True | 59.892 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.094 | 0.739 | 1.701 | 0.939 | 533/579 | 0.921 | 0.667 | 9.433 | 1.003 | 3 | True | 41.515 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.119 | 0.219 | 0.469 | 0.9687 | 230/240 | 0.958 | 0.324 | 4.303 | 1 | 4 | True | 38.865 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.109 | 0.149 | 0.23 | 0.9795 | 347/356 | 0.975 | 0.256 | 5.147 | 1 | 4 | True | 35.842 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.045 | 0.183 | 0.229 | 0.9792 | 865/882 | 0.981 | 0.392 | 8.739 | 1 | 4 | True | 45.512 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.099 | 0.546 | 0.734 | 0.9332 | 330/358 | 0.922 | 0.919 | 9.858 | 1.001 | 4 | True | 54.352 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.303 | 0.414 | 0.895 | 0.9704 | 780/805 | 0.969 | 0.483 | 7.846 | 0.999 | 4 | True | 76.942 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.031 | 0.698 | 1.203 | 0.9304 | 431/470 | 0.917 | 0.926 | 7.797 | 0.998 | 4 | True | 57.289 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.357 | 1.394 | 1.996 | 0.8194 | 171/235 | 0.728 | 1.188 | 8.789 | 0.997 | 3 | True | 51.827 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.147 | 0.227 | 0.322 | 0.984 | 984/999 | 0.985 | 0.297 | 5.941 | 0.999 | 4 | True | 46.31 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.176 | 0.254 | 0.411 | 0.984 | 1319/1328 | 0.993 | 0.472 | 8.129 | 1.001 | 4 | True | 67.681 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 376.317 | - | - | 0 | 438/441 | 0.993 | 0.349 | 7.158 | 1 | 1 | True | 57.73 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=438, totalMatches=441, error=376.317, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 1.366 | 1.106 | 1.683 | 0.8804 | 118/136 | 0.868 | 1.804 | 7.503 | 1 | 3 | True | 42.199 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.915 | 1.186 | 1.748 | 0.9198 | 546/610 | 0.895 | 0.869 | 8.307 | 1.001 | 2 | True | 57.941 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 1.971 | 1.812 | 1.99 | 0.9875 | 1212/1214 | 0.998 | 0.446 | 6.076 | 1.001 | 2 | True | 64.458 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.217 | 0.747 | 1.03 | 0.9788 | 827/829 | 0.998 | 0.767 | 8.678 | 1.001 | 4 | True | 64.628 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.585 | 0.856 | 1.392 | 0.9632 | 1154/1185 | 0.974 | 0.863 | 9.24 | 1 | 4 | True | 77.622 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.032 | 0.843 | 1.703 | 0.9684 | 895/929 | 0.963 | 0.443 | 9.011 | 0.999 | 4 | True | 72.12 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.7 | 1.917 | 5.208 | 0.9437 | 692/733 | 0.944 | 0.985 | 10.832 | 1.003 | 3 | True | 70.844 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.943 | 3.955 | 7.42 | 0.8689 | 164/188 | 0.872 | 2.348 | 9.162 | 0.998 | 2 | True | 34.885 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.93 | 0.764 | 1.377 | 0.9136 | 600/653 | 0.919 | 1.613 | 11.645 | 1 | 4 | True | 78.92 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.231 | 0.445 | 0.984 | 0.9435 | 287/306 | 0.938 | 0.862 | 6.044 | 1 | 4 | True | 37.215 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.024 | 0.235 | 0.333 | 0.984 | 635/646 | 0.983 | 0.255 | 6.23 | 1 | 4 | True | 56.655 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.976 | 1.621 | 2.737 | 0.8812 | 438/494 | 0.887 | 2.178 | 10.974 | 1.002 | 2 | True | 71.994 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.399 | 1.363 | 2.583 | 0.9515 | 359/373 | 0.962 | 1.075 | 13.493 | 0.997 | 4 | True | 59.165 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 331.064 | - | - | 0 | 4/8 | 0.5 | 0 | 0 | 1.341 | 2 | True | 50.284 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=4, totalMatches=8, error=331.064, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.12 | 0.772 | 2.017 | 0.9351 | 376/405 | 0.928 | 0.986 | 8.139 | 0.999 | 4 | True | 41.515 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.141 | 0.633 | 1.035 | 0.9575 | 659/688 | 0.958 | 0.747 | 8.24 | 0.999 | 4 | True | 66.043 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.619 | 0.584 | 0.862 | 0.96 | 1375/1424 | 0.966 | 0.813 | 10.318 | 1 | 4 | True | 76.872 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 2.035 | 1.89 | 2.425 | 0.9463 | 654/693 | 0.944 | 0.877 | 9.626 | 0.997 | 2 | True | 62.077 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.187 | 0.264 | 0.415 | 0.9828 | 367/368 | 0.997 | 0.608 | 8.399 | 1 | 4 | True | 41.315 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.232 | 0.444 | 0.629 | 0.959 | 891/928 | 0.96 | 0.735 | 8.896 | 1 | 4 | True | 45.344 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.65 | 0.877 | 2.17 | 0.9473 | 981/1026 | 0.956 | 1.104 | 10.261 | 1 | 4 | True | 71.006 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.571 | 0.811 | 1.375 | 0.9793 | 347/351 | 0.989 | 0.558 | 8.976 | 1 | 4 | True | 52.719 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.93 | 0.764 | 1.377 | 0.9136 | 600/653 | 0.919 | 1.613 | 11.645 | 1 | 4 | True | 74.818 | - | - |
| i_tools_1_2 | illumination | 1-2 | False | 292.606 | - | - | 0 | 41/93 | 0.441 | 2.163 | 8.183 | 1.034 | 1 | True | 61.413 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=41, totalMatches=93, error=292.606, tolerance=35 |
| i_toy_1_2 | illumination | 1-2 | True | 0.177 | 0.414 | 1.109 | 0.9685 | 427/433 | 0.986 | 0.922 | 7.25 | 1.001 | 4 | True | 33.991 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 1.061 | 1.833 | 2.491 | 0.9552 | 128/134 | 0.955 | 0.78 | 8.674 | 1.001 | 2 | True | 38.719 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.067 | 0.31 | 0.398 | 0.9713 | 1436/1455 | 0.987 | 0.832 | 9.781 | 1.001 | 4 | True | 71.362 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.502 | 1.573 | 2.653 | 0.8646 | 147/182 | 0.808 | 1.144 | 5.677 | 0.999 | 3 | True | 36.308 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.063 | 0.126 | 0.226 | 0.9949 | 691/691 | 1 | 0.197 | 7.969 | 1 | 4 | True | 67.129 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.484 | 0.505 | 0.764 | 0.9699 | 1407/1440 | 0.977 | 0.673 | 9.088 | 1.001 | 4 | True | 73.886 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.367 | 0.419 | 0.685 | 0.9751 | 737/745 | 0.989 | 0.734 | 7.483 | 1 | 4 | True | 60.814 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 315.718 | - | - | 0 | 879/897 | 0.98 | 0.956 | 8.14 | 1.014 | 0 | True | 63.999 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=879, totalMatches=897, error=315.718, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.111 | 2.052 | 2.863 | 0.9456 | 139/147 | 0.946 | 0.944 | 3.748 | 0.617 | 2 | True | 31.339 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.068 | 0.533 | 0.581 | 0.9823 | 1524/1537 | 0.992 | 0.505 | 8.757 | 0.868 | 2 | True | 85.33 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.283 | 0.58 | 0.959 | 0.9608 | 589/611 | 0.964 | 0.75 | 7.848 | 0.729 | 4 | True | 58.05 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 219.776 | - | - | 0 | 485/503 | 0.964 | 1.393 | 10.452 | 1.906 | 0 | True | 55.954 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=485, totalMatches=503, error=219.776, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 254.382 | - | - | 0 | 640/716 | 0.894 | 1.615 | 13.565 | 1.014 | 1 | True | 86.563 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=640, totalMatches=716, error=254.382, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 148.245 | - | - | 0 | 429/460 | 0.933 | 0.886 | 5.372 | 0.666 | 1 | True | 61.612 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=429, totalMatches=460, error=148.245, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.099 | 0.64 | 1.135 | 0.9697 | 1154/1173 | 0.984 | 0.826 | 9.133 | 1.095 | 2 | True | 74.296 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.514 | 1.135 | 1.486 | 0.8802 | 158/185 | 0.854 | 1.523 | 8.897 | 0.383 | 4 | True | 57.885 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.043 | 0.321 | 0.362 | 0.9503 | 699/744 | 0.94 | 0.635 | 4.18 | 0.461 | 4 | True | 62.6 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.231 | 0.443 | 0.657 | 0.9276 | 613/674 | 0.909 | 0.872 | 7.233 | 0.352 | 3 | True | 75.991 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.233 | 1.126 | 1.413 | 0.7628 | 109/166 | 0.657 | 0.82 | 5.477 | 0.182 | 4 | True | 69.762 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.071 | 0.393 | 0.801 | 0.967 | 911/927 | 0.983 | 0.907 | 7.76 | 0.721 | 3 | True | 77.749 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 35.753 | - | - | 0 | 878/992 | 0.885 | 0.976 | 9.743 | 0.78 | 1 | True | 88.527 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=878, totalMatches=992, error=35.753, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 221.668 | - | - | 0 | 1383/1392 | 0.994 | 0.394 | 7.137 | 1.124 | 1 | True | 77.866 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1383, totalMatches=1392, error=221.668, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 150.087 | - | - | 0 | 1024/1047 | 0.978 | 0.839 | 9.122 | 0.843 | 1 | True | 88.564 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1024, totalMatches=1047, error=150.087, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.328 | 0.747 | 1.094 | 0.9244 | 460/498 | 0.924 | 1.296 | 11.571 | 0.528 | 4 | True | 67.844 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.082 | 0.273 | 0.488 | 0.9694 | 984/1007 | 0.977 | 0.694 | 7.807 | 0.743 | 4 | True | 76.878 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 378.904 | - | - | 0 | 108/156 | 0.692 | 2.061 | 9.056 | 2.443 | 1 | True | 64.601 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=108, totalMatches=156, error=378.904, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 192.562 | - | - | 0 | 693/712 | 0.973 | 1.517 | 9.158 | 1.773 | 1 | True | 65.197 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=693, totalMatches=712, error=192.562, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.097 | 0.462 | 0.846 | 0.9504 | 807/864 | 0.934 | 0.514 | 6.728 | 0.587 | 4 | True | 72.141 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.128 | 1.931 | 4.83 | 0.9303 | 645/698 | 0.924 | 1.078 | 9.744 | 0.702 | 2 | True | 69.284 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 223.246 | - | - | 0 | 200/210 | 0.952 | 1.069 | 6.863 | 1.133 | 1 | True | 53.52 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=200, totalMatches=210, error=223.246, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 152.575 | - | - | 0 | 1100/1125 | 0.978 | 0.906 | 10.553 | 1.023 | 0 | True | 74.486 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1100, totalMatches=1125, error=152.575, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.171 | 0.451 | 0.616 | 0.9687 | 915/941 | 0.972 | 0.622 | 8.869 | 0.681 | 3 | True | 73.091 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.167 | 1.597 | 4.141 | 0.955 | 727/746 | 0.975 | 1.194 | 9.58 | 1.325 | 2 | True | 55.486 | - | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.072 | 0.225 | 0.272 | 0.9684 | 877/897 | 0.978 | 0.747 | 6.324 | 0.711 | 4 | True | 85.007 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.134 | 0.495 | 1.186 | 0.9287 | 729/816 | 0.893 | 0.49 | 5.886 | 0.463 | 4 | True | 75.885 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 270.342 | - | - | 0 | 952/983 | 0.968 | 0.938 | 8.922 | 1.368 | 0 | True | 78.464 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=952, totalMatches=983, error=270.342, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.216 | 2.217 | 5.703 | 0.9113 | 497/551 | 0.902 | 1.342 | 8.399 | 1.275 | 2 | True | 73.863 | - | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 252.129 | - | - | 0 | 897/925 | 0.97 | 0.843 | 9.351 | 1.162 | 0 | True | 61.773 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=897, totalMatches=925, error=252.129, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.209 | 0.531 | 1.379 | 0.9611 | 855/871 | 0.982 | 1.112 | 10.926 | 0.737 | 2 | True | 76.84 | - | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.249 | 1.773 | 3.553 | 0.9209 | 389/430 | 0.905 | 1.027 | 7.905 | 0.489 | 3 | True | 70.429 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 202.934 | - | - | 0 | 723/725 | 0.997 | 0.781 | 7.852 | 0.763 | 1 | True | 58.072 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=723, totalMatches=725, error=202.934, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 137.909 | - | - | 0 | 981/1008 | 0.973 | 0.6 | 8.315 | 1.193 | 0 | True | 72.405 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=981, totalMatches=1008, error=137.909, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.623 | 0.961 | 1.404 | 0.938 | 754/785 | 0.961 | 1.552 | 9.291 | 1.041 | 2 | True | 71.631 | - | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.516 | 2.004 | 3.507 | 0.9524 | 689/711 | 0.969 | 1.18 | 10.309 | 1.053 | 2 | True | 66.734 | - | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.113 | 1.806 | 3.306 | 0.9603 | 258/266 | 0.97 | 0.893 | 7.458 | 0.843 | 3 | True | 52.957 | - | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 183.374 | - | - | 0 | 1088/1103 | 0.986 | 1.38 | 8.888 | 0.961 | 1 | True | 73.219 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1088, totalMatches=1103, error=183.374, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.166 | 0.561 | 0.614 | 0.9181 | 429/485 | 0.885 | 0.71 | 4.869 | 0.275 | 4 | True | 86.745 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.226 | 0.27 | 0.536 | 0.9633 | 902/929 | 0.971 | 0.801 | 9.211 | 0.418 | 4 | True | 85.708 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 315.881 | - | - | 0 | 161/168 | 0.958 | 1.238 | 8.419 | 1.114 | 0 | True | 57.772 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=161, totalMatches=168, error=315.881, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 375.891 | - | - | 0 | 1542/1550 | 0.995 | 0.309 | 6.931 | 1.012 | 1 | True | 70.635 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1542, totalMatches=1550, error=375.891, tolerance=35 |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.031 | 0.227 | 0.299 | 0.971 | 1449/1504 | 0.963 | 0.344 | 6.048 | 0.971 | 2 | True | 78.639 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 321.632 | - | - | 0 | 573/636 | 0.901 | 0.962 | 7.669 | 0.874 | 1 | True | 83.398 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=573, totalMatches=636, error=321.632, tolerance=35 |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.42 | 0.978 | 1.466 | 0.9208 | 472/527 | 0.896 | 0.841 | 12.288 | 0.896 | 4 | True | 51.604 | - | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.061 | 0.39 | 0.599 | 0.9657 | 379/391 | 0.969 | 0.673 | 7.802 | 0.515 | 4 | True | 60.425 | - | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.462 | 1.263 | 1.916 | 0.8893 | 199/226 | 0.881 | 1.737 | 13.952 | 0.762 | 3 | True | 52.569 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 129.671 | - | - | 0 | 838/883 | 0.949 | 0.541 | 8.081 | 1.12 | 0 | True | 73.62 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=838, totalMatches=883, error=129.671, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 171.697 | - | - | 0 | 842/925 | 0.91 | 0.687 | 5.907 | 0.829 | 1 | True | 83.07 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=842, totalMatches=925, error=171.697, tolerance=35 |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.067 | 1.342 | 1.818 | 0.9717 | 410/414 | 0.99 | 0.885 | 4.147 | 0.986 | 2 | True | 55.178 | - | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.068 | 0.251 | 0.342 | 0.9532 | 768/804 | 0.955 | 0.856 | 9.75 | 0.901 | 2 | True | 72.007 | - | - |
| v_war_1_2 | viewpoint | 1-2 | True | 1.015 | 2.124 | 4.4 | 0.8759 | 263/304 | 0.865 | 1.927 | 9.193 | 0.761 | 2 | True | 76.467 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 317.262 | - | - | 0 | 1141/1159 | 0.984 | 0.746 | 9.132 | 1.004 | 0 | True | 68.149 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1141, totalMatches=1159, error=317.262, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.202 | 0.525 | 0.997 | 0.9221 | 294/325 | 0.905 | 0.98 | 7.588 | 0.369 | 4 | True | 63.43 | - | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.22 | 2.199 | 3.112 | 0.9278 | 621/672 | 0.924 | 1.174 | 9.28 | 0.686 | 2 | True | 65.39 | - | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.131 | 0.774 | 0.955 | 0.9666 | 529/543 | 0.974 | 0.741 | 8.396 | 1.195 | 2 | True | 61.584 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 271.102 | - | - | 0 | 899/921 | 0.976 | 0.975 | 8.465 | 0.846 | 1 | True | 74.323 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=899, totalMatches=921, error=271.102, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 213.747 | - | - | 0 | 340/368 | 0.924 | 1.234 | 9.953 | 1.016 | 1 | True | 61.49 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=340, totalMatches=368, error=213.747, tolerance=35 |
