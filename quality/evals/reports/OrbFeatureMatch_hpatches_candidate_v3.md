# OrbFeatureMatch HPatches Candidate v3

GeneratedAtUtc: `2026-04-29T14:00:12.7813699+00:00`
CandidateVersion: `v3`
SelectedProfile: `strict_ratio_more_features`

## Candidate Summary

| Metric | Value |
|---|---:|
| Cases | 116 |
| Passed | 89 |
| Failed | 27 |
| Pass rate | 0.767241 |
| Mean position error px | 48.16355 |
| P95 position error px | 284.300747 |
| Runtime ms | 2847.879 |

## Sweep Validation

| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms | Params |
|---|---|---:|---:|---:|---:|---:|---|
| default_v2 | 1-2 | 35/59 | 0.59322 | 90.563725 | 356.422881 | 1389.424 | ratio=0.75, ransac=5.0, minInlierRatio=0.25, maxFeatures=1200, fast=20 |
| looser_ransac | 1-2 | 35/59 | 0.59322 | 89.082827 | 356.422881 | 1321.166 | ratio=0.75, ransac=7.0, minInlierRatio=0.2, maxFeatures=1200, fast=20 |
| partial_plane | 1-2 | 35/59 | 0.59322 | 87.207736 | 349.400678 | 1666.127 | ratio=0.82, ransac=7.0, minInlierRatio=0.15, maxFeatures=1600, fast=12 |
| strict_ratio_more_features | 1-2 | 35/59 | 0.59322 | 84.863489 | 338.706579 | 1671.02 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=1600, fast=12 |
| high_ratio_viewpoint | 1-2 | 35/59 | 0.59322 | 88.08291 | 363.276422 | 1726.353 | ratio=0.88, ransac=8.0, minInlierRatio=0.15, maxFeatures=1600, fast=10 |

## Holdout

| Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---:|---:|---:|---:|---:|
| 1-3 | 36/59 | 0.610169 | 103.553313 | 401.784745 | 1645.686 |

## Case Diagnostics

| Case | Type | Pair | Passed | Error px | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |
|---|---|---|---|---:|---:|---:|---:|---:|---|---|
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.132842 | 0.970917 | 0.446708 | 1.000695 | 4 | True | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.390985 | 1 | 0.264474 | 0.994481 | 2 | True | - |
| i_bologna_1_2 | illumination | 1-2 | True | 0.409557 | 0.990909 | 0.634225 | 0.994714 | 3 | True | - |
| i_books_1_2 | illumination | 1-2 | True | 0.150957 | 0.995702 | 0.121875 | 0.999364 | 4 | True | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.330622 | 0.982353 | 0.47165 | 1.000298 | 4 | True | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.298911 | 0.988506 | 0.781571 | 1.002817 | 3 | True | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.286647 | 0.947977 | 0.743713 | 0.993203 | 3 | True | - |
| i_castle_1_2 | illumination | 1-2 | True | 5.862939 | 1 | 0.499366 | 0.954224 | 3 | True | - |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.742246 | 1 | 0.286389 | 0.990189 | 3 | True | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.008972 | 1 | 0.07261 | 1.000031 | 4 | True | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.016989 | 1 | 0.156439 | 0.999904 | 4 | True | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.069652 | 1 | 0.53921 | 1.001152 | 4 | True | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.250444 | 1 | 0.758481 | 1.004126 | 2 | True | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.024197 | 0.992424 | 0.224948 | 0.999911 | 4 | True | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.111861 | 1 | 0.311936 | 0.999661 | 4 | True | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.17006 | 1 | 0.587833 | 0.998594 | 4 | True | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.222719 | 1 | 0.415038 | 1.001132 | 3 | True | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.032993 | 0.997691 | 0.134837 | 0.999873 | 4 | True | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.055535 | 1 | 0.217698 | 0.999848 | 4 | True | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.050157 | 1 | 0.381205 | 0.998931 | 4 | True | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.036242 | 1 | 0.08629 | 0.999592 | 4 | True | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.048674 | 1 | 0.419607 | 0.999717 | 4 | True | - |
| i_kions_1_2 | illumination | 1-2 | True | 2.600729 | 0.733333 | 1.151165 | 1.085678 | 2 | True | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.033208 | 0.997338 | 0.074372 | 1.000111 | 4 | True | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.090515 | 1 | 0.197333 | 1.000125 | 4 | True | - |
| i_leuven_1_2 | illumination | 1-2 | False | 249.143721 | 0.994652 | 0.891686 | 1.000831 | 1 | True | Projected quadrilateral is invalid. |
| i_lionday_1_2 | illumination | 1-2 | True | 3.972214 | 0.875 | 1.443004 | 0.924328 | 3 | True | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.729623 | 0.76 | 1.229604 | 0.998758 | 3 | True | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 282.449996 | 1 | 0.83915 | 1.000747 | 1 | True | Projected quadrilateral is invalid. |
| i_melon_1_2 | illumination | 1-2 | True | 0.065529 | 0.998308 | 0.344702 | 1.00033 | 4 | True | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.104586 | 0.978873 | 0.353198 | 1.000133 | 4 | True | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.038213 | 1 | 0.056087 | 0.99992 | 4 | True | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.353185 | 0.990099 | 0.917854 | 0.999629 | 3 | True | - |
| i_nuts_1_2 | illumination | 1-2 | True | 0.647833 | 0.966667 | 0.960946 | 1.001986 | 3 | True | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.97905 | 1 | 1.094405 | 1.000909 | 3 | True | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.263572 | 0.995 | 0.825311 | 0.998889 | 4 | True | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.021272 | 0.996633 | 0.118013 | 0.99984 | 4 | True | - |
| i_pencils_1_2 | illumination | 1-2 | True | 0.395641 | 0.966667 | 0.857415 | 1.001662 | 2 | True | - |
| i_pinard_1_2 | illumination | 1-2 | True | 0.655921 | 1 | 0.636843 | 0.995726 | 4 | True | - |
| i_pool_1_2 | illumination | 1-2 | False | 0.5 | - | - | - | 0 | False | At least four point correspondences are required. |
| i_porta_1_2 | illumination | 1-2 | True | 0.133196 | 0.997587 | 0.380663 | 0.999539 | 4 | True | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.122454 | 1 | 0.443081 | 0.999079 | 4 | True | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.089204 | 0.991639 | 0.311118 | 0.999307 | 4 | True | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.912326 | 0.941935 | 0.649006 | 0.999446 | 2 | True | - |
| i_school_1_2 | illumination | 1-2 | True | 0.098094 | 1 | 0.448709 | 1.000358 | 4 | True | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.183062 | 1 | 0.597025 | 0.997946 | 4 | True | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.095179 | 0.997701 | 0.262061 | 0.999557 | 4 | True | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.267868 | 1 | 0.645126 | 0.999896 | 4 | True | - |
| i_table_1_2 | illumination | 1-2 | True | 0.97905 | 1 | 1.094405 | 1.000909 | 3 | True | - |
| i_tools_1_2 | illumination | 1-2 | True | 20.198836 | 0.692308 | 0.582225 | 0.818018 | 2 | True | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.043856 | 0.997622 | 0.217527 | 0.999673 | 4 | True | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.787244 | 0.986577 | 0.91451 | 1.001049 | 3 | True | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.128782 | 1 | 0.53847 | 1.00171 | 4 | True | - |
| i_village_1_2 | illumination | 1-2 | True | 0.480511 | 1 | 0.624418 | 1.013179 | 2 | True | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.032553 | 0.999088 | 0.103454 | 0.999998 | 4 | True | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.291574 | 0.961702 | 0.589569 | 0.998978 | 4 | True | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.4331 | 0.997033 | 0.678236 | 0.999559 | 4 | True | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 472.751812 | 0.976351 | 1.063081 | 1.014307 | 0 | True | Projected quadrilateral is invalid. |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.082257 | 0.951724 | 1.025543 | 0.618169 | 2 | True | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.000953 | 0.967532 | 0.925196 | 0.868285 | 2 | True | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.683352 | 0.971831 | 1.000308 | 0.732085 | 4 | True | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 307.229725 | 0.87619 | 1.479039 | 1.913893 | 0 | True | Projected quadrilateral is invalid. |
| v_azzola_1_2 | viewpoint | 1-2 | False | 206.769103 | 0.991071 | 1.331064 | 1.014981 | 1 | True | Projected quadrilateral is invalid. |
| v_bark_1_2 | viewpoint | 1-2 | False | 260.983704 | 0.979058 | 1.184479 | 0.664129 | 1 | True | Projected quadrilateral is invalid. |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.092224 | 0.997792 | 1.054517 | 1.09448 | 2 | True | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 1.461103 | 0.925926 | 1.632352 | 0.379187 | 3 | True | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.274186 | 0.993884 | 0.929423 | 0.460581 | 4 | True | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.491789 | 0.968254 | 0.923349 | 0.351294 | 3 | True | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.329925 | 0.946237 | 0.695217 | 0.182914 | 4 | True | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.319949 | 0.90566 | 1.172216 | 0.718313 | 3 | True | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 206.000423 | 1 | 1.105156 | 0.778965 | 1 | True | Projected quadrilateral is invalid. |
| v_bricks_1_2 | viewpoint | 1-2 | False | 238.083452 | 0.878205 | 0.971264 | 1.123218 | 1 | True | Projected quadrilateral is invalid. |
| v_busstop_1_2 | viewpoint | 1-2 | False | 118.196081 | 1 | 1.090529 | 0.842498 | 1 | True | Projected quadrilateral is invalid. |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.477512 | 0.971429 | 0.914114 | 0.528453 | 4 | True | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.196752 | 0.806142 | 0.855262 | 0.741012 | 4 | True | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 338.706579 | 0.631579 | 1.51135 | 2.446702 | 1 | True | Projected quadrilateral is invalid. |
| v_churchill_1_2 | viewpoint | 1-2 | False | 242.519227 | 0.97619 | 1.266203 | 1.771353 | 1 | True | Projected quadrilateral is invalid. |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.168445 | 0.862559 | 0.909821 | 0.587876 | 4 | True | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.447701 | 0.913978 | 1.188326 | 0.701932 | 2 | True | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 284.300747 | 0.87234 | 1.253499 | 1.135026 | 1 | True | Projected quadrilateral is invalid. |
| v_courses_1_2 | viewpoint | 1-2 | False | 27.166793 | 0.993521 | 0.998374 | 1.022598 | 0 | True | Projected quadrilateral is invalid. |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.106035 | 1 | 0.873891 | 0.681225 | 3 | True | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.274632 | 0.921951 | 1.263023 | 1.318403 | 2 | True | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.216901 | 0.998382 | 0.938992 | 0.710786 | 4 | True | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.187485 | 0.989822 | 0.953494 | 0.462938 | 4 | True | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 199.064942 | 0.887324 | 1.058893 | 1.372214 | 0 | True | Projected quadrilateral is invalid. |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.211052 | 0.910569 | 1.38421 | 1.274647 | 2 | True | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 80.876578 | 0.883295 | 0.999204 | 1.158951 | 0 | True | Projected quadrilateral is invalid. |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.112781 | 0.877023 | 1.115405 | 0.737161 | 2 | True | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.769012 | 0.915966 | 1.143252 | 0.491362 | 3 | True | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 118.928344 | 0.912381 | 0.920263 | 0.763659 | 1 | True | Projected quadrilateral is invalid. |
| v_london_1_2 | viewpoint | 1-2 | False | 247.168343 | 0.994565 | 1.039276 | 1.193743 | 0 | True | Projected quadrilateral is invalid. |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.807304 | 0.833333 | 1.367343 | 1.040738 | 2 | True | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.385394 | 0.898649 | 1.180902 | 1.043018 | 2 | True | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.190696 | 0.978495 | 1.050185 | 0.841928 | 3 | True | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 186.912417 | 0.891892 | 1.585657 | 0.960253 | 1 | True | Projected quadrilateral is invalid. |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.532854 | 0.943231 | 0.843809 | 0.276618 | 4 | True | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.331584 | 0.981308 | 0.920682 | 0.417103 | 4 | True | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 189.396125 | 0.815126 | 1.098156 | 1.112491 | 0 | True | Projected quadrilateral is invalid. |
| v_strand_1_2 | viewpoint | 1-2 | False | 54.717487 | 0.991474 | 0.866227 | 1.012401 | 1 | True | Projected quadrilateral is invalid. |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.120466 | 0.945988 | 0.871958 | 0.970978 | 2 | True | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 116.340669 | 0.924051 | 1.169817 | 0.871833 | 1 | True | Projected quadrilateral is invalid. |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.603465 | 0.904306 | 1.063006 | 0.901859 | 4 | True | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.125891 | 0.99729 | 0.939627 | 0.514996 | 4 | True | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.352945 | 1 | 1.143504 | 0.762959 | 3 | True | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 205.484149 | 0.988152 | 0.884775 | 1.11892 | 0 | True | Projected quadrilateral is invalid. |
| v_vitro_1_2 | viewpoint | 1-2 | False | 103.151274 | 0.915584 | 0.977537 | 0.830859 | 1 | True | Projected quadrilateral is invalid. |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.0721 | 1 | 1.102231 | 0.985699 | 2 | True | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.417303 | 0.974522 | 1.151152 | 0.899843 | 2 | True | - |
| v_war_1_2 | viewpoint | 1-2 | True | 2.178537 | 0.890909 | 1.318266 | 0.764502 | 2 | True | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 345.315381 | 0.966887 | 1.01903 | 1.006001 | 0 | True | Projected quadrilateral is invalid. |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.544405 | 1 | 0.879792 | 0.368664 | 4 | True | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.346301 | 0.878788 | 1.186518 | 0.687097 | 2 | True | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.247053 | 1 | 1.365532 | 1.19805 | 2 | True | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 332.116923 | 0.916084 | 1.060782 | 0.845537 | 1 | True | Projected quadrilateral is invalid. |
| v_yuri_1_2 | viewpoint | 1-2 | False | 110.605251 | 1 | 0.989244 | 1.008134 | 1 | True | Projected quadrilateral is invalid. |
