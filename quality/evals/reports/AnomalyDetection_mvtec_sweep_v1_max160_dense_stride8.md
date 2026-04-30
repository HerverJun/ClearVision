# AnomalyDetection MVTec AD Lite Candidate v1

GeneratedAtUtc: `2026-04-29T16:06:04.9575121+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v1`
Profile: `max160_dense_stride8`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.8885 |
| Pixel AUROC | 0.8334 |
| Image precision | 1.0000 |
| Image recall | 0.4138 |
| Image F1 | 0.5854 |
| Image TP / FP / FN / TN | 36 / 0 / 51 / 33 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 160 |
| Patch size / stride | 16 / 8 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 17370.767 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 1906 | 8449.122 | 3343.734 | 0.8538 | 0.7508 |
| toothbrush | 60 | 42 | 30 | 433 | 466.134 | 1184.797 | 0.9500 | 0.9276 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 51 |
| below_threshold_anomaly | 33 |
| defect_defective | 19 |
| zero_score_anomaly | 18 |
| defect_bent | 11 |
| defect_broken | 9 |
| defect_glue | 8 |
| defect_metal_contamination | 4 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/000 | True | False | 0.0327 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/001 | True | False | 0.2739 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/002 | True | False | 0.0376 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/003 | True | False | 0.2052 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | True | False | 0.0570 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/011 | True | False | 0.0306 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/broken/000 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/002 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.1916 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0004 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/glue/001 | True | False | 0.0172 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/002 | True | False | 0.0207 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/003 | True | False | 0.1760 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/004 | True | False | 0.2937 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/005 | True | False | 0.1275 | anomaly_miss, below_threshold_anomaly, defect_glue |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
