# AnomalyDetection MVTec AD Lite Baseline

GeneratedAtUtc: `2026-04-26T07:33:26.6807218+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.6609 |
| Pixel AUROC | 0.6709 |
| Max side | 128 |
| Patch size / stride | 16 / 16 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 5103.933 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 338 | 386.407 | 816.854 | 0.6140 | 0.5605 |
| toothbrush | 60 | 42 | 30 | 77 | 21.978 | 742.390 | 0.7500 | 0.7630 |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
