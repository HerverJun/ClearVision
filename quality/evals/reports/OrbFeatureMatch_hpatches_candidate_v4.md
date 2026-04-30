# OrbFeatureMatch HPatches Candidate v4

GeneratedAtUtc: `2026-04-29T15:11:48.0274803+00:00`
CandidateVersion: `v4`
SelectedProfile: `replay_safe_dense_strict`

## Candidate Summary

| Metric | Value |
|---|---:|
| Cases | 116 |
| Passed | 90 |
| Failed | 26 |
| Pass rate | 0.775862 |
| Mean position error px | 45.005668 |
| P95 position error px | 267.972396 |
| Runtime ms | 3572.659 |

## Sweep Validation

| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms | Params |
|---|---|---:|---:|---:|---:|---:|---|
| default_v3 | 1-2 | 35/59 | 0.59322 | 90.563725 | 356.422881 | 1657.237 | ratio=0.75, ransac=5.0, minInlierRatio=0.25, maxFeatures=1200, fast=20, edge=15, akazeThreshold=0.001 |
| looser_ransac_v3 | 1-2 | 35/59 | 0.59322 | 89.082827 | 356.422881 | 1630.598 | ratio=0.75, ransac=7.0, minInlierRatio=0.2, maxFeatures=1200, fast=20, edge=15, akazeThreshold=0.001 |
| orb_v3 | 1-2 | 35/59 | 0.59322 | 84.863489 | 338.706579 | 2037.297 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=1600, fast=12, edge=15, akazeThreshold=0.001 |
| dense_low_detector_threshold | 1-2 | 35/59 | 0.59322 | 81.32806 | 324.619173 | 1957.959 | ratio=0.75, ransac=7.0, minInlierRatio=0.2, maxFeatures=2000, fast=20, edge=15, akazeThreshold=0.0006 |
| dense_high_ratio_low_detector_threshold | 1-2 | 34/59 | 0.576271 | 87.190794 | 324.619173 | 1995.409 | ratio=0.82, ransac=8.0, minInlierRatio=0.15, maxFeatures=2000, fast=20, edge=15, akazeThreshold=0.0006 |
| partial_plane_low_detector_threshold | 1-2 | 34/59 | 0.576271 | 103.356781 | 380.622071 | 1958.711 | ratio=0.88, ransac=10.0, minInlierRatio=0.1, maxFeatures=2000, fast=20, edge=15, akazeThreshold=0.0005 |
| strict_geometry | 1-2 | 35/59 | 0.59322 | 81.208056 | 338.706579 | 1793.348 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=1600, fast=20, edge=15, akazeThreshold=0.001 |
| orb_low_edge_dense | 1-2 | 35/59 | 0.59322 | 88.010579 | 343.903003 | 2384.268 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=2000, fast=8, edge=5, akazeThreshold=0.001 |
| orb_low_edge_loose_ransac | 1-2 | 35/59 | 0.59322 | 86.943255 | 338.706579 | 2351.636 | ratio=0.75, ransac=8.0, minInlierRatio=0.15, maxFeatures=2000, fast=8, edge=5, akazeThreshold=0.001 |
| orb_fast_low_threshold | 1-2 | 34/59 | 0.576271 | 78.865149 | 313.373001 | 2492.844 | ratio=0.82, ransac=8.0, minInlierRatio=0.15, maxFeatures=2000, fast=6, edge=8, akazeThreshold=0.001 |
| replay_safe_dense_strict | 1-2 | 35/59 | 0.59322 | 83.520672 | 307.229725 | 2300.596 | ratio=0.7, ransac=7.0, minInlierRatio=0.25, maxFeatures=2000, fast=16, edge=10, akazeThreshold=0.001 |
| replay_safe_high_ratio | 1-2 | 35/59 | 0.59322 | 84.854061 | 324.619173 | 2159.911 | ratio=0.78, ransac=5.0, minInlierRatio=0.2, maxFeatures=2000, fast=16, edge=10, akazeThreshold=0.001 |
| replay_safe_balanced_1800 | 1-2 | 34/59 | 0.576271 | 86.715301 | 338.706579 | 1916.964 | ratio=0.7, ransac=6.0, minInlierRatio=0.25, maxFeatures=1800, fast=20, edge=10, akazeThreshold=0.001 |
| partial_plane_v4 | 1-2 | 35/59 | 0.59322 | 89.335341 | 376.654158 | 2578.895 | ratio=0.85, ransac=10.0, minInlierRatio=0.1, maxFeatures=2000, fast=6, edge=5, akazeThreshold=0.0005 |
| precision_more_features | 1-2 | 36/59 | 0.610169 | 84.4784 | 338.706579 | 2354.487 | ratio=0.65, ransac=7.0, minInlierRatio=0.2, maxFeatures=2000, fast=10, edge=10, akazeThreshold=0.001 |

## Replay Gate

| Profile | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---:|---:|---:|---:|---:|
| default_v3 | 16/20 | 0.8 | 55.895005 | 358.139701 | 587.589 |
| looser_ransac_v3 | 15/20 | 0.75 | 62.310236 | 358.139701 | 584.398 |
| orb_v3 | 15/20 | 0.75 | 66.528539 | 307.229725 | 697.104 |
| dense_low_detector_threshold | 15/20 | 0.75 | 76.060133 | 391.905789 | 710.463 |
| dense_high_ratio_low_detector_threshold | 14/20 | 0.7 | 74.566031 | 313.373001 | 669.253 |
| partial_plane_low_detector_threshold | 15/20 | 0.75 | 68.122053 | 374.233156 | 711.282 |
| strict_geometry | 15/20 | 0.75 | 71.757004 | 391.905789 | 625.697 |
| orb_low_edge_dense | 15/20 | 0.75 | 59.439094 | 303.09964 | 791.814 |
| orb_low_edge_loose_ransac | 15/20 | 0.75 | 69.438572 | 290.176744 | 793.646 |
| orb_fast_low_threshold | 15/20 | 0.75 | 68.644843 | 313.373001 | 829.595 |
| replay_safe_dense_strict | 16/20 | 0.8 | 42.956602 | 279.156349 | 784.24 |
| replay_safe_high_ratio | 16/20 | 0.8 | 45.610944 | 259.28841 | 764.735 |
| replay_safe_balanced_1800 | 16/20 | 0.8 | 49.869046 | 241.920228 | 712.645 |
| partial_plane_v4 | 15/20 | 0.75 | 71.85608 | 291.2353 | 909.642 |
| precision_more_features | 14/20 | 0.7 | 72.546619 | 361.698392 | 793.952 |

## Holdout Selection

| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---|---:|---:|---:|---:|---:|
| replay_safe_dense_strict | 1-3 | 37/59 | 0.627119 | 98.143773 | 361.471132 | 2124.6 |
| replay_safe_high_ratio | 1-3 | 37/59 | 0.627119 | 97.359189 | 369.425287 | 2135.802 |
| default_v3 | 1-3 | 34/59 | 0.576271 | 98.405715 | 433.070035 | 1617.142 |
| replay_safe_balanced_1800 | 1-3 | 37/59 | 0.627119 | 91.845553 | 378.325102 | 1921.16 |

## Selected Holdout

| Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---:|---:|---:|---:|---:|
| 1-3 | 37/59 | 0.627119 | 98.143773 | 361.471132 | 2124.6 |

## Case Diagnostics

| Case | Type | Pair | Passed | Error px | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |
|---|---|---|---|---:|---:|---:|---:|---:|---|---|
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.1527 | 0.97757 | 0.461565 | 1.000872 | 4 | True | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.090563 | 1 | 0.326053 | 0.999978 | 4 | True | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.206359 | 1 | 0.478505 | 0.997873 | 4 | True | - |
| i_books_1_2 | illumination | 1-2 | True | 0.076671 | 0.995037 | 0.126429 | 1.000268 | 4 | True | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.303064 | 0.978313 | 0.474532 | 0.999896 | 4 | True | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.301988 | 0.99 | 0.82433 | 1.001938 | 3 | True | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.668047 | 0.905213 | 0.720209 | 0.993764 | 3 | True | - |
| i_castle_1_2 | illumination | 1-2 | True | 2.779783 | 0.909091 | 0.599329 | 0.970424 | 3 | True | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.475201 | 0.984615 | 0.336967 | 0.994056 | 4 | True | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.008797 | 1 | 0.074606 | 0.999972 | 4 | True | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.17443 | 1 | 0.341776 | 1.001296 | 2 | True | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.093121 | 0.992063 | 0.505604 | 1.000632 | 4 | True | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.122438 | 1 | 0.670315 | 1.00443 | 3 | True | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.03915 | 0.997207 | 0.211185 | 0.999855 | 4 | True | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.130621 | 0.99898 | 0.339547 | 0.999706 | 4 | True | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.048633 | 1 | 0.464872 | 0.999732 | 4 | True | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.22216 | 0.990196 | 0.348551 | 1.000925 | 4 | True | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.034711 | 0.998102 | 0.155945 | 1.000076 | 4 | True | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.06765 | 1 | 0.233053 | 0.999485 | 4 | True | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.086285 | 1 | 0.385542 | 0.999863 | 3 | True | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.02611 | 1 | 0.078541 | 0.999719 | 4 | True | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.029092 | 0.986755 | 0.388403 | 0.999693 | 4 | True | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.985079 | 0.821429 | 0.851804 | 0.993205 | 3 | True | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.034287 | 0.994939 | 0.096787 | 1.000065 | 4 | True | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.097141 | 1 | 0.198251 | 1.000096 | 4 | True | - |
| i_leuven_1_2 | illumination | 1-2 | False | 259.28841 | 0.939675 | 0.874073 | 1.000781 | 1 | True | Projected quadrilateral is invalid. |
| i_lionday_1_2 | illumination | 1-2 | True | 0.754802 | 0.944444 | 1.354111 | 1.001855 | 2 | True | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.814643 | 0.742268 | 1.226649 | 0.998553 | 2 | True | - |
| i_londonbridge_1_2 | illumination | 1-2 | True | 2.02002 | 0.938095 | 0.81485 | 1.0011 | 2 | True | - |
| i_melon_1_2 | illumination | 1-2 | True | 0.066536 | 1 | 0.335809 | 1.000465 | 4 | True | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.119021 | 0.975655 | 0.377035 | 1.000146 | 4 | True | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.029088 | 1 | 0.044407 | 0.99961 | 4 | True | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.243705 | 0.993333 | 0.8471 | 1.000233 | 3 | True | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.231581 | 0.946237 | 0.749439 | 1.002188 | 2 | True | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.914985 | 0.991228 | 1.101155 | 0.999221 | 3 | True | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.291827 | 0.946154 | 0.807656 | 0.998639 | 4 | True | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.011977 | 0.998126 | 0.115067 | 1.000077 | 4 | True | - |
| i_pencils_1_2 | illumination | 1-2 | True | 1.074384 | 0.886364 | 0.938859 | 1.004155 | 2 | True | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.430155 | 1 | 0.493629 | 0.997144 | 4 | True | - |
| i_pool_1_2 | illumination | 1-2 | False | 0.5 | - | - | - | 0 | False | At least four point correspondences are required. |
| i_porta_1_2 | illumination | 1-2 | True | 0.114634 | 1 | 0.359667 | 0.999687 | 4 | True | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.041542 | 0.996575 | 0.382247 | 0.999342 | 4 | True | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.095243 | 0.990411 | 0.289331 | 0.999506 | 4 | True | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.887213 | 0.97619 | 0.698339 | 0.999243 | 2 | True | - |
| i_school_1_2 | illumination | 1-2 | True | 0.074159 | 1 | 0.420566 | 1.000395 | 4 | True | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.204232 | 1 | 0.61198 | 0.998043 | 4 | True | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.065864 | 1 | 0.254856 | 0.999618 | 4 | True | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.292874 | 1 | 0.663009 | 0.999961 | 4 | True | - |
| i_table_1_2 | illumination | 1-2 | True | 0.914985 | 0.991228 | 1.101155 | 0.999221 | 3 | True | - |
| i_tools_1_2 | illumination | 1-2 | True | 10.765562 | 0.785714 | 0.518621 | 0.916827 | 3 | True | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.100447 | 0.999022 | 0.288815 | 0.999792 | 4 | True | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.909688 | 0.939394 | 0.836843 | 0.99425 | 3 | True | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.119938 | 0.998239 | 0.506369 | 1.001357 | 4 | True | - |
| i_village_1_2 | illumination | 1-2 | True | 0.569082 | 1 | 0.590652 | 1.017238 | 2 | True | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.026802 | 0.999258 | 0.106084 | 0.999998 | 4 | True | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.29297 | 0.961228 | 0.550579 | 0.99922 | 4 | True | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.417358 | 0.997567 | 0.646846 | 0.999564 | 4 | True | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 279.156349 | 0.972678 | 1.039843 | 1.014147 | 0 | True | Projected quadrilateral is invalid. |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.150824 | 0.990712 | 1.060725 | 0.618717 | 2 | True | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.08781 | 1 | 0.954642 | 0.868336 | 2 | True | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.394088 | 1 | 1.06862 | 0.729999 | 4 | True | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.229725 | 1 | 1.492776 | 1.898965 | 0 | True | Projected quadrilateral is invalid. |
| v_azzola_1_2 | viewpoint | 1-2 | False | 116.338579 | 0.915385 | 1.31997 | 1.012592 | 1 | True | Projected quadrilateral is invalid. |
| v_bark_1_2 | viewpoint | 1-2 | False | 260.983704 | 0.908297 | 1.270747 | 0.661949 | 1 | True | Projected quadrilateral is invalid. |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.144718 | 0.956044 | 1.036997 | 1.094919 | 2 | True | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.563477 | 0.970588 | 1.518896 | 0.384788 | 3 | True | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.24839 | 0.995506 | 0.945061 | 0.46059 | 4 | True | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.50712 | 0.935593 | 0.875938 | 0.350938 | 3 | True | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.439957 | 0.991228 | 0.715646 | 0.183216 | 4 | True | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.596761 | 0.900524 | 1.16286 | 0.717898 | 3 | True | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 206.000423 | 0.942857 | 1.103408 | 0.780466 | 1 | True | Projected quadrilateral is invalid. |
| v_bricks_1_2 | viewpoint | 1-2 | False | 217.228131 | 0.936464 | 0.966169 | 1.122566 | 1 | True | Projected quadrilateral is invalid. |
| v_busstop_1_2 | viewpoint | 1-2 | False | 227.419325 | 1 | 1.10058 | 0.843942 | 1 | True | Projected quadrilateral is invalid. |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.337582 | 0.965909 | 1.007013 | 0.529237 | 4 | True | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.222532 | 1 | 0.941205 | 0.742165 | 4 | True | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 338.706579 | 0.631579 | 1.578652 | 2.130559 | 1 | True | Projected quadrilateral is invalid. |
| v_churchill_1_2 | viewpoint | 1-2 | False | 143.407785 | 0.949541 | 1.307151 | 1.772939 | 1 | True | Projected quadrilateral is invalid. |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.16293 | 0.931818 | 0.930986 | 0.587106 | 4 | True | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.552932 | 0.990291 | 1.200488 | 0.701809 | 2 | True | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 243.874766 | 0.972222 | 1.228753 | 1.132024 | 1 | True | Projected quadrilateral is invalid. |
| v_courses_1_2 | viewpoint | 1-2 | False | 119.449957 | 0.998246 | 0.967169 | 1.022344 | 0 | True | Projected quadrilateral is invalid. |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.033147 | 0.974811 | 0.896713 | 0.680967 | 3 | True | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.32969 | 0.785425 | 1.278495 | 1.313862 | 2 | True | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.137693 | 1 | 0.930299 | 0.711081 | 4 | True | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.159594 | 0.975155 | 0.933482 | 0.462773 | 4 | True | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 157.731345 | 0.850575 | 1.13823 | 1.36864 | 0 | True | Projected quadrilateral is invalid. |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.218959 | 0.885906 | 1.280904 | 1.280477 | 2 | True | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 97.944012 | 0.875229 | 0.97272 | 1.161485 | 0 | True | Projected quadrilateral is invalid. |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.229865 | 0.956989 | 1.120791 | 0.736828 | 2 | True | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.712105 | 0.875 | 1.216486 | 0.491657 | 3 | True | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 132.913787 | 1 | 0.93724 | 0.763697 | 1 | True | Projected quadrilateral is invalid. |
| v_london_1_2 | viewpoint | 1-2 | False | 248.893977 | 1 | 1.064742 | 1.193429 | 0 | True | Projected quadrilateral is invalid. |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.48515 | 0.945652 | 1.452292 | 1.042437 | 2 | True | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.430648 | 0.994819 | 1.35657 | 1.045372 | 2 | True | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.205769 | 1 | 1.08359 | 0.842645 | 3 | True | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 234.569713 | 0.992537 | 1.56208 | 0.960576 | 1 | True | Projected quadrilateral is invalid. |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.453381 | 0.988142 | 0.886682 | 0.276073 | 4 | True | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.180484 | 0.985401 | 0.926176 | 0.416988 | 4 | True | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 267.972396 | 0.956522 | 1.093066 | 1.11468 | 0 | True | Projected quadrilateral is invalid. |
| v_strand_1_2 | viewpoint | 1-2 | False | 19.468644 | 1 | 0.870664 | 1.012482 | 1 | True | Projected quadrilateral is invalid. |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.104253 | 0.963384 | 0.860992 | 0.97133 | 2 | True | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 189.489024 | 0.947368 | 1.19755 | 0.869887 | 1 | True | Projected quadrilateral is invalid. |
| v_talent_1_2 | viewpoint | 1-2 | True | 1.11549 | 0.830435 | 1.078996 | 0.90169 | 4 | True | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.149769 | 0.978214 | 0.951284 | 0.515181 | 4 | True | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.407782 | 0.978947 | 1.158382 | 0.762376 | 3 | True | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 177.838065 | 0.986193 | 0.872294 | 1.119842 | 0 | True | Projected quadrilateral is invalid. |
| v_vitro_1_2 | viewpoint | 1-2 | False | 73.318238 | 0.923469 | 1.002006 | 0.830473 | 1 | True | Projected quadrilateral is invalid. |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.083246 | 1 | 1.110869 | 0.985768 | 2 | True | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.418152 | 0.983607 | 1.123164 | 0.899885 | 2 | True | - |
| v_war_1_2 | viewpoint | 1-2 | True | 2.561756 | 1 | 1.545146 | 0.756977 | 2 | True | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 358.203005 | 0.902326 | 0.98767 | 1.008251 | 0 | True | Projected quadrilateral is invalid. |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.510667 | 1 | 0.870373 | 0.368849 | 4 | True | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.534414 | 0.995495 | 1.185258 | 0.686716 | 2 | True | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.265726 | 0.984615 | 1.348 | 1.202217 | 2 | True | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 219.077919 | 0.960784 | 1.019533 | 0.844834 | 1 | True | Projected quadrilateral is invalid. |
| v_yuri_1_2 | viewpoint | 1-2 | False | 276.367316 | 0.973597 | 0.99649 | 1.008683 | 1 | True | Projected quadrilateral is invalid. |
