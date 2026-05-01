# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T15:38:37.3097882+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v1`
Profile: `otsu_local_area4`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 480 |
| Passed | 480 |
| Failed gates | 0 |
| Defect images | 120 |
| Normal images | 360 |
| Pixel IoU | 0.0162 |
| Dice | 0.0319 |
| Pixel F1 | 0.0319 |
| Image AUROC | 0.7783 |
| Pixel AUROC | 0.6879 |
| Image precision | 0.2500 |
| Image recall | 1.0000 |
| Image F1 | 0.4000 |
| False positive per normal image | 1.0000 |
| Runtime p95 ms | 9.128 |
| Method | LocalContrast |
| Threshold mode | Otsu |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
| Response normalize mode | RawClamp |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 120 | 360 | 0 | 0 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| oversegmentation_false_positive | 360 |
| mask_overgrowth_false_positive | 82 |
| undersegmentation_false_negative | 12 |
| mask_boundary_mismatch | 3 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
