# SurfaceDefectDetection KolektorSDD2 Sweep v1

GeneratedAtUtc: `2026-04-29T15:38:51+00:00`
SelectedProfile: `balanced_floor_14_area7`

| Profile | Split | Cases | Pixel F1 | Image AUROC | Image F1 | FP/normal | P95 ms | Score |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| baseline_default | train-validation | 480 | 0.3206 | 0.7960 | 0.7226 | 0.1528 | 5.833 | 2.4492 |
| recall_floor_12_area3 | train-validation | 480 | 0.3428 | 0.7917 | 0.4607 | 0.7806 | 6.954 | 1.9976 |
| recall_floor_10_area3 | train-validation | 480 | 0.2916 | 0.7853 | 0.4040 | 0.9833 | 5.777 | 1.7036 |
| balanced_floor_14_area5 | train-validation | 480 | 0.3364 | 0.7969 | 0.7372 | 0.1472 | 6.068 | 2.5139 |
| balanced_floor_14_area6 | train-validation | 480 | 0.3329 | 0.7942 | 0.7459 | 0.0917 | 5.986 | 2.5325 |
| balanced_floor_14_area7 | train-validation | 480 | 0.3316 | 0.7929 | 0.7434 | 0.0611 | 6.060 | 2.5370 |
| noise_guard_floor_18_area8 | train-validation | 480 | 0.2485 | 0.7868 | 0.6703 | 0.0028 | 6.607 | 2.2322 |
| wide_background_14_area4 | train-validation | 480 | 0.3327 | 0.7995 | 0.5455 | 0.5111 | 13.058 | 2.1558 |
| tight_background_12_area4 | train-validation | 480 | 0.3607 | 0.7894 | 0.6523 | 0.2750 | 6.405 | 2.4386 |
| close_only_12_area3 | train-validation | 480 | 0.2072 | 0.7824 | 0.4007 | 0.9972 | 8.746 | 1.4333 |
| otsu_local_area4 | train-validation | 480 | 0.0319 | 0.7783 | 0.4000 | 1.0000 | 9.128 | 0.9019 |
| gradient_percentile_stats | train-validation | 480 | 0.2202 | 0.8821 | 0.4000 | 1.0000 | 32.193 | 1.4887 |
