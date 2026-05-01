# AnomalyDetection Threshold Calibration v1

GeneratedAtUtc: `2026-04-30T12:53:41+00:00`
Accepted: `True`
TargetCandidateVersion: `v2`
TargetProfile: `max192_dense_stride8_threshold_010`
ClaimBoundary: `MVTec AD Lite score-threshold calibration only; no product default promotion and no field sign-off claim.`

## Summary

| Metric | Current | Selected | Delta |
|---|---:|---:|---:|
| Threshold | 0.35 | 0.1 | - |
| Image precision | 1 | 0.9583 | - |
| Image recall | 0.6322 | 0.7931 | 0.1609 |
| Image F1 | 0.7746 | 0.8679 | 0.0933 |
| False positives | 0 | 3 | - |
| False negatives | 32 | 18 | - |

## Threshold Sweep

| Threshold | Precision | Recall | F1 | TP | FP | FN | Recovered |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.35 | 1 | 0.6322 | 0.7746 | 55 | 0 | 32 | 0 |
| 0.3 | 1 | 0.6552 | 0.7917 | 57 | 0 | 30 | 2 |
| 0.25 | 1 | 0.6897 | 0.8163 | 60 | 0 | 27 | 5 |
| 0.2 | 1 | 0.7126 | 0.8322 | 62 | 0 | 25 | 7 |
| 0.15 | 0.9853 | 0.7701 | 0.8645 | 67 | 1 | 20 | 12 |
| 0.1 selected | 0.9583 | 0.7931 | 0.8679 | 69 | 3 | 18 | 14 |
| 0.05 | 0.9452 | 0.7931 | 0.8625 | 69 | 4 | 18 | 14 |
| 0.01 | 0.939 | 0.8851 | 0.9112 | 77 | 5 | 10 | 22 |

## Remaining Misses

| Defect | Count |
|---|---:|
| bent | 5 |
| broken | 5 |
| defective | 5 |
| glue | 3 |

## Gates

- productDefaultChange: `False`
- selectedBelowCurrentThreshold: `True`
- precisionFloor: `True`
- falsePositiveLimit: `True`
- recallImproved: `True`
- f1Improved: `True`

## Evidence

- `quality/evals/reports/AnomalyDetection_mvtec_candidate_v1.json`
