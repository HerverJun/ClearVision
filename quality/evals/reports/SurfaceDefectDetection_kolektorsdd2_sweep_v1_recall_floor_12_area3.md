# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:37:52.1908104+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `recall_floor_12_area3`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.2069 |
| Dice | 0.3428 |
| Pixel F1 | 0.3428 |
| Image AUROC | 0.7917 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.2993 |
| Image recall | 1.0000 |
| Image F1 | 0.4607 |
| False positive per normal image | 0.7806 |
| Runtime p95 ms | 6.954 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 12.000 |
| Min area | 3 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 120 | 281 | 0 | 79 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 253 |
| undersegmentation_false_negative | 43 |
| oversegmentation_false_positive | 28 |
| mask_boundary_mismatch | 8 |
| mask_overgrowth_false_positive | 7 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
