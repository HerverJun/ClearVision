# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:16:56.1164110+00:00`
Operator: `AkazeFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 80 |
| Passed | 67 |
| Failed | 13 |
| Pass rate | 0.8375 |
| Mean position error px | 44.87 |
| P95 position error px | 318.251 |
| P95 corner error px | 12.387 |
| Mean inliers | 312.125 |
| Mean score | 0.7809 |
| Runtime ms | 7376.165 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.345 | 0.447 | 0.711 | 0.9078 | 244/278 | 0.878 | 0.48 | 2.582 | 1 | 4 | True | 325.914 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.031 | 0.742 | 1.077 | 0.9663 | 96/98 | 0.98 | 0.433 | 2.527 | 1.001 | 4 | True | 86.747 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 1.114 | 14.408 | 27.321 | 0.9156 | 16/18 | 0.889 | 0.449 | 0.974 | 1.013 | 2 | True | 52.59 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.5 | 2.588 | 4.347 | 0.9665 | 286/291 | 0.983 | 0.465 | 6.134 | 0.994 | 3 | True | 83.963 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.642 | 0.93 | 1.581 | 0.8863 | 492/570 | 0.863 | 0.741 | 4.383 | 1.001 | 3 | True | 93.024 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.46 | 0.464 | 0.935 | 0.9413 | 478/491 | 0.974 | 0.851 | 4.822 | 1.001 | 4 | True | 85.068 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.568 | 0.652 | 0.928 | 0.963 | 311/313 | 0.994 | 0.645 | 4.84 | 1 | 4 | True | 94.077 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 1.08 | 7.616 | 12.387 | 0.8252 | 22/28 | 0.786 | 1.097 | 3.513 | 1.004 | 2 | True | 70.08 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.092 | 0.635 | 1.185 | 0.9603 | 402/415 | 0.969 | 0.434 | 5.094 | 0.999 | 4 | True | 93.416 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.009 | 0.104 | 0.194 | 0.9543 | 789/848 | 0.93 | 0.143 | 2.314 | 1 | 4 | True | 142.353 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.287 | 2.136 | 4.863 | 0.9362 | 25/26 | 0.962 | 0.823 | 2.692 | 0.994 | 3 | True | 100.876 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.301 | 0.735 | 0.987 | 0.9441 | 150/153 | 0.98 | 0.87 | 3.616 | 1 | 4 | True | 108.466 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.403 | 1.15 | 2.42 | 0.9252 | 40/42 | 0.952 | 0.938 | 3.391 | 1.003 | 3 | True | 190.97 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.084 | 0.128 | 0.328 | 0.97 | 295/299 | 0.987 | 0.437 | 3.92 | 1 | 4 | True | 97.768 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.299 | 0.294 | 0.34 | 0.9865 | 471/473 | 0.996 | 0.215 | 3.358 | 1 | 4 | True | 82.292 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.237 | 2.047 | 3.799 | 0.8724 | 258/312 | 0.827 | 0.624 | 6.399 | 1.003 | 2 | True | 57.452 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.126 | 0.387 | 0.942 | 0.9832 | 65/65 | 1 | 0.324 | 4.204 | 0.999 | 4 | True | 54.228 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.105 | 0.202 | 0.325 | 0.9741 | 195/201 | 0.97 | 0.184 | 1.747 | 1 | 4 | True | 50.099 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.086 | 0.37 | 0.766 | 0.9781 | 508/516 | 0.984 | 0.258 | 2.422 | 0.999 | 4 | True | 56.335 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.213 | 0.498 | 0.918 | 0.9584 | 142/144 | 0.986 | 0.654 | 3.988 | 1 | 4 | True | 64.841 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.119 | 0.301 | 0.429 | 0.9751 | 455/462 | 0.985 | 0.32 | 4.746 | 0.999 | 4 | True | 88.233 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.084 | 0.815 | 1.349 | 0.9515 | 224/228 | 0.982 | 0.75 | 4.557 | 0.998 | 4 | True | 89.054 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.269 | 1.26 | 2.062 | 0.8771 | 54/63 | 0.857 | 0.854 | 2.893 | 0.996 | 4 | True | 64.254 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.121 | 0.226 | 0.396 | 0.9851 | 642/646 | 0.994 | 0.221 | 3.642 | 1 | 4 | True | 74.593 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.224 | 0.407 | 0.973 | 0.9696 | 785/799 | 0.982 | 0.4 | 6.24 | 1.001 | 4 | True | 97.765 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 376.317 | - | - | 0 | 244/248 | 0.984 | 0.258 | 2.634 | 1.001 | 1 | True | 83.765 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=244, totalMatches=248, error=376.317, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 3.378 | 14.832 | 26.343 | 0.8214 | 40/49 | 0.816 | 1.496 | 3.415 | 0.956 | 2 | True | 62.157 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.915 | 0.968 | 1.395 | 0.9113 | 164/182 | 0.901 | 0.662 | 4.38 | 1 | 3 | True | 71.644 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 235.136 | - | - | 0 | 731/823 | 0.888 | 0.398 | 3.19 | 1.001 | 1 | True | 106.686 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=731, totalMatches=823, error=235.136, tolerance=35 |
| i_melon_1_2 | illumination | 1-2 | True | 0.236 | 0.921 | 1.798 | 0.9561 | 555/566 | 0.981 | 0.64 | 4.651 | 1.003 | 3 | True | 92.638 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.528 | 0.915 | 1.272 | 0.9502 | 623/636 | 0.98 | 0.744 | 4.445 | 0.999 | 4 | True | 96.265 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.063 | 0.678 | 1.122 | 0.957 | 558/589 | 0.947 | 0.271 | 3.802 | 1 | 3 | True | 93.15 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.414 | 1.448 | 1.972 | 0.9389 | 310/327 | 0.948 | 0.626 | 4.999 | 0.999 | 2 | True | 102.797 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.317 | 4.704 | 9.247 | 0.7273 | 35/53 | 0.66 | 1.658 | 3.821 | 0.999 | 2 | True | 52.253 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.685 | 1.37 | 3.761 | 0.858 | 260/304 | 0.855 | 1.203 | 5.62 | 1.003 | 3 | True | 104.637 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.277 | 0.443 | 0.745 | 0.9575 | 137/139 | 0.986 | 0.668 | 4.768 | 0.999 | 4 | True | 57.603 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.037 | 0.26 | 0.441 | 0.9881 | 399/399 | 1 | 0.23 | 3.306 | 1.001 | 4 | True | 83.42 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.989 | 1.18 | 2.399 | 0.8661 | 174/194 | 0.897 | 1.49 | 5.419 | 1.003 | 2 | True | 118.375 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.252 | 0.721 | 1.309 | 0.8969 | 141/160 | 0.881 | 0.729 | 5.377 | 0.999 | 4 | True | 85.17 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 230.107 | - | - | 0 | 0/2 | 0 | - | - | 0 | 0 | False | 77.288 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=2, error=230.107, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.128 | 0.253 | 0.51 | 0.9147 | 237/265 | 0.894 | 0.525 | 3.172 | 1.001 | 4 | True | 55.848 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.171 | 0.378 | 0.509 | 0.9636 | 313/315 | 0.994 | 0.635 | 4.697 | 0.999 | 4 | True | 89.629 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.525 | 0.581 | 0.819 | 0.9541 | 809/824 | 0.982 | 0.692 | 4.866 | 1 | 4 | True | 105.008 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 2.049 | 1.93 | 2.446 | 0.9561 | 324/331 | 0.979 | 0.623 | 5.366 | 0.999 | 2 | True | 83.169 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.2 | 0.411 | 0.551 | 0.9729 | 178/179 | 0.994 | 0.463 | 3.037 | 1 | 4 | True | 57.764 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.265 | 0.709 | 0.945 | 0.9242 | 512/556 | 0.921 | 0.623 | 5.025 | 0.999 | 4 | True | 60.726 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.266 | 0.948 | 2.758 | 0.9018 | 515/576 | 0.894 | 0.77 | 6.03 | 1.003 | 3 | True | 88.532 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.528 | 1.652 | 4.169 | 0.8984 | 182/213 | 0.854 | 0.416 | 3.606 | 1.004 | 3 | True | 80.976 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.685 | 1.37 | 3.761 | 0.858 | 260/304 | 0.855 | 1.203 | 5.62 | 1.003 | 3 | True | 99.475 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 17.052 | 199.432 | 382.627 | 0.8784 | 18/21 | 0.857 | 0.829 | 2.586 | 1.088 | 2 | True | 96.384 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.055 | 0.731 | 1.171 | 0.9423 | 183/192 | 0.953 | 0.616 | 4.473 | 1.003 | 3 | True | 48.612 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 1.302 | 1.453 | 2.918 | 0.9503 | 53/55 | 0.964 | 0.572 | 3.179 | 0.999 | 4 | True | 60.101 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.016 | 0.408 | 0.77 | 0.9234 | 777/833 | 0.933 | 0.763 | 5.499 | 1 | 4 | True | 90.194 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.356 | 2.662 | 7.129 | 0.9121 | 62/67 | 0.925 | 0.904 | 3.118 | 1.012 | 2 | True | 52.119 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.07 | 0.16 | 0.259 | 0.9898 | 393/395 | 0.995 | 0.143 | 1.262 | 1 | 4 | True | 132.893 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.521 | 0.616 | 0.885 | 0.9594 | 767/782 | 0.981 | 0.58 | 3.886 | 1.001 | 4 | True | 94.999 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.253 | 0.501 | 0.888 | 0.9601 | 274/278 | 0.986 | 0.617 | 4.674 | 1.001 | 4 | True | 82.835 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 378.274 | - | - | 0 | 409/415 | 0.986 | 0.956 | 3.775 | 1.014 | 0 | True | 98.962 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=409, totalMatches=415, error=378.274, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.47 | 2.927 | 6.489 | 0.9427 | 45/46 | 0.978 | 0.874 | 2.362 | 0.615 | 2 | True | 48.824 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.081 | 0.65 | 0.771 | 0.9766 | 843/843 | 1 | 0.451 | 3.394 | 0.868 | 2 | True | 116.202 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.379 | 0.585 | 0.931 | 0.9663 | 260/261 | 0.996 | 0.61 | 3.843 | 0.729 | 4 | True | 84.246 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 400.798 | - | - | 0 | 205/225 | 0.911 | 1.109 | 3.834 | 1.901 | 0 | True | 92.53 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=205, totalMatches=225, error=400.798, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 254.382 | - | - | 0 | 257/277 | 0.928 | 1.465 | 4.851 | 1.013 | 1 | True | 108.656 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=257, totalMatches=277, error=254.382, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 187.998 | - | - | 0 | 126/126 | 1 | 0.762 | 4.048 | 0.666 | 1 | True | 77.061 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=126, totalMatches=126, error=187.998, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.082 | 0.676 | 1.062 | 0.9583 | 591/594 | 0.995 | 0.751 | 5.069 | 1.095 | 2 | True | 96.702 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.273 | 1.797 | 4.026 | 0.9256 | 65/66 | 0.985 | 1.274 | 3.38 | 0.383 | 3 | True | 96.519 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.032 | 0.439 | 0.72 | 0.9474 | 339/353 | 0.96 | 0.595 | 3.432 | 0.461 | 4 | True | 80.891 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.251 | 0.558 | 0.692 | 0.8982 | 278/315 | 0.883 | 0.718 | 4.155 | 0.351 | 3 | True | 104.238 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.284 | 0.655 | 1.067 | 0.9016 | 58/62 | 0.935 | 0.691 | 2.402 | 0.182 | 4 | True | 89.719 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.067 | 0.632 | 1.217 | 0.9215 | 447/478 | 0.935 | 0.825 | 2.947 | 0.721 | 3 | True | 101.434 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 196.692 | - | - | 0 | 431/433 | 0.995 | 0.878 | 3.815 | 0.779 | 1 | True | 105.079 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=431, totalMatches=433, error=196.692, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 221.668 | - | - | 0 | 761/787 | 0.967 | 0.358 | 1.977 | 1.124 | 1 | True | 96.923 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=761, totalMatches=787, error=221.668, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 150.087 | - | - | 0 | 473/515 | 0.918 | 0.652 | 2.848 | 0.842 | 1 | True | 194.466 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=473, totalMatches=515, error=150.087, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.388 | 1.159 | 2.861 | 0.8993 | 156/170 | 0.918 | 1.069 | 3.874 | 0.528 | 4 | True | 94.031 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.132 | 0.28 | 0.409 | 0.9561 | 429/439 | 0.977 | 0.605 | 2.914 | 0.742 | 4 | True | 97.801 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 402.336 | - | - | 0 | 20/24 | 0.833 | 1.046 | 2.135 | 2.439 | 1 | True | 105.34 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=20, totalMatches=24, error=402.336, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 192.562 | - | - | 0 | 241/296 | 0.814 | 1.279 | 4.987 | 1.773 | 1 | True | 89.85 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=241, totalMatches=296, error=192.562, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.084 | 0.388 | 0.743 | 0.9704 | 449/456 | 0.985 | 0.409 | 3.055 | 0.587 | 4 | True | 117.98 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.125 | 2.013 | 4.862 | 0.9447 | 322/325 | 0.991 | 0.969 | 3.854 | 0.702 | 2 | True | 102.287 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 318.251 | - | - | 0 | 92/102 | 0.902 | 1.046 | 3.622 | 1.138 | 1 | True | 104.854 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=92, totalMatches=102, error=318.251, tolerance=35 |
