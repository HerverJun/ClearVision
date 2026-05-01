# Matching Tail Case Drilldown v2

GeneratedAtUtc: `2026-04-30T08:35:03+00:00`
ClaimBoundary: `Public HPatches tail-case drilldown only; use as candidate triage, not production signoff.`

## Summary

| Metric | Value |
|---|---:|
| Failed case rows | 144 |
| Center-gate candidate rows | 64 |
| Cross-case groups | 48 |

## Recommended Small Gate

`v_london_1_2,v_strand_1_2,v_underground_1_2,v_abstract_1_2,v_bricks_1_2,v_weapons_1_2,v_courses_1_2,v_fest_1_2,v_tabletop_1_2,v_busstop_1_2,v_boat_1_2,v_soldiers_1_2,v_yuri_1_2,v_bark_1_2,v_laptop_1_2,v_yard_1_2,v_vitro_1_2,v_colors_1_2,v_astronautis_1_2,v_churchill_1_2`

## Center-Gate Candidates

| Rank | Case | Operator | Score | Bucket | Pos px | Inlier ratio | Mean reproj | Max reproj | Area ratio | Corners in | Action |
|---:|---|---|---:|---|---:|---:|---:|---:|---:|---:|---|
| 1 | v_london_1_2 | PlanarMatching(AKAZE) | 91.853 | extreme_viewpoint_crop | 45.222 | 1 | 0.304 | 1.807 | 0.83 | 0 | try-geometry-candidate-selection |
| 2 | v_strand_1_2 | PlanarMatching(AKAZE) | 91.384 | extreme_viewpoint_crop | 6.505 | 1 | 0.223 | 0.997 | 1.012 | 1 | try-geometry-candidate-selection |
| 3 | v_underground_1_2 | PlanarMatching(AKAZE) | 91.184 | extreme_viewpoint_crop | 45.145 | 1 | 0.337 | 2.604 | 0.926 | 0 | try-geometry-candidate-selection |
| 4 | v_abstract_1_2 | AkazeFeatureMatch | 90.717 | extreme_viewpoint_crop | 378.274 | 0.986 | 0.956 | 3.775 | 1.014 | 0 | try-geometry-candidate-selection |
| 5 | v_strand_1_2 | AkazeFeatureMatch | 90.644 | extreme_viewpoint_crop | 233 | 0.996 | 0.261 | 4.324 | 1.012 | 1 | try-geometry-candidate-selection |
| 6 | v_bricks_1_2 | PlanarMatching(AKAZE) | 89.952 | extreme_viewpoint_crop | 25.093 | 1 | 0.297 | 1.176 | 0.929 | 1 | try-geometry-candidate-selection |
| 7 | v_weapons_1_2 | AkazeFeatureMatch | 89.75 | extreme_viewpoint_crop | 332.042 | 0.965 | 0.592 | 4.761 | 1.005 | 0 | try-geometry-candidate-selection |
| 8 | v_bricks_1_2 | AkazeFeatureMatch | 89.687 | extreme_viewpoint_crop | 221.668 | 0.967 | 0.358 | 1.977 | 1.124 | 1 | try-geometry-candidate-selection |
| 9 | v_underground_1_2 | AkazeFeatureMatch | 88.179 | extreme_viewpoint_crop | 129.671 | 0.992 | 0.471 | 3.932 | 1.12 | 0 | try-geometry-candidate-selection |
| 10 | v_courses_1_2 | PlanarMatching(AKAZE) | 88.017 | extreme_viewpoint_crop | 62.456 | 0.99 | 0.519 | 2.309 | 0.846 | 0 | try-geometry-candidate-selection |
| 11 | v_fest_1_2 | PlanarMatching(AKAZE) | 86.449 | extreme_viewpoint_crop | 87.944 | 0.993 | 0.589 | 3.91 | 0.948 | 0 | try-geometry-candidate-selection |
| 12 | v_courses_1_2 | AkazeFeatureMatch | 84.357 | extreme_viewpoint_crop | 152.575 | 0.991 | 0.738 | 5.089 | 1.022 | 0 | try-geometry-candidate-selection |
| 13 | v_london_1_2 | AkazeFeatureMatch | 84.247 | extreme_viewpoint_crop | 137.909 | 0.975 | 0.527 | 4.49 | 1.192 | 0 | try-geometry-candidate-selection |
| 14 | v_fest_1_2 | AkazeFeatureMatch | 84.22 | extreme_viewpoint_crop | 270.342 | 0.989 | 0.84 | 4.495 | 1.368 | 0 | try-geometry-candidate-selection |
| 15 | v_london_1_2 | OrbFeatureMatch | 83.762 | extreme_viewpoint_crop | 248.894 | 1 | 1.065 | 4.403 | 1.193 | 0 | try-geometry-candidate-selection |
| 16 | v_underground_1_2 | OrbFeatureMatch | 83.611 | extreme_viewpoint_crop | 177.838 | 0.986 | 0.872 | 4.259 | 1.12 | 0 | try-geometry-candidate-selection |
| 17 | v_courses_1_2 | OrbFeatureMatch | 83.479 | extreme_viewpoint_crop | 119.45 | 0.998 | 0.967 | 4.029 | 1.022 | 0 | try-geometry-candidate-selection |
| 18 | v_weapons_1_2 | PlanarMatching(AKAZE) | 83.339 | extreme_viewpoint_crop | 47.512 | 1 | 0.595 | 3.306 | 0.697 | 0 | try-geometry-candidate-selection |
| 19 | v_tabletop_1_2 | AkazeFeatureMatch | 83.26 | extreme_viewpoint_crop | 321.632 | 0.943 | 0.791 | 2.82 | 0.876 | 1 | try-geometry-candidate-selection |
| 20 | v_abstract_1_2 | OrbFeatureMatch | 83.209 | extreme_viewpoint_crop | 279.156 | 0.973 | 1.04 | 4.893 | 1.014 | 0 | try-geometry-candidate-selection |
| 21 | v_busstop_1_2 | PlanarMatching(AKAZE) | 82.651 | extreme_viewpoint_crop | 64.686 | 0.997 | 0.606 | 2.817 | 0.843 | 1 | try-geometry-candidate-selection |
| 22 | v_courses_1_2 | PlanarMatching(ORB) | 82.065 | extreme_viewpoint_crop | 62.218 | 0.977 | 0.885 | 3.305 | 1.02 | 0 | try-geometry-candidate-selection |
| 23 | v_boat_1_2 | PlanarMatching(AKAZE) | 80.962 | extreme_viewpoint_crop | 0.344 | 0.967 | 0.549 | 2.33 | 0.962 | 1 | try-geometry-candidate-selection |
| 24 | v_abstract_1_2 | PlanarMatching(AKAZE) | 80.66 | extreme_viewpoint_crop | 66.459 | 0.997 | 0.681 | 4.312 | 0.704 | 0 | try-geometry-candidate-selection |
| 25 | v_soldiers_1_2 | OrbFeatureMatch | 80.529 | extreme_viewpoint_crop | 267.972 | 0.957 | 1.093 | 4.371 | 1.115 | 0 | try-geometry-candidate-selection |
| 26 | v_strand_1_2 | PlanarMatching(ORB) | 80.306 | extreme_viewpoint_crop | 7.114 | 0.973 | 0.628 | 2.783 | 1.012 | 1 | try-geometry-candidate-selection |
| 27 | v_boat_1_2 | AkazeFeatureMatch | 80.282 | extreme_viewpoint_crop | 196.692 | 0.995 | 0.878 | 3.815 | 0.779 | 1 | try-geometry-candidate-selection |
| 28 | v_yuri_1_2 | OrbFeatureMatch | 79.693 | extreme_viewpoint_crop | 276.367 | 0.974 | 0.996 | 4.948 | 1.009 | 1 | try-geometry-candidate-selection |
| 29 | v_bark_1_2 | AkazeFeatureMatch | 79.526 | extreme_viewpoint_crop | 187.998 | 1 | 0.762 | 4.048 | 0.666 | 1 | try-geometry-candidate-selection |
| 30 | v_busstop_1_2 | OrbFeatureMatch | 79.262 | extreme_viewpoint_crop | 227.419 | 1 | 1.101 | 4.159 | 0.844 | 1 | try-geometry-candidate-selection |
| 31 | v_laptop_1_2 | AkazeFeatureMatch | 79.101 | extreme_viewpoint_crop | 202.934 | 0.975 | 0.748 | 4.118 | 0.763 | 1 | try-geometry-candidate-selection |
| 32 | v_abstract_1_2 | PlanarMatching(ORB) | 78.079 | extreme_viewpoint_crop | 66.274 | 0.983 | 0.953 | 3.287 | 0.703 | 0 | try-geometry-candidate-selection |
| 33 | v_yard_1_2 | PlanarMatching(AKAZE) | 77.803 | extreme_viewpoint_crop | 49.778 | 1 | 0.842 | 3.791 | 0.847 | 1 | try-geometry-candidate-selection |
| 34 | v_vitro_1_2 | PlanarMatching(AKAZE) | 77.662 | extreme_viewpoint_crop | 32.932 | 0.953 | 0.625 | 3.443 | 1.023 | 1 | try-geometry-candidate-selection |
| 35 | v_soldiers_1_2 | PlanarMatching(ORB) | 77.447 | extreme_viewpoint_crop | 45.144 | 0.964 | 1.007 | 3.58 | 0.918 | 0 | try-geometry-candidate-selection |
| 36 | v_underground_1_2 | PlanarMatching(ORB) | 76.838 | extreme_viewpoint_crop | 44.592 | 0.993 | 1.01 | 3.92 | 1.381 | 0 | try-geometry-candidate-selection |
| 37 | v_bark_1_2 | PlanarMatching(AKAZE) | 76.779 | extreme_viewpoint_crop | 0.955 | 1 | 0.788 | 4.659 | 1.041 | 1 | try-geometry-candidate-selection |
| 38 | v_busstop_1_2 | AkazeFeatureMatch | 76.764 | extreme_viewpoint_crop | 150.087 | 0.918 | 0.652 | 2.848 | 0.842 | 1 | try-geometry-candidate-selection |
| 39 | v_colors_1_2 | OrbFeatureMatch | 76.319 | extreme_viewpoint_crop | 243.875 | 0.972 | 1.229 | 3.993 | 1.132 | 1 | try-geometry-candidate-selection |
| 40 | v_strand_1_2 | OrbFeatureMatch | 75.899 | extreme_viewpoint_crop | 19.469 | 1 | 0.871 | 5.044 | 1.012 | 1 | try-geometry-candidate-selection |

## Cross-Case Groups

| Rank | Case | Operators failed | Center-gate rows | Max score | Max pos px | Buckets |
|---:|---|---:|---:|---:|---:|---|
| 1 | v_london_1_2 | 4 | 4 | 91.853 | 248.894 | extreme_viewpoint_crop |
| 2 | v_strand_1_2 | 4 | 4 | 91.384 | 233 | extreme_viewpoint_crop |
| 3 | v_underground_1_2 | 4 | 4 | 91.184 | 177.838 | extreme_viewpoint_crop |
| 4 | v_abstract_1_2 | 4 | 4 | 90.717 | 378.274 | extreme_viewpoint_crop |
| 5 | v_bricks_1_2 | 4 | 4 | 89.952 | 221.668 | extreme_viewpoint_crop |
| 6 | v_courses_1_2 | 4 | 4 | 88.017 | 152.575 | extreme_viewpoint_crop |
| 7 | v_boat_1_2 | 4 | 4 | 80.962 | 206 | extreme_viewpoint_crop |
| 8 | v_weapons_1_2 | 4 | 3 | 89.75 | 358.203 | extreme_viewpoint_crop, reprojection_outlier |
| 9 | v_fest_1_2 | 4 | 3 | 86.449 | 270.342 | extreme_viewpoint_crop |
| 10 | v_busstop_1_2 | 4 | 3 | 82.651 | 227.419 | extreme_viewpoint_crop |
| 11 | v_bark_1_2 | 4 | 3 | 79.526 | 260.984 | extreme_viewpoint_crop |
| 12 | v_laptop_1_2 | 4 | 3 | 79.101 | 202.934 | extreme_viewpoint_crop |
| 13 | v_yard_1_2 | 4 | 3 | 77.803 | 271.102 | extreme_viewpoint_crop |
| 14 | v_tabletop_1_2 | 4 | 2 | 83.26 | 321.632 | extreme_viewpoint_crop |
| 15 | v_soldiers_1_2 | 4 | 2 | 80.529 | 267.972 | extreme_viewpoint_crop |
| 16 | v_yuri_1_2 | 2 | 2 | 79.693 | 276.367 | extreme_viewpoint_crop |
| 17 | v_vitro_1_2 | 4 | 2 | 77.662 | 171.697 | extreme_viewpoint_crop |
| 18 | v_colors_1_2 | 4 | 2 | 76.319 | 318.251 | extreme_viewpoint_crop |
| 19 | v_astronautis_1_2 | 4 | 2 | 74.449 | 400.798 | extreme_viewpoint_crop |
| 20 | v_churchill_1_2 | 4 | 2 | 71.287 | 192.562 | extreme_viewpoint_crop |
| 21 | v_azzola_1_2 | 4 | 2 | 68.967 | 254.382 | extreme_viewpoint_crop |
| 22 | v_pomegranate_1_2 | 4 | 1 | 72.416 | 234.57 | extreme_viewpoint_crop |
| 23 | v_charing_1_2 | 4 | 1 | 70.853 | 890.566 | projected_area_drift |
| 24 | i_leuven_1_2 | 4 | 0 | 97.3 | 376.317 | illumination_residual |
| 25 | v_bees_1_2 | 2 | 0 | 80.879 | 68.79 | partial_viewpoint_crop |
