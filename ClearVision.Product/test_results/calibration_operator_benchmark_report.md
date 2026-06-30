# Calibration Operator Benchmark Report

Generated (UTC): 2026-06-28T15:41:52.3312452Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| NPointCalibration | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| CalibrationLoader | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| TranslationRotationCalibration | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| PixelToWorldTransform | 512x512 | 6 | 0.33 | 2.00 | 2.00 | OK |
| CoordinateTransform | 512x512 | 6 | 3.17 | 19.00 | 19.00 | OK |
| StereoCalibration | 512x512 | 6 | 3.17 | 6.00 | 6.00 | OK |
| CameraCalibration | 512x512 | 6 | 7.00 | 15.00 | 15.00 | OK |
| Undistort | 512x512 | 6 | 8.67 | 13.00 | 13.00 | OK |
| FisheyeUndistort | 512x512 | 6 | 14.50 | 22.00 | 22.00 | OK |
| FisheyeCalibration | 512x512 | 6 | 28.67 | 118.00 | 118.00 | OK |
| HandEyeCalibrationValidator | 512x512 | 6 | 86.33 | 133.00 | 133.00 | OK |
| HandEyeCalibration | 512x512 | 6 | 306.50 | 1453.00 | 1453.00 | NeedOptimize |
