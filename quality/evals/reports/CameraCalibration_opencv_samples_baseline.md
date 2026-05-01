# OpenCV Calibration Dataset Baseline

GeneratedAtUtc: `2026-05-01T03:23:14.5784701+00:00`
Dataset: `opencv-calibration-samples-left-right-stereo`

## Summary

Passed: `4/4`
Failed: `0`
Accepted: `True`
AcceptedCaseCount: `2/4`
RequireAcceptedCaseCount: `0`
DetectedImageCount: `12/52`
WorstReprojectionRmsPx: `0.392596`
MaxReprojectionErrorPx: `0.563355`
RuntimeMs: `1077.494`
Thresholds: `{"RequireAccepted":false,"MinDetectedImages":10,"MaxReprojectionRmsPx":1,"MinStereoPairs":10,"MaxStereoReprojectionRmsPx":1,"MaxEpipolarErrorPx":2.5,"CandidateVersion":"v1","Profile":"camera_calibration"}`

## Cases

| Id | Operator | Passed | Accepted | Samples | RMS px | Max px | Runtime ms | Failure reason | Error |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| opencv_calibration_left_camera | CameraCalibration | True | True | 12/13 | 0.337790 | 0.458456 | 354.786 |  |  |
| opencv_calibration_right_camera | CameraCalibration | True | False | 13/13 | 0.367839 | 0.470042 | 275.632 |  |  |
| opencv_calibration_stereo_rig | StereoCalibration | True | False | 13/13 | 0.392596 | 0.563355 | 446.370 |  |  |
| opencv_calibration_stereo_metadata | StereoMetadata | True | True | 13/13 | 0.000000 | 0.000000 | 0.706 |  |  |

## Stereo Metadata

ExpectedPairsFromManifest: `13`
ValidPairs: `13/13`
UniquePairIndexCount: `13`
CalibrationFiles: `{"intrinsics":"quality/public_datasets/opencv_calibration_samples/intrinsics.yml","left_intrinsics":"quality/public_datasets/opencv_calibration_samples/left_intrinsics.yml","stereo_calib":"quality/public_datasets/opencv_calibration_samples/stereo_calib.xml"}`
