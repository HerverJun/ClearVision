# OpenCV Calibration Dataset Baseline

GeneratedAtUtc: `2026-04-29T05:00:17.6682426+00:00`
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
RuntimeMs: `651.221`
Thresholds: `{"RequireAccepted":false,"MinDetectedImages":10,"MaxReprojectionRmsPx":1,"MinStereoPairs":10,"MaxStereoReprojectionRmsPx":1,"MaxEpipolarErrorPx":1}`

## Cases

| Id | Operator | Passed | Accepted | Samples | RMS px | Max px | Runtime ms | Failure reason | Error |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| opencv_calibration_left_camera | CameraCalibration | True | True | 12/13 | 0.337790 | 0.458456 | 356.817 |  |  |
| opencv_calibration_right_camera | CameraCalibration | True | False | 13/13 | 0.367839 | 0.470042 | 293.782 |  |  |
| opencv_calibration_stereo_rig | StereoMetadata | True | True | 13/13 | 0.000000 | 0.000000 | 0.622 |  |  |

## Stereo Metadata

ExpectedPairsFromManifest: `13`
ValidPairs: `13/13`
UniquePairIndexCount: `13`
CalibrationFiles: `{"intrinsics":"quality/public_datasets/opencv_calibration_samples/intrinsics.yml","left_intrinsics":"quality/public_datasets/opencv_calibration_samples/left_intrinsics.yml","stereo_calib":"quality/public_datasets/opencv_calibration_samples/stereo_calib.xml"}`
