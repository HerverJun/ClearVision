# AkazeFeatureMatch HPatches Candidate v3

GeneratedAtUtc: `2026-04-29T13:59:56.9978728+00:00`
CandidateVersion: `v3`
SelectedProfile: `looser_ransac`

## Candidate Summary

| Metric | Value |
|---|---:|
| Cases | 116 |
| Passed | 88 |
| Failed | 28 |
| Pass rate | 0.758621 |
| Mean position error px | 57.242713 |
| P95 position error px | 321.631671 |
| Runtime ms | 7441.981 |

## Sweep Validation

| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms | Params |
|---|---|---:|---:|---:|---:|---:|---|
| default_v2 | 1-2 | 36/59 | 0.610169 | 91.859213 | 378.273983 | 4683.28 | ratio=0.75, ransac=5.0, minInlierRatio=0.25, maxFeatures=1200, fast=20 |
| looser_ransac | 1-2 | 36/59 | 0.610169 | 91.845465 | 378.273983 | 4038.974 | ratio=0.75, ransac=7.0, minInlierRatio=0.2, maxFeatures=1200, fast=20 |
| partial_plane | 1-2 | 35/59 | 0.59322 | 91.467479 | 375.890527 | 4080.1 | ratio=0.82, ransac=7.0, minInlierRatio=0.15, maxFeatures=1600, fast=12 |
| strict_ratio_more_features | 1-2 | 35/59 | 0.59322 | 96.697834 | 378.273983 | 4081.689 | ratio=0.7, ransac=6.0, minInlierRatio=0.2, maxFeatures=1600, fast=12 |
| high_ratio_viewpoint | 1-2 | 35/59 | 0.59322 | 93.376527 | 375.890527 | 4081.55 | ratio=0.88, ransac=8.0, minInlierRatio=0.15, maxFeatures=1600, fast=10 |

## Holdout

| Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |
|---|---:|---:|---:|---:|---:|
| 1-3 | 38/59 | 0.644068 | 85.194942 | 387.886571 | 4303.81 |

## Case Diagnostics

| Case | Type | Pair | Passed | Error px | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |
|---|---|---|---|---:|---:|---:|---:|---:|---|---|
| i_ajuntament_1_2 | illumination | 1-2 | True | 0.380354 | 0.935252 | 0.502957 | 0.99862 | 4 | True | - |
| i_autannes_1_2 | illumination | 1-2 | True | 0.179214 | 1 | 0.495082 | 1.000806 | 3 | True | - |
| i_bologna_1_2 | illumination | 1-2 | True | 1.252344 | 0.944444 | 0.714246 | 1.013038 | 2 | True | - |
| i_books_1_2 | illumination | 1-2 | True | 0.476821 | 0.989691 | 0.494911 | 0.994477 | 3 | True | - |
| i_boutique_1_2 | illumination | 1-2 | True | 0.636083 | 0.94386 | 0.849236 | 1.000649 | 3 | True | - |
| i_bridger_1_2 | illumination | 1-2 | True | 0.449755 | 0.991853 | 0.939179 | 1.001522 | 4 | True | - |
| i_brooklyn_1_2 | illumination | 1-2 | True | 0.592786 | 0.996805 | 0.660207 | 1.000485 | 4 | True | - |
| i_castle_1_2 | illumination | 1-2 | False | 91.586025 | 0.821429 | 1.066954 | 1.017444 | 1 | True | Projected quadrilateral is invalid. |
| i_chestnuts_1_2 | illumination | 1-2 | True | 0.266224 | 0.990361 | 0.522438 | 0.999282 | 4 | True | - |
| i_contruction_1_2 | illumination | 1-2 | True | 0.008448 | 0.958726 | 0.142647 | 1.000087 | 4 | True | - |
| i_crownday_1_2 | illumination | 1-2 | True | 0.286609 | 0.961538 | 0.822509 | 0.994144 | 3 | True | - |
| i_crownnight_1_2 | illumination | 1-2 | True | 0.301156 | 0.980392 | 0.870495 | 1.000452 | 4 | True | - |
| i_dc_1_2 | illumination | 1-2 | True | 0.484609 | 0.97619 | 0.91471 | 1.001167 | 4 | True | - |
| i_dome_1_2 | illumination | 1-2 | True | 0.080043 | 0.979933 | 0.446515 | 0.999889 | 4 | True | - |
| i_duda_1_2 | illumination | 1-2 | True | 0.299361 | 0.995772 | 0.214732 | 1.000132 | 4 | True | - |
| i_fenis_1_2 | illumination | 1-2 | True | 0.191846 | 0.894231 | 0.634928 | 1.002708 | 3 | True | - |
| i_fog_1_2 | illumination | 1-2 | True | 0.125857 | 1 | 0.32415 | 0.998557 | 4 | True | - |
| i_fruits_1_2 | illumination | 1-2 | True | 0.045605 | 0.99005 | 0.208802 | 0.999754 | 4 | True | - |
| i_gonnenberg_1_2 | illumination | 1-2 | True | 0.102425 | 0.988372 | 0.287597 | 0.999217 | 4 | True | - |
| i_greenhouse_1_2 | illumination | 1-2 | True | 0.226831 | 0.993056 | 0.693885 | 1.000109 | 4 | True | - |
| i_greentea_1_2 | illumination | 1-2 | True | 0.124949 | 0.995671 | 0.335141 | 0.99913 | 4 | True | - |
| i_indiana_1_2 | illumination | 1-2 | True | 0.114871 | 0.991228 | 0.804535 | 0.997804 | 4 | True | - |
| i_kions_1_2 | illumination | 1-2 | True | 1.268936 | 0.857143 | 0.8544 | 0.995778 | 4 | True | - |
| i_ktirio_1_2 | illumination | 1-2 | True | 0.121011 | 0.993808 | 0.221344 | 0.999573 | 4 | True | - |
| i_kurhaus_1_2 | illumination | 1-2 | True | 0.217695 | 0.996245 | 0.418604 | 1.001158 | 4 | True | - |
| i_leuven_1_2 | illumination | 1-2 | False | 376.316618 | 0.995968 | 0.2795 | 0.999419 | 1 | True | Projected quadrilateral is invalid. |
| i_lionday_1_2 | illumination | 1-2 | True | 3.806536 | 0.857143 | 1.613892 | 0.956291 | 2 | True | - |
| i_lionnight_1_2 | illumination | 1-2 | True | 0.58237 | 0.857143 | 0.835327 | 0.998626 | 2 | True | - |
| i_londonbridge_1_2 | illumination | 1-2 | False | 235.136131 | 0.905225 | 0.410483 | 1.000847 | 1 | True | Projected quadrilateral is invalid. |
| i_melon_1_2 | illumination | 1-2 | True | 0.248466 | 0.992933 | 0.688516 | 1.002442 | 3 | True | - |
| i_miniature_1_2 | illumination | 1-2 | True | 0.556807 | 0.985849 | 0.778653 | 0.999727 | 4 | True | - |
| i_nescafe_1_2 | illumination | 1-2 | True | 0.037944 | 0.988115 | 0.275255 | 0.999438 | 4 | True | - |
| i_nijmegen_1_2 | illumination | 1-2 | True | 1.541962 | 0.975535 | 0.735281 | 0.999333 | 2 | True | - |
| i_nuts_1_2 | illumination | 1-2 | True | 1.761027 | 0.754717 | 2.660198 | 0.998083 | 2 | True | - |
| i_objects_1_2 | illumination | 1-2 | True | 0.701062 | 0.911184 | 1.296166 | 1.000661 | 3 | True | - |
| i_parking_1_2 | illumination | 1-2 | True | 0.312263 | 1 | 0.732919 | 0.999707 | 4 | True | - |
| i_partyfood_1_2 | illumination | 1-2 | True | 0.037423 | 1 | 0.230085 | 1.000838 | 4 | True | - |
| i_pencils_1_2 | illumination | 1-2 | False | 241.267486 | 0.814433 | 1.60903 | 1.007651 | 1 | True | Projected quadrilateral is invalid. |
| i_pinard_1_2 | illumination | 1-2 | True | 0.368925 | 0.93125 | 0.808724 | 0.997642 | 4 | True | - |
| i_pool_1_2 | illumination | 1-2 | False | 230.10704 | - | - | - | 0 | False | At least four point correspondences are required. |
| i_porta_1_2 | illumination | 1-2 | True | 0.072276 | 0.886792 | 0.564347 | 1.000231 | 4 | True | - |
| i_resort_1_2 | illumination | 1-2 | True | 0.170746 | 0.993651 | 0.634702 | 0.999424 | 4 | True | - |
| i_salon_1_2 | illumination | 1-2 | True | 0.52872 | 0.98301 | 0.699391 | 0.999645 | 4 | True | - |
| i_santuario_1_2 | illumination | 1-2 | True | 1.803231 | 0.876133 | 0.632568 | 0.992928 | 3 | True | - |
| i_school_1_2 | illumination | 1-2 | True | 0.20047 | 0.994413 | 0.463153 | 1.000426 | 4 | True | - |
| i_ski_1_2 | illumination | 1-2 | True | 0.30905 | 0.859712 | 0.622132 | 0.99916 | 4 | True | - |
| i_smurf_1_2 | illumination | 1-2 | True | 0.145328 | 0.951389 | 0.85999 | 1.000747 | 4 | True | - |
| i_steps_1_2 | illumination | 1-2 | True | 0.459907 | 0.957746 | 0.419456 | 1.000476 | 3 | True | - |
| i_table_1_2 | illumination | 1-2 | True | 0.701062 | 0.911184 | 1.296166 | 1.000661 | 3 | True | - |
| i_tools_1_2 | illumination | 1-2 | True | 22.104749 | 0.904762 | 0.799847 | 1.015779 | 2 | True | - |
| i_toy_1_2 | illumination | 1-2 | True | 0.079032 | 0.984375 | 0.803878 | 1.003755 | 3 | True | - |
| i_troulos_1_2 | illumination | 1-2 | True | 0.832641 | 0.981818 | 0.689341 | 0.993654 | 3 | True | - |
| i_veggies_1_2 | illumination | 1-2 | True | 0.102897 | 0.912365 | 0.793615 | 1.00009 | 3 | True | - |
| i_village_1_2 | illumination | 1-2 | True | 0.351435 | 0.985075 | 0.905561 | 1.005095 | 2 | True | - |
| i_whitebuilding_1_2 | illumination | 1-2 | True | 0.06103 | 0.997468 | 0.157347 | 1.000134 | 4 | True | - |
| i_yellowtent_1_2 | illumination | 1-2 | True | 0.521262 | 0.980818 | 0.58007 | 1.000609 | 4 | True | - |
| i_zion_1_2 | illumination | 1-2 | True | 0.226587 | 0.992806 | 0.694469 | 1.001306 | 4 | True | - |
| v_abstract_1_2 | viewpoint | 1-2 | False | 378.273983 | 0.985542 | 0.955722 | 1.013952 | 0 | True | Projected quadrilateral is invalid. |
| v_adam_1_2 | viewpoint | 1-2 | True | 0.443039 | 1 | 0.895664 | 0.614836 | 2 | True | - |
| v_apprentices_1_2 | viewpoint | 1-2 | True | 0.080953 | 1 | 0.451364 | 0.868248 | 2 | True | - |
| v_artisans_1_2 | viewpoint | 1-2 | True | 0.379194 | 0.996169 | 0.609915 | 0.729109 | 4 | True | - |
| v_astronautis_1_2 | viewpoint | 1-2 | False | 400.797501 | 0.96 | 1.153824 | 1.901698 | 0 | True | Projected quadrilateral is invalid. |
| v_azzola_1_2 | viewpoint | 1-2 | False | 254.382484 | 0.945848 | 1.55483 | 1.011228 | 1 | True | Projected quadrilateral is invalid. |
| v_bark_1_2 | viewpoint | 1-2 | False | 187.998106 | 1 | 0.761698 | 0.666024 | 1 | True | Projected quadrilateral is invalid. |
| v_bees_1_2 | viewpoint | 1-2 | True | 0.060408 | 1 | 0.772804 | 1.095151 | 2 | True | - |
| v_beyus_1_2 | viewpoint | 1-2 | True | 0.273274 | 0.984848 | 1.273704 | 0.382507 | 3 | True | - |
| v_bip_1_2 | viewpoint | 1-2 | True | 0.027645 | 0.991501 | 0.598314 | 0.46137 | 4 | True | - |
| v_bird_1_2 | viewpoint | 1-2 | True | 0.227986 | 0.95873 | 0.777221 | 0.351402 | 3 | True | - |
| v_birdwoman_1_2 | viewpoint | 1-2 | True | 0.284451 | 0.935484 | 0.690653 | 0.182353 | 4 | True | - |
| v_blueprint_1_2 | viewpoint | 1-2 | True | 0.051601 | 0.993724 | 0.856841 | 0.720749 | 3 | True | - |
| v_boat_1_2 | viewpoint | 1-2 | False | 196.691884 | 0.995381 | 0.877641 | 0.779138 | 1 | True | Projected quadrilateral is invalid. |
| v_bricks_1_2 | viewpoint | 1-2 | False | 221.668085 | 0.992376 | 0.358983 | 1.124294 | 1 | True | Projected quadrilateral is invalid. |
| v_busstop_1_2 | viewpoint | 1-2 | False | 150.087361 | 0.945631 | 0.664216 | 0.842901 | 1 | True | Projected quadrilateral is invalid. |
| v_calder_1_2 | viewpoint | 1-2 | True | 0.356686 | 0.947059 | 1.090054 | 0.528567 | 4 | True | - |
| v_cartooncity_1_2 | viewpoint | 1-2 | True | 0.149284 | 0.995444 | 0.624955 | 0.742578 | 4 | True | - |
| v_charing_1_2 | viewpoint | 1-2 | False | 402.336312 | 1 | 1.001581 | 2.451951 | 1 | True | Projected quadrilateral is invalid. |
| v_churchill_1_2 | viewpoint | 1-2 | False | 192.562205 | 0.902027 | 1.333311 | 1.769851 | 1 | True | Projected quadrilateral is invalid. |
| v_circus_1_2 | viewpoint | 1-2 | True | 0.088247 | 0.989035 | 0.412118 | 0.586723 | 4 | True | - |
| v_coffeehouse_1_2 | viewpoint | 1-2 | True | 0.124592 | 0.990769 | 0.968697 | 0.701903 | 2 | True | - |
| v_colors_1_2 | viewpoint | 1-2 | False | 318.251043 | 0.960784 | 1.120423 | 1.13565 | 1 | True | Projected quadrilateral is invalid. |
| v_courses_1_2 | viewpoint | 1-2 | False | 152.574977 | 0.998162 | 0.740554 | 1.022296 | 0 | True | Projected quadrilateral is invalid. |
| v_dirtywall_1_2 | viewpoint | 1-2 | True | 0.133283 | 0.931416 | 0.584436 | 0.680648 | 3 | True | - |
| v_dogman_1_2 | viewpoint | 1-2 | True | 0.231916 | 0.977941 | 1.044767 | 1.328501 | 2 | True | - |
| v_eastsouth_1_2 | viewpoint | 1-2 | True | 0.060844 | 1 | 0.711243 | 0.710665 | 4 | True | - |
| v_feast_1_2 | viewpoint | 1-2 | True | 0.127959 | 0.977597 | 0.473158 | 0.462625 | 4 | True | - |
| v_fest_1_2 | viewpoint | 1-2 | False | 270.342162 | 0.995781 | 0.866704 | 1.368625 | 0 | True | Projected quadrilateral is invalid. |
| v_gardens_1_2 | viewpoint | 1-2 | True | 0.427533 | 0.885965 | 1.044662 | 1.277086 | 2 | True | - |
| v_grace_1_2 | viewpoint | 1-2 | False | 241.170499 | 0.91828 | 0.778227 | 1.160766 | 0 | True | Projected quadrilateral is invalid. |
| v_graffiti_1_2 | viewpoint | 1-2 | True | 0.264594 | 0.950276 | 0.97598 | 0.736693 | 2 | True | - |
| v_home_1_2 | viewpoint | 1-2 | True | 0.17279 | 0.981132 | 0.887112 | 0.489352 | 3 | True | - |
| v_laptop_1_2 | viewpoint | 1-2 | False | 202.934063 | 0.994382 | 0.806054 | 0.763007 | 1 | True | Projected quadrilateral is invalid. |
| v_london_1_2 | viewpoint | 1-2 | False | 137.909279 | 0.99619 | 0.530121 | 1.192368 | 0 | True | Projected quadrilateral is invalid. |
| v_machines_1_2 | viewpoint | 1-2 | True | 0.863642 | 0.98827 | 1.356463 | 1.039046 | 2 | True | - |
| v_man_1_2 | viewpoint | 1-2 | True | 0.593388 | 0.997015 | 1.009829 | 1.050802 | 2 | True | - |
| v_maskedman_1_2 | viewpoint | 1-2 | True | 0.231979 | 1 | 0.725516 | 0.841095 | 3 | True | - |
| v_pomegranate_1_2 | viewpoint | 1-2 | False | 183.373565 | 0.944938 | 1.184873 | 0.963959 | 1 | True | Projected quadrilateral is invalid. |
| v_posters_1_2 | viewpoint | 1-2 | True | 0.098615 | 0.850622 | 0.584912 | 0.274644 | 4 | True | - |
| v_samples_1_2 | viewpoint | 1-2 | True | 0.343273 | 0.980769 | 0.755569 | 0.417795 | 4 | True | - |
| v_soldiers_1_2 | viewpoint | 1-2 | False | 58.531453 | 0.893617 | 1.183699 | 1.102374 | 0 | True | Projected quadrilateral is invalid. |
| v_strand_1_2 | viewpoint | 1-2 | False | 232.999725 | 1 | 0.272443 | 1.01214 | 1 | True | Projected quadrilateral is invalid. |
| v_sunseason_1_2 | viewpoint | 1-2 | True | 0.049502 | 0.980324 | 0.316028 | 0.970966 | 2 | True | - |
| v_tabletop_1_2 | viewpoint | 1-2 | False | 321.631671 | 0.938679 | 0.793626 | 0.876426 | 1 | True | Projected quadrilateral is invalid. |
| v_talent_1_2 | viewpoint | 1-2 | True | 0.420268 | 0.996269 | 0.664313 | 0.895633 | 4 | True | - |
| v_tempera_1_2 | viewpoint | 1-2 | True | 0.109689 | 0.872222 | 0.514234 | 0.514971 | 4 | True | - |
| v_there_1_2 | viewpoint | 1-2 | True | 0.709115 | 1 | 1.456119 | 0.762186 | 3 | True | - |
| v_underground_1_2 | viewpoint | 1-2 | False | 129.671158 | 0.991632 | 0.471091 | 1.119886 | 0 | True | Projected quadrilateral is invalid. |
| v_vitro_1_2 | viewpoint | 1-2 | False | 171.69678 | 0.937984 | 0.639316 | 0.82904 | 1 | True | Projected quadrilateral is invalid. |
| v_wall_1_2 | viewpoint | 1-2 | True | 0.070038 | 0.983193 | 0.762754 | 0.986179 | 2 | True | - |
| v_wapping_1_2 | viewpoint | 1-2 | True | 0.095715 | 0.987952 | 0.712797 | 0.900552 | 2 | True | - |
| v_war_1_2 | viewpoint | 1-2 | True | 1.46642 | 0.905882 | 1.592037 | 0.761071 | 2 | True | - |
| v_weapons_1_2 | viewpoint | 1-2 | False | 332.042282 | 0.985915 | 0.638944 | 1.003814 | 0 | True | Projected quadrilateral is invalid. |
| v_woman_1_2 | viewpoint | 1-2 | True | 0.327478 | 1 | 0.754873 | 0.369069 | 4 | True | - |
| v_wormhole_1_2 | viewpoint | 1-2 | True | 0.293774 | 0.990099 | 1.090698 | 0.686176 | 2 | True | - |
| v_wounded_1_2 | viewpoint | 1-2 | True | 0.175575 | 0.980769 | 0.664308 | 1.195748 | 2 | True | - |
| v_yard_1_2 | viewpoint | 1-2 | False | 271.10235 | 0.993348 | 0.849797 | 0.846493 | 1 | True | Projected quadrilateral is invalid. |
| v_yuri_1_2 | viewpoint | 1-2 | True | 0.038741 | 0.97973 | 0.943903 | 1.015431 | 2 | True | - |
