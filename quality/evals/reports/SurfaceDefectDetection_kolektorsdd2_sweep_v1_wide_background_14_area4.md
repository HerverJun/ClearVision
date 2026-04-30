# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:20.9268693+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `wide_background_14_area4`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1995 |
| Dice | 0.3327 |
| Pixel F1 | 0.3327 |
| Image AUROC | 0.7995 |
| Pixel AUROC | 0.6949 |
| Image precision | 0.3826 |
| Image recall | 0.9500 |
| Image F1 | 0.5455 |
| False positive per normal image | 0.5111 |
| Runtime p95 ms | 13.058 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 14.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 45 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 114 | 184 | 6 | 176 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 166 |
| undersegmentation_false_negative | 50 |
| oversegmentation_false_positive | 18 |
| mask_boundary_mismatch | 6 |
| low_contrast_defect_miss | 4 |
| mask_overgrowth_false_positive | 4 |
| small_defect_miss | 2 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
