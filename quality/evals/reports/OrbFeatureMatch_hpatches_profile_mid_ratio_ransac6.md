# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:26:34.6859610+00:00`
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
| Mean position error px | 50.099 |
| P95 position error px | 279.156 |
| P95 corner error px | 10.896 |
| Mean inliers | 405.224 |
| Mean score | 0.7229 |
| Runtime ms | 2956.952 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.181 | 0.435 | 0.634 | 0.9611 | 573/591 | 0.97 | 0.513 | 3.976 | 1.002 | 4 | True | 162.95 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.202 | 0.894 | 1.47 | 0.9835 | 89/89 | 1 | 0.382 | 5.126 | 1.001 | 3 | True | 21.279 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.205 | 0.932 | 1.202 | 0.9715 | 167/168 | 0.994 | 0.585 | 3.462 | 0.998 | 4 | True | 15.277 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.266 | 0.602 | 1.114 | 0.9891 | 852/858 | 0.993 | 0.163 | 4.585 | 0.998 | 4 | True | 22.825 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.315 | 0.411 | 0.539 | 0.9465 | 435/461 | 0.944 | 0.52 | 4.313 | 1 | 4 | True | 24.519 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.315 | 1.407 | 2.471 | 0.936 | 333/350 | 0.951 | 0.864 | 5.627 | 1.002 | 3 | True | 25.764 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.336 | 1.428 | 2.773 | 0.9657 | 246/247 | 0.996 | 0.743 | 4.002 | 0.998 | 3 | True | 25 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 1.498 | 5.86 | 10.598 | 0.9659 | 14/14 | 1 | 0.789 | 1.829 | 0.987 | 2 | True | 21.768 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.397 | 1.958 | 4.908 | 0.9768 | 77/78 | 0.987 | 0.374 | 2.681 | 0.995 | 3 | True | 24.06 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.01 | 0.065 | 0.141 | 0.9962 | 1562/1563 | 0.999 | 0.081 | 4.214 | 1 | 4 | True | 30.774 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 1.529 | 9.962 | 15.363 | 0.9522 | 20/21 | 0.952 | 0.5 | 2.366 | 1.015 | 2 | True | 23.194 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.089 | 0.577 | 1.236 | 0.9715 | 144/145 | 0.993 | 0.572 | 3.73 | 1.001 | 3 | True | 26.481 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.057 | 1.942 | 4.095 | 0.9596 | 48/49 | 0.98 | 0.674 | 2.902 | 1.004 | 2 | True | 36.456 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.041 | 0.098 | 0.166 | 0.9845 | 399/403 | 0.99 | 0.232 | 4.612 | 1 | 4 | True | 24.549 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.151 | 0.168 | 0.213 | 0.9831 | 1034/1035 | 0.999 | 0.379 | 4.874 | 1 | 4 | True | 20.68 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.049 | 0.217 | 0.255 | 0.978 | 495/495 | 1 | 0.509 | 5.25 | 1 | 4 | True | 20.27 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.26 | 0.715 | 1.017 | 0.9709 | 108/110 | 0.982 | 0.442 | 3.221 | 1.001 | 4 | True | 19.073 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.017 | 0.076 | 0.104 | 0.9913 | 1118/1120 | 0.998 | 0.178 | 3.581 | 1 | 4 | True | 16.085 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.085 | 0.27 | 0.536 | 0.9865 | 848/850 | 0.998 | 0.283 | 5.663 | 0.999 | 4 | True | 20.135 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.078 | 0.595 | 1.042 | 0.9654 | 91/94 | 0.968 | 0.395 | 3.571 | 1 | 3 | True | 22.712 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.027 | 0.07 | 0.152 | 0.9936 | 532/535 | 0.994 | 0.078 | 2.976 | 1 | 4 | True | 22.638 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.051 | 0.77 | 1.208 | 0.9747 | 187/188 | 0.995 | 0.519 | 3.766 | 1.001 | 3 | True | 23.376 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.832 | 1.467 | 2.277 | 0.812 | 29/39 | 0.744 | 1.086 | 6.197 | 0.997 | 3 | True | 20.159 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.039 | 0.087 | 0.172 | 0.9916 | 1399/1409 | 0.993 | 0.103 | 4.957 | 1 | 4 | True | 19.1 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.082 | 0.11 | 0.211 | 0.99 | 1038/1039 | 0.999 | 0.218 | 5.004 | 1 | 4 | True | 27.249 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 766/923 | 0.83 | 0.886 | 4.946 | 0.997 | 1 | True | 20.445 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=766, totalMatches=923, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 0.894 | 6.126 | 12.134 | 0.868 | 26/30 | 0.867 | 1.358 | 3.354 | 1.009 | 2 | True | 18.272 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.681 | 3.194 | 7.571 | 0.7753 | 84/123 | 0.683 | 1.165 | 2.919 | 0.998 | 2 | True | 24.108 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 196.939 | - | - | 0 | 900/903 | 0.997 | 0.851 | 4.465 | 1.001 | 1 | True | 25.212 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=900, totalMatches=903, error=196.939, tolerance=35 |
| i_melon_1_2 | illumination | 1-2 | True | 0.076 | 0.235 | 0.64 | 0.9829 | 779/780 | 0.999 | 0.379 | 5.033 | 1.001 | 4 | True | 22.046 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.139 | 0.589 | 1.013 | 0.9677 | 572/587 | 0.974 | 0.421 | 5.927 | 1 | 4 | True | 25.702 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.077 | 1.041 | 1.617 | 0.9944 | 692/693 | 0.999 | 0.112 | 5.913 | 0.999 | 4 | True | 22.134 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.436 | 1.194 | 1.92 | 0.9497 | 184/188 | 0.979 | 0.892 | 6.819 | 1 | 2 | True | 25.356 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.269 | 3.78 | 5.653 | 0.9018 | 110/124 | 0.887 | 0.836 | 6.021 | 1.005 | 2 | True | 15.104 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 1.009 | 1.377 | 3.071 | 0.9185 | 132/141 | 0.936 | 1.074 | 5.292 | 0.997 | 4 | True | 30.198 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.214 | 0.45 | 0.843 | 0.9534 | 299/306 | 0.977 | 0.787 | 5.652 | 0.999 | 4 | True | 18.59 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.016 | 0.071 | 0.18 | 0.9935 | 1094/1096 | 0.998 | 0.127 | 4.159 | 1 | 4 | True | 21.034 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.601 | 1.644 | 2.621 | 0.8954 | 54/60 | 0.9 | 1.147 | 4.226 | 1.005 | 2 | True | 27.32 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.43 | 1.881 | 3.324 | 0.9751 | 376/377 | 0.997 | 0.543 | 5.597 | 0.997 | 4 | True | 20.26 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | - | - | 0 | 0/1 | 0 | - | - | 0 | 0 | False | 16.891 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=1, error=241.446, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.144 | 0.513 | 0.985 | 0.9356 | 932/1021 | 0.913 | 0.382 | 6.011 | 0.998 | 4 | True | 18.235 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.034 | 0.274 | 0.6 | 0.9806 | 343/344 | 0.997 | 0.413 | 5.006 | 0.999 | 4 | True | 27.329 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.114 | 0.191 | 0.411 | 0.9779 | 783/794 | 0.986 | 0.336 | 5.912 | 0.999 | 4 | True | 27.046 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.873 | 2.003 | 2.326 | 0.9607 | 236/240 | 0.983 | 0.698 | 4.1 | 0.999 | 2 | True | 23.31 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.089 | 0.153 | 0.222 | 0.9798 | 484/485 | 0.998 | 0.441 | 3.673 | 1 | 4 | True | 18.261 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.214 | 0.458 | 0.994 | 0.9708 | 527/527 | 1 | 0.676 | 6.04 | 0.998 | 4 | True | 21.408 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.093 | 0.176 | 0.372 | 0.987 | 588/588 | 1 | 0.301 | 5.863 | 1 | 4 | True | 25.017 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.299 | 0.63 | 1.133 | 0.9696 | 752/753 | 0.999 | 0.686 | 4.928 | 1 | 4 | True | 23.122 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 1.009 | 1.377 | 3.071 | 0.9185 | 132/141 | 0.936 | 1.074 | 5.292 | 0.997 | 4 | True | 27.675 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 2.128 | 12.136 | 26.03 | 0.7994 | 15/22 | 0.682 | 0.592 | 0.996 | 1.003 | 2 | True | 22.568 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.108 | 0.218 | 0.316 | 0.9852 | 1086/1089 | 0.997 | 0.307 | 5.892 | 1 | 4 | True | 16.843 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.589 | 1.249 | 1.943 | 0.9609 | 160/160 | 1 | 0.904 | 4.564 | 0.995 | 4 | True | 12.88 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.102 | 0.439 | 0.88 | 0.9764 | 627/628 | 0.998 | 0.527 | 4.938 | 1.001 | 4 | True | 24.865 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.645 | 4.084 | 10.896 | 0.9104 | 28/31 | 0.903 | 0.841 | 2.423 | 1.018 | 2 | True | 19.717 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.031 | 0.081 | 0.122 | 0.9944 | 1387/1388 | 0.999 | 0.12 | 4.978 | 1 | 4 | True | 28.107 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.297 | 0.442 | 0.492 | 0.9477 | 638/672 | 0.949 | 0.565 | 4.894 | 0.999 | 4 | True | 24.414 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.463 | 0.363 | 0.481 | 0.9568 | 438/449 | 0.976 | 0.688 | 6.578 | 0.999 | 4 | True | 24.381 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156 | - | - | 0 | 430/442 | 0.973 | 1.073 | 4.936 | 1.014 | 0 | True | 25.098 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=430, totalMatches=442, error=279.156, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.217 | 1.635 | 2.543 | 0.9443 | 372/377 | 0.987 | 1.121 | 5.79 | 0.62 | 2 | True | 14.931 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.084 | 0.804 | 1.173 | 0.957 | 647/649 | 0.997 | 0.956 | 5.468 | 0.868 | 2 | True | 30.11 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.49 | 0.582 | 0.757 | 0.9532 | 202/202 | 1 | 1.084 | 4.256 | 0.731 | 4 | True | 23.591 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 317.071 | - | - | 0 | 159/173 | 0.919 | 1.399 | 5.202 | 1.903 | 0 | True | 24.331 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=159, totalMatches=173, error=317.071, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 116.339 | - | - | 0 | 164/171 | 0.959 | 1.301 | 5.112 | 1.015 | 1 | True | 32.809 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=164, totalMatches=171, error=116.339, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 260.984 | - | - | 0 | 236/254 | 0.929 | 1.306 | 4.83 | 0.661 | 1 | True | 22.193 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=236, totalMatches=254, error=260.984, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.12 | 0.755 | 1.441 | 0.9511 | 601/605 | 0.993 | 1.048 | 5.297 | 1.094 | 2 | True | 25.434 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.523 | 2.408 | 4.096 | 0.8392 | 44/54 | 0.815 | 1.364 | 4.16 | 0.384 | 3 | True | 22.264 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.224 | 0.563 | 0.978 | 0.9535 | 471/476 | 0.989 | 0.943 | 2.985 | 0.461 | 4 | True | 24.352 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.477 | 0.771 | 1.388 | 0.9515 | 335/341 | 0.982 | 0.897 | 3.588 | 0.351 | 3 | True | 25.716 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.416 | 0.634 | 0.822 | 0.915 | 134/140 | 0.957 | 0.802 | 3.047 | 0.183 | 4 | True | 25.929 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.178 | 1.831 | 2.506 | 0.91 | 421/455 | 0.925 | 1.132 | 4.474 | 0.721 | 3 | True | 29.619 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 283.268 | - | - | 0 | 390/432 | 0.903 | 1.122 | 4.953 | 0.778 | 1 | True | 30.121 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=390, totalMatches=432, error=283.268, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 217.228 | - | - | 0 | 412/423 | 0.974 | 0.969 | 5.188 | 1.123 | 1 | True | 30.91 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=412, totalMatches=423, error=217.228, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 227.419 | - | - | 0 | 374/392 | 0.954 | 1.099 | 4.304 | 0.843 | 1 | True | 33.261 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=374, totalMatches=392, error=227.419, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.323 | 1.515 | 1.938 | 0.9545 | 113/113 | 1 | 1.052 | 4.029 | 0.529 | 4 | True | 24.016 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.221 | 0.473 | 0.62 | 0.9138 | 662/723 | 0.916 | 0.921 | 4.944 | 0.742 | 4 | True | 28.896 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 378.828 | - | - | 0 | 17/30 | 0.567 | 1.152 | 3.975 | 2.178 | 1 | True | 24.981 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=17, totalMatches=30, error=378.828, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 131.097 | - | - | 0 | 248/274 | 0.905 | 1.388 | 6.216 | 1.774 | 1 | True | 23.31 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=248, totalMatches=274, error=131.097, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.173 | 0.391 | 0.572 | 0.9361 | 309/323 | 0.957 | 0.927 | 3.258 | 0.587 | 4 | True | 26.067 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.725 | 1.844 | 2.449 | 0.8683 | 108/127 | 0.85 | 1.144 | 3.695 | 0.696 | 2 | True | 26.966 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.875 | - | - | 0 | 46/49 | 0.939 | 1.087 | 4.092 | 1.133 | 1 | True | 16.284 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=46, totalMatches=49, error=243.875, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 119.45 | - | - | 0 | 586/637 | 0.92 | 0.979 | 4.291 | 1.023 | 0 | True | 25.312 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=586, totalMatches=637, error=119.45, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.093 | 0.329 | 0.548 | 0.9436 | 452/466 | 0.97 | 0.923 | 5.784 | 0.681 | 3 | True | 30.318 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.233 | 5.986 | 10.387 | 0.8239 | 233/302 | 0.772 | 1.166 | 4.502 | 1.337 | 2 | True | 20.872 | - | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.096 | 0.565 | 0.933 | 0.9491 | 812/829 | 0.979 | 0.917 | 6.276 | 0.711 | 4 | True | 30.712 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.116 | 0.545 | 1.179 | 0.9341 | 504/528 | 0.955 | 0.946 | 4.104 | 0.461 | 4 | True | 28.897 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 157.731 | - | - | 0 | 379/414 | 0.915 | 1.096 | 5.625 | 1.369 | 0 | True | 27.858 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=379, totalMatches=414, error=157.731, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.392 | 3.25 | 5.895 | 0.8752 | 171/196 | 0.872 | 1.265 | 4.994 | 1.281 | 2 | True | 25.185 | - | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 97.944 | - | - | 0 | 559/617 | 0.906 | 0.966 | 5.343 | 1.16 | 0 | True | 23.778 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=559, totalMatches=617, error=97.944, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.148 | 0.896 | 1.316 | 0.9296 | 424/439 | 0.966 | 1.193 | 5.647 | 0.737 | 2 | True | 25.228 | - | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.878 | 3.261 | 5.742 | 0.9282 | 142/147 | 0.966 | 1.228 | 3.784 | 0.49 | 3 | True | 25.795 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 132.914 | - | - | 0 | 692/727 | 0.952 | 0.96 | 4.765 | 0.764 | 1 | True | 20.723 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=692, totalMatches=727, error=132.914, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 248.894 | - | - | 0 | 516/519 | 0.994 | 1.078 | 4.401 | 1.194 | 0 | True | 29.096 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=516, totalMatches=519, error=248.894, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.993 | 2.727 | 3.899 | 0.9048 | 247/261 | 0.946 | 1.521 | 5.914 | 1.039 | 2 | True | 25.071 | - | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.272 | 3.005 | 5.973 | 0.9135 | 238/250 | 0.952 | 1.391 | 6.039 | 1.046 | 2 | True | 25.273 | - | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.265 | 2.556 | 4.851 | 0.9529 | 142/142 | 1 | 1.089 | 6.044 | 0.842 | 3 | True | 19.943 | - | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 234.57 | - | - | 0 | 353/364 | 0.97 | 1.556 | 5.384 | 0.961 | 1 | True | 27.291 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=353, totalMatches=364, error=234.57, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.467 | 1.314 | 1.774 | 0.9622 | 287/287 | 1 | 0.876 | 2.904 | 0.276 | 4 | True | 32.325 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.191 | 0.516 | 0.793 | 0.9508 | 314/319 | 0.984 | 0.94 | 3.145 | 0.417 | 4 | True | 28.487 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 267.972 | - | - | 0 | 141/154 | 0.916 | 1.163 | 4.257 | 1.113 | 0 | True | 22.814 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=141, totalMatches=154, error=267.972, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 19.469 | - | - | 0 | 1082/1085 | 0.997 | 0.887 | 5.001 | 1.012 | 1 | True | 26.041 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1082, totalMatches=1085, error=19.469, tolerance=35 |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.102 | 0.546 | 0.828 | 0.9443 | 816/843 | 0.968 | 0.88 | 5.259 | 0.971 | 2 | True | 30.743 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 189.489 | - | - | 0 | 119/127 | 0.937 | 1.203 | 4.596 | 0.87 | 1 | True | 29.852 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=119, totalMatches=127, error=189.489, tolerance=35 |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.657 | 2.111 | 4.412 | 0.9181 | 268/286 | 0.937 | 1.094 | 4.624 | 0.9 | 4 | True | 25.07 | - | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.138 | 0.372 | 0.551 | 0.9579 | 510/510 | 1 | 0.973 | 4.614 | 0.515 | 4 | True | 22.357 | - | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.276 | 0.575 | 0.834 | 0.9136 | 113/122 | 0.926 | 1.061 | 3.684 | 0.763 | 3 | True | 17.925 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 177.838 | - | - | 0 | 505/567 | 0.891 | 0.852 | 3.527 | 1.117 | 0 | True | 28.374 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=505, totalMatches=567, error=177.838, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 73.318 | - | - | 0 | 228/254 | 0.898 | 1.012 | 4.564 | 0.83 | 1 | True | 30.663 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=228, totalMatches=254, error=73.318, tolerance=35 |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.084 | 3.214 | 7.72 | 0.9053 | 422/462 | 0.913 | 1.089 | 5.572 | 0.99 | 2 | True | 20.524 | - | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.553 | 1.742 | 2.907 | 0.8819 | 203/233 | 0.871 | 1.095 | 4.584 | 0.898 | 2 | True | 31.176 | - | - |
| v_war_1_2 | viewpoint | 1-2 | True | 3.629 | 7.599 | 15.145 | 0.8668 | 85/100 | 0.85 | 1.172 | 5.944 | 0.751 | 2 | True | 25.411 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 358.203 | - | - | 0 | 480/497 | 0.966 | 1.013 | 5.086 | 1.007 | 0 | True | 24.768 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=480, totalMatches=497, error=358.203, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.517 | 0.872 | 1.32 | 0.9371 | 117/122 | 0.959 | 0.935 | 4.538 | 0.368 | 4 | True | 24.178 | - | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.299 | 3.81 | 5.171 | 0.8891 | 232/261 | 0.889 | 1.153 | 4.16 | 0.683 | 2 | True | 24.356 | - | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.326 | 1.103 | 2.074 | 0.9407 | 155/156 | 0.994 | 1.291 | 4.728 | 1.199 | 2 | True | 23.503 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 219.078 | - | - | 0 | 179/188 | 0.952 | 1.04 | 4.268 | 0.844 | 1 | True | 27.997 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=179, totalMatches=188, error=219.078, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 324.619 | - | - | 0 | 343/345 | 0.994 | 1.019 | 4.929 | 1.01 | 1 | True | 23.624 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=343, totalMatches=345, error=324.619, tolerance=35 |
