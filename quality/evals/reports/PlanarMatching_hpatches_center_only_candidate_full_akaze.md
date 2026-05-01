# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:55:18.9933386+00:00`
Operator: `PlanarMatching`
Dataset: `HPatches`
DatasetKind: `public HPatches real-image homography feature matching benchmark`
Accepted: `True`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 116 |
| Passed | 78 |
| Failed | 38 |
| Pass rate | 0.6724 |
| Mean position error px | 8648.632 |
| P95 position error px | 118.723 |
| P95 corner error px | 8.254 |
| Mean inliers | 226.845 |
| Mean score | 0.7914 |
| Runtime ms | 28424.523 |
| Max features | 1600 |
| Min inliers | 6 |
| Match ratio | 0.75 |
| RANSAC threshold px | 5 |
| Min inlier ratio | 0.2 |
| Detector type | AKAZE |
| Score threshold | 0.5 |
| Multi-scale | True |
| Scale range | 0.2 |
| ORB FAST threshold | 20 |
| ORB edge threshold | 15 |
| AKAZE detector threshold | 0.001 |
| Allow center-only projection | True |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.383 | 0.549 | 0.837 | 0.8925 | 277/278 | 0.996 | 0.492 | 3.554 | 0.999 | 4 | True | 201.361 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.445 | 0.959 | 1.389 | 0.8329 | 97/98 | 0.99 | 0.496 | 3.363 | 1.001 | 3 | True | 178.782 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 8.038 | 13.879 | 26.647 | 0.7963 | 17/18 | 0.944 | 0.714 | 3.913 | 1.013 | 2 | True | 112.454 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 2.795 | 3.468 | 5.705 | 0.8647 | 279/291 | 0.959 | 0.463 | 6.028 | 0.995 | 3 | True | 195.305 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.751 | 1.021 | 2.072 | 0.7727 | 285/300 | 0.95 | 0.536 | 3.792 | 1.002 | 2 | True | 234.681 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.481 | 0.606 | 0.895 | 0.769 | 298/300 | 0.993 | 0.977 | 5.644 | 0.827 | 4 | True | 189.834 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 1.305 | 1.859 | 3.133 | 0.784 | 286/300 | 0.953 | 0.652 | 5.132 | 1.005 | 2 | True | 200.637 | - | - |
| i_castle_1_2 | illumination | 1-2 | True | 10.199 | 13.578 | 18.545 | 0.6927 | 11/12 | 0.917 | 1.018 | 1.746 | 1.561 | 2 | True | 153.583 | - | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.168 | 0.569 | 1.072 | 0.7893 | 286/300 | 0.953 | 0.299 | 5.403 | 0.998 | 4 | True | 226.926 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.052 | 0.099 | 0.145 | 0.8387 | 300/300 | 1 | 0.075 | 0.519 | 1 | 4 | True | 207.042 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 1.657 | 2.136 | 4.863 | 0.733 | 25/26 | 0.962 | 0.823 | 2.692 | 0.994 | 3 | True | 193.761 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.205 | 0.735 | 0.987 | 0.7791 | 150/153 | 0.98 | 0.87 | 3.616 | 1 | 4 | True | 179.884 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 2.282 | 3.321 | 6.168 | 0.7308 | 25/25 | 1 | 1.343 | 4.309 | 1.564 | 2 | True | 176.367 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.066 | 0.153 | 0.309 | 0.8632 | 298/299 | 0.997 | 0.452 | 3.928 | 1 | 4 | True | 182.721 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.243 | 0.274 | 0.312 | 0.8836 | 300/300 | 1 | 0.162 | 3.341 | 1 | 4 | True | 166.879 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.177 | 0.268 | 0.516 | 0.8329 | 296/300 | 0.987 | 0.546 | 3.122 | 1.001 | 4 | True | 109.934 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.335 | 0.387 | 0.942 | 0.8367 | 65/65 | 1 | 0.324 | 4.204 | 0.999 | 4 | True | 94.5 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.082 | 0.155 | 0.284 | 0.9362 | 200/201 | 0.995 | 0.208 | 5.038 | 1 | 4 | True | 100.09 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.149 | 0.258 | 0.455 | 0.8391 | 300/300 | 1 | 0.198 | 1.083 | 0.999 | 4 | True | 109.43 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.377 | 0.536 | 0.832 | 0.7501 | 107/108 | 0.991 | 0.686 | 3.167 | 0.827 | 4 | True | 141.26 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.106 | 0.424 | 0.602 | 0.8057 | 300/300 | 1 | 0.27 | 2.87 | 0.999 | 4 | True | 201.938 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.352 | 0.774 | 1.35 | 0.7504 | 128/131 | 0.977 | 0.733 | 4.586 | 0.824 | 4 | True | 157.806 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 0.876 | 1.257 | 2.031 | 0.7024 | 56/63 | 0.889 | 0.854 | 2.888 | 0.996 | 4 | True | 146.792 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.085 | 0.153 | 0.237 | 0.8525 | 300/300 | 1 | 0.146 | 0.983 | 0.999 | 4 | True | 129.108 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.077 | 0.135 | 0.22 | 0.8082 | 300/300 | 1 | 0.147 | 1.077 | 1 | 4 | True | 191.032 | - | - |
| i_leuven_1_2 | illumination | 1-2 | True | 1.024 | 1.677 | 3.802 | 0.9434 | 247/248 | 0.996 | 0.28 | 3.844 | 0.999 | 1 | True | 157.415 | - | - |
| i_lionday_1_2 | illumination | 1-2 | True | 0.12 | 1.559 | 2.146 | 0.7041 | 42/49 | 0.857 | 1.356 | 3.383 | 1.001 | 2 | True | 119.313 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.979 | 0.985 | 1.394 | 0.767 | 261/268 | 0.974 | 0.696 | 4.37 | 1.001 | 2 | True | 152.396 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 1.801 | 1.858 | 2.056 | 0.8209 | 300/300 | 1 | 0.281 | 1.698 | 1.001 | 1 | True | 176.49 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.265 | 0.825 | 1.419 | 0.824 | 295/300 | 0.983 | 0.631 | 5.053 | 1.001 | 4 | True | 181.286 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.138 | 0.349 | 0.666 | 0.7892 | 299/300 | 0.997 | 0.516 | 3.584 | 1 | 4 | True | 214.54 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.183 | 0.337 | 0.55 | 0.8118 | 299/300 | 0.997 | 0.138 | 1.239 | 1 | 4 | True | 202.866 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.019 | 1.86 | 5.194 | 0.7823 | 268/300 | 0.893 | 0.804 | 6.931 | 1.004 | 3 | True | 183.07 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 2.392 | 4.468 | 8.254 | 0.6601 | 28/34 | 0.824 | 1.648 | 4.051 | 0.824 | 2 | True | 92.899 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.637 | 0.763 | 1.124 | 0.7438 | 284/300 | 0.947 | 1.126 | 4.919 | 1 | 4 | True | 200.763 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.13 | 0.707 | 1.1 | 0.7923 | 119/139 | 0.856 | 0.74 | 4.528 | 0.999 | 4 | True | 107.801 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.049 | 0.159 | 0.243 | 0.9002 | 300/300 | 1 | 0.168 | 2.335 | 1.001 | 4 | True | 158.096 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 1.166 | 1.342 | 3.84 | 0.6935 | 135/149 | 0.906 | 1.462 | 5.316 | 0.83 | 3 | True | 188.809 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.703 | 0.778 | 2.301 | 0.8087 | 152/160 | 0.95 | 0.728 | 5.431 | 0.998 | 4 | True | 172.344 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 1000000 | - | - | 0.3284 | 0/1 | 0 | 0 | 0 | 0 | 0 | False | 146.285 | Insufficient feature matches (1 < 6). | isMatch=False, score=0.328, inliers=0, totalMatches=1, error=1000000, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.424 | 0.665 | 1.271 | 0.8672 | 190/193 | 0.984 | 0.81 | 4.578 | 0.827 | 4 | True | 115.19 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.245 | 0.349 | 0.497 | 0.8553 | 300/300 | 1 | 0.624 | 4.695 | 1 | 4 | True | 182.643 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.212 | 0.526 | 0.862 | 0.797 | 300/300 | 1 | 0.391 | 2.59 | 1 | 4 | True | 219.827 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 2.082 | 2.096 | 2.278 | 0.7998 | 295/300 | 0.983 | 0.427 | 2.33 | 1 | 2 | True | 169.237 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.103 | 0.411 | 0.551 | 0.8768 | 178/179 | 0.994 | 0.463 | 3.037 | 1 | 4 | True | 112.617 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.503 | 0.606 | 0.671 | 0.8165 | 299/300 | 0.997 | 0.725 | 4.461 | 0.826 | 4 | True | 125.138 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.205 | 0.31 | 0.487 | 0.7926 | 291/300 | 0.97 | 0.528 | 4.013 | 1.001 | 4 | True | 196.373 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.781 | 0.86 | 1.335 | 0.9142 | 211/213 | 0.991 | 0.413 | 4.369 | 1 | 4 | True | 155.92 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 0.637 | 0.763 | 1.124 | 0.7438 | 284/300 | 0.947 | 1.126 | 4.919 | 1 | 4 | True | 323.444 | - | - |
| i_tools_1_2 | illumination | 1-2 | False | 153.71 | 162.773 | 361.617 | 0.7407 | 8/8 | 1 | 0.421 | 0.921 | 0.453 | 2 | True | 288.86 | - | isMatch=True, score=0.741, inliers=8, totalMatches=8, error=153.71, tolerance=35 |
| i_toy_1_2 | illumination | 1-2 | True | 0.087 | 0.687 | 1.124 | 0.8769 | 184/192 | 0.958 | 0.638 | 4.583 | 1.003 | 4 | True | 135.936 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.919 | 1.453 | 2.918 | 0.8169 | 53/55 | 0.964 | 0.572 | 3.179 | 0.999 | 4 | True | 166.995 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.181 | 0.344 | 0.568 | 0.7945 | 300/300 | 1 | 0.496 | 3.873 | 1 | 4 | True | 332.22 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 0.821 | 1.16 | 2.425 | 0.7439 | 66/67 | 0.985 | 0.906 | 3.03 | 1.005 | 2 | True | 152.396 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.046 | 0.091 | 0.12 | 0.9213 | 300/300 | 1 | 0.118 | 1.09 | 1 | 4 | True | 278.497 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.094 | 0.549 | 0.702 | 0.7937 | 298/300 | 0.993 | 0.434 | 2.587 | 1.001 | 4 | True | 339.336 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.633 | 0.713 | 1.083 | 0.8199 | 253/278 | 0.91 | 0.599 | 4.418 | 1 | 4 | True | 258.571 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.459 | 0.942 | 1.228 | 0.848 | 299/300 | 0.997 | 0.681 | 4.312 | 0.704 | 0 | True | 279.34 | - | isMatch=True, score=0.848, inliers=299, totalMatches=300, error=66.459, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | False | 45.672 | 2.18 | 4.41 | 0.8238 | 46/46 | 1 | 0.896 | 2.326 | 0.615 | 2 | True | 135.107 | - | isMatch=True, score=0.824, inliers=46, totalMatches=46, error=45.672, tolerance=35 |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 14.371 | 0.641 | 0.93 | 0.7982 | 299/300 | 0.997 | 0.317 | 1.355 | 0.869 | 2 | True | 367.238 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | False | 97.086 | 0.564 | 0.943 | 0.795 | 258/259 | 0.996 | 0.604 | 3.847 | 0.729 | 4 | True | 266.317 | - | isMatch=True, score=0.795, inliers=258, totalMatches=259, error=97.086, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 52.419 | 1.308 | 2.223 | 0.7889 | 213/215 | 0.991 | 0.851 | 3.239 | 1.321 | 0 | True | 273.729 | - | isMatch=True, score=0.789, inliers=213, totalMatches=215, error=52.419, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 77.887 | 7.601 | 9.506 | 0.7333 | 281/300 | 0.937 | 1.373 | 6.426 | 0.706 | 1 | True | 367.477 | - | isMatch=True, score=0.733, inliers=281, totalMatches=300, error=77.887, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.955 | 0.409 | 0.515 | 0.8126 | 300/300 | 1 | 0.788 | 4.659 | 1.041 | 1 | True | 245.118 | - | - |
| v_bees_1_2 | viewpoint | 1-2 | False | 68.399 | 1.1 | 2.073 | 0.7935 | 299/300 | 0.997 | 0.45 | 3.113 | 0.904 | 2 | True | 332.578 | - | isMatch=True, score=0.793, inliers=299, totalMatches=300, error=68.399, tolerance=35 |
| v_beyus_1_2 | viewpoint | 1-2 | True | 29.82 | 2.742 | 5.01 | 0.7111 | 79/84 | 0.94 | 1.544 | 4.938 | 0.474 | 4 | True | 269.597 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.442 | 0.259 | 0.682 | 0.8105 | 300/300 | 1 | 0.58 | 3.311 | 0.569 | 4 | True | 272.013 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | False | 35.72 | 0.427 | 0.743 | 0.7767 | 299/300 | 0.997 | 0.751 | 3.189 | 0.548 | 3 | True | 333.679 | - | isMatch=True, score=0.777, inliers=299, totalMatches=300, error=35.72, tolerance=35 |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 14.746 | 0.538 | 1.126 | 0.745 | 116/118 | 0.983 | 0.579 | 2.255 | 0.225 | 4 | True | 301.026 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | False | 71.744 | 0.404 | 0.653 | 0.7844 | 290/291 | 0.997 | 0.842 | 4.306 | 0.501 | 3 | True | 331.984 | - | isMatch=True, score=0.784, inliers=290, totalMatches=291, error=71.744, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | True | 0.65 | 0.574 | 0.933 | 0.7892 | 300/300 | 1 | 0.553 | 2.184 | 0.541 | 1 | True | 376.544 | - | - |
| v_bricks_1_2 | viewpoint | 1-2 | True | 25.093 | 0.551 | 0.839 | 0.7975 | 300/300 | 1 | 0.297 | 1.176 | 0.929 | 1 | True | 351.562 | - | - |
| v_busstop_1_2 | viewpoint | 1-2 | False | 65.668 | 1.821 | 2.411 | 0.7831 | 299/300 | 0.997 | 0.574 | 2.953 | 0.696 | 1 | True | 372.826 | - | isMatch=True, score=0.783, inliers=299, totalMatches=300, error=65.668, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 28.248 | 0.795 | 1.297 | 0.7478 | 269/281 | 0.957 | 1.022 | 3.873 | 0.826 | 4 | True | 330.183 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 12.725 | 0.869 | 1.759 | 0.8013 | 300/300 | 1 | 0.295 | 1.498 | 0.915 | 4 | True | 337.948 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 221.663 | 3.214 | 6.867 | 0.7135 | 22/23 | 0.957 | 0.984 | 2.434 | 2.453 | 1 | True | 290.516 | - | isMatch=True, score=0.714, inliers=22, totalMatches=23, error=221.663, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 118.723 | 2.761 | 4.328 | 0.7447 | 267/300 | 0.89 | 1.009 | 4.459 | 1.227 | 1 | True | 308.934 | - | isMatch=True, score=0.745, inliers=267, totalMatches=300, error=118.723, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 4.751 | 0.4 | 0.659 | 0.7981 | 299/300 | 0.997 | 0.336 | 1.726 | 0.587 | 4 | True | 344.594 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | False | 52.292 | 2.204 | 5.031 | 0.7666 | 299/300 | 0.997 | 0.995 | 4.466 | 0.866 | 2 | True | 316.829 | - | isMatch=True, score=0.767, inliers=299, totalMatches=300, error=52.292, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 53.857 | 1.303 | 2.106 | 0.8144 | 80/81 | 0.988 | 1.112 | 3.415 | 0.934 | 1 | True | 226.433 | - | isMatch=True, score=0.814, inliers=80, totalMatches=81, error=53.857, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.456 | 1.931 | 3.956 | 0.788 | 297/300 | 0.99 | 0.519 | 2.309 | 0.846 | 0 | True | 346.84 | - | isMatch=True, score=0.788, inliers=297, totalMatches=300, error=62.456, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 30.258 | 0.469 | 0.755 | 0.7872 | 300/300 | 1 | 0.476 | 2.59 | 0.472 | 3 | True | 321.213 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | False | 149.49 | 3.987 | 8.355 | 0.8087 | 294/300 | 0.98 | 0.86 | 5.405 | 0.92 | 2 | True | 260.306 | - | isMatch=True, score=0.809, inliers=294, totalMatches=300, error=149.49, tolerance=35 |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 1.67 | 0.347 | 0.456 | 0.7927 | 299/300 | 0.997 | 0.465 | 2.58 | 0.587 | 4 | True | 374.394 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 7.123 | 0.231 | 0.372 | 0.7945 | 300/300 | 1 | 0.393 | 2.797 | 0.571 | 4 | True | 339.333 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 87.944 | 1.918 | 2.742 | 0.7886 | 298/300 | 0.993 | 0.589 | 3.91 | 0.948 | 0 | True | 348.602 | - | isMatch=True, score=0.789, inliers=298, totalMatches=300, error=87.944, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | False | 158.076 | 2.009 | 4.799 | 0.7627 | 227/231 | 0.983 | 0.993 | 4.272 | 1.054 | 2 | True | 322.343 | - | isMatch=True, score=0.763, inliers=227, totalMatches=231, error=158.076, tolerance=35 |
| v_grace_1_2 | viewpoint | 1-2 | False | 66.988 | 0.975 | 1.634 | 0.7935 | 298/300 | 0.993 | 0.697 | 3.742 | 1.161 | 0 | True | 276.671 | - | isMatch=True, score=0.794, inliers=298, totalMatches=300, error=66.988, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 23.431 | 0.727 | 1.546 | 0.781 | 300/300 | 1 | 0.775 | 3.828 | 0.91 | 2 | True | 366.328 | - | - |
| v_home_1_2 | viewpoint | 1-2 | False | 56.077 | 2.007 | 4.039 | 0.7466 | 171/175 | 0.977 | 0.827 | 4.836 | 0.489 | 3 | True | 319.933 | - | isMatch=True, score=0.747, inliers=171, totalMatches=175, error=56.077, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | True | 0.74 | 0.393 | 0.576 | 0.8427 | 299/300 | 0.997 | 0.51 | 4.055 | 0.63 | 1 | True | 268.106 | - | - |
| v_london_1_2 | viewpoint | 1-2 | False | 45.222 | 1.069 | 1.695 | 0.7981 | 300/300 | 1 | 0.304 | 1.807 | 0.83 | 0 | True | 319.598 | - | isMatch=True, score=0.798, inliers=300, totalMatches=300, error=45.222, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | False | 70.757 | 2.243 | 2.718 | 0.761 | 298/300 | 0.993 | 1.214 | 4.273 | 1.037 | 2 | True | 327.5 | - | isMatch=True, score=0.761, inliers=298, totalMatches=300, error=70.757, tolerance=35 |
| v_man_1_2 | viewpoint | 1-2 | False | 113.897 | 2.194 | 2.812 | 0.792 | 284/300 | 0.947 | 0.824 | 5.072 | 0.731 | 2 | True | 304.784 | - | isMatch=True, score=0.792, inliers=284, totalMatches=300, error=113.897, tolerance=35 |
| v_maskedman_1_2 | viewpoint | 1-2 | False | 79.937 | 2.689 | 4.443 | 0.8249 | 107/110 | 0.973 | 0.676 | 2.878 | 0.694 | 3 | True | 220.363 | - | isMatch=True, score=0.825, inliers=107, totalMatches=110, error=79.937, tolerance=35 |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 37.779 | 0.949 | 1.774 | 0.7896 | 297/300 | 0.99 | 1.051 | 4.315 | 0.669 | 1 | True | 319.898 | - | isMatch=True, score=0.79, inliers=297, totalMatches=300, error=37.779, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 25.803 | 0.876 | 1.343 | 0.7773 | 300/300 | 1 | 0.837 | 4.613 | 0.43 | 4 | True | 349.109 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 10.106 | 0.353 | 0.557 | 0.7875 | 300/300 | 1 | 0.511 | 2.153 | 0.29 | 4 | True | 363.339 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 45.926 | 1.193 | 1.625 | 0.7726 | 34/34 | 1 | 1.396 | 4.244 | 0.918 | 0 | True | 250.673 | - | isMatch=True, score=0.773, inliers=34, totalMatches=34, error=45.926, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | True | 6.505 | 0.257 | 0.394 | 0.8151 | 300/300 | 1 | 0.223 | 0.997 | 1.012 | 1 | True | 329.243 | - | - |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 26.182 | 0.483 | 0.661 | 0.7992 | 297/300 | 0.99 | 0.245 | 1.78 | 0.971 | 2 | True | 332.244 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | True | 23.468 | 3.785 | 6.433 | 0.7268 | 139/146 | 0.952 | 0.898 | 4.299 | 0.606 | 1 | True | 355.906 | - | - |
| v_talent_1_2 | viewpoint | 1-2 | False | 108.15 | 1.078 | 1.559 | 0.7913 | 252/254 | 0.992 | 0.581 | 2.917 | 0.741 | 4 | True | 275.921 | - | isMatch=True, score=0.791, inliers=252, totalMatches=254, error=108.15, tolerance=35 |
| v_tempera_1_2 | viewpoint | 1-2 | True | 6.689 | 0.378 | 0.491 | 0.8306 | 243/244 | 0.996 | 0.75 | 4.608 | 0.805 | 4 | True | 265.849 | - | - |
| v_there_1_2 | viewpoint | 1-2 | False | 50.143 | 0.949 | 1.284 | 0.7691 | 74/79 | 0.937 | 1.214 | 3.402 | 0.762 | 3 | True | 234.346 | - | isMatch=True, score=0.769, inliers=74, totalMatches=79, error=50.143, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 45.145 | 0.575 | 0.713 | 0.7963 | 300/300 | 1 | 0.337 | 2.604 | 0.926 | 0 | True | 329.245 | - | isMatch=True, score=0.796, inliers=300, totalMatches=300, error=45.145, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | True | 32.841 | 0.478 | 0.751 | 0.7827 | 295/300 | 0.983 | 0.567 | 3.247 | 0.829 | 1 | True | 371.853 | - | - |
| v_wall_1_2 | viewpoint | 1-2 | False | 84.216 | 1.232 | 1.879 | 0.8409 | 118/119 | 0.992 | 0.76 | 3.255 | 0.986 | 2 | True | 221.352 | - | isMatch=True, score=0.841, inliers=118, totalMatches=119, error=84.216, tolerance=35 |
| v_wapping_1_2 | viewpoint | 1-2 | False | 36.272 | 0.326 | 0.384 | 0.7766 | 297/300 | 0.99 | 0.681 | 2.922 | 0.901 | 2 | True | 324.209 | - | isMatch=True, score=0.777, inliers=297, totalMatches=300, error=36.272, tolerance=35 |
| v_war_1_2 | viewpoint | 1-2 | False | 101.535 | 4.7 | 5.898 | 0.6889 | 93/104 | 0.894 | 1.301 | 4.289 | 0.533 | 2 | True | 348.604 | - | isMatch=True, score=0.689, inliers=93, totalMatches=104, error=101.535, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 47.512 | 1.097 | 1.601 | 0.8007 | 300/300 | 1 | 0.595 | 3.306 | 0.697 | 0 | True | 331.895 | - | isMatch=True, score=0.801, inliers=300, totalMatches=300, error=47.512, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | False | 36.617 | 0.368 | 0.641 | 0.7808 | 223/228 | 0.978 | 0.746 | 4.571 | 0.576 | 4 | True | 273.185 | - | isMatch=True, score=0.781, inliers=223, totalMatches=228, error=36.617, tolerance=35 |
| v_wormhole_1_2 | viewpoint | 1-2 | False | 89.717 | 2.501 | 3.247 | 0.7693 | 298/300 | 0.993 | 0.92 | 4.668 | 0.566 | 2 | True | 323.746 | - | isMatch=True, score=0.769, inliers=298, totalMatches=300, error=89.717, tolerance=35 |
| v_wounded_1_2 | viewpoint | 1-2 | False | 56.511 | 1.37 | 1.836 | 0.8215 | 299/300 | 0.997 | 0.517 | 4.489 | 0.988 | 2 | True | 289.7 | - | isMatch=True, score=0.821, inliers=299, totalMatches=300, error=56.511, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.778 | 1.104 | 1.544 | 0.7829 | 300/300 | 1 | 0.842 | 3.791 | 0.847 | 1 | True | 325.321 | - | isMatch=True, score=0.783, inliers=300, totalMatches=300, error=49.778, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | True | 3.007 | 2.534 | 4.064 | 0.79 | 144/148 | 0.973 | 0.881 | 4.08 | 1.014 | 1 | True | 271.319 | - | - |
