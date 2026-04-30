# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:01.0481810+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `balanced_floor_14_area5`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.2022 |
| Dice | 0.3364 |
| Pixel F1 | 0.3364 |
| Image AUROC | 0.7969 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.6558 |
| Image recall | 0.8417 |
| Image F1 | 0.7372 |
| False positive per normal image | 0.1472 |
| Runtime p95 ms | 6.068 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 14.000 |
| Min area | 5 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 101 | 53 | 19 | 307 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 52 |
| undersegmentation_false_negative | 48 |
| low_contrast_defect_miss | 12 |
| small_defect_miss | 7 |
| mask_boundary_mismatch | 1 |
| oversegmentation_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
