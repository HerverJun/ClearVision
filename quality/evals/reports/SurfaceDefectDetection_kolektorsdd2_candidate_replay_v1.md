# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T16:13:09.0015134+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `balanced_floor_14_area7`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed gates | 0 |
| Defect images | 2 |
| Normal images | 18 |
| Pixel IoU | 0.0000 |
| Dice | 0.0000 |
| Pixel F1 | 0.0000 |
| Image AUROC | 0.6111 |
| Pixel AUROC | 0.6168 |
| Image precision | 0.0000 |
| Image recall | 0.0000 |
| Image F1 | 0.0000 |
| False positive per normal image | 0.4444 |
| Runtime p95 ms | 7.049 |
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
| 0 | 8 | 2 | 10 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 7 |
| low_contrast_defect_miss | 2 |
| oversegmentation_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
