# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:09.7644155+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `balanced_floor_14_area7`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1988 |
| Dice | 0.3316 |
| Pixel F1 | 0.3316 |
| Image AUROC | 0.7929 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.7925 |
| Image recall | 0.7000 |
| Image F1 | 0.7434 |
| False positive per normal image | 0.0611 |
| Runtime p95 ms | 6.060 |
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
| 84 | 22 | 36 | 338 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| undersegmentation_false_negative | 36 |
| low_contrast_defect_miss | 24 |
| texture_noise_false_positive | 22 |
| small_defect_miss | 12 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
