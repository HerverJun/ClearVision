# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:21:59.5096353+00:00`
Operator: `PlanarMatching`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 116 |
| Passed | 70 |
| Failed | 46 |
| Pass rate | 0.6034 |
| Mean position error px | 8653.276 |
| P95 position error px | 114.786 |
| P95 corner error px | 10.31 |
| Mean inliers | 211.612 |
| Mean score | 0.6249 |
| Runtime ms | 7330.209 |
| Max features | 1600 |
| Min inliers | 6 |
| Match ratio | 0.75 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.2 |
| Detector type | ORB |
| Score threshold | 0.5 |
| Multi-scale | True |
| Scale range | 0.2 |
| ORB FAST threshold | 20 |
| ORB edge threshold | 15 |
| AKAZE detector threshold | 0.001 |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.464 | 0.536 | 1.386 | 0.7941 | 296/300 | 0.987 | 0.356 | 2.842 | 0.695 | 3 | True | 203.537 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 2.38 | 3.564 | 7.02 | 0.7598 | 37/37 | 1 | 0.275 | 3.152 | 0.992 | 2 | True | 53.916 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.651 | 1.482 | 2.254 | 0.7625 | 125/126 | 0.992 | 0.661 | 3.454 | 0.998 | 3 | True | 36.287 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.183 | 0.496 | 0.629 | 0.8101 | 299/300 | 0.997 | 0.102 | 2.361 | 0.694 | 4 | True | 54.77 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.289 | 0.478 | 0.869 | 0.7783 | 290/300 | 0.967 | 0.443 | 3.536 | 1.001 | 4 | True | 70.905 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.394 | 0.614 | 1.294 | 0.7672 | 293/300 | 0.977 | 0.779 | 4.181 | 1.002 | 3 | True | 67.118 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.542 | 1.465 | 2.533 | 0.76 | 226/230 | 0.983 | 0.693 | 3.969 | 0.996 | 3 | True | 65.172 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.214 | 4.38 | 6.591 | 0.6964 | 14/15 | 0.933 | 1.06 | 2.567 | 0.987 | 3 | True | 60.49 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 2.852 | 3.52 | 6.795 | 0.7493 | 53/53 | 1 | 0.407 | 2.188 | 0.992 | 3 | True | 65.904 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8142 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 70.785 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 1.802 | 2.73 | 4.712 | 0.7286 | 18/19 | 0.947 | 0.385 | 1.85 | 1.001 | 2 | True | 60.378 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.104 | 0.335 | 0.557 | 0.7509 | 113/115 | 0.983 | 0.54 | 2.764 | 1 | 4 | True | 62.341 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 1.015 | 2.181 | 4.341 | 0.7164 | 48/51 | 0.941 | 0.763 | 3.3 | 1.003 | 2 | True | 73.024 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.072 | 0.163 | 0.285 | 0.7886 | 283/287 | 0.986 | 0.259 | 4.646 | 1 | 4 | True | 62.366 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.121 | 0.251 | 0.318 | 0.8076 | 300/300 | 1 | 0.113 | 1.971 | 1 | 4 | True | 52.756 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.155 | 1.442 | 2.283 | 0.7663 | 292/293 | 0.997 | 0.997 | 4.837 | 0.692 | 3 | True | 51.981 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.156 | 0.447 | 0.539 | 0.7655 | 83/83 | 1 | 0.282 | 2.322 | 0.998 | 4 | True | 48.123 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.041 | 0.056 | 0.073 | 0.8257 | 300/300 | 1 | 0.017 | 1.184 | 1 | 4 | True | 35.463 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.129 | 0.283 | 0.446 | 0.8059 | 299/300 | 0.997 | 0.093 | 3.939 | 0.999 | 4 | True | 55.774 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.429 | 0.774 | 1.101 | 0.7407 | 66/68 | 0.971 | 0.375 | 2.023 | 1.002 | 4 | True | 62.842 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.14 | 0.231 | 0.351 | 0.8049 | 300/300 | 1 | 0.079 | 3.56 | 1 | 4 | True | 61.96 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.181 | 0.282 | 0.356 | 0.7618 | 163/164 | 0.994 | 0.494 | 3.756 | 1 | 4 | True | 61.372 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 2.515 | 5.767 | 8.035 | 0.6641 | 15/18 | 0.833 | 1.111 | 3.226 | 0.681 | 3 | True | 56.406 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8136 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 53.394 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8113 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 68.181 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 1.215 | 1.689 | 3.477 | 0.1654 | 286/300 | 0.953 | 0.603 | 2.872 | 1 | 1 | True | 51.876 | Projected quadrilateral is invalid. | isMatch=False, score=0.165, inliers=286, totalMatches=300, error=1.215, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 6.211 | 7.055 | 12.833 | 0.6964 | 22/24 | 0.917 | 1.051 | 1.905 | 0.699 | 2 | True | 46.642 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.385 | 0.925 | 1.458 | 0.6271 | 72/105 | 0.686 | 0.972 | 3.233 | 0.697 | 3 | True | 68.403 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 1.789 | 1.802 | 2.111 | 0.7915 | 299/300 | 0.997 | 0.463 | 3.47 | 0.694 | 2 | True | 64.762 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.088 | 0.158 | 0.213 | 0.8018 | 300/300 | 1 | 0.196 | 4.225 | 1 | 4 | True | 59.259 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.376 | 0.477 | 1.011 | 0.7952 | 296/300 | 0.987 | 0.165 | 2.414 | 1 | 4 | True | 68.191 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8121 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 59.889 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 0.994 | 1.445 | 1.712 | 0.7288 | 94/96 | 0.979 | 1.053 | 3.436 | 0.693 | 3 | True | 63.29 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.897 | 1.492 | 2.895 | 0.7284 | 74/78 | 0.949 | 0.861 | 4.019 | 0.993 | 4 | True | 29.18 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 1.285 | 1.325 | 2.854 | 0.7206 | 112/116 | 0.966 | 1.161 | 4.869 | 0.997 | 4 | True | 73.98 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.231 | 0.372 | 0.551 | 0.7644 | 211/217 | 0.972 | 0.632 | 4.876 | 0.999 | 4 | True | 41.199 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.007 | 0.017 | 0.033 | 0.8118 | 300/300 | 1 | 0.007 | 0.995 | 1 | 4 | True | 53.435 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.417 | 1.418 | 1.884 | 0.6882 | 39/43 | 0.907 | 1.198 | 4.481 | 1.002 | 3 | True | 67.89 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.635 | 1.536 | 2.866 | 0.7709 | 267/275 | 0.971 | 0.571 | 4.836 | 0.997 | 4 | True | 54.155 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 1000000 | - | - | 0.3146 | 0/3 | 0 | 0 | 0 | 0 | 0 | False | 36.059 | Insufficient feature matches (3 < 6). | isMatch=False, score=0.315, inliers=0, totalMatches=3, error=1000000, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.046 | 0.1 | 0.202 | 0.8103 | 300/300 | 1 | 0.134 | 3.568 | 1 | 4 | True | 45.017 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.134 | 0.304 | 0.535 | 0.7868 | 300/300 | 1 | 0.46 | 4.915 | 0.999 | 4 | True | 67.861 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.049 | 0.08 | 0.128 | 0.7999 | 296/300 | 0.987 | 0.09 | 2.121 | 1 | 4 | True | 73.473 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.8 | 1.845 | 2.358 | 0.7594 | 173/175 | 0.989 | 0.653 | 4.079 | 0.999 | 2 | True | 60.749 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.205 | 0.298 | 0.591 | 0.7983 | 300/300 | 1 | 0.301 | 3.606 | 1.001 | 4 | True | 48.983 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.156 | 0.573 | 0.968 | 0.7826 | 298/300 | 0.993 | 0.568 | 4.667 | 0.998 | 4 | True | 55.414 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.079 | 0.172 | 0.291 | 0.8003 | 300/300 | 1 | 0.209 | 4.023 | 1 | 4 | True | 64.558 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.508 | 0.745 | 1.235 | 0.8045 | 300/300 | 1 | 0.334 | 2.64 | 0.694 | 4 | True | 51.044 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 1.285 | 1.325 | 2.854 | 0.7206 | 112/116 | 0.966 | 1.161 | 4.869 | 0.997 | 4 | True | 73.362 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 20.178 | 29.593 | 48.764 | 0.6271 | 8/12 | 0.667 | 0.272 | 0.468 | 1.084 | 2 | True | 64.036 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.087 | 0.159 | 0.344 | 0.8087 | 300/300 | 1 | 0.047 | 2.448 | 1.001 | 4 | True | 45.895 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.952 | 1.133 | 1.779 | 0.7636 | 110/111 | 0.991 | 0.78 | 2.898 | 1 | 3 | True | 30.232 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.335 | 0.575 | 0.913 | 0.7967 | 300/300 | 1 | 0.314 | 3.66 | 1.001 | 4 | True | 64.285 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 3.323 | 4.654 | 9.389 | 0.7177 | 30/31 | 0.968 | 0.887 | 3.313 | 1.015 | 2 | True | 50.57 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.045 | 0.064 | 0.138 | 0.814 | 300/300 | 1 | 0.01 | 0.965 | 0.694 | 4 | True | 61.002 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.139 | 0.303 | 0.586 | 0.785 | 294/300 | 0.98 | 0.434 | 3.381 | 0.694 | 4 | True | 66.963 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.128 | 0.54 | 0.696 | 0.7832 | 299/300 | 0.997 | 0.55 | 2.926 | 0.694 | 4 | True | 61.206 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.274 | 1.093 | 1.752 | 0.1557 | 295/300 | 0.983 | 0.953 | 3.287 | 0.703 | 0 | True | 60.425 | Projected quadrilateral is invalid. | isMatch=False, score=0.156, inliers=295, totalMatches=300, error=66.274, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | False | 44.731 | 1.913 | 3.688 | 0.7732 | 154/154 | 1 | 0.918 | 3.569 | 0.51 | 2 | True | 28.995 | - | isMatch=True, score=0.773, inliers=154, totalMatches=154, error=44.731, tolerance=35 |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 14.903 | 0.891 | 1.181 | 0.7836 | 300/300 | 1 | 0.743 | 3.056 | 0.716 | 2 | True | 80.513 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | False | 97.186 | 0.861 | 2.202 | 0.7482 | 176/177 | 0.994 | 0.994 | 4.656 | 0.731 | 4 | True | 62.622 | - | isMatch=True, score=0.748, inliers=176, totalMatches=177, error=97.186, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 51.921 | 1.268 | 2.206 | 0.1343 | 150/180 | 0.833 | 1.172 | 4.381 | 1.323 | 0 | True | 64.622 | Projected quadrilateral is invalid. | isMatch=False, score=0.134, inliers=150, totalMatches=180, error=51.921, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 81.583 | 2.566 | 2.902 | 0.1314 | 152/177 | 0.859 | 1.388 | 4.938 | 0.835 | 1 | True | 83.673 | Projected quadrilateral is invalid. | isMatch=False, score=0.131, inliers=152, totalMatches=177, error=81.583, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 1.367 | 2.543 | 5.536 | 0.134 | 157/175 | 0.897 | 1.188 | 4.065 | 0.659 | 1 | True | 57.495 | Projected quadrilateral is invalid. | isMatch=False, score=0.134, inliers=157, totalMatches=175, error=1.367, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | False | 68.79 | 0.545 | 1.296 | 0.7753 | 299/300 | 0.997 | 0.879 | 3.21 | 0.903 | 2 | True | 65.938 | - | isMatch=True, score=0.775, inliers=299, totalMatches=300, error=68.79, tolerance=35 |
| v_beyus_1_2 | viewpoint | 1-2 | True | 32.81 | 3.122 | 4.267 | 0.7079 | 59/62 | 0.952 | 1.214 | 3.646 | 0.317 | 3 | True | 58.245 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.504 | 0.677 | 1.414 | 0.7592 | 219/223 | 0.982 | 0.841 | 2.739 | 0.321 | 4 | True | 62.486 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | False | 36.02 | 0.963 | 1.802 | 0.7668 | 284/285 | 0.996 | 0.912 | 3.127 | 0.351 | 3 | True | 65.469 | - | isMatch=True, score=0.767, inliers=284, totalMatches=285, error=36.02, tolerance=35 |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 15.032 | 0.771 | 1.597 | 0.7404 | 117/120 | 0.975 | 0.813 | 2.624 | 0.284 | 4 | True | 64.487 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | False | 70.152 | 2.252 | 3.434 | 0.7641 | 297/300 | 0.99 | 1.036 | 4.866 | 0.595 | 3 | True | 73.246 | - | isMatch=True, score=0.764, inliers=297, totalMatches=300, error=70.152, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | False | 0.791 | 1.398 | 1.971 | 0.1564 | 300/300 | 1 | 1.001 | 4.449 | 0.962 | 1 | True | 81.063 | Projected quadrilateral is invalid. | isMatch=False, score=0.156, inliers=300, totalMatches=300, error=0.791, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 25.077 | 0.711 | 1.546 | 0.1526 | 293/300 | 0.977 | 0.882 | 3.636 | 0.928 | 1 | True | 78.743 | Projected quadrilateral is invalid. | isMatch=False, score=0.153, inliers=293, totalMatches=300, error=25.077, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 65.001 | 2.705 | 4.524 | 0.1499 | 264/300 | 0.88 | 1.005 | 3.264 | 0.697 | 1 | True | 83.658 | Projected quadrilateral is invalid. | isMatch=False, score=0.15, inliers=264, totalMatches=300, error=65.001, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 28.065 | 1.125 | 2.311 | 0.7192 | 122/130 | 0.938 | 1.04 | 3.303 | 0.65 | 4 | True | 62.956 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 11.574 | 1.005 | 2.014 | 0.7787 | 300/300 | 1 | 0.807 | 3.057 | 0.741 | 4 | True | 78.071 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 890.566 | 1140.266 | 3668.533 | 0.1114 | 7/20 | 0.35 | 1.192 | 2.365 | 5.939 | 0 | True | 89.354 | Projected quadrilateral is invalid. | isMatch=False, score=0.111, inliers=7, totalMatches=20, error=890.566, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 117.416 | 1.87 | 3.566 | 0.148 | 273/277 | 0.986 | 1.213 | 4.879 | 1.23 | 1 | True | 61.473 | Projected quadrilateral is invalid. | isMatch=False, score=0.148, inliers=273, totalMatches=277, error=117.416, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 4.543 | 0.619 | 1.201 | 0.7703 | 290/300 | 0.967 | 0.755 | 3.627 | 0.723 | 4 | True | 68.398 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | False | 52.567 | 2.143 | 5.229 | 0.7195 | 98/101 | 0.97 | 1.223 | 4.574 | 0.701 | 2 | True | 67.093 | - | isMatch=True, score=0.719, inliers=98, totalMatches=101, error=52.567, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 54.064 | 1.233 | 2 | 0.1295 | 34/42 | 0.81 | 0.983 | 3.744 | 0.785 | 1 | True | 34.164 | Projected quadrilateral is invalid. | isMatch=False, score=0.129, inliers=34, totalMatches=42, error=54.064, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.218 | 5.563 | 10.31 | 0.1551 | 293/300 | 0.977 | 0.885 | 3.305 | 1.02 | 0 | True | 66.362 | Projected quadrilateral is invalid. | isMatch=False, score=0.155, inliers=293, totalMatches=300, error=62.218, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 30.679 | 0.712 | 0.998 | 0.7711 | 299/300 | 0.997 | 0.858 | 3.426 | 0.681 | 3 | True | 76.189 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | False | 149.821 | 4.734 | 8.711 | 0.7503 | 286/296 | 0.966 | 1.18 | 4.201 | 1.093 | 2 | True | 56.502 | - | isMatch=True, score=0.75, inliers=286, totalMatches=296, error=149.821, tolerance=35 |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 1.76 | 0.562 | 1.054 | 0.7832 | 300/300 | 1 | 0.73 | 2.192 | 0.493 | 4 | True | 81.628 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 6.99 | 0.928 | 1.405 | 0.7743 | 299/300 | 0.997 | 0.884 | 3.405 | 0.724 | 4 | True | 70.382 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 88.113 | 1.524 | 2.128 | 0.1527 | 297/300 | 0.99 | 1.216 | 4.06 | 1.689 | 0 | True | 70.008 | Projected quadrilateral is invalid. | isMatch=False, score=0.153, inliers=297, totalMatches=300, error=88.113, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | False | 157.526 | 3.07 | 5.023 | 0.7427 | 186/189 | 0.984 | 1.067 | 5.088 | 0.888 | 2 | True | 64.938 | - | isMatch=True, score=0.743, inliers=186, totalMatches=189, error=157.526, tolerance=35 |
| v_grace_1_2 | viewpoint | 1-2 | False | 67.355 | 1.587 | 3.197 | 0.1558 | 241/300 | 0.803 | 0.945 | 3.773 | 1.157 | 0 | True | 59.735 | Projected quadrilateral is invalid. | isMatch=False, score=0.156, inliers=241, totalMatches=300, error=67.355, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 23.821 | 0.673 | 1.076 | 0.7668 | 299/300 | 0.997 | 0.997 | 5.226 | 0.512 | 2 | True | 68.454 | - | - |
| v_home_1_2 | viewpoint | 1-2 | False | 56.459 | 1.574 | 2.879 | 0.732 | 160/163 | 0.982 | 1.279 | 4.453 | 0.765 | 3 | True | 66.619 | - | isMatch=True, score=0.732, inliers=160, totalMatches=163, error=56.459, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | False | 0.733 | 0.371 | 0.533 | 0.1583 | 269/300 | 0.897 | 0.832 | 3.576 | 0.943 | 1 | True | 56.43 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=269, totalMatches=300, error=0.733, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 45.467 | 1.354 | 2.56 | 0.1538 | 287/300 | 0.957 | 1.022 | 4.336 | 1.192 | 0 | True | 73.156 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=287, totalMatches=300, error=45.467, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | False | 70.543 | 1.895 | 2.139 | 0.7283 | 215/227 | 0.947 | 1.29 | 5.269 | 0.723 | 2 | True | 65.749 | - | isMatch=True, score=0.728, inliers=215, totalMatches=227, error=70.543, tolerance=35 |
| v_man_1_2 | viewpoint | 1-2 | False | 114.786 | 2.005 | 3.203 | 0.7419 | 224/230 | 0.974 | 1.179 | 4.994 | 0.868 | 2 | True | 62.787 | - | isMatch=True, score=0.742, inliers=224, totalMatches=230, error=114.786, tolerance=35 |
| v_maskedman_1_2 | viewpoint | 1-2 | False | 79.742 | 1.232 | 2.346 | 0.7393 | 129/131 | 0.985 | 1.007 | 5.586 | 0.586 | 3 | True | 52.275 | - | isMatch=True, score=0.739, inliers=129, totalMatches=131, error=79.742, tolerance=35 |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 39.045 | 2.958 | 7.105 | 0.1513 | 228/300 | 0.76 | 1.344 | 4.358 | 0.966 | 1 | True | 67.366 | Projected quadrilateral is invalid. | isMatch=False, score=0.151, inliers=228, totalMatches=300, error=39.045, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 26.676 | 1.268 | 1.75 | 0.7668 | 299/300 | 0.997 | 1.018 | 4.535 | 0.43 | 4 | True | 76.991 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 10.405 | 0.393 | 0.545 | 0.7716 | 298/300 | 0.993 | 0.857 | 2.548 | 0.345 | 4 | True | 67.441 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 45.144 | 1.189 | 2.393 | 0.1397 | 106/110 | 0.964 | 1.007 | 3.58 | 0.918 | 0 | True | 39.351 | Projected quadrilateral is invalid. | isMatch=False, score=0.14, inliers=106, totalMatches=110, error=45.144, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 7.114 | 1.312 | 1.59 | 0.1583 | 292/300 | 0.973 | 0.628 | 2.783 | 1.012 | 1 | True | 67.766 | Projected quadrilateral is invalid. | isMatch=False, score=0.158, inliers=292, totalMatches=300, error=7.114, tolerance=35 |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 26.551 | 0.972 | 1.43 | 0.7784 | 295/300 | 0.983 | 0.693 | 3.408 | 0.971 | 2 | True | 78.974 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 19.053 | 8.875 | 16.468 | 0.123 | 105/135 | 0.778 | 1.209 | 4.387 | 1.072 | 1 | True | 77.48 | Projected quadrilateral is invalid. | isMatch=False, score=0.123, inliers=105, totalMatches=135, error=19.053, tolerance=35 |
| v_talent_1_2 | viewpoint | 1-2 | False | 109.78 | 2.147 | 4.575 | 0.758 | 247/255 | 0.969 | 0.759 | 4.968 | 0.741 | 4 | True | 65.038 | - | isMatch=True, score=0.758, inliers=247, totalMatches=255, error=109.78, tolerance=35 |
| v_tempera_1_2 | viewpoint | 1-2 | True | 6.736 | 0.305 | 0.58 | 0.7804 | 300/300 | 1 | 0.797 | 4.508 | 0.635 | 4 | True | 56.119 | - | - |
| v_there_1_2 | viewpoint | 1-2 | False | 48.689 | 2.05 | 6.157 | 0.7385 | 59/60 | 0.983 | 1.053 | 2.938 | 0.526 | 3 | True | 33.698 | - | isMatch=True, score=0.738, inliers=59, totalMatches=60, error=48.689, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 44.592 | 0.761 | 1.24 | 0.1542 | 298/300 | 0.993 | 1.01 | 3.92 | 1.381 | 0 | True | 71.527 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=298, totalMatches=300, error=44.592, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 33.173 | 0.718 | 0.994 | 0.1433 | 207/233 | 0.888 | 1.081 | 4.8 | 1.025 | 1 | True | 80.252 | Projected quadrilateral is invalid. | isMatch=False, score=0.143, inliers=207, totalMatches=233, error=33.173, tolerance=35 |
| v_wall_1_2 | viewpoint | 1-2 | False | 84.796 | 1.561 | 2.233 | 0.7711 | 300/300 | 1 | 0.99 | 4.509 | 0.985 | 2 | True | 51.901 | - | isMatch=True, score=0.771, inliers=300, totalMatches=300, error=84.796, tolerance=35 |
| v_wapping_1_2 | viewpoint | 1-2 | False | 36.047 | 1.094 | 2.492 | 0.7378 | 201/205 | 0.98 | 1.199 | 4.739 | 0.899 | 2 | True | 76.283 | - | isMatch=True, score=0.738, inliers=201, totalMatches=205, error=36.047, tolerance=35 |
| v_war_1_2 | viewpoint | 1-2 | False | 105.671 | 4.81 | 8.02 | 0.7226 | 79/81 | 0.975 | 1.028 | 4.263 | 0.531 | 2 | True | 69.912 | - | isMatch=True, score=0.723, inliers=79, totalMatches=81, error=105.671, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 48.019 | 2.538 | 6.079 | 0.1544 | 272/300 | 0.907 | 0.984 | 4.689 | 1.008 | 0 | True | 64.221 | Projected quadrilateral is invalid. | isMatch=False, score=0.154, inliers=272, totalMatches=300, error=48.019, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | False | 37.368 | 1.58 | 1.987 | 0.7399 | 120/123 | 0.976 | 0.821 | 2.412 | 0.305 | 4 | True | 62.127 | - | isMatch=True, score=0.74, inliers=120, totalMatches=123, error=37.368, tolerance=35 |
| v_wormhole_1_2 | viewpoint | 1-2 | False | 89.577 | 2.203 | 2.501 | 0.7469 | 230/234 | 0.983 | 1.163 | 4.483 | 0.688 | 2 | True | 66.086 | - | isMatch=True, score=0.747, inliers=230, totalMatches=234, error=89.577, tolerance=35 |
| v_wounded_1_2 | viewpoint | 1-2 | False | 62.217 | 9.18 | 10.483 | 0.7175 | 137/146 | 0.938 | 1.238 | 5.04 | 0.834 | 2 | True | 63.15 | - | isMatch=True, score=0.718, inliers=137, totalMatches=146, error=62.217, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.436 | 1.935 | 2.346 | 0.1432 | 245/251 | 0.976 | 1.255 | 4.095 | 1.043 | 1 | True | 72.325 | Projected quadrilateral is invalid. | isMatch=False, score=0.143, inliers=245, totalMatches=251, error=49.436, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 2.169 | 1.534 | 1.981 | 0.1548 | 300/300 | 1 | 0.984 | 4.967 | 1.011 | 1 | True | 59.693 | Projected quadrilateral is invalid. | isMatch=False, score=0.155, inliers=300, totalMatches=300, error=2.169, tolerance=35 |
