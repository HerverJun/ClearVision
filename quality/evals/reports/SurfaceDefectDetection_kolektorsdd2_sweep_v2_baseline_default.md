# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:52:47.1444238+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `baseline_default`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1909 |
| Dice | 0.3206 |
| Pixel F1 | 0.3206 |
| Image AUROC | 0.7960 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.6429 |
| Image recall | 0.8250 |
| Image F1 | 0.7226 |
| False positive per normal image | 0.1528 |
| Runtime p95 ms | 4.731 |
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
| Small noise area max | 0 |
| Min elongation small component | 0 |
| Compact noise area max | 0 |
| Compact noise circularity min | 0 |
| Compact noise fill ratio min | 0 |
| Min local response prominence | 0 |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 99 | 55 | 21 | 305 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 55 |
| undersegmentation_false_negative | 47 |
| low_contrast_defect_miss | 14 |
| small_defect_miss | 7 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
