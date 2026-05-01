# Quality Flywheel Detection Precision v2

GeneratedAtUtc: `2026-04-30T12:54:35+00:00`
Accepted: `True`
ClaimBoundary: `Public dataset and replay evidence only; no real production-site sign-off.`

## Summary

| Family | Decision | Replay improved | Replay worse | Key delta | Next action |
|---|---|---:|---:|---:|---|
| SurfaceDefectDetection | candidate-ready-with-low-contrast-backlog | 10 | 8 | PixelF1 0.0137 | Do not lower the global threshold further; target low-contrast and undersegmentation misses with guarded local normalization or component filtering. |
| AnomalyDetection | candidate-ready-threshold-calibrated-runtime-heavy | 14 | 0 | ImageF1 0.78 | Keep v2 as a promotion-ready candidate and require field replay before any default threshold promotion. |
| EdgeDetection | hold-recall-tuning | 12 | 8 | BoundaryRecall -0.0566 | Keep EdgeDetection in hold status until recall-guard replay has a profile that restores recall without losing the F1/precision gain. |

## SurfaceDefectDetection

| Metric | Baseline | Candidate | Delta |
|---|---:|---:|---:|
| Pixel F1 | 0.2692 | 0.2829 | 0.0137 |
| Recall at fixed FPR | 0.5182 | 0.5182 | 0 |
| FP/normal | 0.1398 | 0.0515 | -0.0884 |

## AnomalyDetection

| Metric | Baseline | Candidate | Delta |
|---|---:|---:|---:|
| Image AUROC | 0.6609 | 0.9178 | 0.2569 |
| Pixel AUROC | 0.6709 | 0.8692 | 0.1984 |
| Threshold | 0.35 | 0.1 | - |
| Image precision | 1 | 0.9583 | - |
| Image recall | 0.046 | 0.7931 | 0.7471 |
| Image F1 | 0.0879 | 0.8679 | 0.78 |
| Image FP | 0 | 3 | 3 |
| Image FN | 83 | 18 | - |

### Threshold Calibration

Selected: `0.1`; precision `0.958333`; recall `0.793103`; false positives `3`.

### Missed By Defect Type

| Defect | Missed |
|---|---:|
| bent | 5 |
| broken | 5 |
| defective | 5 |
| glue | 3 |

## EdgeDetection

| Metric | Replay old | Replay candidate | Delta |
|---|---:|---:|---:|
| Boundary precision | 0.1691 | 0.1811 | 0.0119 |
| Boundary recall | 0.6858 | 0.6292 | -0.0566 |
| Boundary F1 | 0.2714 | 0.2812 | 0.0098 |
| Full baseline boundary->predicted px | 5.1925 | - | - |

### Edge Replay Taxonomy

| Taxonomy | Count |
|---|---:|
| boundary_f1_drop_gt_0_01 | 4 |
| boundary_recall_drop | 20 |
| consensus_recall_drop | 16 |
| precision_gain_recall_tradeoff | 10 |
| reduced_edge_density | 20 |

### Edge Recall-Guard Sweep

Decision: `hold-current-no-recall-safe-profile`; SelectedProfile: `None`

| Profile | Precision | Recall | F1 | B->P px |
|---|---:|---:|---:|---:|
| fixed_50_150_no_l2 | 0.1691 | 0.6858 | 0.2714 | 11.1145 |
| fixed_50_150_l2 | 0.1811 | 0.6292 | 0.2812 | 12.85 |
| recall_guard_45_135_l2 | 0.1765 | 0.663 | 0.2788 | 11.641 |
| recall_guard_40_120_l2 | 0.1703 | 0.6911 | 0.2733 | 10.8675 |
| recall_guard_35_105_l2 | 0.1659 | 0.721 | 0.2698 | 7.3376 |

## Gates

- productDefaultChange: `False`
- detectionScopedReplay: `detection`
- noPassFailRegressions: `True`
- surfaceFalsePositiveNotHigher: `True`
- surfaceRecallAtFixedFprNotLower: `True`
- anomalyAurocNotLower: `True`
- anomalyCandidateVersion: `v2`
- anomalyThresholdCalibrationAttached: `True`
- anomalyPrecisionFloor: `True`
- anomalyFalsePositiveLimit: `True`
- edgeRecallNeedsFollowup: `True`

## Evidence

- `quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_baseline.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_candidate_v1.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_sweep_v1.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v1.json`
- `quality/evals/reports/AnomalyDetection_mvtec_baseline.json`
- `quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.json`
- `quality/evals/reports/AnomalyDetection_mvtec_sweep_v1.json`
- `quality/evals/reports/AnomalyDetection_mvtec_failure_taxonomy_v1.json`
- `quality/evals/reports/QualityFlywheel_anomaly_threshold_calibration_v1.json`
- `quality/evals/reports/EdgeDetection_bsds500_baseline.json`
- `quality/evals/reports/EdgeDetection_bsds500_candidate_replay_v1.json`
- `quality/evals/reports/QualityFlywheel_edge_detection_recall_guard_sweep_v1.json`
