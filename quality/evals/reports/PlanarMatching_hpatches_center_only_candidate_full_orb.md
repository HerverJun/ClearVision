# HPatches Feature Match Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T08:54:49.9920609+00:00`
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
| Mean position error px | 8653.648 |
| P95 position error px | 114.786 |
| P95 corner error px | 12.833 |
| Mean inliers | 213.103 |
| Mean score | 0.7505 |
| Runtime ms | 7317.268 |
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
| Allow center-only projection | True |

## Cases

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Score | Inliers | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Center in | Runtime ms | Homography failure | Failure |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- | --- |
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.464 | 0.536 | 1.386 | 0.7941 | 296/300 | 0.987 | 0.356 | 2.842 | 0.695 | 3 | True | 199.933 | - | - |
| i_autannes_1_2 | illumination | 1-2 | True | 2.38 | 3.564 | 7.02 | 0.7598 | 37/37 | 1 | 0.275 | 3.152 | 0.992 | 2 | True | 53.537 | - | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.651 | 1.482 | 2.254 | 0.7625 | 125/126 | 0.992 | 0.661 | 3.454 | 0.998 | 3 | True | 36.37 | - | - |
| i_books_1_2 | illumination | 1-2 | True | 0.183 | 0.496 | 0.629 | 0.8101 | 299/300 | 0.997 | 0.102 | 2.361 | 0.694 | 4 | True | 53.457 | - | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.289 | 0.478 | 0.869 | 0.7783 | 290/300 | 0.967 | 0.443 | 3.536 | 1.001 | 4 | True | 68.693 | - | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.394 | 0.614 | 1.294 | 0.7672 | 293/300 | 0.977 | 0.779 | 4.181 | 1.002 | 3 | True | 66.293 | - | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.542 | 1.465 | 2.533 | 0.76 | 226/230 | 0.983 | 0.693 | 3.969 | 0.996 | 3 | True | 65.565 | - | - |
| i_castle_1_2 | illumination | 1-2 | False | 36.088 | 46.587 | 113.594 | 0.7063 | 11/12 | 0.917 | 0.618 | 1.133 | 0.755 | 1 | True | 58.952 | - | isMatch=True, score=0.706, inliers=11, totalMatches=12, error=36.088, tolerance=35 |
| i_chestnuts_1_2 | illumination | 1-2 | True | 2.852 | 3.52 | 6.795 | 0.7493 | 53/53 | 1 | 0.407 | 2.188 | 0.992 | 3 | True | 67.304 | - | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8142 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 71.238 | - | - |
| i_crownday_1_2 | illumination | 1-2 | True | 8.064 | 8.827 | 18.221 | 0.7306 | 15/15 | 1 | 0.833 | 1.966 | 1.576 | 1 | True | 60.832 | - | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.104 | 0.335 | 0.557 | 0.7509 | 113/115 | 0.983 | 0.54 | 2.764 | 1 | 4 | True | 61.303 | - | - |
| i_dc_1_2 | illumination | 1-2 | True | 1.015 | 2.181 | 4.341 | 0.7164 | 48/51 | 0.941 | 0.763 | 3.3 | 1.003 | 2 | True | 72.839 | - | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.072 | 0.163 | 0.285 | 0.7886 | 283/287 | 0.986 | 0.259 | 4.646 | 1 | 4 | True | 59.48 | - | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.121 | 0.251 | 0.318 | 0.8076 | 300/300 | 1 | 0.113 | 1.971 | 1 | 4 | True | 54.231 | - | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.155 | 1.442 | 2.283 | 0.7663 | 292/293 | 0.997 | 0.997 | 4.837 | 0.692 | 3 | True | 52.925 | - | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.156 | 0.447 | 0.539 | 0.7655 | 83/83 | 1 | 0.282 | 2.322 | 0.998 | 4 | True | 45.219 | - | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.041 | 0.056 | 0.073 | 0.8257 | 300/300 | 1 | 0.017 | 1.184 | 1 | 4 | True | 34.248 | - | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.129 | 0.283 | 0.446 | 0.8059 | 299/300 | 0.997 | 0.093 | 3.939 | 0.999 | 4 | True | 56.11 | - | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.429 | 0.774 | 1.101 | 0.7407 | 66/68 | 0.971 | 0.375 | 2.023 | 1.002 | 4 | True | 61.447 | - | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.14 | 0.231 | 0.351 | 0.8049 | 300/300 | 1 | 0.079 | 3.56 | 1 | 4 | True | 62.015 | - | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.181 | 0.282 | 0.356 | 0.7618 | 163/164 | 0.994 | 0.494 | 3.756 | 1 | 4 | True | 64.227 | - | - |
| i_kions_1_2 | illumination | 1-2 | True | 2.515 | 5.767 | 8.035 | 0.6641 | 15/18 | 0.833 | 1.111 | 3.226 | 0.681 | 3 | True | 56.876 | - | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8136 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 53.007 | - | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8113 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 66.843 | - | - |
| i_leuven_1_2 | illumination | 1-2 | True | 0.616 | 1.913 | 4.421 | 0.7825 | 297/300 | 0.99 | 0.829 | 3.279 | 1.562 | 1 | True | 50.43 | - | - |
| i_lionday_1_2 | illumination | 1-2 | True | 6.211 | 7.055 | 12.833 | 0.6964 | 22/24 | 0.917 | 1.051 | 1.905 | 0.699 | 2 | True | 47.276 | - | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.385 | 0.925 | 1.458 | 0.6271 | 72/105 | 0.686 | 0.972 | 3.233 | 0.697 | 3 | True | 68.717 | - | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 1.789 | 1.802 | 2.111 | 0.7915 | 299/300 | 0.997 | 0.463 | 3.47 | 0.694 | 2 | True | 66.315 | - | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.088 | 0.158 | 0.213 | 0.8018 | 300/300 | 1 | 0.196 | 4.225 | 1 | 4 | True | 57.505 | - | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.376 | 0.477 | 1.011 | 0.7952 | 296/300 | 0.987 | 0.165 | 2.414 | 1 | 4 | True | 67.818 | - | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0 | 0 | 0 | 0.8121 | 300/300 | 1 | 0 | 0 | 1 | 4 | True | 59.857 | - | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 0.994 | 1.445 | 1.712 | 0.7288 | 94/96 | 0.979 | 1.053 | 3.436 | 0.693 | 3 | True | 62.844 | - | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.897 | 1.492 | 2.895 | 0.7284 | 74/78 | 0.949 | 0.861 | 4.019 | 0.993 | 4 | True | 29.784 | - | - |
| i_objects_1_2 | illumination | 1-2 | True | 1.285 | 1.325 | 2.854 | 0.7206 | 112/116 | 0.966 | 1.161 | 4.869 | 0.997 | 4 | True | 73.031 | - | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.231 | 0.372 | 0.551 | 0.7644 | 211/217 | 0.972 | 0.632 | 4.876 | 0.999 | 4 | True | 41.336 | - | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.007 | 0.017 | 0.033 | 0.8118 | 300/300 | 1 | 0.007 | 0.995 | 1 | 4 | True | 52.045 | - | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.417 | 1.418 | 1.884 | 0.6882 | 39/43 | 0.907 | 1.198 | 4.481 | 1.002 | 3 | True | 67.476 | - | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.635 | 1.536 | 2.866 | 0.7709 | 267/275 | 0.971 | 0.571 | 4.836 | 0.997 | 4 | True | 53.273 | - | - |
| i_pool_1_2 | illumination | 1-2 | False | 1000000 | - | - | 0.3146 | 0/3 | 0 | 0 | 0 | 0 | 0 | False | 35.956 | Insufficient feature matches (3 < 6). | isMatch=False, score=0.315, inliers=0, totalMatches=3, error=1000000, tolerance=35 |
| i_porta_1_2 | illumination | 1-2 | True | 0.046 | 0.1 | 0.202 | 0.8103 | 300/300 | 1 | 0.134 | 3.568 | 1 | 4 | True | 44.52 | - | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.134 | 0.304 | 0.535 | 0.7868 | 300/300 | 1 | 0.46 | 4.915 | 0.999 | 4 | True | 68.32 | - | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.049 | 0.08 | 0.128 | 0.7999 | 296/300 | 0.987 | 0.09 | 2.121 | 1 | 4 | True | 71.63 | - | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.8 | 1.845 | 2.358 | 0.7594 | 173/175 | 0.989 | 0.653 | 4.079 | 0.999 | 2 | True | 60.898 | - | - |
| i_school_1_2 | illumination | 1-2 | True | 0.205 | 0.298 | 0.591 | 0.7983 | 300/300 | 1 | 0.301 | 3.606 | 1.001 | 4 | True | 48.119 | - | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.156 | 0.573 | 0.968 | 0.7826 | 298/300 | 0.993 | 0.568 | 4.667 | 0.998 | 4 | True | 55.898 | - | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.079 | 0.172 | 0.291 | 0.8003 | 300/300 | 1 | 0.209 | 4.023 | 1 | 4 | True | 64.598 | - | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.508 | 0.745 | 1.235 | 0.8045 | 300/300 | 1 | 0.334 | 2.64 | 0.694 | 4 | True | 49.564 | - | - |
| i_table_1_2 | illumination | 1-2 | True | 1.285 | 1.325 | 2.854 | 0.7206 | 112/116 | 0.966 | 1.161 | 4.869 | 0.997 | 4 | True | 74.386 | - | - |
| i_tools_1_2 | illumination | 1-2 | True | 20.178 | 29.593 | 48.764 | 0.6271 | 8/12 | 0.667 | 0.272 | 0.468 | 1.084 | 2 | True | 63.293 | - | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.087 | 0.159 | 0.344 | 0.8087 | 300/300 | 1 | 0.047 | 2.448 | 1.001 | 4 | True | 45.58 | - | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.952 | 1.133 | 1.779 | 0.7636 | 110/111 | 0.991 | 0.78 | 2.898 | 1 | 3 | True | 31.214 | - | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.335 | 0.575 | 0.913 | 0.7967 | 300/300 | 1 | 0.314 | 3.66 | 1.001 | 4 | True | 69.595 | - | - |
| i_village_1_2 | illumination | 1-2 | True | 3.323 | 4.654 | 9.389 | 0.7177 | 30/31 | 0.968 | 0.887 | 3.313 | 1.015 | 2 | True | 50.551 | - | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.045 | 0.064 | 0.138 | 0.814 | 300/300 | 1 | 0.01 | 0.965 | 0.694 | 4 | True | 61.386 | - | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.139 | 0.303 | 0.586 | 0.785 | 294/300 | 0.98 | 0.434 | 3.381 | 0.694 | 4 | True | 67.216 | - | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.128 | 0.54 | 0.696 | 0.7832 | 299/300 | 0.997 | 0.55 | 2.926 | 0.694 | 4 | True | 60.19 | - | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 66.199 | 1.26 | 1.583 | 0.7679 | 299/300 | 0.997 | 1.077 | 4.43 | 0.837 | 0 | True | 60.162 | - | isMatch=True, score=0.768, inliers=299, totalMatches=300, error=66.199, tolerance=35 |
| v_adam_1_2 | viewpoint | 1-2 | False | 44.731 | 1.913 | 3.688 | 0.7732 | 154/154 | 1 | 0.918 | 3.569 | 0.51 | 2 | True | 28.55 | - | isMatch=True, score=0.773, inliers=154, totalMatches=154, error=44.731, tolerance=35 |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 14.903 | 0.891 | 1.181 | 0.7836 | 300/300 | 1 | 0.743 | 3.056 | 0.716 | 2 | True | 78.628 | - | - |
| v_artisans_1_2 | viewpoint | 1-2 | False | 97.186 | 0.861 | 2.202 | 0.7482 | 176/177 | 0.994 | 0.994 | 4.656 | 0.731 | 4 | True | 62.638 | - | isMatch=True, score=0.748, inliers=176, totalMatches=177, error=97.186, tolerance=35 |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 55.926 | 7.333 | 13.66 | 0.7363 | 167/170 | 0.982 | 1.19 | 4.284 | 1.567 | 0 | True | 62.942 | - | isMatch=True, score=0.736, inliers=167, totalMatches=170, error=55.926, tolerance=35 |
| v_azzola_1_2 | viewpoint | 1-2 | False | 82.029 | 1.23 | 2.095 | 0.709 | 140/149 | 0.94 | 1.382 | 5.51 | 0.701 | 1 | True | 83.213 | - | isMatch=True, score=0.709, inliers=140, totalMatches=149, error=82.029, tolerance=35 |
| v_bark_1_2 | viewpoint | 1-2 | True | 0.242 | 1.342 | 3.44 | 0.7407 | 159/162 | 0.981 | 1.025 | 3.499 | 0.461 | 1 | True | 57.092 | - | - |
| v_bees_1_2 | viewpoint | 1-2 | False | 68.79 | 0.545 | 1.296 | 0.7753 | 299/300 | 0.997 | 0.879 | 3.21 | 0.903 | 2 | True | 64.82 | - | isMatch=True, score=0.775, inliers=299, totalMatches=300, error=68.79, tolerance=35 |
| v_beyus_1_2 | viewpoint | 1-2 | True | 32.81 | 3.122 | 4.267 | 0.7079 | 59/62 | 0.952 | 1.214 | 3.646 | 0.317 | 3 | True | 58.417 | - | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.504 | 0.677 | 1.414 | 0.7592 | 219/223 | 0.982 | 0.841 | 2.739 | 0.321 | 4 | True | 61.582 | - | - |
| v_bird_1_2 | viewpoint | 1-2 | False | 36.02 | 0.963 | 1.802 | 0.7668 | 284/285 | 0.996 | 0.912 | 3.127 | 0.351 | 3 | True | 65.873 | - | isMatch=True, score=0.767, inliers=284, totalMatches=285, error=36.02, tolerance=35 |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 15.032 | 0.771 | 1.597 | 0.7404 | 117/120 | 0.975 | 0.813 | 2.624 | 0.284 | 4 | True | 63.942 | - | - |
| v_blueprint_1_2 | viewpoint | 1-2 | False | 70.152 | 2.252 | 3.434 | 0.7641 | 297/300 | 0.99 | 1.036 | 4.866 | 0.595 | 3 | True | 72.722 | - | isMatch=True, score=0.764, inliers=297, totalMatches=300, error=70.152, tolerance=35 |
| v_boat_1_2 | viewpoint | 1-2 | True | 0.791 | 1.398 | 1.971 | 0.7727 | 300/300 | 1 | 1.001 | 4.449 | 0.962 | 1 | True | 81.677 | - | - |
| v_bricks_1_2 | viewpoint | 1-2 | True | 25.112 | 0.595 | 0.905 | 0.7655 | 296/300 | 0.987 | 0.903 | 2.969 | 1.126 | 1 | True | 78.457 | - | - |
| v_busstop_1_2 | viewpoint | 1-2 | False | 65.451 | 2.51 | 4.216 | 0.7633 | 299/300 | 0.997 | 1.014 | 3.474 | 0.585 | 1 | True | 84.573 | - | isMatch=True, score=0.763, inliers=299, totalMatches=300, error=65.451, tolerance=35 |
| v_calder_1_2 | viewpoint | 1-2 | True | 28.065 | 1.125 | 2.311 | 0.7192 | 122/130 | 0.938 | 1.04 | 3.303 | 0.65 | 4 | True | 64.539 | - | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 11.574 | 1.005 | 2.014 | 0.7787 | 300/300 | 1 | 0.807 | 3.057 | 0.741 | 4 | True | 78.027 | - | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 890.566 | 1140.266 | 3668.533 | 0.1114 | 7/20 | 0.35 | 1.192 | 2.365 | 5.939 | 0 | True | 90.737 | Projected quadrilateral is invalid. | isMatch=False, score=0.111, inliers=7, totalMatches=20, error=890.566, tolerance=35 |
| v_churchill_1_2 | viewpoint | 1-2 | False | 117.416 | 1.87 | 3.566 | 0.7519 | 273/277 | 0.986 | 1.213 | 4.879 | 1.23 | 1 | True | 61.855 | - | isMatch=True, score=0.752, inliers=273, totalMatches=277, error=117.416, tolerance=35 |
| v_circus_1_2 | viewpoint | 1-2 | True | 4.543 | 0.619 | 1.201 | 0.7703 | 290/300 | 0.967 | 0.755 | 3.627 | 0.723 | 4 | True | 68.789 | - | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | False | 52.567 | 2.143 | 5.229 | 0.7195 | 98/101 | 0.97 | 1.223 | 4.574 | 0.701 | 2 | True | 66.179 | - | isMatch=True, score=0.719, inliers=98, totalMatches=101, error=52.567, tolerance=35 |
| v_colors_1_2 | viewpoint | 1-2 | False | 53.481 | 1.324 | 2.003 | 0.7241 | 38/40 | 0.95 | 1.062 | 2.833 | 0.935 | 1 | True | 35.236 | - | isMatch=True, score=0.724, inliers=38, totalMatches=40, error=53.481, tolerance=35 |
| v_courses_1_2 | viewpoint | 1-2 | False | 62.619 | 1.88 | 3.847 | 0.7735 | 299/300 | 0.997 | 0.883 | 5.981 | 0.711 | 0 | True | 67.826 | - | isMatch=True, score=0.774, inliers=299, totalMatches=300, error=62.619, tolerance=35 |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 30.679 | 0.712 | 0.998 | 0.7711 | 299/300 | 0.997 | 0.858 | 3.426 | 0.681 | 3 | True | 77.213 | - | - |
| v_dogman_1_2 | viewpoint | 1-2 | False | 149.821 | 4.734 | 8.711 | 0.7503 | 286/296 | 0.966 | 1.18 | 4.201 | 1.093 | 2 | True | 57.524 | - | isMatch=True, score=0.75, inliers=286, totalMatches=296, error=149.821, tolerance=35 |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 1.76 | 0.562 | 1.054 | 0.7832 | 300/300 | 1 | 0.73 | 2.192 | 0.493 | 4 | True | 79.446 | - | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 6.99 | 0.928 | 1.405 | 0.7743 | 299/300 | 0.997 | 0.884 | 3.405 | 0.724 | 4 | True | 69.976 | - | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 88.227 | 1.534 | 3.782 | 0.758 | 290/300 | 0.967 | 0.942 | 4.33 | 0.949 | 0 | True | 70.989 | - | isMatch=True, score=0.758, inliers=290, totalMatches=300, error=88.227, tolerance=35 |
| v_gardens_1_2 | viewpoint | 1-2 | False | 157.526 | 3.07 | 5.023 | 0.7427 | 186/189 | 0.984 | 1.067 | 5.088 | 0.888 | 2 | True | 64.135 | - | isMatch=True, score=0.743, inliers=186, totalMatches=189, error=157.526, tolerance=35 |
| v_grace_1_2 | viewpoint | 1-2 | False | 66.281 | 0.281 | 0.516 | 0.761 | 287/300 | 0.957 | 0.86 | 3.662 | 0.806 | 0 | True | 60.502 | - | isMatch=True, score=0.761, inliers=287, totalMatches=300, error=66.281, tolerance=35 |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 23.821 | 0.673 | 1.076 | 0.7668 | 299/300 | 0.997 | 0.997 | 5.226 | 0.512 | 2 | True | 69.141 | - | - |
| v_home_1_2 | viewpoint | 1-2 | False | 56.459 | 1.574 | 2.879 | 0.732 | 160/163 | 0.982 | 1.279 | 4.453 | 0.765 | 3 | True | 65.426 | - | isMatch=True, score=0.732, inliers=160, totalMatches=163, error=56.459, tolerance=35 |
| v_laptop_1_2 | viewpoint | 1-2 | True | 1.102 | 0.495 | 0.885 | 0.7789 | 300/300 | 1 | 0.838 | 3.446 | 0.632 | 1 | True | 56.812 | - | - |
| v_london_1_2 | viewpoint | 1-2 | False | 45.712 | 0.752 | 0.979 | 0.7671 | 299/300 | 0.997 | 1.027 | 5.934 | 0.986 | 0 | True | 73.297 | - | isMatch=True, score=0.767, inliers=299, totalMatches=300, error=45.712, tolerance=35 |
| v_machines_1_2 | viewpoint | 1-2 | False | 70.543 | 1.895 | 2.139 | 0.7283 | 215/227 | 0.947 | 1.29 | 5.269 | 0.723 | 2 | True | 65.763 | - | isMatch=True, score=0.728, inliers=215, totalMatches=227, error=70.543, tolerance=35 |
| v_man_1_2 | viewpoint | 1-2 | False | 114.786 | 2.005 | 3.203 | 0.7419 | 224/230 | 0.974 | 1.179 | 4.994 | 0.868 | 2 | True | 64.117 | - | isMatch=True, score=0.742, inliers=224, totalMatches=230, error=114.786, tolerance=35 |
| v_maskedman_1_2 | viewpoint | 1-2 | False | 79.742 | 1.232 | 2.346 | 0.7393 | 129/131 | 0.985 | 1.007 | 5.586 | 0.586 | 3 | True | 53.488 | - | isMatch=True, score=0.739, inliers=129, totalMatches=131, error=79.742, tolerance=35 |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 37.955 | 2.039 | 3.343 | 0.7308 | 261/280 | 0.932 | 1.275 | 5.314 | 0.797 | 1 | True | 67.994 | - | isMatch=True, score=0.731, inliers=261, totalMatches=280, error=37.955, tolerance=35 |
| v_posters_1_2 | viewpoint | 1-2 | True | 26.676 | 1.268 | 1.75 | 0.7668 | 299/300 | 0.997 | 1.018 | 4.535 | 0.43 | 4 | True | 76.519 | - | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 10.405 | 0.393 | 0.545 | 0.7716 | 298/300 | 0.993 | 0.857 | 2.548 | 0.345 | 4 | True | 66.58 | - | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 44.946 | 1.207 | 2.166 | 0.7461 | 99/102 | 0.971 | 0.935 | 2.997 | 0.773 | 0 | True | 38.78 | - | isMatch=True, score=0.746, inliers=99, totalMatches=102, error=44.946, tolerance=35 |
| v_strand_1_2 | viewpoint | 1-2 | True | 6.623 | 0.723 | 1.062 | 0.7882 | 300/300 | 1 | 0.582 | 4.46 | 0.703 | 1 | True | 66.678 | - | - |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 26.551 | 0.972 | 1.43 | 0.7784 | 295/300 | 0.983 | 0.693 | 3.408 | 0.971 | 2 | True | 77.583 | - | - |
| v_tabletop_1_2 | viewpoint | 1-2 | True | 21.429 | 6.617 | 9.617 | 0.72 | 123/131 | 0.939 | 0.913 | 3.2 | 0.722 | 1 | True | 76.723 | - | - |
| v_talent_1_2 | viewpoint | 1-2 | False | 109.78 | 2.147 | 4.575 | 0.758 | 247/255 | 0.969 | 0.759 | 4.968 | 0.741 | 4 | True | 64.836 | - | isMatch=True, score=0.758, inliers=247, totalMatches=255, error=109.78, tolerance=35 |
| v_tempera_1_2 | viewpoint | 1-2 | True | 6.736 | 0.305 | 0.58 | 0.7804 | 300/300 | 1 | 0.797 | 4.508 | 0.635 | 4 | True | 58.932 | - | - |
| v_there_1_2 | viewpoint | 1-2 | False | 48.689 | 2.05 | 6.157 | 0.7385 | 59/60 | 0.983 | 1.053 | 2.938 | 0.526 | 3 | True | 33.15 | - | isMatch=True, score=0.738, inliers=59, totalMatches=60, error=48.689, tolerance=35 |
| v_underground_1_2 | viewpoint | 1-2 | False | 44.556 | 0.563 | 0.797 | 0.777 | 298/300 | 0.993 | 0.714 | 3.498 | 0.778 | 0 | True | 72.013 | - | isMatch=True, score=0.777, inliers=298, totalMatches=300, error=44.556, tolerance=35 |
| v_vitro_1_2 | viewpoint | 1-2 | True | 33.737 | 1.44 | 1.851 | 0.7313 | 196/211 | 0.929 | 0.993 | 3.712 | 0.687 | 1 | True | 80.147 | - | - |
| v_wall_1_2 | viewpoint | 1-2 | False | 84.796 | 1.561 | 2.233 | 0.7711 | 300/300 | 1 | 0.99 | 4.509 | 0.985 | 2 | True | 53.638 | - | isMatch=True, score=0.771, inliers=300, totalMatches=300, error=84.796, tolerance=35 |
| v_wapping_1_2 | viewpoint | 1-2 | False | 36.047 | 1.094 | 2.492 | 0.7378 | 201/205 | 0.98 | 1.199 | 4.739 | 0.899 | 2 | True | 76.644 | - | isMatch=True, score=0.738, inliers=201, totalMatches=205, error=36.047, tolerance=35 |
| v_war_1_2 | viewpoint | 1-2 | False | 105.671 | 4.81 | 8.02 | 0.7226 | 79/81 | 0.975 | 1.028 | 4.263 | 0.531 | 2 | True | 67.028 | - | isMatch=True, score=0.723, inliers=79, totalMatches=81, error=105.671, tolerance=35 |
| v_weapons_1_2 | viewpoint | 1-2 | False | 47.759 | 2.442 | 4.643 | 0.7563 | 289/300 | 0.963 | 1.034 | 4.652 | 0.827 | 0 | True | 66.196 | - | isMatch=True, score=0.756, inliers=289, totalMatches=300, error=47.759, tolerance=35 |
| v_woman_1_2 | viewpoint | 1-2 | False | 37.368 | 1.58 | 1.987 | 0.7399 | 120/123 | 0.976 | 0.821 | 2.412 | 0.305 | 4 | True | 62.841 | - | isMatch=True, score=0.74, inliers=120, totalMatches=123, error=37.368, tolerance=35 |
| v_wormhole_1_2 | viewpoint | 1-2 | False | 89.577 | 2.203 | 2.501 | 0.7469 | 230/234 | 0.983 | 1.163 | 4.483 | 0.688 | 2 | True | 65.6 | - | isMatch=True, score=0.747, inliers=230, totalMatches=234, error=89.577, tolerance=35 |
| v_wounded_1_2 | viewpoint | 1-2 | False | 62.217 | 9.18 | 10.483 | 0.7175 | 137/146 | 0.938 | 1.238 | 5.04 | 0.834 | 2 | True | 62.558 | - | isMatch=True, score=0.718, inliers=137, totalMatches=146, error=62.217, tolerance=35 |
| v_yard_1_2 | viewpoint | 1-2 | False | 49.036 | 2.019 | 2.744 | 0.7459 | 214/220 | 0.973 | 0.984 | 3.72 | 0.587 | 1 | True | 72.773 | - | isMatch=True, score=0.746, inliers=214, totalMatches=220, error=49.036, tolerance=35 |
| v_yuri_1_2 | viewpoint | 1-2 | True | 2.169 | 1.534 | 1.981 | 0.7717 | 300/300 | 1 | 0.984 | 4.967 | 1.011 | 1 | True | 60.185 | - | - |
