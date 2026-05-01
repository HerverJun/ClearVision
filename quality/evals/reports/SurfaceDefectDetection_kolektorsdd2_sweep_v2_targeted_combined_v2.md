# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:53:20.0689081+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `targeted_combined_v2`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1562 |
| Dice | 0.2702 |
| Pixel F1 | 0.2702 |
| Image AUROC | 0.8035 |
| Pixel AUROC | 0.6642 |
| Image precision | 0.2636 |
| Image recall | 0.9667 |
| Image F1 | 0.4143 |
| False positive per normal image | 0.9000 |
| Runtime p95 ms | 7.154 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 6 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 1.5 / 8 |
| Component filter mode | ShapeAndResponseStats |
| Small noise area max | 32 |
| Min elongation small component | 2.5 |
| Compact noise area max | 64 |
| Compact noise circularity min | 0.68 |
| Compact noise fill ratio min | 0.45 |
| Min local response prominence | 4 |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 116 | 324 | 4 | 36 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| oversegmentation_false_positive | 228 |
| texture_noise_false_positive | 96 |
| mask_overgrowth_false_positive | 19 |
| undersegmentation_false_negative | 19 |
| mask_boundary_mismatch | 12 |
| small_defect_miss | 3 |
| low_contrast_defect_miss | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
