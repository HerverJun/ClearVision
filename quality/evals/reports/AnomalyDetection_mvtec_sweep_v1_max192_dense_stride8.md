# AnomalyDetection MVTec AD Lite Candidate v1

GeneratedAtUtc: `2026-04-29T16:07:24.8936296+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v1`
Profile: `max192_dense_stride8`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.9178 |
| Pixel AUROC | 0.8692 |
| Image precision | 1.0000 |
| Image recall | 0.6322 |
| Image F1 | 0.7746 |
| Image TP / FP / FN / TN | 55 / 0 / 32 / 33 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 192 |
| Patch size / stride | 16 / 8 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 30423.258 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 2793 | 17868.822 | 6060.205 | 0.8839 | 0.7931 |
| toothbrush | 60 | 42 | 30 | 635 | 985.768 | 1542.691 | 0.9833 | 0.9542 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 32 |
| below_threshold_anomaly | 22 |
| zero_score_anomaly | 10 |
| defect_broken | 9 |
| defect_defective | 9 |
| defect_bent | 8 |
| defect_glue | 5 |
| defect_metal_contamination | 1 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/002 | True | False | 0.2614 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/003 | True | False | 0.3001 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | True | False | 0.0336 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/011 | True | False | 0.1545 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/broken/000 | True | False | 0.2220 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/002 | True | False | 0.0154 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/004 | True | False | 0.1946 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.0347 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/006 | True | False | 0.1235 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/007 | True | False | 0.2539 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/glue/001 | True | False | 0.3155 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/002 | True | False | 0.0148 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/007 | True | False | 0.2267 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/metal_contamination/007 | True | False | 0.1530 | anomaly_miss, below_threshold_anomaly, defect_metal_contamination |
| toothbrush/defective/001 | True | False | 0.0141 | anomaly_miss, below_threshold_anomaly, defect_defective |
| toothbrush/defective/002 | True | False | 0.1805 | anomaly_miss, below_threshold_anomaly, defect_defective |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
