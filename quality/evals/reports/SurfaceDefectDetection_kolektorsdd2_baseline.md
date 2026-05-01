# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T03:16:00.7800701+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `baseline`
Profile: `baseline_default`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 1004 |
| Passed | 1004 |
| Failed gates | 0 |
| Defect images | 110 |
| Normal images | 894 |
| Pixel IoU | 0.1556 |
| Dice | 0.2692 |
| Pixel F1 | 0.2692 |
| Image AUROC | 0.7724 |
| Pixel AUROC | 0.6600 |
| Image precision | 0.4266 |
| Image recall | 0.8455 |
| Image F1 | 0.5671 |
| False positive per normal image | 0.1398 |
| Runtime p95 ms | 5.170 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 2 / 8 |
| Component filter mode | AreaOnly |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 93 | 125 | 17 | 769 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 123 |
| undersegmentation_false_negative | 46 |
| low_contrast_defect_miss | 15 |
| oversegmentation_false_positive | 2 |
| small_defect_miss | 2 |
| mask_overgrowth_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
