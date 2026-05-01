# SurfaceDefectDetection KolektorSDD2 Sweep v1

GeneratedAtUtc: `2026-05-01T03:24:20+00:00`
SelectedProfile: `baseline_default`

| Profile | Split | Cases | Pixel F1 | Image AUROC | Image F1 | FP/normal | P95 ms | Score |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| baseline_default | train-validation | 480 | 0.3206 | 0.7960 | 0.7226 | 0.1528 | 5.533 | 2.4498 |
| clahe_local_mean_light | train-validation | 480 | 0.0831 | 0.7963 | 0.4000 | 1.0000 | 6.349 | 1.0663 |
| clahe_local_mean_tile12 | train-validation | 480 | 0.0285 | 0.7169 | 0.4000 | 1.0000 | 6.486 | 0.8373 |
| clahe_response_stats | train-validation | 480 | 0.0345 | 0.7996 | 0.4000 | 1.0000 | 8.986 | 0.9158 |
| recall_guard_low_threshold | train-validation | 480 | 0.0360 | 0.7958 | 0.4000 | 1.0000 | 6.056 | 0.9253 |
| recall_guard_floor_10_area6 | train-validation | 480 | 0.0232 | 0.7954 | 0.4000 | 1.0000 | 6.134 | 0.8862 |
| precision_guard_normal | train-validation | 480 | 0.1085 | 0.7970 | 0.4000 | 1.0000 | 6.921 | 1.1419 |
| precision_guard_area8_stats | train-validation | 480 | 0.2485 | 0.7868 | 0.6703 | 0.0028 | 4.629 | 2.2362 |
| tight_background_12_area4 | train-validation | 480 | 0.3607 | 0.7894 | 0.6523 | 0.2750 | 4.682 | 2.4421 |
| gradient_percentile_stats | train-validation | 480 | 0.2202 | 0.8821 | 0.4000 | 1.0000 | 6.979 | 1.5392 |
