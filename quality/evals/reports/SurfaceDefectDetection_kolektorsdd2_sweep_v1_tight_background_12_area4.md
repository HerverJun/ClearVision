# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T03:24:10.2176505+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `tight_background_12_area4`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.2200 |
| Dice | 0.3607 |
| Pixel F1 | 0.3607 |
| Image AUROC | 0.7894 |
| Pixel AUROC | 0.6714 |
| Image precision | 0.5171 |
| Image recall | 0.8833 |
| Image F1 | 0.6523 |
| False positive per normal image | 0.2750 |
| Runtime p95 ms | 4.682 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 12.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 21 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 2 / 8 |
| Component filter mode | AreaOnly |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 106 | 99 | 14 | 261 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 96 |
| undersegmentation_false_negative | 43 |
| low_contrast_defect_miss | 10 |
| small_defect_miss | 4 |
| oversegmentation_false_positive | 3 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
