# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-05-01T04:51:32.4812239+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`
CandidateVersion: `v2`
Profile: `baseline_default`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 1 |
| Passed | 1 |
| Failed gates | 0 |
| Defect images | 0 |
| Normal images | 1 |
| Pixel IoU | 0.0000 |
| Dice | 0.0000 |
| Pixel F1 | 0.0000 |
| Image AUROC | 0.0000 |
| Pixel AUROC | 0.0000 |
| Image precision | 0.0000 |
| Image recall | 0.0000 |
| Image F1 | 0.0000 |
| False positive per normal image | 1.0000 |
| Runtime p95 ms | 42.303 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Threshold | 15.000 |
| Min area | 4 |
| Morph clean size | 1 |
| Morph mode | OpenClose |
| Background kernel | 31 |
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
| 0 | 1 | 0 | 0 |

## Failure Taxonomy

| Taxonomy | Count |
| --- | ---: |
| texture_noise_false_positive | 1 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
