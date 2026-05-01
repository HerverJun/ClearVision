# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:53:13.9415049+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `undersegmentation_closeopen_kernel3`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.2403 |
| Dice | 0.3875 |
| Pixel F1 | 0.3875 |
| Image AUROC | 0.7865 |
| Pixel AUROC | 0.6714 |
| Image precision | 0.5301 |
| Image recall | 0.8083 |
| Image F1 | 0.6403 |
| False positive per normal image | 0.2389 |
| Runtime p95 ms | 4.323 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 3 |
| Morph mode | CloseOpen |
| Background kernel | 21 |
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
| 97 | 86 | 23 | 274 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 83 |
| undersegmentation_false_negative | 39 |
| low_contrast_defect_miss | 15 |
| small_defect_miss | 8 |
| oversegmentation_false_positive | 3 |
| mask_boundary_mismatch | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
