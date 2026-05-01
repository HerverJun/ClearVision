# AnomalyDetection MVTec AD Lite Candidate v2

GeneratedAtUtc: `2026-04-30T12:54:09.1532264+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v2`
Profile: `max192_dense_stride8_threshold_010`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.9178 |
| Pixel AUROC | 0.8692 |
| Image precision | 0.9583 |
| Image recall | 0.7931 |
| Image F1 | 0.8679 |
| Image TP / FP / FN / TN | 69 / 3 / 18 / 30 |
| Min image AUROC | 0.7000 |
| Min pixel AUROC | 0.7000 |
| Min category image AUROC | 0.7000 |
| Min category pixel AUROC | 0.7000 |
| Failed gates | 0 |
| Max side | 192 |
| Patch size / stride | 16 / 8 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 26361.742 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 2793 | 15585.383 | 5197.157 | 0.8839 | 0.7931 |
| toothbrush | 60 | 42 | 30 | 635 | 825.613 | 1327.829 | 0.9833 | 0.9542 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 18 |
| zero_score_anomaly | 10 |
| below_threshold_anomaly | 8 |
| defect_bent | 5 |
| defect_broken | 5 |
| defect_defective | 5 |
| above_threshold_good | 3 |
| defect_glue | 3 |
| good_false_positive | 3 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | True | False | 0.0336 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/broken/002 | True | False | 0.0154 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.0347 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/glue/002 | True | False | 0.0148 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/good/005 | False | True | 0.1267 | good_false_positive, above_threshold_good |
| grid/good/010 | False | True | 0.1177 | good_false_positive, above_threshold_good |
| grid/good/014 | False | True | 0.1848 | good_false_positive, above_threshold_good |
| toothbrush/defective/001 | True | False | 0.0141 | anomaly_miss, below_threshold_anomaly, defect_defective |
| toothbrush/defective/003 | True | False | 0.0244 | anomaly_miss, below_threshold_anomaly, defect_defective |
| toothbrush/defective/004 | True | False | 0.0497 | anomaly_miss, below_threshold_anomaly, defect_defective |
| toothbrush/defective/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_defective |
| toothbrush/defective/020 | True | False | 0.0225 | anomaly_miss, below_threshold_anomaly, defect_defective |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
