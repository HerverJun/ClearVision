# Calibration Operator Benchmark Report

Generated (UTC): 2026-08-04T02:12:02.7572676Z

| Operator | Resolution | Iterations | Avg (ms) | P95 (ms) | P99 (ms) | Status |
|---|---:|---:|---:|---:|---:|---|
| CoordinateTransform | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| NPointCalibration | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| TranslationRotationCalibration | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| PixelToWorldTransform | 512x512 | 6 | 0.00 | 0.00 | 0.00 | OK |
| CalibrationLoader | 512x512 | 6 | 0.67 | 4.00 | 4.00 | OK |
| Undistort | 512x512 | 6 | 22.00 | 105.00 | 105.00 | OK |
| FisheyeUndistort | 512x512 | 6 | 71.17 | 172.00 | 172.00 | OK |
| FisheyeCalibration | 512x512 | 6 | 108.83 | 329.00 | 329.00 | NeedOptimize |
| CameraCalibration | 512x512 | 6 | 161.17 | 957.00 | 957.00 | NeedOptimize |
| HandEyeCalibration | 512x512 | 6 | 166.83 | 352.00 | 352.00 | NeedOptimize |
| StereoCalibration | 512x512 | 6 | 213.17 | 1254.00 | 1254.00 | NeedOptimize |
| HandEyeCalibrationValidator | 512x512 | 6 | 490.50 | 1365.00 | 1365.00 | NeedOptimize |
