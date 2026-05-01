# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:05.3250917+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `balanced_floor_14_area6`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1997 |
| Dice | 0.3329 |
| Pixel F1 | 0.3329 |
| Image AUROC | 0.7942 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.7339 |
| Image recall | 0.7583 |
| Image F1 | 0.7459 |
| False positive per normal image | 0.0917 |
| Runtime p95 ms | 5.986 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 14.000 |
| Min area | 6 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 91 | 33 | 29 | 327 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| undersegmentation_false_negative | 39 |
| texture_noise_false_positive | 33 |
| low_contrast_defect_miss | 20 |
| small_defect_miss | 9 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
