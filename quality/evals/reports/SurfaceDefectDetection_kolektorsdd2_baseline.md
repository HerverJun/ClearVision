# SurfaceDefectDetection KolektorSDD2 Baseline

GeneratedAtUtc: `2026-04-29T05:01:07.3791061+00:00`
Index: `quality/datasets/kolektorsdd2_index.json`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 1004 |
| Passed | 1004 |
| Failed gates | 0 |
| Defect images | 110 |
| Normal images | 894 |
| Pixel IoU | 0.1556 |
| Dice | 0.2692 |
| Pixel F1 | 0.2692 |
| Image AUROC | 0.7724 |
| Pixel AUROC | 0.6600 |
| False positive per normal image | 0.1398 |
| Runtime p95 ms | 4.891 |
| Method | LocalContrast |
| Threshold mode | Manual |
| Max side | 256 |

## Image Confusion

| TP | FP | FN | TN |
| ---: | ---: | ---: | ---: |
| 93 | 125 | 17 | 769 |

## Notes

- Runner calls the product `SurfaceDefectDetectionOperator` first and records its current real-dataset behavior.
- Gate is currently a robust smoke baseline: zero operator crashes by default, mask metrics, image confusion, image AUROC, and pixel AUROC are reported.
- KolektorSDD2 is public noncommercial data; keep this evidence separate from commercial release claims.
