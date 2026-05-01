# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:25:16.3823486+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 116 |
| Passed | 90 |
| Failed | 26 |
| Pass rate | 0.7759 |
| Mean position error px | 47.691 |
| P95 position error px | 283.268 |
| P95 corner error px | 6.696 |
| Mean inliers | 445.922 |
| Mean score | 0.7191 |
| Runtime ms | 3003.023 |
| Max features | 2000 |
| Min inliers | 6 |
| Match ratio | 0.78 |
| RANSAC threshold px | 5 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.187 | 0.428 | 0.741 | 0.9532 | 630/652 | 0.966 | 0.544 | 4.013 | 1.002 | 4 | True | 161.658 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.532 | 1.589 | 2.478 | 0.966 | 91/93 | 0.978 | 0.427 | 3.509 | 1 | 3 | True | 21.862 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.133 | 1.839 | 2.578 | 0.9236 | 185/200 | 0.925 | 0.677 | 4.533 | 0.995 | 3 | True | 14.843 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.214 | 0.464 | 0.809 | 0.9869 | 907/913 | 0.993 | 0.183 | 4.588 | 0.999 | 4 | True | 22.407 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.343 | 0.347 | 0.455 | 0.9455 | 492/515 | 0.955 | 0.577 | 4.734 | 1 | 4 | True | 24.332 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.294 | 0.667 | 1.117 | 0.9421 | 410/419 | 0.979 | 0.889 | 4.395 | 1.002 | 3 | True | 26.317 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.277 | 1.278 | 1.775 | 0.9265 | 283/302 | 0.937 | 0.75 | 3.976 | 0.997 | 3 | True | 25.736 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.196 | 4.957 | 6.696 | 0.8614 | 17/20 | 0.85 | 1.081 | 2.779 | 0.986 | 2 | True | 29.313 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.264 | 1.984 | 4.608 | 0.9723 | 95/96 | 0.99 | 0.423 | 3.467 | 0.995 | 3 | True | 25.277 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.007 | 0.069 | 0.137 | 0.9952 | 1575/1576 | 0.999 | 0.086 | 4.217 | 1 | 4 | True | 31.17 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.365 | 1.985 | 4.386 | 0.8878 | 29/34 | 0.853 | 0.604 | 3.272 | 0.999 | 3 | True | 23.782 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.104 | 0.581 | 1.037 | 0.9576 | 165/168 | 0.982 | 0.628 | 3.695 | 1 | 4 | True | 26.353 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.104 | 1.211 | 1.455 | 0.9116 | 66/72 | 0.917 | 0.822 | 4.338 | 0.998 | 3 | True | 36.85 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.039 | 0.102 | 0.139 | 0.9799 | 433/439 | 0.986 | 0.242 | 4.61 | 1 | 4 | True | 24.001 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.156 | 0.166 | 0.183 | 0.9782 | 1088/1091 | 0.997 | 0.39 | 3.938 | 1 | 4 | True | 21.176 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.097 | 0.246 | 0.392 | 0.9644 | 562/567 | 0.991 | 0.593 | 4.3 | 1 | 4 | True | 20.518 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.297 | 0.679 | 0.978 | 0.958 | 117/121 | 0.967 | 0.459 | 3.351 | 1 | 4 | True | 19.073 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.019 | 0.094 | 0.16 | 0.9805 | 1152/1173 | 0.982 | 0.186 | 3.599 | 1 | 4 | True | 17.258 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.096 | 0.223 | 0.541 | 0.9808 | 895/899 | 0.996 | 0.322 | 4.693 | 0.999 | 4 | True | 20.357 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.111 | 0.573 | 0.965 | 0.9345 | 112/121 | 0.926 | 0.475 | 3.591 | 1 | 4 | True | 23.226 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.053 | 0.125 | 0.222 | 0.9925 | 549/552 | 0.995 | 0.088 | 2.976 | 1 | 4 | True | 23.362 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.056 | 0.548 | 0.803 | 0.9642 | 219/221 | 0.991 | 0.595 | 3.766 | 1 | 4 | True | 25.14 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.678 | 2.428 | 3.739 | 0.8199 | 40/53 | 0.755 | 0.872 | 2.154 | 0.993 | 3 | True | 20.445 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.036 | 0.069 | 0.123 | 0.9886 | 1424/1439 | 0.99 | 0.11 | 3.603 | 1 | 4 | True | 19.788 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.094 | 0.102 | 0.221 | 0.9872 | 1069/1071 | 0.998 | 0.227 | 4.549 | 1 | 4 | True | 28.885 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 967/975 | 0.992 | 0.907 | 4.428 | 1.001 | 1 | True | 20.492 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=967, totalMatches=975, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 1.22 | 2.766 | 4.086 | 0.844 | 37/44 | 0.841 | 1.322 | 3.392 | 1 | 2 | True | 17.913 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.847 | 1.193 | 1.694 | 0.7622 | 111/161 | 0.689 | 1.292 | 3.337 | 0.999 | 3 | True | 23.404 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.019 | 1.967 | 2.065 | 0.9074 | 888/973 | 0.913 | 0.859 | 5.093 | 1.001 | 2 | True | 25.035 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.172 | 0.584 | 0.929 | 0.9552 | 818/855 | 0.957 | 0.404 | 4.791 | 1 | 4 | True | 22.115 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.166 | 0.6 | 1.014 | 0.9583 | 619/642 | 0.964 | 0.425 | 3.727 | 1 | 4 | True | 25.244 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.046 | 1.14 | 1.769 | 0.9899 | 716/722 | 0.992 | 0.106 | 4.338 | 0.999 | 3 | True | 22.116 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.091 | 1.1 | 1.267 | 0.9254 | 211/222 | 0.95 | 0.912 | 4.675 | 1 | 2 | True | 26.06 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.601 | 0.988 | 2.131 | 0.8907 | 137/155 | 0.884 | 0.877 | 4.665 | 0.996 | 4 | True | 15.516 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.852 | 0.748 | 1.305 | 0.9011 | 163/177 | 0.921 | 1.068 | 3.787 | 1 | 3 | True | 28.957 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.174 | 0.404 | 0.733 | 0.9391 | 346/362 | 0.956 | 0.706 | 4.746 | 0.999 | 4 | True | 23.712 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.019 | 0.098 | 0.146 | 0.9893 | 1138/1144 | 0.995 | 0.15 | 4.169 | 1 | 4 | True | 23.992 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.563 | 1.546 | 2.613 | 0.8583 | 66/77 | 0.857 | 1.217 | 4.263 | 1.005 | 2 | True | 32.797 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.302 | 1.119 | 1.828 | 0.9653 | 407/415 | 0.981 | 0.464 | 4.803 | 0.998 | 4 | True | 21.116 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | - | - | 0 | 4/6 | 0.667 | 0 | 0 | 0 | 4 | True | 17.311 | Insufficient inliers (4 < 6). | isMatch=False, score=0, inliers=4, totalMatches=6, error=241.446, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.153 | 0.183 | 0.32 | 0.976 | 1070/1075 | 0.995 | 0.413 | 3.651 | 1 | 4 | True | 18.205 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.026 | 0.262 | 0.425 | 0.9729 | 399/402 | 0.993 | 0.443 | 5.01 | 0.999 | 4 | True | 28.002 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.112 | 0.189 | 0.349 | 0.9679 | 840/860 | 0.977 | 0.372 | 4.682 | 1 | 4 | True | 26.994 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.906 | 1.981 | 2.191 | 0.8903 | 227/262 | 0.866 | 0.7 | 3.745 | 0.999 | 2 | True | 23.551 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.075 | 0.14 | 0.232 | 0.9744 | 538/539 | 0.998 | 0.473 | 3.636 | 1.001 | 4 | True | 18.437 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.198 | 0.499 | 0.982 | 0.9592 | 579/583 | 0.993 | 0.713 | 4.761 | 0.998 | 4 | True | 21.003 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.037 | 0.067 | 0.103 | 0.9785 | 627/635 | 0.987 | 0.282 | 4.232 | 1 | 4 | True | 25.254 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.31 | 0.631 | 1.136 | 0.9603 | 824/828 | 0.995 | 0.715 | 3.715 | 1 | 4 | True | 20.772 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.852 | 0.748 | 1.305 | 0.9011 | 163/177 | 0.921 | 1.068 | 3.787 | 1 | 3 | True | 28.157 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 3.888 | 21.717 | 42.074 | 0.7404 | 20/34 | 0.588 | 0.639 | 1.327 | 1.022 | 2 | True | 23.448 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.055 | 0.16 | 0.254 | 0.9797 | 1136/1145 | 0.992 | 0.309 | 4.962 | 1 | 4 | True | 17.358 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.668 | 1.099 | 1.482 | 0.8963 | 161/179 | 0.899 | 0.933 | 4.595 | 0.996 | 4 | True | 13.348 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.116 | 0.417 | 0.738 | 0.9679 | 687/691 | 0.994 | 0.558 | 4.624 | 1.002 | 4 | True | 24.798 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.08 | 3.77 | 8.241 | 0.9087 | 40/43 | 0.93 | 1.022 | 5.153 | 1.014 | 2 | True | 19.862 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.03 | 0.071 | 0.119 | 0.9929 | 1421/1422 | 0.999 | 0.129 | 4.983 | 1 | 4 | True | 29.783 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.273 | 0.393 | 0.48 | 0.9342 | 690/739 | 0.934 | 0.565 | 5.08 | 0.999 | 4 | True | 25.225 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.411 | 0.272 | 0.457 | 0.9573 | 508/513 | 0.99 | 0.72 | 3.66 | 1 | 4 | True | 25.728 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 85.606 | - | - | 0 | 477/523 | 0.912 | 1.056 | 6.856 | 1.015 | 0 | True | 25.32 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=477, totalMatches=523, error=85.606, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.192 | 1.314 | 1.6 | 0.9315 | 433/441 | 0.982 | 1.129 | 3.935 | 0.62 | 2 | True | 15.721 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.079 | 0.688 | 0.889 | 0.9464 | 723/726 | 0.996 | 0.989 | 4.618 | 0.868 | 2 | True | 30.966 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.631 | 0.728 | 0.954 | 0.9362 | 235/238 | 0.987 | 1.096 | 4.406 | 0.73 | 4 | True | 24.352 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 313.373 | - | - | 0 | 212/221 | 0.959 | 1.398 | 5.224 | 1.901 | 0 | True | 25.53 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=212, totalMatches=221, error=313.373, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 116.339 | - | - | 0 | 217/235 | 0.923 | 1.219 | 4.963 | 1.011 | 1 | True | 32.247 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=217, totalMatches=235, error=116.339, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 290.88 | - | - | 0 | 276/282 | 0.979 | 1.313 | 5.089 | 0.665 | 1 | True | 22.601 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=276, totalMatches=282, error=290.88, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.112 | 0.821 | 1.656 | 0.9381 | 676/684 | 0.988 | 1.069 | 4.449 | 1.095 | 2 | True | 24.955 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.608 | 2.915 | 4.05 | 0.8429 | 68/80 | 0.85 | 1.438 | 4.644 | 0.379 | 4 | True | 22.261 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.227 | 0.584 | 0.997 | 0.9346 | 482/497 | 0.97 | 0.94 | 3.015 | 0.461 | 4 | True | 31.531 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.484 | 0.845 | 1.458 | 0.9394 | 372/382 | 0.974 | 0.891 | 3.529 | 0.351 | 3 | True | 26.25 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.443 | 0.555 | 0.827 | 0.9057 | 149/156 | 0.955 | 0.826 | 2.95 | 0.183 | 4 | True | 26.542 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.218 | 1.451 | 2.443 | 0.9068 | 498/529 | 0.941 | 1.177 | 4.45 | 0.72 | 3 | True | 29.476 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 283.268 | - | - | 0 | 459/503 | 0.913 | 1.14 | 5.446 | 0.78 | 1 | True | 30.455 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=459, totalMatches=503, error=283.268, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 217.228 | - | - | 0 | 460/478 | 0.962 | 0.935 | 3.724 | 1.124 | 1 | True | 29.763 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=460, totalMatches=478, error=217.228, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 227.419 | - | - | 0 | 389/470 | 0.828 | 1.119 | 4.414 | 0.838 | 1 | True | 34.034 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=389, totalMatches=470, error=227.419, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.374 | 1.406 | 2.308 | 0.9249 | 145/150 | 0.967 | 1.095 | 4.035 | 0.528 | 4 | True | 23.729 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.265 | 0.915 | 2.013 | 0.9276 | 738/771 | 0.957 | 0.942 | 4.937 | 0.742 | 4 | True | 29.12 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 378.828 | - | - | 0 | 22/40 | 0.55 | 1.262 | 5.168 | 2.398 | 1 | True | 25.022 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=22, totalMatches=40, error=378.828, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 131.097 | - | - | 0 | 318/336 | 0.946 | 1.41 | 4.768 | 1.773 | 1 | True | 22.72 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=318, totalMatches=336, error=131.097, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.155 | 0.368 | 0.555 | 0.8962 | 347/387 | 0.897 | 0.906 | 3.316 | 0.587 | 4 | True | 27.24 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.519 | 1.446 | 1.62 | 0.8271 | 134/168 | 0.798 | 1.187 | 3.969 | 0.698 | 2 | True | 25.766 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.875 | - | - | 0 | 72/78 | 0.923 | 1.247 | 3.994 | 1.131 | 1 | True | 15.872 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=72, totalMatches=78, error=243.875, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 119.45 | - | - | 0 | 645/705 | 0.915 | 0.978 | 3.852 | 1.026 | 0 | True | 25.134 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=645, totalMatches=705, error=119.45, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.174 | 0.82 | 1.736 | 0.9006 | 473/521 | 0.908 | 0.941 | 5.726 | 0.68 | 3 | True | 30.209 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.212 | 3.841 | 6.84 | 0.9112 | 352/367 | 0.959 | 1.279 | 5.579 | 1.323 | 2 | True | 20.77 | - | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.138 | 0.555 | 0.648 | 0.9505 | 861/862 | 0.999 | 0.941 | 4.495 | 0.711 | 4 | True | 31.2 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.189 | 0.443 | 0.898 | 0.9468 | 564/568 | 0.993 | 0.951 | 4.11 | 0.463 | 4 | True | 29.051 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 157.731 | - | - | 0 | 423/489 | 0.865 | 1.073 | 4.51 | 1.364 | 0 | True | 27.219 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=423, totalMatches=489, error=157.731, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.227 | 1.958 | 4.171 | 0.8502 | 206/245 | 0.841 | 1.201 | 4.929 | 1.279 | 2 | True | 25.257 | - | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 97.944 | - | - | 0 | 575/695 | 0.827 | 1.005 | 5.431 | 1.161 | 0 | True | 23.595 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=575, totalMatches=695, error=97.944, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.186 | 0.635 | 0.787 | 0.9103 | 495/521 | 0.95 | 1.202 | 4.456 | 0.736 | 2 | True | 26.201 | - | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.782 | 2.401 | 3.577 | 0.812 | 147/191 | 0.77 | 1.182 | 3.802 | 0.492 | 3 | True | 25.909 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 132.914 | - | - | 0 | 809/814 | 0.994 | 0.975 | 4.869 | 0.763 | 1 | True | 20.768 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=809, totalMatches=814, error=132.914, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 248.894 | - | - | 0 | 511/573 | 0.892 | 1.024 | 3.932 | 1.192 | 0 | True | 29.251 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=511, totalMatches=573, error=248.894, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.682 | 2.239 | 4.085 | 0.8982 | 321/333 | 0.964 | 1.581 | 5.288 | 1.041 | 2 | True | 25.508 | - | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.486 | 2.699 | 4.357 | 0.9096 | 298/310 | 0.961 | 1.333 | 6.365 | 1.046 | 2 | True | 24.829 | - | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.466 | 2.405 | 4.421 | 0.8911 | 155/171 | 0.906 | 1.109 | 4.624 | 0.843 | 3 | True | 20.385 | - | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 234.57 | - | - | 0 | 390/436 | 0.894 | 1.503 | 5.097 | 0.96 | 1 | True | 27.964 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=390, totalMatches=436, error=234.57, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.44 | 1.261 | 1.674 | 0.9511 | 331/332 | 0.997 | 0.912 | 3.52 | 0.276 | 4 | True | 31.661 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.218 | 0.49 | 0.663 | 0.9398 | 363/370 | 0.981 | 0.96 | 3.472 | 0.417 | 4 | True | 28.196 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 270.843 | - | - | 0 | 194/201 | 0.965 | 1.182 | 4.231 | 1.114 | 0 | True | 23.236 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=194, totalMatches=201, error=270.843, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 19.469 | - | - | 0 | 1050/1156 | 0.908 | 0.882 | 4.547 | 1.013 | 1 | True | 27.93 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1050, totalMatches=1156, error=19.469, tolerance=35 |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.141 | 0.934 | 1.216 | 0.9171 | 847/908 | 0.933 | 0.887 | 5.262 | 0.972 | 2 | True | 31.623 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 189.489 | - | - | 0 | 160/180 | 0.889 | 1.2 | 4.55 | 0.875 | 1 | True | 30.128 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=160, totalMatches=180, error=189.489, tolerance=35 |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.656 | 2.206 | 3.962 | 0.923 | 340/351 | 0.969 | 1.153 | 4.644 | 0.897 | 4 | True | 24.943 | - | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.135 | 0.352 | 0.487 | 0.9376 | 542/555 | 0.977 | 0.955 | 4.595 | 0.515 | 4 | True | 21.901 | - | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.1 | 0.631 | 0.838 | 0.8908 | 151/167 | 0.904 | 1.089 | 4.278 | 0.763 | 3 | True | 17.623 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 177.838 | - | - | 0 | 607/628 | 0.967 | 0.893 | 3.767 | 1.12 | 0 | True | 28.342 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=607, totalMatches=628, error=177.838, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 154.444 | - | - | 0 | 247/317 | 0.779 | 0.972 | 4.607 | 0.83 | 1 | True | 30.159 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=247, totalMatches=317, error=154.444, tolerance=35 |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.126 | 1.605 | 2.268 | 0.9331 | 524/530 | 0.989 | 1.17 | 4.812 | 0.986 | 2 | True | 19.844 | - | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.379 | 0.938 | 1.881 | 0.8872 | 260/289 | 0.9 | 1.11 | 4.743 | 0.901 | 2 | True | 31.462 | - | - |
| v_war_1_2 | viewpoint | 1-2 | True | 1.107 | 2.774 | 3.839 | 0.8326 | 117/142 | 0.824 | 1.361 | 5.942 | 0.763 | 2 | True | 25.52 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 358.203 | - | - | 0 | 522/556 | 0.939 | 1 | 5.083 | 1.007 | 0 | True | 25.147 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=522, totalMatches=556, error=358.203, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.573 | 1.757 | 2.783 | 0.9407 | 139/142 | 0.979 | 0.92 | 4.503 | 0.369 | 4 | True | 22.576 | - | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.604 | 1.782 | 2.618 | 0.9189 | 303/313 | 0.968 | 1.225 | 4.013 | 0.688 | 2 | True | 24.493 | - | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.664 | 7.078 | 12.342 | 0.8983 | 170/183 | 0.929 | 1.207 | 4.561 | 1.202 | 2 | True | 23.791 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 219.078 | - | - | 0 | 184/228 | 0.807 | 1.024 | 4.19 | 0.845 | 1 | True | 27.764 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=184, totalMatches=228, error=219.078, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 324.619 | - | - | 0 | 386/395 | 0.977 | 1.035 | 4.678 | 1.01 | 1 | True | 23.127 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=386, totalMatches=395, error=324.619, tolerance=35 |
