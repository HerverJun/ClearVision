# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:37:56.6534564+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `recall_floor_10_area3`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1707 |
| Dice | 0.2916 |
| Pixel F1 | 0.2916 |
| Image AUROC | 0.7853 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.2532 |
| Image recall | 1.0000 |
| Image F1 | 0.4040 |
| False positive per normal image | 0.9833 |
| Runtime p95 ms | 5.777 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 10.000 |
| Min area | 3 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 120 | 354 | 0 | 6 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| oversegmentation_false_positive | 194 |
| texture_noise_false_positive | 160 |
| undersegmentation_false_negative | 28 |
| mask_overgrowth_false_positive | 17 |
| mask_boundary_mismatch | 15 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
