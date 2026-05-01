# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:26:30.1510273+00:00`
Operator: `OrbFeatureMatch`
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
| Mean position error px | 46.698 |
| P95 position error px | 267.972 |
| P95 corner error px | 9.428 |
| Mean inliers | 362.224 |
| Mean score | 0.7177 |
| Runtime ms | 2959.422 |
| Max features | 2000 |
| Min inliers | 6 |
| Match ratio | 0.7 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.25 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.153 | 0.244 | 0.299 | 0.9637 | 523/535 | 0.978 | 0.462 | 3.971 | 1.001 | 4 | True | 162.518 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.155 | 0.76 | 1.061 | 0.9786 | 85/86 | 0.988 | 0.29 | 3.521 | 1 | 3 | True | 21.836 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.211 | 0.955 | 1.539 | 0.971 | 134/135 | 0.993 | 0.481 | 3.501 | 0.998 | 4 | True | 15.506 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.184 | 0.306 | 0.589 | 0.9899 | 801/806 | 0.994 | 0.129 | 4.185 | 0.999 | 4 | True | 21.828 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.303 | 0.32 | 0.452 | 0.9635 | 406/415 | 0.978 | 0.475 | 4.341 | 1 | 4 | True | 24.602 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.279 | 0.467 | 1.028 | 0.9506 | 295/300 | 0.983 | 0.776 | 4.211 | 1.001 | 3 | True | 25.403 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.659 | 3.774 | 7.58 | 0.9065 | 189/211 | 0.896 | 0.697 | 3.869 | 0.995 | 3 | True | 25.528 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.78 | 8.45 | 16.101 | 0.9189 | 10/11 | 0.909 | 0.599 | 1.566 | 0.97 | 3 | True | 21.156 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.475 | 2.288 | 5.798 | 0.9741 | 64/65 | 0.985 | 0.337 | 2.506 | 0.994 | 4 | True | 25.076 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.009 | 0.064 | 0.142 | 0.9961 | 1546/1546 | 1 | 0.075 | 4.214 | 1 | 4 | True | 31.485 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.174 | 1.51 | 2.343 | 0.9823 | 15/15 | 1 | 0.342 | 2.287 | 1.001 | 2 | True | 23.581 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.093 | 0.348 | 0.779 | 0.9694 | 125/126 | 0.992 | 0.506 | 3.659 | 1.001 | 4 | True | 26.726 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.122 | 1.528 | 4.577 | 0.9652 | 36/36 | 1 | 0.67 | 2.831 | 1.004 | 3 | True | 37.891 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.039 | 0.106 | 0.261 | 0.9875 | 357/358 | 0.997 | 0.211 | 4.618 | 1 | 4 | True | 24.112 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.123 | 0.143 | 0.216 | 0.9817 | 978/980 | 0.998 | 0.331 | 3.948 | 1 | 4 | True | 21.037 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.051 | 0.269 | 0.329 | 0.975 | 432/433 | 0.998 | 0.458 | 4.103 | 1 | 4 | True | 20.738 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.222 | 0.584 | 0.892 | 0.9765 | 101/102 | 0.99 | 0.349 | 3.235 | 1.001 | 4 | True | 19.218 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.035 | 0.062 | 0.119 | 0.9909 | 1052/1054 | 0.998 | 0.156 | 3.545 | 1 | 4 | True | 16.456 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.063 | 0.233 | 0.479 | 0.9878 | 794/795 | 0.999 | 0.222 | 4.097 | 1 | 4 | True | 21.041 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.086 | 0.693 | 1.277 | 0.98 | 74/74 | 1 | 0.386 | 3.573 | 1 | 3 | True | 22.613 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.026 | 0.087 | 0.201 | 0.9959 | 509/509 | 1 | 0.079 | 2.974 | 1 | 4 | True | 24.45 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.02 | 1.028 | 1.682 | 0.9309 | 138/151 | 0.914 | 0.42 | 3.68 | 0.999 | 3 | True | 23.129 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.985 | 2.338 | 3.017 | 0.8576 | 23/28 | 0.821 | 0.852 | 1.778 | 0.993 | 3 | True | 19.937 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.026 | 0.057 | 0.109 | 0.9922 | 1374/1383 | 0.993 | 0.081 | 3.591 | 1 | 4 | True | 18.701 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.091 | 0.114 | 0.243 | 0.9896 | 990/991 | 0.999 | 0.19 | 4.159 | 1 | 4 | True | 26.243 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 750/862 | 0.87 | 0.872 | 5.033 | 1.001 | 1 | True | 20.044 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=750, totalMatches=862, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 0.755 | 4.435 | 6.515 | 0.8992 | 17/18 | 0.944 | 1.354 | 3.188 | 1.002 | 2 | True | 17.618 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.815 | 1.097 | 1.571 | 0.7946 | 72/97 | 0.742 | 1.227 | 2.977 | 0.999 | 2 | True | 23.537 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 196.939 | - | - | 0 | 840/840 | 1 | 0.83 | 4.475 | 1.001 | 1 | True | 25.33 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=840, totalMatches=840, error=196.939, tolerance=35 |
| i_melon_1_2 | illumination | 1-2 | True | 0.034 | 0.103 | 0.143 | 0.9822 | 723/725 | 0.997 | 0.315 | 4.227 | 1 | 4 | True | 23.275 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.144 | 0.56 | 1.066 | 0.9661 | 519/534 | 0.972 | 0.356 | 3.622 | 1 | 4 | True | 25.551 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.029 | 0.242 | 0.357 | 0.9977 | 666/666 | 1 | 0.044 | 3.57 | 1 | 4 | True | 22.407 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.246 | 1.116 | 1.476 | 0.9496 | 148/150 | 0.987 | 0.831 | 4.97 | 1 | 3 | True | 25.899 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.33 | 0.674 | 1.108 | 0.936 | 88/93 | 0.946 | 0.665 | 4.442 | 0.999 | 3 | True | 15.225 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.906 | 0.712 | 1.04 | 0.9225 | 109/114 | 0.956 | 1.029 | 5.275 | 1 | 3 | True | 28.994 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.459 | 1.195 | 3.535 | 0.8851 | 226/260 | 0.869 | 0.829 | 6.4 | 0.997 | 4 | True | 17.919 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.012 | 0.081 | 0.189 | 0.993 | 1065/1067 | 0.998 | 0.115 | 4.157 | 1 | 4 | True | 20.309 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.974 | 1.12 | 3.155 | 0.8644 | 37/44 | 0.841 | 0.928 | 2.718 | 1.004 | 3 | True | 27.351 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.241 | 0.994 | 1.521 | 0.9727 | 325/330 | 0.985 | 0.365 | 4.79 | 0.998 | 4 | True | 20.095 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 0.5 | - | - | 0 | 0/0 | 0 | - | - | 0 | 0 | False | 17.015 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=0, error=0.5, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.12 | 0.156 | 0.309 | 0.9806 | 963/965 | 0.998 | 0.352 | 3.659 | 1 | 4 | True | 18.349 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.038 | 0.174 | 0.288 | 0.978 | 290/292 | 0.993 | 0.352 | 4.98 | 0.999 | 4 | True | 25.443 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.094 | 0.14 | 0.284 | 0.9771 | 718/730 | 0.984 | 0.267 | 3.528 | 1 | 4 | True | 26.729 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.889 | 2.058 | 2.364 | 0.9443 | 202/210 | 0.962 | 0.671 | 3.697 | 0.999 | 2 | True | 23.539 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.074 | 0.189 | 0.226 | 0.9782 | 426/426 | 1 | 0.421 | 3.667 | 1 | 4 | True | 18.458 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.203 | 0.549 | 1.134 | 0.9678 | 461/462 | 0.998 | 0.598 | 4.756 | 0.998 | 4 | True | 21.102 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.048 | 0.106 | 0.152 | 0.9858 | 524/527 | 0.994 | 0.213 | 4.073 | 1 | 4 | True | 24.829 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.287 | 0.653 | 1.153 | 0.9653 | 686/687 | 0.999 | 0.654 | 3.717 | 1 | 4 | True | 20.507 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.906 | 0.712 | 1.04 | 0.9225 | 109/114 | 0.956 | 1.029 | 5.275 | 1 | 3 | True | 28.754 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 10.766 | 67.883 | 118.914 | 0.8553 | 11/14 | 0.786 | 0.519 | 0.908 | 0.917 | 3 | True | 22.281 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.091 | 0.224 | 0.284 | 0.9838 | 1018/1023 | 0.995 | 0.261 | 4.937 | 1 | 4 | True | 19.022 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.88 | 1.251 | 2.375 | 0.9019 | 118/132 | 0.894 | 0.768 | 2.968 | 0.996 | 4 | True | 12.939 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.119 | 0.361 | 0.81 | 0.9724 | 566/568 | 0.996 | 0.495 | 3.875 | 1.001 | 4 | True | 24.707 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.569 | 3.573 | 9.428 | 0.9694 | 19/19 | 1 | 0.591 | 1.976 | 1.017 | 2 | True | 19.588 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.027 | 0.056 | 0.09 | 0.9941 | 1347/1348 | 0.999 | 0.106 | 3.555 | 1 | 4 | True | 27.838 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.288 | 0.449 | 0.501 | 0.9499 | 594/619 | 0.96 | 0.539 | 4.004 | 0.999 | 4 | True | 26.854 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.417 | 0.31 | 0.484 | 0.9651 | 410/411 | 0.998 | 0.647 | 4.721 | 1 | 4 | True | 25.151 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156 | - | - | 0 | 330/366 | 0.902 | 1.025 | 4.861 | 1.014 | 0 | True | 25.824 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=330, totalMatches=366, error=279.156, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.197 | 2.438 | 3.988 | 0.9292 | 313/323 | 0.969 | 1.037 | 4.032 | 0.619 | 2 | True | 15.086 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.111 | 0.864 | 1.185 | 0.9493 | 582/584 | 0.997 | 0.941 | 4.054 | 0.868 | 2 | True | 30.646 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.531 | 0.924 | 1.241 | 0.9366 | 169/172 | 0.983 | 1.037 | 3.367 | 0.73 | 4 | True | 23.72 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.23 | - | - | 0 | 127/134 | 0.948 | 1.39 | 5.076 | 1.895 | 0 | True | 24.735 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=127, totalMatches=134, error=307.23, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 116.339 | - | - | 0 | 124/130 | 0.954 | 1.288 | 5.092 | 1.014 | 1 | True | 32.422 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=124, totalMatches=130, error=116.339, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 260.984 | - | - | 0 | 218/229 | 0.952 | 1.244 | 4.229 | 0.662 | 1 | True | 22.635 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=218, totalMatches=229, error=260.984, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.079 | 0.58 | 0.949 | 0.8763 | 476/546 | 0.872 | 1.026 | 5.273 | 1.095 | 2 | True | 25.751 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.224 | 4.553 | 8.462 | 0.8392 | 28/34 | 0.824 | 1.23 | 3.146 | 0.386 | 3 | True | 21.818 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.248 | 0.561 | 0.933 | 0.9485 | 443/445 | 0.996 | 0.945 | 2.97 | 0.461 | 4 | True | 23.471 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.492 | 0.944 | 1.639 | 0.9212 | 277/295 | 0.939 | 0.873 | 3.468 | 0.351 | 3 | True | 26.182 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.44 | 0.896 | 1.086 | 0.9314 | 113/114 | 0.991 | 0.716 | 2.026 | 0.183 | 4 | True | 26.059 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.81 | 2.459 | 3.772 | 0.8616 | 327/382 | 0.856 | 1.142 | 3.942 | 0.717 | 3 | True | 29.931 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 206 | - | - | 0 | 301/350 | 0.86 | 1.076 | 4.886 | 0.782 | 1 | True | 30.133 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=301, totalMatches=350, error=206, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 217.228 | - | - | 0 | 324/362 | 0.895 | 0.91 | 4.508 | 1.123 | 1 | True | 30.148 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=324, totalMatches=362, error=217.228, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 227.419 | - | - | 0 | 322/325 | 0.991 | 1.083 | 4.096 | 0.843 | 1 | True | 33.402 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=322, totalMatches=325, error=227.419, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.385 | 2.578 | 5.342 | 0.8698 | 75/88 | 0.852 | 0.943 | 2.565 | 0.531 | 4 | True | 24.572 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.199 | 0.46 | 0.735 | 0.9482 | 649/654 | 0.992 | 0.919 | 4.235 | 0.742 | 4 | True | 29.848 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 338.707 | - | - | 0 | 10/19 | 0.526 | 0.717 | 1.134 | 2.098 | 1 | True | 24.659 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=10, totalMatches=19, error=338.707, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 143.408 | - | - | 0 | 189/218 | 0.867 | 1.264 | 5.119 | 1.766 | 1 | True | 23.168 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=189, totalMatches=218, error=143.408, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.189 | 0.759 | 1.038 | 0.8829 | 231/264 | 0.875 | 0.932 | 3.256 | 0.587 | 4 | True | 26.209 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.513 | 3.088 | 5.778 | 0.9293 | 101/103 | 0.981 | 1.157 | 4.567 | 0.702 | 2 | True | 25.914 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.875 | - | - | 0 | 35/36 | 0.972 | 1.229 | 3.993 | 1.132 | 1 | True | 16.316 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=35, totalMatches=36, error=243.875, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 119.45 | - | - | 0 | 560/570 | 0.982 | 0.955 | 4.017 | 1.022 | 0 | True | 25.754 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=560, totalMatches=570, error=119.45, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.029 | 0.719 | 0.934 | 0.9124 | 367/397 | 0.924 | 0.889 | 5.976 | 0.681 | 3 | True | 30.042 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.404 | 2.157 | 3.764 | 0.9011 | 232/247 | 0.939 | 1.263 | 4.834 | 1.323 | 2 | True | 20.808 | - | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.126 | 0.529 | 0.584 | 0.9513 | 790/791 | 0.999 | 0.926 | 6.324 | 0.711 | 4 | True | 30.782 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.039 | 0.946 | 1.195 | 0.8511 | 393/483 | 0.814 | 0.894 | 3.646 | 0.463 | 4 | True | 29.851 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 157.731 | - | - | 0 | 298/348 | 0.856 | 1.038 | 5.523 | 1.365 | 0 | True | 27.235 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=298, totalMatches=348, error=157.731, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.218 | 2.593 | 3.494 | 0.8389 | 123/149 | 0.826 | 1.257 | 4.616 | 1.283 | 2 | True | 25.466 | - | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 97.944 | - | - | 0 | 453/545 | 0.831 | 0.936 | 4.487 | 1.16 | 0 | True | 23.672 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=453, totalMatches=545, error=97.944, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.23 | 1.4 | 1.86 | 0.9064 | 348/372 | 0.935 | 1.121 | 4.551 | 0.738 | 2 | True | 25.418 | - | - |
| v_home_1_2 | viewpoint | 1-2 | True | 2.573 | 10.26 | 17.249 | 0.8383 | 98/120 | 0.817 | 1.173 | 3.115 | 0.503 | 3 | True | 25.952 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 132.914 | - | - | 0 | 636/637 | 0.998 | 0.931 | 3.91 | 0.764 | 1 | True | 20.792 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=636, totalMatches=637, error=132.914, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 248.894 | - | - | 0 | 470/471 | 0.998 | 1.059 | 3.695 | 1.193 | 0 | True | 29.134 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=470, totalMatches=471, error=248.894, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.316 | 2.182 | 3.517 | 0.8603 | 161/184 | 0.875 | 1.369 | 4.683 | 1.046 | 2 | True | 25.032 | - | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.358 | 2.84 | 4.99 | 0.8982 | 180/193 | 0.933 | 1.248 | 5.921 | 1.045 | 2 | True | 24.565 | - | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.279 | 3.461 | 6.051 | 0.915 | 105/111 | 0.946 | 1.066 | 2.877 | 0.843 | 3 | True | 20.184 | - | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 234.57 | - | - | 0 | 257/268 | 0.959 | 1.486 | 5.255 | 0.96 | 1 | True | 27.712 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=257, totalMatches=268, error=234.57, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.465 | 1.435 | 2.134 | 0.9255 | 239/253 | 0.945 | 0.85 | 2.547 | 0.276 | 4 | True | 31.836 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.246 | 0.443 | 0.74 | 0.9248 | 260/274 | 0.949 | 0.908 | 3.136 | 0.417 | 4 | True | 27.948 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 267.972 | - | - | 0 | 113/115 | 0.983 | 1.094 | 4.378 | 1.115 | 0 | True | 21.398 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=113, totalMatches=115, error=267.972, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 19.469 | - | - | 0 | 1015/1017 | 0.998 | 0.863 | 4.522 | 1.012 | 1 | True | 25.332 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1015, totalMatches=1017, error=19.469, tolerance=35 |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.145 | 0.614 | 0.845 | 0.9097 | 725/792 | 0.915 | 0.844 | 3.521 | 0.971 | 2 | True | 31.015 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 189.489 | - | - | 0 | 84/95 | 0.884 | 1.134 | 4.534 | 0.867 | 1 | True | 30.114 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=84, totalMatches=95, error=189.489, tolerance=35 |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.612 | 2.372 | 4.567 | 0.8069 | 173/230 | 0.752 | 1.095 | 4.635 | 0.9 | 4 | True | 25.167 | - | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.147 | 0.51 | 0.79 | 0.9381 | 448/459 | 0.976 | 0.939 | 4.601 | 0.515 | 4 | True | 21.689 | - | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.475 | 1.461 | 2.418 | 0.9074 | 89/95 | 0.937 | 1.115 | 3.49 | 0.762 | 3 | True | 17.446 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 177.838 | - | - | 0 | 497/507 | 0.98 | 0.854 | 3.407 | 1.12 | 0 | True | 28.579 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=497, totalMatches=507, error=177.838, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 73.318 | - | - | 0 | 178/196 | 0.908 | 1.002 | 4.479 | 0.831 | 1 | True | 30.321 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=178, totalMatches=196, error=73.318, tolerance=35 |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.103 | 1.544 | 2.052 | 0.9403 | 394/396 | 0.995 | 1.097 | 3.945 | 0.986 | 2 | True | 20.115 | - | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.373 | 1.216 | 2.302 | 0.9111 | 172/183 | 0.94 | 1.077 | 3.506 | 0.9 | 2 | True | 31.89 | - | - |
| v_war_1_2 | viewpoint | 1-2 | True | 2.476 | 3.061 | 4.267 | 0.8984 | 67/71 | 0.944 | 1.361 | 3.684 | 0.758 | 2 | True | 25.482 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 358.203 | - | - | 0 | 355/430 | 0.826 | 0.972 | 5.005 | 1.004 | 0 | True | 25.567 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=355, totalMatches=430, error=358.203, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.511 | 1.338 | 2.041 | 0.9549 | 102/102 | 1 | 0.87 | 3.036 | 0.369 | 4 | True | 22.866 | - | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.566 | 2.057 | 2.847 | 0.9342 | 220/222 | 0.991 | 1.174 | 4.139 | 0.687 | 2 | True | 24.477 | - | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.424 | 9.059 | 17.224 | 0.8747 | 117/130 | 0.9 | 1.356 | 4.63 | 1.202 | 2 | True | 23.916 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 219.078 | - | - | 0 | 151/153 | 0.987 | 1.026 | 3.299 | 0.845 | 1 | True | 28.627 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=151, totalMatches=153, error=219.078, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 276.367 | - | - | 0 | 286/303 | 0.944 | 0.981 | 3.637 | 1.009 | 1 | True | 23.119 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=286, totalMatches=303, error=276.367, tolerance=35 |
