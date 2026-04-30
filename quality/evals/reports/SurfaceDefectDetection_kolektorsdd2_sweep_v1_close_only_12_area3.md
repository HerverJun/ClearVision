# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:30.9272861+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `close_only_12_area3`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.1155 |
| Dice | 0.2072 |
| Pixel F1 | 0.2072 |
| Image AUROC | 0.7824 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.2505 |
| Image recall | 1.0000 |
| Image F1 | 0.4007 |
| False positive per normal image | 0.9972 |
| Runtime p95 ms | 8.746 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 12.000 |
| Min area | 3 |
| Morph clean size | 3 |
| Morph mode | CloseOnly |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 120 | 359 | 0 | 1 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| oversegmentation_false_positive | 322 |
| texture_noise_false_positive | 37 |
| mask_overgrowth_false_positive | 33 |
| undersegmentation_false_negative | 17 |
| mask_boundary_mismatch | 14 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
