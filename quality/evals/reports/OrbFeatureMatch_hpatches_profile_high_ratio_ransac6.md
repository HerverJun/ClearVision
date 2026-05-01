# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:26:39.2085709+00:00`
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
| Mean position error px | 47.725 |
| P95 position error px | 283.268 |
| P95 corner error px | 8.241 |
| Mean inliers | 451.25 |
| Mean score | 0.7274 |
| Runtime ms | 2960.315 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.187 | 0.428 | 0.741 | 0.9579 | 630/652 | 0.966 | 0.544 | 4.013 | 1.002 | 4 | True | 161.742 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.482 | 1.487 | 2.26 | 0.9746 | 92/93 | 0.989 | 0.451 | 5.168 | 1 | 3 | True | 22.804 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.097 | 1.609 | 2.182 | 0.9431 | 190/200 | 0.95 | 0.681 | 4.534 | 0.995 | 3 | True | 16.058 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.214 | 0.464 | 0.809 | 0.9885 | 907/913 | 0.993 | 0.183 | 4.588 | 0.999 | 4 | True | 24.122 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.343 | 0.347 | 0.455 | 0.9505 | 492/515 | 0.955 | 0.577 | 4.734 | 1 | 4 | True | 25.225 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.305 | 0.716 | 1.187 | 0.9503 | 411/419 | 0.981 | 0.907 | 5.333 | 1.002 | 3 | True | 25.401 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.37 | 1.085 | 1.549 | 0.9514 | 294/302 | 0.974 | 0.787 | 4.597 | 0.999 | 3 | True | 24.456 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.196 | 4.957 | 6.696 | 0.8708 | 17/20 | 0.85 | 1.081 | 2.779 | 0.986 | 2 | True | 21.547 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.264 | 1.984 | 4.608 | 0.976 | 95/96 | 0.99 | 0.423 | 3.467 | 0.995 | 3 | True | 24.712 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.007 | 0.069 | 0.137 | 0.996 | 1575/1576 | 0.999 | 0.086 | 4.217 | 1 | 4 | True | 30.996 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.365 | 1.985 | 4.386 | 0.893 | 29/34 | 0.853 | 0.604 | 3.272 | 0.999 | 3 | True | 25.284 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.104 | 0.581 | 1.037 | 0.9631 | 165/168 | 0.982 | 0.628 | 3.695 | 1 | 4 | True | 25.51 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.713 | 3.092 | 9.804 | 0.8907 | 63/72 | 0.875 | 0.939 | 7.574 | 1.011 | 2 | True | 37.396 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.039 | 0.102 | 0.139 | 0.982 | 433/439 | 0.986 | 0.242 | 4.61 | 1 | 4 | True | 23.83 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.165 | 0.208 | 0.332 | 0.982 | 1090/1091 | 0.999 | 0.405 | 5.646 | 1 | 4 | True | 20.269 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.084 | 0.266 | 0.396 | 0.9711 | 564/567 | 0.995 | 0.602 | 5.236 | 1 | 4 | True | 20.606 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.297 | 0.679 | 0.978 | 0.962 | 117/121 | 0.967 | 0.459 | 3.351 | 1 | 4 | True | 19.134 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.025 | 0.073 | 0.124 | 0.9889 | 1167/1173 | 0.995 | 0.192 | 3.603 | 1 | 4 | True | 16.562 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.087 | 0.201 | 0.497 | 0.9846 | 897/899 | 0.998 | 0.328 | 5.657 | 0.999 | 4 | True | 18.99 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.111 | 0.573 | 0.965 | 0.9386 | 112/121 | 0.926 | 0.475 | 3.591 | 1 | 4 | True | 23.047 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.053 | 0.125 | 0.222 | 0.9932 | 549/552 | 0.995 | 0.088 | 2.976 | 1 | 4 | True | 23.372 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.056 | 0.548 | 0.803 | 0.9693 | 219/221 | 0.991 | 0.595 | 3.766 | 1 | 4 | True | 23.276 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.553 | 2.524 | 3.984 | 0.8315 | 41/53 | 0.774 | 1.019 | 6.099 | 0.994 | 3 | True | 21.499 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.043 | 0.087 | 0.174 | 0.9896 | 1425/1439 | 0.99 | 0.117 | 4.955 | 1 | 4 | True | 19.307 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.074 | 0.186 | 0.535 | 0.9866 | 1064/1071 | 0.993 | 0.227 | 5.022 | 1.001 | 4 | True | 26.765 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.288 | - | - | 0 | 970/975 | 0.995 | 0.92 | 5.849 | 1.001 | 1 | True | 20.578 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=970, totalMatches=975, error=259.288, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 1.784 | 2.45 | 3.159 | 0.7983 | 33/44 | 0.75 | 1.486 | 3.776 | 0.994 | 3 | True | 18.268 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.847 | 1.193 | 1.694 | 0.7734 | 111/161 | 0.689 | 1.292 | 3.337 | 0.999 | 3 | True | 23.494 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.016 | 1.886 | 2.03 | 0.938 | 931/973 | 0.957 | 0.887 | 7.035 | 1.001 | 2 | True | 25.01 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.159 | 0.539 | 0.748 | 0.9654 | 829/855 | 0.97 | 0.413 | 4.999 | 1.001 | 4 | True | 21.494 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.161 | 0.565 | 0.953 | 0.9633 | 622/642 | 0.969 | 0.453 | 5.953 | 1 | 4 | True | 25.287 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.063 | 1.011 | 1.595 | 0.9915 | 718/722 | 0.994 | 0.127 | 5.91 | 0.999 | 4 | True | 22.898 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.173 | 1.211 | 1.389 | 0.9433 | 216/222 | 0.973 | 0.968 | 6.025 | 0.999 | 3 | True | 25.574 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.616 | 0.851 | 1.575 | 0.8999 | 138/155 | 0.89 | 0.921 | 4.618 | 0.996 | 4 | True | 14.629 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.915 | 0.93 | 1.453 | 0.9221 | 167/177 | 0.944 | 1.085 | 3.797 | 0.999 | 3 | True | 27.861 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.216 | 0.437 | 0.814 | 0.9481 | 350/362 | 0.967 | 0.778 | 5.696 | 0.999 | 4 | True | 18.496 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.019 | 0.098 | 0.146 | 0.9906 | 1138/1144 | 0.995 | 0.15 | 4.169 | 1 | 4 | True | 21.179 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.649 | 1.473 | 2.653 | 0.8785 | 68/77 | 0.883 | 1.324 | 4.685 | 1.005 | 2 | True | 27.962 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.45 | 1.679 | 3.08 | 0.9733 | 414/415 | 0.998 | 0.588 | 6 | 0.997 | 4 | True | 20.932 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | - | - | 0 | 4/6 | 0.667 | 0 | 0 | 0 | 4 | True | 17.517 | Insufficient inliers (4 < 6). | isMatch=False, score=0, inliers=4, totalMatches=6, error=241.446, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.144 | 0.18 | 0.34 | 0.9807 | 1073/1075 | 0.998 | 0.423 | 6.003 | 1 | 4 | True | 18.851 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.018 | 0.301 | 0.619 | 0.9772 | 400/402 | 0.995 | 0.464 | 5.011 | 0.999 | 4 | True | 27.166 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.126 | 0.202 | 0.424 | 0.9722 | 843/860 | 0.98 | 0.392 | 5.933 | 0.999 | 4 | True | 26.895 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.917 | 2.227 | 2.312 | 0.9148 | 236/262 | 0.901 | 0.709 | 3.732 | 1 | 2 | True | 22.761 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.075 | 0.14 | 0.232 | 0.9785 | 538/539 | 0.998 | 0.473 | 3.636 | 1.001 | 4 | True | 18.844 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.197 | 0.504 | 1.057 | 0.9666 | 581/583 | 0.997 | 0.729 | 6.044 | 0.998 | 4 | True | 21.199 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.06 | 0.122 | 0.219 | 0.9827 | 631/635 | 0.994 | 0.321 | 5.829 | 1 | 4 | True | 24.511 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.323 | 0.627 | 1.123 | 0.9675 | 827/828 | 0.999 | 0.737 | 4.973 | 1 | 4 | True | 19.756 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.915 | 0.93 | 1.453 | 0.9221 | 167/177 | 0.944 | 1.085 | 3.797 | 0.999 | 3 | True | 27.716 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 3.888 | 21.717 | 42.074 | 0.7459 | 20/34 | 0.588 | 0.639 | 1.327 | 1.022 | 2 | True | 23.851 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.066 | 0.168 | 0.333 | 0.9831 | 1140/1145 | 0.996 | 0.335 | 5.912 | 1 | 4 | True | 17.537 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.506 | 1.086 | 1.782 | 0.9126 | 164/179 | 0.916 | 0.956 | 5.825 | 0.995 | 4 | True | 12.846 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.118 | 0.435 | 0.743 | 0.9731 | 688/691 | 0.996 | 0.567 | 4.927 | 1.002 | 4 | True | 24.391 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.08 | 3.77 | 8.241 | 0.9175 | 40/43 | 0.93 | 1.022 | 5.153 | 1.014 | 2 | True | 20.249 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.03 | 0.071 | 0.119 | 0.994 | 1421/1422 | 0.999 | 0.129 | 4.983 | 1 | 4 | True | 27.671 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.297 | 0.404 | 0.494 | 0.9405 | 694/739 | 0.939 | 0.602 | 5.213 | 0.999 | 4 | True | 25.306 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.447 | 0.301 | 0.44 | 0.9645 | 510/513 | 0.994 | 0.746 | 4.714 | 0.999 | 4 | True | 25.526 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 85.606 | - | - | 0 | 500/523 | 0.956 | 1.07 | 7.068 | 1.014 | 0 | True | 24.322 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=500, totalMatches=523, error=85.606, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.181 | 1.259 | 1.5 | 0.9448 | 437/441 | 0.991 | 1.163 | 5.788 | 0.62 | 2 | True | 15.276 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.069 | 0.685 | 0.881 | 0.9555 | 724/726 | 0.997 | 0.995 | 5.458 | 0.868 | 2 | True | 30.165 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.631 | 0.728 | 0.954 | 0.9457 | 235/238 | 0.987 | 1.096 | 4.406 | 0.73 | 4 | True | 23.764 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 313.373 | - | - | 0 | 218/221 | 0.986 | 1.465 | 5.356 | 1.9 | 0 | True | 24.045 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=218, totalMatches=221, error=313.373, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 116.339 | - | - | 0 | 227/235 | 0.966 | 1.332 | 5.591 | 1.012 | 1 | True | 31.943 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=227, totalMatches=235, error=116.339, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 290.88 | - | - | 0 | 278/282 | 0.986 | 1.341 | 5.133 | 0.664 | 1 | True | 22.113 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=278, totalMatches=282, error=290.88, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.106 | 0.737 | 1.557 | 0.9491 | 679/684 | 0.993 | 1.084 | 5.291 | 1.094 | 2 | True | 24.918 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.569 | 2.899 | 3.513 | 0.886 | 73/80 | 0.913 | 1.525 | 5.354 | 0.382 | 3 | True | 21.706 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.233 | 0.566 | 1.007 | 0.9504 | 489/497 | 0.984 | 0.943 | 2.981 | 0.461 | 4 | True | 23.142 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.492 | 0.885 | 1.659 | 0.9526 | 376/382 | 0.984 | 0.897 | 3.527 | 0.351 | 3 | True | 25.708 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.531 | 0.56 | 0.969 | 0.9152 | 150/156 | 0.962 | 0.852 | 3.374 | 0.183 | 4 | True | 26.298 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.184 | 1.084 | 1.833 | 0.9273 | 509/529 | 0.962 | 1.202 | 4.452 | 0.72 | 3 | True | 29.448 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 283.268 | - | - | 0 | 476/503 | 0.946 | 1.17 | 6.259 | 0.779 | 1 | True | 30.088 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=476, totalMatches=503, error=283.268, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 217.228 | - | - | 0 | 474/478 | 0.992 | 0.978 | 5.431 | 1.123 | 1 | True | 29.954 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=474, totalMatches=478, error=217.228, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 227.419 | - | - | 0 | 405/470 | 0.862 | 1.123 | 4.432 | 0.84 | 1 | True | 33.522 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=405, totalMatches=470, error=227.419, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.465 | 1.644 | 2.404 | 0.9429 | 148/150 | 0.987 | 1.152 | 4.44 | 0.528 | 4 | True | 24.449 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.259 | 0.825 | 1.802 | 0.9426 | 748/771 | 0.97 | 0.948 | 4.941 | 0.742 | 4 | True | 29.495 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 378.828 | - | - | 0 | 23/40 | 0.575 | 1.259 | 5.174 | 2.505 | 1 | True | 23.873 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=23, totalMatches=40, error=378.828, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 131.097 | - | - | 0 | 326/336 | 0.97 | 1.463 | 6.316 | 1.773 | 1 | True | 23.2 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=326, totalMatches=336, error=131.097, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.152 | 0.372 | 0.574 | 0.9117 | 353/387 | 0.912 | 0.925 | 3.293 | 0.587 | 4 | True | 26.327 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 1.058 | 2.676 | 3.082 | 0.8443 | 136/168 | 0.81 | 1.179 | 5.116 | 0.698 | 2 | True | 25.868 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.875 | - | - | 0 | 73/78 | 0.936 | 1.305 | 5.327 | 1.131 | 1 | True | 16.539 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=73, totalMatches=78, error=243.875, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 119.45 | - | - | 0 | 676/705 | 0.959 | 0.998 | 4.32 | 1.025 | 0 | True | 25.233 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=676, totalMatches=705, error=119.45, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.17 | 0.702 | 1.304 | 0.9225 | 486/521 | 0.933 | 0.939 | 5.749 | 0.68 | 3 | True | 30.38 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.257 | 3.031 | 5.467 | 0.9353 | 362/367 | 0.986 | 1.324 | 6.373 | 1.324 | 2 | True | 20.664 | - | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.138 | 0.555 | 0.648 | 0.9587 | 861/862 | 0.999 | 0.941 | 4.495 | 0.711 | 4 | True | 31.023 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.185 | 0.402 | 0.791 | 0.9566 | 566/568 | 0.996 | 0.959 | 4.116 | 0.463 | 4 | True | 28.662 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 157.731 | - | - | 0 | 432/489 | 0.883 | 1.088 | 5.629 | 1.365 | 0 | True | 27.966 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=432, totalMatches=489, error=157.731, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.243 | 2.522 | 5.407 | 0.8894 | 220/245 | 0.898 | 1.261 | 4.986 | 1.279 | 2 | True | 24.781 | - | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 97.944 | - | - | 0 | 593/695 | 0.853 | 1.014 | 5.424 | 1.161 | 0 | True | 24.216 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=593, totalMatches=695, error=97.944, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.129 | 0.514 | 0.764 | 0.9344 | 509/521 | 0.977 | 1.225 | 4.674 | 0.736 | 2 | True | 24.991 | - | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.72 | 3.874 | 4.516 | 0.8276 | 150/191 | 0.785 | 1.259 | 3.931 | 0.491 | 3 | True | 25.287 | - | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 132.914 | - | - | 0 | 812/814 | 0.998 | 0.982 | 4.88 | 0.763 | 1 | True | 21.484 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=812, totalMatches=814, error=132.914, tolerance=35 |
| v_london_1_2 | viewpoint | 1-2 | False | 248.894 | - | - | 0 | 543/573 | 0.948 | 1.067 | 3.932 | 1.192 | 0 | True | 29.155 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=543, totalMatches=573, error=248.894, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.64 | 2.089 | 3.714 | 0.9234 | 329/333 | 0.988 | 1.62 | 6.15 | 1.041 | 2 | True | 24.829 | - | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.212 | 4.738 | 10.067 | 0.8634 | 266/310 | 0.858 | 1.356 | 6.157 | 1.048 | 2 | True | 24.883 | - | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.34 | 2.374 | 4.491 | 0.9379 | 167/171 | 0.977 | 1.138 | 6.006 | 0.843 | 3 | True | 19.827 | - | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 234.57 | - | - | 0 | 415/436 | 0.952 | 1.535 | 5.842 | 0.96 | 1 | True | 27.182 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=415, totalMatches=436, error=234.57, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.44 | 1.261 | 1.674 | 0.9589 | 331/332 | 0.997 | 0.912 | 3.52 | 0.276 | 4 | True | 32.004 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.23 | 0.521 | 0.624 | 0.9523 | 366/370 | 0.989 | 0.967 | 3.475 | 0.417 | 4 | True | 28.733 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 270.843 | - | - | 0 | 196/201 | 0.975 | 1.223 | 5.831 | 1.115 | 0 | True | 24.062 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=196, totalMatches=201, error=270.843, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | False | 19.469 | - | - | 0 | 1080/1156 | 0.934 | 0.886 | 4.693 | 1.013 | 1 | True | 25.598 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=1080, totalMatches=1156, error=19.469, tolerance=35 |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.123 | 0.55 | 0.858 | 0.9351 | 865/908 | 0.953 | 0.9 | 5.258 | 0.972 | 2 | True | 31.667 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 189.489 | - | - | 0 | 164/180 | 0.911 | 1.223 | 4.555 | 0.872 | 1 | True | 29.927 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=164, totalMatches=180, error=189.489, tolerance=35 |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.652 | 1.873 | 3.648 | 0.9364 | 343/351 | 0.977 | 1.181 | 6.691 | 0.897 | 4 | True | 25.397 | - | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.138 | 0.368 | 0.461 | 0.9557 | 553/555 | 0.996 | 0.98 | 4.598 | 0.515 | 4 | True | 23.768 | - | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.218 | 0.464 | 0.633 | 0.9214 | 158/167 | 0.946 | 1.134 | 4.353 | 0.763 | 3 | True | 18.131 | - | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 177.838 | - | - | 0 | 613/628 | 0.976 | 0.92 | 5.772 | 1.12 | 0 | True | 27.431 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=613, totalMatches=628, error=177.838, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | False | 154.444 | - | - | 0 | 257/317 | 0.811 | 1.003 | 4.6 | 0.831 | 1 | True | 30.63 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=257, totalMatches=317, error=154.444, tolerance=35 |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.122 | 1.532 | 2.073 | 0.9456 | 527/530 | 0.994 | 1.188 | 5.841 | 0.986 | 2 | True | 20.931 | - | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.373 | 0.909 | 1.968 | 0.9074 | 266/289 | 0.92 | 1.13 | 4.71 | 0.9 | 2 | True | 32.107 | - | - |
| v_war_1_2 | viewpoint | 1-2 | True | 3.428 | 4.87 | 8.197 | 0.8657 | 122/142 | 0.859 | 1.316 | 5.846 | 0.753 | 2 | True | 25.726 | - | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 358.203 | - | - | 0 | 530/556 | 0.953 | 1.004 | 5.093 | 1.007 | 0 | True | 24.816 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=530, totalMatches=556, error=358.203, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.564 | 1.424 | 2.172 | 0.9541 | 141/142 | 0.993 | 0.973 | 4.631 | 0.369 | 4 | True | 23.504 | - | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.572 | 1.723 | 2.63 | 0.9428 | 311/313 | 0.994 | 1.241 | 4.723 | 0.687 | 2 | True | 24.422 | - | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.648 | 4.978 | 9.597 | 0.919 | 174/183 | 0.951 | 1.248 | 4.549 | 1.202 | 2 | True | 24.036 | - | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 219.078 | - | - | 0 | 196/228 | 0.86 | 1.046 | 4.276 | 0.845 | 1 | True | 28.06 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=196, totalMatches=228, error=219.078, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | False | 324.619 | - | - | 0 | 388/395 | 0.982 | 1.056 | 4.948 | 1.01 | 1 | True | 24.474 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=388, totalMatches=395, error=324.619, tolerance=35 |
