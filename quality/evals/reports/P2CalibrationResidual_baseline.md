# P2 Calibration Residual Baseline

GeneratedAtUtc: `2026-04-29T03:29:47.2951802+00:00`

## Summary

CaseCount: 72
Passed: 72
Failed: 0
RuntimeMs: 123.037

## Operators

| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryBytesAvg |
|---|---:|---:|---:|---:|---:|
| CalibrationLoader | 24 | 24 | 0 | 3.31 | 39254 |
| NPointCalibration | 24 | 24 | 0 | 1.406 | 21999 |
| TranslationRotationCalibration | 24 | 24 | 0 | 0.411 | 22329 |

## Scenarios

| Scenario | Cases | Passed | Failed | RuntimeMsAvg |
|---|---:|---:|---:|---:|
| Affine geometry oracle | 8 | 8 | 0 | 3.708 |
| Degenerate geometry failure contract | 4 | 4 | 0 | 0.113 |
| Insufficient points failure contract | 8 | 8 | 0 | 0.188 |
| Invalid JSON failure contract | 4 | 4 | 0 | 1.738 |
| Missing file failure contract | 8 | 8 | 0 | 0.139 |
| Parameter validation contract | 8 | 8 | 0 | 0.102 |
| Perspective geometry oracle | 8 | 8 | 0 | 0.365 |
| Rigid transform oracle | 8 | 8 | 0 | 0.109 |
| Similarity transform oracle | 8 | 8 | 0 | 0.99 |
| Valid bundle load | 8 | 8 | 0 | 8.853 |
