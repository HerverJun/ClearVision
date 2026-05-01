# OpenCV Calibration Dataset Baseline

GeneratedAtUtc: `2026-04-30T04:24:47.6899975+00:00`
Dataset: `opencv-calibration-samples-left-right-stereo`

## Summary

Passed: `3/3`
Failed: `0`
Accepted: `True`
AcceptedCaseCount: `2/3`
RequireAcceptedCaseCount: `0`
DetectedImageCount: `12/39`
WorstReprojectionRmsPx: `0.367839`
MaxReprojectionErrorPx: `0.470042`
RuntimeMs: `609.532`
Thresholds: `{"RequireAccepted":false,"MinDetectedImages":10,"MaxReprojectionRmsPx":1,"MinStereoPairs":10,"MaxStereoReprojectionRmsPx":1,"MaxEpipolarErrorPx":1,"CandidateVersion":"v1","Profile":"camera_calibration"}`

## Cases

| Id | Operator | Passed | Accepted | Samples | RMS px | Max px | Runtime ms | Failure reason | Error |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| opencv_calibration_left_camera | CameraCalibration | True | True | 12/13 | 0.337790 | 0.458456 | 350.137 |  |  |
| opencv_calibration_right_camera | CameraCalibration | True | False | 13/13 | 0.367839 | 0.470042 | 258.824 |  |  |
| opencv_calibration_stereo_rig | StereoMetadata | True | True | 13/13 | 0.000000 | 0.000000 | 0.571 |  |  |

## Stereo Metadata

