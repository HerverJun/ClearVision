# Calibration Synthetic Baseline

GeneratedAtUtc: `2026-04-28T03:07:26.076002+00:00`
CasesRoot: `quality/synthetic/cases/calibration`

## Summary

- Cases: 216
- Passed: 216
- Failed: 0

## Operators

| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryAllocationBytesAvg |
|---|---:|---:|---:|---:|---:|
| CalibrationLoader | 24 | 24 | 0 | 0.046 | 1144 |
| CameraCalibration | 24 | 24 | 0 | 0.077 | 1116 |
| CoordinateTransform | 24 | 24 | 0 | 0.041 | 1144 |
| FisheyeUndistort | 24 | 24 | 0 | 0.073 | 1585 |
| HandEyeCalibration | 24 | 24 | 0 | 0.058 | 1735 |
| NPointCalibration | 24 | 24 | 0 | 0.042 | 1144 |
| PixelToWorldTransform | 24 | 24 | 0 | 0.041 | 1144 |
| StereoCalibration | 24 | 24 | 0 | 0.043 | 1138 |
| Undistort | 24 | 24 | 0 | 0.064 | 1593 |

## Scenarios

| Scenario | Cases | Passed | Failed | RuntimeMsAvg |
|---|---:|---:|---:|---:|
| edge_coverage | 27 | 27 | 0 | 0.049 |
| mild_distortion | 27 | 27 | 0 | 0.049 |
| nominal_grid | 27 | 27 | 0 | 0.081 |
| planar_roundtrip | 27 | 27 | 0 | 0.048 |
| pose_bundle | 27 | 27 | 0 | 0.054 |
| strong_distortion | 27 | 27 | 0 | 0.048 |
| tilted_board | 27 | 27 | 0 | 0.052 |
| wide_angle | 27 | 27 | 0 | 0.051 |
