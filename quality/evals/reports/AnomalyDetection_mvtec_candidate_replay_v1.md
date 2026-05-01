# AnomalyDetection MVTec AD Lite Candidate v1

GeneratedAtUtc: `2026-04-30T11:52:33.3988275+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v1`
Profile: `max192_dense_stride8`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 264 |
| Test images | 20 |
| Test anomaly images | 20 |
| Test good images | 0 |
| Image AUROC | 0.0000 |
| Pixel AUROC | 0.6212 |
| Image precision | 1.0000 |
| Image recall | 0.2500 |
| Image F1 | 0.4000 |
| Image TP / FP / FN / TN | 5 / 0 / 15 / 0 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 192 |
| Patch size / stride | 16 / 8 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 19056.190 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 20 | 20 | 2793 | 15429.799 | 1350.793 | 0.0000 | 0.6212 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 15 |
| below_threshold_anomaly | 9 |
| defect_broken | 8 |
| defect_bent | 7 |
| zero_score_anomaly | 6 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
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

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
