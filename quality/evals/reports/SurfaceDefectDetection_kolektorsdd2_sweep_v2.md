# SurfaceDefectDetection KolektorSDD2 Sweep v2

GeneratedAtUtc: `2026-05-01T04:53:25+00:00`
SelectedProfile: `baseline_default`
TargetTaxonomy: `texture_noise_false_positive, low_contrast_defect_miss, undersegmentation_false_negative`
GlobalThresholdPolicy: `No v2 profile may lower the manual global threshold below baseline 15.0.`

| Profile | Target taxonomy cases | Split | Cases | Pixel F1 | Image AUROC | Image F1 | FP/normal | P95 ms | Score |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|
| baseline_default | 116 | train-validation | 480 | 0.3206 | 0.7960 | 0.7226 | 0.1528 | 4.731 | 2.4515 |
| texture_noise_shape_response_area6 | 116 | train-validation | 480 | 0.2702 | 0.8035 | 0.4143 | 0.9000 | 7.388 | 1.6851 |
| texture_noise_compact_circularity_area8 | 73 | train-validation | 480 | 0.2104 | 0.7817 | 0.4586 | 0.0028 | 4.687 | 1.9062 |
| texture_noise_prominence_guard | 4 | train-validation | 480 | 0.0836 | 0.7962 | 0.4000 | 1.0000 | 16.680 | 1.0470 |
| low_contrast_clahe_local_mean | 4 | train-validation | 480 | 0.0831 | 0.7963 | 0.4000 | 1.0000 | 10.556 | 1.0579 |
| low_contrast_clahe_percentile_stats | 0 | train-validation | 480 | 0.0285 | 0.7169 | 0.4000 | 1.0000 | 15.383 | 0.8195 |
| undersegmentation_closeopen_kernel3 | 137 | train-validation | 480 | 0.3875 | 0.7865 | 0.6403 | 0.2389 | 4.323 | 2.5235 |
| undersegmentation_closeonly_kernel3 | 189 | train-validation | 480 | 0.4162 | 0.7896 | 0.5891 | 0.4250 | 4.596 | 2.4856 |
| targeted_combined_v2 | 116 | train-validation | 480 | 0.2702 | 0.8035 | 0.4143 | 0.9000 | 7.154 | 1.6856 |
