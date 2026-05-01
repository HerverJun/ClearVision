# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:16:00.6732123+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `texture_noise_shape_response_area8`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1175 |
| Dice | 0.2104 |
| Pixel F1 | 0.2104 |
| Image AUROC | 0.7817 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.9730 |
| Image recall | 0.3000 |
| Image F1 | 0.4586 |
| False positive per normal image | 0.0028 |
| Runtime p95 ms | 5.643 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 18.000 |
| Min area | 8 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| CLAHE clip / tile | 2 / 8 |
| Component filter mode | ShapeAndResponseStats |
| Small noise area max | 48 |
| Min elongation small component | 2.5 |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 36 | 1 | 84 | 359 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| low_contrast_defect_miss | 65 |
| small_defect_miss | 19 |
| undersegmentation_false_negative | 7 |
| texture_noise_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
