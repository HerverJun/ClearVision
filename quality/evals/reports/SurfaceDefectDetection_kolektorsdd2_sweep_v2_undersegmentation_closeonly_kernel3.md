# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:53:16.6239232+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `undersegmentation_closeonly_kernel3`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.2627 |
| Dice | 0.4162 |
| Pixel F1 | 0.4162 |
| Image AUROC | 0.7896 |
| Pixel AUROC | 0.6714 |
| Image precision | 0.4270 |
| Image recall | 0.9500 |
| Image F1 | 0.5891 |
| False positive per normal image | 0.4250 |
| Runtime p95 ms | 4.596 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 3 |
| Morph mode | CloseOnly |
| Background kernel | 21 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 2 / 8 |
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
| 114 | 153 | 6 | 207 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 143 |
| undersegmentation_false_negative | 41 |
| oversegmentation_false_positive | 10 |
| low_contrast_defect_miss | 5 |
| mask_overgrowth_false_positive | 3 |
| mask_boundary_mismatch | 1 |
| small_defect_miss | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
