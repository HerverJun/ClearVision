# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:52:59.2731124+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `texture_noise_prominence_guard`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.0436 |
| Dice | 0.0836 |
| Pixel F1 | 0.0836 |
| Image AUROC | 0.7962 |
| Pixel AUROC | 0.6642 |
| Image precision | 0.2500 |
| Image recall | 1.0000 |
| Image F1 | 0.4000 |
| False positive per normal image | 1.0000 |
| Runtime p95 ms | 16.680 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 1.5 / 8 |
| Component filter mode | ShapeAndResponseStats |
| Small noise area max | 0 |
| Min elongation small component | 0 |
| Compact noise area max | 80 |
| Compact noise circularity min | 0.55 |
| Compact noise fill ratio min | 0.35 |
| Min local response prominence | 6 |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 120 | 360 | 0 | 0 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| oversegmentation_false_positive | 360 |
| mask_overgrowth_false_positive | 73 |
| mask_boundary_mismatch | 7 |
| undersegmentation_false_negative | 4 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
