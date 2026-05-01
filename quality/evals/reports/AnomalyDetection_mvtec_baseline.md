# AnomalyDetection MVTec AD Lite Baseline

GeneratedAtUtc: `2026-05-01T03:16:15.1408476+00:00`
Index: `quality/datasets/mvtec_ad_lite_index.json`
CandidateVersion: `baseline`
Profile: `baseline_default`

## Summary

| Metric | Value |
| --- | ---: |
| Train images | 324 |
| Test images | 120 |
| Test anomaly images | 87 |
| Test good images | 33 |
| Image AUROC | 0.6609 |
| Pixel AUROC | 0.6709 |
| Image precision | 1.0000 |
| Image recall | 0.0460 |
| Image F1 | 0.0879 |
| Image TP / FP / FN / TN | 4 / 0 / 83 / 33 |
| Min image AUROC | 0.5000 |
| Min pixel AUROC | 0.5000 |
| Min category image AUROC | 0.5000 |
| Min category pixel AUROC | 0.5000 |
| Failed gates | 0 |
| Max side | 128 |
| Patch size / stride | 16 / 16 |
| Pixel sample stride | 2 |
| Coreset ratio | 0.0200 |
| Feature extractor | lab_gradient_stats |
| Embedding model id |  |
| Embedding model source | None |
| Embedding model configured | False |
| Runtime ms | 5731.224 |

## Categories

| Category | Train | Test | Anomaly | Bank features | Train ms | Infer ms | Image AUROC | Pixel AUROC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| grid | 264 | 78 | 57 | 338 | 409.422 | 902.908 | 0.6140 | 0.5605 |
| toothbrush | 60 | 42 | 30 | 77 | 22.736 | 791.145 | 0.7500 | 0.7630 |

## Failure Taxonomy

| Tag | Count |
| --- | ---: |
| anomaly_miss | 83 |
| zero_score_anomaly | 59 |
| defect_defective | 28 |
| below_threshold_anomaly | 24 |
| defect_bent | 12 |
| defect_broken | 12 |
| defect_metal_contamination | 11 |
| defect_glue | 10 |
| defect_thread | 10 |

## Diagnostic Images

| Case | Is anomaly | Predicted | Score | Taxonomy |
| --- | --- | --- | ---: | --- |
| grid/bent/000 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/001 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/002 | True | False | 0.0270 | anomaly_miss, below_threshold_anomaly, defect_bent |
| grid/bent/003 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/009 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/bent/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_bent |
| grid/broken/000 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/001 | True | False | 0.0386 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/002 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/003 | True | False | 0.1227 | anomaly_miss, below_threshold_anomaly, defect_broken |
| grid/broken/004 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/005 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/006 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/007 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/008 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/009 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/010 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/broken/011 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_broken |
| grid/glue/001 | True | False | 0.0000 | anomaly_miss, zero_score_anomaly, defect_glue |

## Notes

- Baseline uses the current SimplePatchCore-Lite implementation; `onnx_embedding` is an explicit candidate path and keeps model artifacts outside git.
- Images and masks are resized to the configured max side before evaluation.
- Pixel AUROC is computed from the normalized float `ScoreMap`, not from the thresholded mask.
- Current AUROC is recorded as baseline evidence for SimplePatchCore-Lite; it is not a claim of production-grade anomaly accuracy.
