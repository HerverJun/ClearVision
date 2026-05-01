# AnomalyDetection MVTec AD Lite Candidate v1

GeneratedAtUtc: `2026-04-29T16:06:52.2886673+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v1`
Profile: `max160_patch12_stride6`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.9025 |
| Pixel AUROC | 0.8577 |
| Image precision | 0.9811 |
| Image recall | 0.5977 |
| Image F1 | 0.7429 |
| Image TP / FP / FN / TN | 52 / 1 / 35 / 32 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 160 |
| Patch size / stride | 12 / 6 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Runtime ms | 45192.811 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 3569 | 28522.425 | 9266.933 | 0.8855 | 0.7640 |
| toothbrush | 60 | 42 | 30 | 811 | 1537.325 | 1946.753 | 0.9500 | 0.9491 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 35 |
| below_threshold_anomaly | 23 |
| zero_score_anomaly | 12 |
| defect_defective | 11 |
| defect_bent | 9 |
| defect_broken | 7 |
| defect_glue | 5 |
| defect_metal_contamination | 2 |
| above_threshold_good | 1 |
| defect_thread | 1 |
| good_false_positive | 1 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/000 | True | False | 0.0879 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/001 | True | False | 0.0298 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/004 | True | False | 0.0376 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | True | False | 0.0162 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/010 | True | False | 0.2152 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/broken/000 | True | False | 0.2649 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.2779 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/007 | True | False | 0.0366 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0021 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/glue/001 | True | False | 0.0672 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/002 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/glue/003 | True | False | 0.1139 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/007 | True | False | 0.0278 | anomaly_miss, below_threshold_anomaly, defect_glue |
| grid/glue/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |
| grid/good/005 | False | True | 0.3743 | good_false_positive, above_threshold_good |
| grid/metal_contamination/008 | True | False | 0.2459 | anomaly_miss, below_threshold_anomaly, defect_metal_contamination |
| grid/metal_contamination/009 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_metal_contamination |
| grid/thread/003 | True | False | 0.2836 | anomaly_miss, below_threshold_anomaly, defect_thread |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation with `lab_gradient_stats` features.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
