# AnomalyDetection MVTec AD Lite Candidate v1

GeneratedAtUtc: `2026-04-29T16:05:26.1243566+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v1`
Profile: `patch12_stride6`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.8795 |
| Pixel AUROC | 0.8361 |
| Image precision | 1.0000 |
| Image recall | 0.4943 |
| Image F1 | 0.6615 |
| Image TP / FP / FN / TN | 43 / 0 / 44 / 33 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 128 |
| Patch size / stride | 12 / 6 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 22142.437 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 2328 | 12351.102 | 4345.945 | 0.8538 | 0.7757 |
| toothbrush | 60 | 42 | 30 | 529 | 668.819 | 1294.211 | 0.9250 | 0.9186 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 44 |
| below_threshold_anomaly | 26 |
| defect_defective | 20 |
| zero_score_anomaly | 18 |
| defect_bent | 8 |
| defect_broken | 8 |
| defect_glue | 5 |
| defect_metal_contamination | 3 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/002 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/003 | True | False | 0.1511 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0263 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/010 | True | False | 0.1936 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/011 | True | False | 0.0188 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/broken/002 | True | False | 0.1200 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/007 | True | False | 0.0008 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/glue/001 | True | False | 0.0929 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/002 | True | False | 0.0728 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/007 | True | False | 0.0153 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/metal_contamination/007 | True | False | 0.0175 | anomaly_miss, below_threshold_anomaly, defect_metal_contamination |
| grid/metal_contamination/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_metal_contamination |
| grid/metal_contamination/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_metal_contamination |
| toothbrush/defective/001 | True | False | 0.0128 | anomaly_miss, below_threshold_anomaly, defect_defective |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
