# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:51.5030065+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `balanced_floor_14_area7`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 1004 |
| Passed | 1004 |
| Failed gates | 0 |
| Defect images | 110 |
| Normal images | 894 |
| Pixel IoU | 0.1648 |
| Dice | 0.2829 |
| Pixel F1 | 0.2829 |
| Image AUROC | 0.7728 |
| Pixel AUROC | 0.6600 |
| Image precision | 0.6462 |
| Image recall | 0.7636 |
| Image F1 | 0.7000 |
| False positive per normal image | 0.0515 |
| Runtime p95 ms | 6.295 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 14.000 |
| Min area | 7 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 84 | 46 | 26 | 848 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 45 |
| undersegmentation_false_negative | 37 |
| low_contrast_defect_miss | 19 |
| small_defect_miss | 7 |
| mask_overgrowth_false_positive | 1 |
| oversegmentation_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
