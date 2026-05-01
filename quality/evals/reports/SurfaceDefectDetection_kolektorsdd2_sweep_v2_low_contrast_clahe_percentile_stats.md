# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:53:10.8057013+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `low_contrast_clahe_percentile_stats`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.0145 |
| Dice | 0.0285 |
| Pixel F1 | 0.0285 |
| Image AUROC | 0.7169 |
| Pixel AUROC | 0.6485 |
| Image precision | 0.2500 |
| Image recall | 1.0000 |
| Image F1 | 0.4000 |
| False positive per normal image | 1.0000 |
| Runtime p95 ms | 15.383 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | PercentileClip |
| CLAHE clip / tile | 2 / 12 |
| Component filter mode | ResponseStats |
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
| 120 | 360 | 0 | 0 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| oversegmentation_false_positive | 360 |
| mask_overgrowth_false_positive | 114 |
| mask_boundary_mismatch | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
