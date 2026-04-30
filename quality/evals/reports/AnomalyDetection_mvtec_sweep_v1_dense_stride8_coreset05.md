# AnomalyDetection MVTec AD Lite Candidate v1

GeneratedAtUtc: `2026-04-29T16:05:45.4932603+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v1`
Profile: `dense_stride8_coreset05`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.8029 |
| Pixel AUROC | 0.7924 |
| Image precision | 1.0000 |
| Image recall | 0.4253 |
| Image F1 | 0.5968 |
| Image TP / FP / FN / TN | 37 / 0 / 50 / 33 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 128 |
| Patch size / stride | 16 / 8 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0500 |
| Runtime ms | 17335.478 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 2970 | 8797.882 | 3372.716 | 0.7118 | 0.6436 |
| toothbrush | 60 | 42 | 30 | 675 | 473.679 | 1164.148 | 0.9611 | 0.9432 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 50 |
| zero_score_anomaly | 27 |
| below_threshold_anomaly | 23 |
| defect_bent | 11 |
| defect_defective | 11 |
| defect_metal_contamination | 11 |
| defect_broken | 9 |
| defect_glue | 5 |
| defect_thread | 3 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/001 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/002 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.1821 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0121 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/009 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/broken/002 | True | False | 0.2505 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/009 | True | False | 0.2632 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/glue/001 | True | False | 0.1195 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/002 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
