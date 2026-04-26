# P2 Calibration Residual Baseline

GeneratedAtUtc: `2026-04-26T16:35:52.6512919+00:00`

## Summary

CaseCount: 72
Passed: 72
Failed: 0
RuntimeMs: 817.121

## Operators

| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryBytesAvg |
|---|---:|---:|---:|---:|---:|
| CalibrationLoader | 24 | 24 | 0 | 5.125 | 40263 |
| NPointCalibration | 24 | 24 | 0 | 28.377 | 19952 |
| TranslationRotationCalibration | 24 | 24 | 0 | 0.545 | 21322 |

## Scenarios

| Scenario | Cases | Passed | Failed | RuntimeMsAvg |
|---|---:|---:|---:|---:|
| Affine geometry oracle | 8 | 8 | 0 | 84.349 |
| Degenerate geometry failure contract | 4 | 4 | 0 | 0.147 |
| Insufficient points failure contract | 8 | 8 | 0 | 0.34 |
| Invalid JSON failure contract | 4 | 4 | 0 | 2.925 |
| Missing file failure contract | 8 | 8 | 0 | 0.105 |
| Parameter validation contract | 8 | 8 | 0 | 0.118 |
| Perspective geometry oracle | 8 | 8 | 0 | 0.499 |
| Rigid transform oracle | 8 | 8 | 0 | 0.197 |
| Similarity transform oracle | 8 | 8 | 0 | 1.265 |
| Valid bundle load | 8 | 8 | 0 | 13.732 |
