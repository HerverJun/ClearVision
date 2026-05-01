# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-05-01T03:17:03.9316998+00:00`
Operator: `OrbFeatureMatch`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 80 |
| Passed | 68 |
| Failed | 12 |
| Pass rate | 0.85 |
| Mean position error px | 39.051 |
| P95 position error px | 291.625 |
| P95 corner error px | 25.406 |
| Mean inliers | 262.838 |
| Mean score | 0.7961 |
| Runtime ms | 1728.745 |
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
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.13 | 0.555 | 1.128 | 0.9543 | 380/395 | 0.962 | 0.479 | 3.617 | 1.002 | 4 | True | 272.084 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 1.077 | 9.894 | 25.406 | 0.9776 | 28/28 | 1 | 0.432 | 4.272 | 0.979 | 2 | True | 18.041 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.214 | 2.54 | 3.932 | 0.96 | 109/110 | 0.991 | 0.675 | 3.411 | 1.001 | 2 | True | 11.042 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.144 | 1.144 | 1.449 | 0.987 | 608/614 | 0.99 | 0.146 | 4.197 | 1.001 | 2 | True | 15.696 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.42 | 0.563 | 0.849 | 0.9638 | 282/287 | 0.983 | 0.514 | 4.008 | 1 | 4 | True | 18.842 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.286 | 0.732 | 0.99 | 0.9478 | 248/253 | 0.98 | 0.797 | 3.935 | 1.002 | 4 | True | 20.462 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.236 | 1.721 | 2.6 | 0.9449 | 168/172 | 0.977 | 0.816 | 3.965 | 0.996 | 4 | True | 20.515 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 4.858 | 45.704 | 68.188 | 0.9756 | 9/9 | 1 | 0.471 | 1.746 | 0.944 | 2 | True | 16.246 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.5 | 3.092 | 6.371 | 0.9812 | 47/47 | 1 | 0.362 | 2.304 | 0.994 | 3 | True | 17.939 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.02 | 0.065 | 0.114 | 0.9949 | 936/936 | 1 | 0.098 | 3.558 | 1 | 4 | True | 25.742 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.025 | 1.121 | 2.034 | 0.9357 | 11/12 | 0.917 | 0.356 | 1.123 | 0.997 | 4 | True | 20.162 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.029 | 0.673 | 1.419 | 0.9661 | 96/96 | 1 | 0.653 | 2.934 | 1.002 | 3 | True | 20.286 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 1.864 | 11.103 | 19.726 | 0.9221 | 34/36 | 0.944 | 0.912 | 3.367 | 1.017 | 2 | True | 31.803 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.018 | 0.314 | 0.478 | 0.972 | 213/217 | 0.982 | 0.344 | 4.638 | 1 | 4 | True | 18.278 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.154 | 0.185 | 0.331 | 0.9811 | 607/607 | 1 | 0.364 | 4.577 | 1 | 4 | True | 15.243 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.439 | 2.284 | 3.991 | 0.9343 | 287/306 | 0.938 | 0.608 | 4.313 | 1.009 | 2 | True | 14.024 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.265 | 0.882 | 1.359 | 0.9404 | 38/40 | 0.95 | 0.619 | 2.888 | 1.001 | 3 | True | 14.242 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.009 | 0.087 | 0.118 | 0.9918 | 707/708 | 0.999 | 0.144 | 3.591 | 1 | 4 | True | 12.257 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.124 | 0.207 | 0.322 | 0.9801 | 525/527 | 0.996 | 0.343 | 4.139 | 0.999 | 4 | True | 14.499 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.22 | 0.986 | 1.934 | 0.9609 | 50/52 | 0.962 | 0.346 | 1.801 | 0.999 | 3 | True | 17.446 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.026 | 0.063 | 0.082 | 0.9923 | 366/368 | 0.995 | 0.091 | 2.976 | 1 | 4 | True | 18.065 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.133 | 0.416 | 0.643 | 0.967 | 123/124 | 0.992 | 0.551 | 3.703 | 0.999 | 4 | True | 18.564 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.879 | 42.036 | 75.442 | 0.629 | 7/19 | 0.368 | 0.455 | 0.723 | 0.956 | 2 | True | 19.711 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.04 | 0.147 | 0.25 | 0.9934 | 878/881 | 0.997 | 0.091 | 3.593 | 1 | 4 | True | 13.732 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.08 | 0.109 | 0.201 | 0.9879 | 585/587 | 0.997 | 0.197 | 3.494 | 1 | 4 | True | 20.609 | - | - |
| i_leuven_1_2 | illumination | 1-2 | False | 358.14 | - | - | 0 | 510/594 | 0.859 | 0.935 | 4.189 | 0.996 | 1 | True | 14.945 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=510, totalMatches=594, error=358.14, tolerance=35 |
| i_lionday_1_2 | illumination | 1-2 | True | 1.123 | 4.457 | 7.654 | 0.8895 | 19/21 | 0.905 | 1.12 | 2.691 | 1.003 | 2 | True | 13.644 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.692 | 3.331 | 6.06 | 0.7547 | 53/77 | 0.688 | 1.425 | 3.588 | 1.003 | 2 | True | 17.442 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.025 | 1.781 | 2.044 | 0.9518 | 547/550 | 0.995 | 0.871 | 7.016 | 1.001 | 2 | True | 19.574 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.128 | 0.158 | 0.37 | 0.9744 | 475/478 | 0.994 | 0.427 | 4.169 | 1 | 4 | True | 16.422 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.112 | 0.567 | 0.826 | 0.9694 | 341/349 | 0.977 | 0.348 | 3.596 | 1 | 4 | True | 20.179 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.036 | 1.249 | 2.184 | 0.9951 | 480/480 | 1 | 0.095 | 4.027 | 0.998 | 3 | True | 16.54 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.287 | 1.213 | 1.393 | 0.9435 | 89/91 | 0.978 | 0.857 | 3.804 | 1 | 2 | True | 19.999 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.335 | 0.831 | 1.09 | 0.9197 | 73/78 | 0.936 | 0.87 | 4.005 | 0.999 | 4 | True | 10.224 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.826 | 1.415 | 3.513 | 0.9313 | 75/77 | 0.974 | 1.05 | 4.499 | 0.997 | 4 | True | 22.333 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.225 | 0.44 | 0.546 | 0.9448 | 188/195 | 0.964 | 0.683 | 4.822 | 0.999 | 4 | True | 12.641 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.044 | 0.148 | 0.234 | 0.9889 | 712/717 | 0.993 | 0.14 | 4.151 | 1 | 4 | True | 15.224 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 1.635 | 1.189 | 3.174 | 0.8693 | 29/33 | 0.879 | 1.235 | 4.074 | 1 | 3 | True | 22.441 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.551 | 1.66 | 2.19 | 0.951 | 188/193 | 0.974 | 0.671 | 5.008 | 0.996 | 4 | True | 15.968 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 241.446 | - | - | 0 | 0/1 | 0 | - | - | 0 | 0 | False | 13.23 | At least four point correspondences are required. | isMatch=False, score=0, inliers=0, totalMatches=1, error=241.446, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.151 | 0.236 | 0.397 | 0.9818 | 702/703 | 0.999 | 0.337 | 3.645 | 0.999 | 4 | True | 13.18 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.159 | 0.457 | 1.059 | 0.9724 | 221/221 | 1 | 0.532 | 3.595 | 0.999 | 4 | True | 19.112 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.074 | 0.209 | 0.365 | 0.9712 | 488/499 | 0.978 | 0.322 | 4.264 | 0.999 | 4 | True | 20.541 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.927 | 2.151 | 2.48 | 0.9394 | 120/125 | 0.96 | 0.745 | 4.045 | 1.001 | 2 | True | 17.463 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.103 | 0.16 | 0.253 | 0.9721 | 246/248 | 0.992 | 0.453 | 5.037 | 1.001 | 4 | True | 13.953 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.187 | 0.407 | 0.779 | 0.961 | 319/322 | 0.991 | 0.653 | 5.132 | 0.999 | 4 | True | 14.652 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.122 | 0.178 | 0.305 | 0.9738 | 374/381 | 0.982 | 0.311 | 4.228 | 1 | 4 | True | 19.186 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.297 | 0.647 | 1.091 | 0.9641 | 470/472 | 0.996 | 0.647 | 3.691 | 1 | 4 | True | 14.888 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.826 | 1.415 | 3.513 | 0.9313 | 75/77 | 0.974 | 1.05 | 4.499 | 0.997 | 4 | True | 22.118 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 4.169 | 14.486 | 21.794 | 0.8225 | 14/19 | 0.737 | 0.632 | 1.72 | 0.983 | 3 | True | 17.55 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.028 | 0.37 | 0.708 | 0.9838 | 699/703 | 0.994 | 0.252 | 4.942 | 1 | 4 | True | 11.354 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.561 | 1.36 | 1.924 | 0.9472 | 116/118 | 0.983 | 0.838 | 2.91 | 1 | 3 | True | 11.331 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.131 | 0.81 | 1.393 | 0.9656 | 351/353 | 0.994 | 0.604 | 4.847 | 1.002 | 2 | True | 18.581 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.211 | 2.716 | 7.392 | 0.9626 | 21/21 | 1 | 0.72 | 2.23 | 1.01 | 2 | True | 14.078 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.035 | 0.087 | 0.176 | 0.9936 | 842/842 | 1 | 0.124 | 3.493 | 1 | 4 | True | 24.329 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.244 | 0.362 | 0.457 | 0.9402 | 359/382 | 0.94 | 0.514 | 3.867 | 0.999 | 4 | True | 20.15 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.65 | 0.906 | 2.072 | 0.9213 | 273/295 | 0.925 | 0.727 | 4.672 | 0.998 | 4 | True | 17.558 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 391.906 | - | - | 0 | 275/279 | 0.986 | 1.076 | 3.704 | 1.014 | 0 | True | 18.481 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=275, totalMatches=279, error=391.906, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.059 | 1.834 | 2.757 | 0.9367 | 223/227 | 0.982 | 1.033 | 4.117 | 0.62 | 2 | True | 11.336 | - | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.075 | 0.678 | 1.311 | 0.9495 | 405/406 | 0.998 | 0.948 | 6.293 | 0.868 | 2 | True | 26.806 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.898 | 2.927 | 5.656 | 0.8836 | 110/124 | 0.887 | 1.048 | 5.288 | 0.733 | 4 | True | 19.125 | - | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 108.405 | - | - | 0 | 105/110 | 0.955 | 1.344 | 4.216 | 1.904 | 0 | True | 19.782 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=105, totalMatches=110, error=108.405, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 210.718 | - | - | 0 | 113/123 | 0.919 | 1.263 | 3.859 | 1.021 | 1 | True | 27.033 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=113, totalMatches=123, error=210.718, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | False | 290.88 | - | - | 0 | 162/162 | 1 | 1.196 | 4.751 | 0.659 | 1 | True | 17.073 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=162, totalMatches=162, error=290.88, tolerance=35 |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.142 | 0.89 | 1.492 | 0.9259 | 349/362 | 0.964 | 1.049 | 4.227 | 1.095 | 2 | True | 21.352 | - | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 4.953 | 21.976 | 45.244 | 0.7768 | 28/38 | 0.737 | 1.513 | 3.528 | 0.351 | 4 | True | 18.235 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.305 | 0.549 | 0.782 | 0.9456 | 238/241 | 0.988 | 0.917 | 2.914 | 0.461 | 4 | True | 18.411 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.633 | 1.358 | 3.343 | 0.8349 | 170/218 | 0.78 | 0.849 | 3.062 | 0.35 | 3 | True | 21.528 | - | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.46 | 0.405 | 0.95 | 0.925 | 83/84 | 0.988 | 0.805 | 2.986 | 0.183 | 4 | True | 21.815 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.712 | 2.191 | 3.582 | 0.787 | 219/304 | 0.72 | 1.141 | 4.592 | 0.722 | 3 | True | 26.07 | - | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 79.051 | - | - | 0 | 275/281 | 0.979 | 1.113 | 5.229 | 0.779 | 1 | True | 25.143 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=275, totalMatches=281, error=79.051, tolerance=35 |
| v_bricks_1_2 | viewpoint | 1-2 | False | 299.73 | - | - | 0 | 241/272 | 0.886 | 1.019 | 4.835 | 1.12 | 1 | True | 23.374 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=241, totalMatches=272, error=299.73, tolerance=35 |
| v_busstop_1_2 | viewpoint | 1-2 | False | 291.625 | - | - | 0 | 259/263 | 0.985 | 1.041 | 3.424 | 0.843 | 1 | True | 27.468 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=259, totalMatches=263, error=291.625, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.571 | 4.807 | 7.21 | 0.8427 | 58/72 | 0.806 | 0.97 | 2.517 | 0.531 | 4 | True | 18.807 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.201 | 0.59 | 1.148 | 0.9434 | 424/431 | 0.984 | 0.92 | 4.041 | 0.742 | 4 | True | 24.051 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 310.93 | - | - | 0 | 19/26 | 0.731 | 1.295 | 3.999 | 2.361 | 1 | True | 19.765 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=19, totalMatches=26, error=310.93, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 255.466 | - | - | 0 | 155/179 | 0.866 | 1.342 | 6.528 | 1.77 | 1 | True | 17.803 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=155, totalMatches=179, error=255.466, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.277 | 0.734 | 1.161 | 0.919 | 175/186 | 0.941 | 0.935 | 2.71 | 0.586 | 4 | True | 22.342 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.577 | 2.218 | 4.276 | 0.8861 | 85/93 | 0.914 | 1.284 | 5.792 | 0.702 | 2 | True | 20.588 | - | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.875 | - | - | 0 | 45/48 | 0.938 | 1.116 | 4.28 | 1.134 | 1 | True | 15.997 | Projected quadrilateral is invalid. | isMatch=False, score=0, inliers=45, totalMatches=48, error=243.875, tolerance=35 |
