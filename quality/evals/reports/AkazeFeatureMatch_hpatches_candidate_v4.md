# AkazeFeatureMatch HPatches Candidate v4

GeneratedAtUtc: `2026-04-30T08:20:27.1347457+00:00`
CandidateVersion: `v4`
SelectedProfile: `default_v3`

## Candidate Summary

| Metric | Value |
|---|---:|
| Cases | 116 |
| Passed | 90 |
| Failed | 26 |
| Pass rate | 0.775862 |
| Mean position error px | 54.341248 |
| P95 position error px | 321.631671 |
| P95 corner error px | 9.246627 |
| Runtime ms | 6867.832 |

## Sweep Validation

| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms | Params |
|---|---|---:|---:|---:|---:|---:|---|
| default_v3 | 1-2 | 36/59 | 0.610169 | 91.859213 | 378.273983 | 3960.956 | ratio=0.75, ransac=5.0, minInlierRatio=0.25, maxFeatures=1200, fast=20, edge=15, akazeThreshold=0.001 |
| looser_ransac_v3 | 1-2 | 36/59 | 0.610169 | 91.845465 | 378.273983 | 3888.481 | ratio=0.75, ransac=7.0, minInlierRatio=0.2, maxFeatures=1200, fast=20, edge=15, akazeThreshold=0.001 |
| orb_v3 | 1-2 | 35/59 | 0.59322 | 96.697834 | 378.273983 | 4013.36 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=1600, fast=12, edge=15, akazeThreshold=0.001 |
| dense_low_detector_threshold | 1-2 | 35/59 | 0.59322 | 100.290656 | 400.797501 | 4274.57 | ratio=0.75, ransac=7.0, minInlierRatio=0.2, maxFeatures=2000, fast=20, edge=15, akazeThreshold=0.0006 |
| dense_high_ratio_low_detector_threshold | 1-2 | 35/59 | 0.59322 | 99.762822 | 378.903641 | 4284.404 | ratio=0.82, ransac=8.0, minInlierRatio=0.15, maxFeatures=2000, fast=20, edge=15, akazeThreshold=0.0006 |
| partial_plane_low_detector_threshold | 1-2 | 35/59 | 0.59322 | 92.61542 | 321.631671 | 4417.771 | ratio=0.88, ransac=10.0, minInlierRatio=0.1, maxFeatures=2000, fast=20, edge=15, akazeThreshold=0.0005 |
| strict_geometry | 1-2 | 35/59 | 0.59322 | 96.697834 | 378.273983 | 3987.182 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=1600, fast=20, edge=15, akazeThreshold=0.001 |
| orb_low_edge_dense | 1-2 | 35/59 | 0.59322 | 100.498957 | 400.797501 | 4063.894 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=2000, fast=8, edge=5, akazeThreshold=0.001 |
| orb_low_edge_loose_ransac | 1-2 | 35/59 | 0.59322 | 101.934104 | 400.797501 | 4043.553 | ratio=0.75, ransac=8.0, minInlierRatio=0.15, maxFeatures=2000, fast=8, edge=5, akazeThreshold=0.001 |
| orb_fast_low_threshold | 1-2 | 35/59 | 0.59322 | 95.322428 | 378.273983 | 4074.56 | ratio=0.82, ransac=8.0, minInlierRatio=0.15, maxFeatures=2000, fast=6, edge=8, akazeThreshold=0.001 |
| replay_safe_dense_strict | 1-2 | 35/59 | 0.59322 | 100.50365 | 400.797501 | 4051.27 | ratio=0.7, ransac=7.0, minInlierRatio=0.25, maxFeatures=2000, fast=16, edge=10, akazeThreshold=0.001 |
| replay_safe_high_ratio | 1-2 | 35/59 | 0.59322 | 98.0926 | 378.273983 | 4056.803 | ratio=0.78, ransac=5.0, minInlierRatio=0.2, maxFeatures=2000, fast=16, edge=10, akazeThreshold=0.001 |
| replay_safe_balanced_1800 | 1-2 | 35/59 | 0.59322 | 97.201196 | 378.273983 | 3985.562 | ratio=0.7, ransac=6.0, minInlierRatio=0.25, maxFeatures=1800, fast=20, edge=10, akazeThreshold=0.001 |
| partial_plane_v4 | 1-2 | 35/59 | 0.59322 | 95.689839 | 375.890527 | 4471.798 | ratio=0.85, ransac=10.0, minInlierRatio=0.1, maxFeatures=2000, fast=6, edge=5, akazeThreshold=0.0005 |
| precision_more_features | 1-2 | 35/59 | 0.59322 | 96.333646 | 400.797501 | 4063.833 | ratio=0.65, ransac=7.0, minInlierRatio=0.2, maxFeatures=2000, fast=10, edge=10, akazeThreshold=0.001 |

## Replay Gate

| Profile | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---:|---:|---:|---:|---:|
| default_v3 | 13/20 | 0.65 | 104.581523 | 378.273983 | 1172.639 |
| looser_ransac_v3 | 11/20 | 0.55 | 121.463145 | 378.273983 | 1179.961 |
| orb_v3 | 13/20 | 0.65 | 101.305182 | 378.273983 | 1184.618 |
| dense_low_detector_threshold | 13/20 | 0.65 | 100.850275 | 400.797501 | 1246.891 |
| dense_high_ratio_low_detector_threshold | 11/20 | 0.55 | 121.146177 | 400.797501 | 1248.483 |
| partial_plane_low_detector_threshold | 13/20 | 0.65 | 97.450267 | 331.063816 | 1291.261 |
| strict_geometry | 13/20 | 0.65 | 101.305182 | 378.273983 | 1181.213 |
| orb_low_edge_dense | 13/20 | 0.65 | 101.302615 | 378.273983 | 1186.482 |
| orb_low_edge_loose_ransac | 11/20 | 0.55 | 135.103869 | 378.273983 | 1187.413 |
| orb_fast_low_threshold | 11/20 | 0.55 | 127.150576 | 378.273983 | 1187.52 |
| replay_safe_dense_strict | 10/20 | 0.5 | 142.968809 | 378.273983 | 1190.847 |
| replay_safe_high_ratio | 13/20 | 0.65 | 98.45561 | 376.316618 | 1186.732 |
| replay_safe_balanced_1800 | 13/20 | 0.65 | 101.30282 | 378.273983 | 1183.186 |
| partial_plane_v4 | 12/20 | 0.6 | 108.87734 | 376.316618 | 1281.423 |
| precision_more_features | 13/20 | 0.65 | 100.7676 | 376.316618 | 1194.765 |

## Holdout Selection

| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---|---:|---:|---:|---:|---:|
| default_v3 | 1-3 | 37/59 | 0.627119 | 87.640303 | 387.886571 | 3915.351 |
| partial_plane_low_detector_threshold | 1-3 | 39/59 | 0.661017 | 77.874613 | 351.574346 | 4398.977 |
| precision_more_features | 1-3 | 35/59 | 0.59322 | 97.083001 | 316.12201 | 4017.358 |
| strict_geometry | 1-3 | 37/59 | 0.627119 | 79.966026 | 316.081865 | 4038.71 |

## Selected Holdout

| Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---:|---:|---:|---:|---:|
| 1-3 | 37/59 | 0.627119 | 87.640303 | 387.886571 | 3915.351 |

## Case Diagnostics

| Case | Type | Pair | Passed | Error px | Mean corner px | Max corner px | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |
|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.34539 | 0.446743 | 0.711482 | 0.877698 | 0.480229 | 0.999691 | 4 | True | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.030519 | 0.74194 | 1.077389 | 0.979592 | 0.432802 | 1.00137 | 4 | True | - |
| i_bologna_1_2 | illumination | 1-2 | True | 1.113518 | 14.407775 | 27.321496 | 0.888889 | 0.448777 | 1.012949 | 2 | True | - |
| i_books_1_2 | illumination | 1-2 | True | 0.49971 | 2.587699 | 4.347357 | 0.982818 | 0.464783 | 0.993997 | 3 | True | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.641795 | 0.929666 | 1.580716 | 0.863158 | 0.741367 | 1.001288 | 3 | True | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.459709 | 0.463674 | 0.934556 | 0.973523 | 0.851443 | 1.001322 | 4 | True | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.56763 | 0.651512 | 0.928301 | 0.99361 | 0.645106 | 1.000206 | 4 | True | - |
| i_castle_1_2 | illumination | 1-2 | True | 1.080462 | 7.616406 | 12.387278 | 0.785714 | 1.097353 | 1.004207 | 2 | True | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.09229 | 0.635006 | 1.185378 | 0.968675 | 0.434243 | 0.999439 | 4 | True | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.009063 | 0.103884 | 0.194076 | 0.930425 | 0.143362 | 1.000072 | 4 | True | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.286609 | 2.136231 | 4.863329 | 0.961538 | 0.822509 | 0.994144 | 3 | True | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.301156 | 0.735115 | 0.986974 | 0.980392 | 0.870495 | 1.000452 | 4 | True | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.403307 | 1.150306 | 2.41981 | 0.952381 | 0.937608 | 1.002664 | 3 | True | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.083837 | 0.127507 | 0.328216 | 0.986622 | 0.436715 | 0.999855 | 4 | True | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.299361 | 0.294118 | 0.34036 | 0.995772 | 0.214732 | 1.000132 | 4 | True | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.236542 | 2.047108 | 3.79921 | 0.826923 | 0.624258 | 1.00333 | 2 | True | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.125857 | 0.387336 | 0.942336 | 1 | 0.32415 | 0.998557 | 4 | True | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.104744 | 0.202253 | 0.325265 | 0.970149 | 0.183728 | 0.999604 | 4 | True | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.086101 | 0.370052 | 0.766339 | 0.984496 | 0.257651 | 0.998844 | 4 | True | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.212932 | 0.498411 | 0.91784 | 0.986111 | 0.654427 | 1.000262 | 4 | True | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.11945 | 0.301207 | 0.428978 | 0.984848 | 0.319891 | 0.998933 | 4 | True | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.083647 | 0.814751 | 1.349373 | 0.982456 | 0.749847 | 0.997861 | 4 | True | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.268936 | 1.259698 | 2.062241 | 0.857143 | 0.8544 | 0.995778 | 4 | True | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.121011 | 0.226246 | 0.395596 | 0.993808 | 0.221344 | 0.999573 | 4 | True | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.223807 | 0.40663 | 0.972517 | 0.982478 | 0.39955 | 1.001466 | 4 | True | - |
| i_leuven_1_2 | illumination | 1-2 | False | 376.316618 | - | - | 0.983871 | 0.258411 | 1.000733 | 1 | True | Projected quadrilateral is invalid. |
| i_lionday_1_2 | illumination | 1-2 | True | 3.377624 | 14.83237 | 26.343431 | 0.816327 | 1.496036 | 0.955633 | 2 | True | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.915185 | 0.968488 | 1.394706 | 0.901099 | 0.661661 | 1.000042 | 3 | True | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 235.136131 | - | - | 0.888214 | 0.397671 | 1.00118 | 1 | True | Projected quadrilateral is invalid. |
| i_melon_1_2 | illumination | 1-2 | True | 0.236384 | 0.920945 | 1.798278 | 0.980565 | 0.640176 | 1.002882 | 3 | True | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.527763 | 0.914823 | 1.27169 | 0.97956 | 0.744341 | 0.999491 | 4 | True | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.062782 | 0.678056 | 1.121512 | 0.947368 | 0.270664 | 1.000199 | 3 | True | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.413999 | 1.447803 | 1.97223 | 0.948012 | 0.626227 | 0.999153 | 2 | True | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.317081 | 4.703598 | 9.246627 | 0.660377 | 1.657714 | 0.998833 | 2 | True | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.6845 | 1.370223 | 3.76074 | 0.855263 | 1.203032 | 1.002849 | 3 | True | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.277374 | 0.443088 | 0.744508 | 0.985612 | 0.667962 | 0.999184 | 4 | True | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.037423 | 0.259734 | 0.44051 | 1 | 0.230085 | 1.000838 | 4 | True | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.989353 | 1.18022 | 2.398928 | 0.896907 | 1.489759 | 1.003404 | 2 | True | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.252407 | 0.720603 | 1.309329 | 0.88125 | 0.728673 | 0.9989 | 4 | True | - |
| i_pool_1_2 | illumination | 1-2 | False | 230.10704 | - | - | - | - | - | 0 | False | At least four point correspondences are required. |
| i_porta_1_2 | illumination | 1-2 | True | 0.128452 | 0.253295 | 0.509581 | 0.89434 | 0.524942 | 1.000661 | 4 | True | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.170746 | 0.378296 | 0.509483 | 0.993651 | 0.634702 | 0.999424 | 4 | True | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.525284 | 0.581268 | 0.818515 | 0.981796 | 0.692174 | 0.999722 | 4 | True | - |
| i_santuario_1_2 | illumination | 1-2 | True | 2.049038 | 1.930336 | 2.446152 | 0.978852 | 0.623248 | 0.999034 | 2 | True | - |
| i_school_1_2 | illumination | 1-2 | True | 0.20047 | 0.411476 | 0.550981 | 0.994413 | 0.463153 | 1.000426 | 4 | True | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.265366 | 0.708589 | 0.945045 | 0.920863 | 0.622748 | 0.999336 | 4 | True | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.265988 | 0.947565 | 2.757507 | 0.894097 | 0.769963 | 1.002977 | 3 | True | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.52775 | 1.65168 | 4.168987 | 0.85446 | 0.416322 | 1.003511 | 3 | True | - |
| i_table_1_2 | illumination | 1-2 | True | 0.6845 | 1.370223 | 3.76074 | 0.855263 | 1.203032 | 1.002849 | 3 | True | - |
| i_tools_1_2 | illumination | 1-2 | True | 17.052355 | 199.432102 | 382.627471 | 0.857143 | 0.828971 | 1.087736 | 2 | True | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.055435 | 0.731093 | 1.171153 | 0.953125 | 0.616153 | 1.002995 | 3 | True | - |
| i_troulos_1_2 | illumination | 1-2 | True | 1.301589 | 1.452813 | 2.918149 | 0.963636 | 0.572118 | 0.998631 | 4 | True | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.01586 | 0.407555 | 0.770404 | 0.932773 | 0.763278 | 1.000061 | 4 | True | - |
| i_village_1_2 | illumination | 1-2 | True | 0.355783 | 2.661515 | 7.128772 | 0.925373 | 0.904033 | 1.011906 | 2 | True | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.069589 | 0.15958 | 0.259422 | 0.994937 | 0.143353 | 1.00015 | 4 | True | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.521262 | 0.616359 | 0.884974 | 0.980818 | 0.58007 | 1.000609 | 4 | True | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.252678 | 0.501109 | 0.888179 | 0.985612 | 0.617487 | 1.000908 | 4 | True | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 378.273983 | - | - | 0.985542 | 0.955722 | 1.013952 | 0 | True | Projected quadrilateral is invalid. |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.470272 | 2.927375 | 6.488764 | 0.978261 | 0.874052 | 0.614935 | 2 | True | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.080953 | 0.649744 | 0.770812 | 1 | 0.451364 | 0.868248 | 2 | True | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.379194 | 0.584886 | 0.931169 | 0.996169 | 0.609915 | 0.729109 | 4 | True | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 400.797501 | - | - | 0.911111 | 1.109113 | 1.901131 | 0 | True | Projected quadrilateral is invalid. |
| v_azzola_1_2 | viewpoint | 1-2 | False | 254.382484 | - | - | 0.927798 | 1.465231 | 1.013417 | 1 | True | Projected quadrilateral is invalid. |
| v_bark_1_2 | viewpoint | 1-2 | False | 187.998106 | - | - | 1 | 0.761698 | 0.666024 | 1 | True | Projected quadrilateral is invalid. |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.081625 | 0.675725 | 1.062043 | 0.994949 | 0.750711 | 1.095271 | 2 | True | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.273274 | 1.79689 | 4.025664 | 0.984848 | 1.273704 | 0.382507 | 3 | True | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.031807 | 0.439342 | 0.720305 | 0.96034 | 0.59457 | 0.461313 | 4 | True | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.250958 | 0.558436 | 0.692045 | 0.88254 | 0.718302 | 0.351381 | 3 | True | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.284451 | 0.654719 | 1.067359 | 0.935484 | 0.690653 | 0.182353 | 4 | True | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.066955 | 0.63191 | 1.216833 | 0.935146 | 0.825107 | 0.720763 | 3 | True | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 196.691884 | - | - | 0.995381 | 0.877641 | 0.779138 | 1 | True | Projected quadrilateral is invalid. |
| v_bricks_1_2 | viewpoint | 1-2 | False | 221.668085 | - | - | 0.966963 | 0.358361 | 1.124241 | 1 | True | Projected quadrilateral is invalid. |
| v_busstop_1_2 | viewpoint | 1-2 | False | 150.087361 | - | - | 0.918447 | 0.651966 | 0.84202 | 1 | True | Projected quadrilateral is invalid. |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.387724 | 1.159389 | 2.860635 | 0.917647 | 1.068792 | 0.528152 | 4 | True | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.132108 | 0.280342 | 0.408935 | 0.977221 | 0.60513 | 0.742467 | 4 | True | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 402.336312 | - | - | 0.833333 | 1.045712 | 2.439398 | 1 | True | Projected quadrilateral is invalid. |
| v_churchill_1_2 | viewpoint | 1-2 | False | 192.562205 | - | - | 0.814189 | 1.279321 | 1.772745 | 1 | True | Projected quadrilateral is invalid. |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.084028 | 0.388181 | 0.743017 | 0.984649 | 0.408595 | 0.586807 | 4 | True | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.124592 | 2.013323 | 4.862147 | 0.990769 | 0.968697 | 0.701903 | 2 | True | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 318.251043 | - | - | 0.901961 | 1.045754 | 1.137903 | 1 | True | Projected quadrilateral is invalid. |
| v_courses_1_2 | viewpoint | 1-2 | False | 152.574977 | - | - | 0.990809 | 0.738282 | 1.022244 | 0 | True | Projected quadrilateral is invalid. |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.144964 | 0.965969 | 1.49967 | 0.873894 | 0.600084 | 0.68028 | 3 | True | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.249127 | 2.678929 | 7.058114 | 0.955882 | 0.994291 | 1.327837 | 2 | True | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.077848 | 0.281689 | 0.46654 | 0.997183 | 0.695856 | 0.710662 | 4 | True | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.098276 | 0.281624 | 0.394823 | 0.92057 | 0.441033 | 0.462834 | 4 | True | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 270.342162 | - | - | 0.989451 | 0.840084 | 1.368456 | 0 | True | Projected quadrilateral is invalid. |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.251553 | 2.883659 | 4.576908 | 0.877193 | 0.95289 | 1.275059 | 2 | True | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 241.170499 | - | - | 0.849462 | 0.76123 | 1.160514 | 0 | True | Projected quadrilateral is invalid. |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.351195 | 0.928017 | 1.445751 | 0.870166 | 0.95083 | 0.735968 | 2 | True | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.169852 | 2.021447 | 3.97461 | 0.968553 | 0.825685 | 0.489408 | 3 | True | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 202.934063 | - | - | 0.974719 | 0.748279 | 0.763112 | 1 | True | Projected quadrilateral is invalid. |
| v_london_1_2 | viewpoint | 1-2 | False | 137.909279 | - | - | 0.975238 | 0.526765 | 1.192473 | 0 | True | Projected quadrilateral is invalid. |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.868395 | 1.520527 | 1.877137 | 0.970674 | 1.296259 | 1.03874 | 2 | True | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.551957 | 2.525231 | 3.343612 | 0.98806 | 0.986086 | 1.050449 | 2 | True | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.231979 | 3.236403 | 5.835359 | 1 | 0.725516 | 0.841095 | 3 | True | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 183.373565 | - | - | 0.898757 | 1.044877 | 0.963219 | 1 | True | Projected quadrilateral is invalid. |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.11795 | 0.303748 | 0.350483 | 0.987552 | 0.607538 | 0.275004 | 4 | True | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.278899 | 0.416948 | 0.647619 | 0.92094 | 0.709604 | 0.417705 | 4 | True | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 58.531453 | - | - | 0.851064 | 1.089464 | 1.083899 | 1 | True | Projected quadrilateral is invalid. |
| v_strand_1_2 | viewpoint | 1-2 | False | 232.999725 | - | - | 0.995855 | 0.261464 | 1.012139 | 1 | True | Projected quadrilateral is invalid. |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.049502 | 0.360993 | 0.493358 | 0.980324 | 0.316028 | 0.970966 | 2 | True | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 321.631671 | - | - | 0.943396 | 0.791395 | 0.875707 | 1 | True | Projected quadrilateral is invalid. |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.420268 | 0.856532 | 1.226534 | 0.996269 | 0.664313 | 0.895633 | 4 | True | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.096075 | 0.290702 | 0.418936 | 0.994444 | 0.512887 | 0.514735 | 4 | True | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.878066 | 2.261953 | 3.236268 | 0.898734 | 1.143221 | 0.76236 | 3 | True | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 129.671158 | - | - | 0.991632 | 0.471091 | 1.119886 | 0 | True | Projected quadrilateral is invalid. |
| v_vitro_1_2 | viewpoint | 1-2 | False | 171.69678 | - | - | 0.878553 | 0.609051 | 0.828836 | 1 | True | Projected quadrilateral is invalid. |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.086082 | 2.577814 | 6.065208 | 0.915966 | 0.728323 | 0.988636 | 2 | True | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.094341 | 0.30526 | 0.4946 | 0.981928 | 0.681416 | 0.900524 | 2 | True | - |
| v_war_1_2 | viewpoint | 1-2 | True | 2.014785 | 2.916878 | 4.012195 | 0.858824 | 1.486557 | 0.758738 | 2 | True | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 332.042282 | - | - | 0.964789 | 0.592445 | 1.004912 | 0 | True | Projected quadrilateral is invalid. |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.327478 | 0.436183 | 0.990619 | 1 | 0.754873 | 0.369069 | 4 | True | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.399088 | 2.27877 | 2.656465 | 0.947195 | 0.991081 | 0.685139 | 2 | True | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.175575 | 1.231988 | 2.023179 | 0.980769 | 0.664308 | 1.195748 | 2 | True | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 271.10235 | - | - | 0.89357 | 0.815037 | 0.845979 | 1 | True | Projected quadrilateral is invalid. |
| v_yuri_1_2 | viewpoint | 1-2 | True | 0.08342 | 3.507153 | 5.447185 | 0.932432 | 0.956783 | 1.015877 | 2 | True | - |
