# AnomalyDetection MVTec AD Lite Candidate v2

GeneratedAtUtc: `2026-05-01T03:33:30.6648559+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `v2`
Profile: `max192_dense_stride8_threshold_010`

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
| Image recall | 0.5500 |
| Image F1 | 0.7097 |
| Image TP / FP / FN / TN | 11 / 0 / 9 / 0 |
| Min image AUROC | 0.0000 |
| Min pixel AUROC | 0.0000 |
| Min category image AUROC | 0.0000 |
| Min category pixel AUROC | 0.0000 |
| Failed gates | 0 |
| Max side | 192 |
| Patch size / stride | 16 / 8 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Feature extractor | lab_gradient_stats |
| Embedding model id |  |
| Embedding model source | None |
| Embedding model configured | False |
| Runtime ms | 20476.626 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 20 | 20 | 2793 | 16684.970 | 1458.000 | 0.0000 | 0.6212 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 9 |
| zero_score_anomaly | 6 |
| defect_bent | 5 |
| defect_broken | 4 |
| below_threshold_anomaly | 3 |

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

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation; `onnx_embedding` is an explicit candidate path and keeps model artifacts outside git.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
