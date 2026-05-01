# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T03:59:44.5177274+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `texture_noise_response_stats_area8`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1418 |
| Dice | 0.2485 |
| Pixel F1 | 0.2485 |
| Image AUROC | 0.7868 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.9839 |
| Image recall | 0.5083 |
| Image F1 | 0.6703 |
| False positive per normal image | 0.0028 |
| Runtime p95 ms | 4.591 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 18.000 |
| Min area | 8 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 2 / 8 |
| Component filter mode | ResponseStats |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 61 | 1 | 59 | 359 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| low_contrast_defect_miss | 41 |
| undersegmentation_false_negative | 25 |
| small_defect_miss | 18 |
| texture_noise_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
