# Calibration Geometry Round-Trip Baseline

GeneratedAtUtc: `2026-04-26T15:31:01.5032501+00:00`
DatasetKind: `deterministic synthetic geometry round-trip`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 192 |
| Passed | 192 |
| Failed | 0 |
| Runtime ms | 4.651 |
| Memory bytes | 148760 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Mean error | Max error |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CameraCalibration | 24 | 24 | 0 | 0.098 | 0 | 0 |
| CoordinateTransform | 24 | 24 | 0 | 0.018 | 0 | 0 |
| FisheyeCalibration | 24 | 24 | 0 | 0.017 | 0 | 0 |
| FisheyeUndistort | 24 | 24 | 0 | 0.003 | 0 | 0 |
| HandEyeCalibration | 24 | 24 | 0 | 0.035 | 0 | 0 |
| PixelToWorldTransform | 24 | 24 | 0 | 0.003 | 0 | 0 |
| StereoCalibration | 24 | 24 | 0 | 0.009 | 0 | 0 |
| Undistort | 24 | 24 | 0 | 0.011 | 0 | 0 |

## Scenarios

| Scenario | Cases | Passed | Failed | Mean error | Max error |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2D homography round-trip | 24 | 24 | 0 | 0 | 0 |
| AX=XB rigid transform round-trip | 24 | 24 | 0 | 0 | 0 |
| Brown-Conrady undistort round-trip | 24 | 24 | 0 | 0 | 0 |
| Camera intrinsics planar round-trip | 24 | 24 | 0 | 0 | 0 |
| Fisheye undistort round-trip | 24 | 24 | 0 | 0 | 0 |
| Kannala-Brandt fisheye round-trip | 24 | 24 | 0 | 0 | 0 |
| Pixel/world homography round-trip | 24 | 24 | 0 | 0 | 0 |
| Stereo disparity depth round-trip | 24 | 24 | 0 | 0 | 0 |

## Cases

| Case | Operator | Scenario | Passed | Error | Tolerance | Unit | Runtime ms | Failure |
| --- | --- | --- | --- | ---: | ---: | --- | ---: | --- |
| CameraCalibration_round_trip_0000 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 2.293 | - |
| Undistort_round_trip_0000 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.212 | - |
| HandEyeCalibration_round_trip_0000 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.765 | - |
| CoordinateTransform_round_trip_0000 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.371 | - |
| PixelToWorldTransform_round_trip_0000 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.023 | - |
| StereoCalibration_round_trip_0000 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.16 | - |
| FisheyeCalibration_round_trip_0000 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.353 | - |
| FisheyeUndistort_round_trip_0000 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.021 | - |
| CameraCalibration_round_trip_0001 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.004 | - |
| Undistort_round_trip_0001 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0001 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.005 | - |
| CoordinateTransform_round_trip_0001 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.003 | - |
| PixelToWorldTransform_round_trip_0001 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0001 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.006 | - |
| FisheyeCalibration_round_trip_0001 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0001 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0002 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0002 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0002 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.004 | - |
| CoordinateTransform_round_trip_0002 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0002 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0002 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0002 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.006 | - |
| FisheyeUndistort_round_trip_0002 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0003 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0003 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0003 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0003 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0003 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0003 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0003 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0003 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.005 | - |
| CameraCalibration_round_trip_0004 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0004 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0004 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0004 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0004 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0004 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0004 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0004 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0005 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.004 | - |
| Undistort_round_trip_0005 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0005 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0005 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0005 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0005 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0005 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0005 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0006 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0006 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.004 | - |
| HandEyeCalibration_round_trip_0006 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0006 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0006 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0006 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0006 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0006 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0007 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0007 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0007 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.005 | - |
| CoordinateTransform_round_trip_0007 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0007 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0007 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0007 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0007 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0008 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0008 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0008 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.005 | - |
| CoordinateTransform_round_trip_0008 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0008 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0008 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0008 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0008 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0009 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0009 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0009 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0009 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.004 | - |
| PixelToWorldTransform_round_trip_0009 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0009 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0009 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0009 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0010 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0010 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0010 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0010 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0010 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.003 | - |
| StereoCalibration_round_trip_0010 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0010 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0010 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0011 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0011 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0011 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0011 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0011 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0011 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.004 | - |
| FisheyeCalibration_round_trip_0011 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0011 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0012 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0012 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0012 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0012 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0012 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0012 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0012 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.005 | - |
| FisheyeUndistort_round_trip_0012 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0013 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0013 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0013 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0013 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0013 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0013 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0013 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0013 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.004 | - |
| CameraCalibration_round_trip_0014 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0014 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0014 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0014 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0014 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0014 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0014 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0014 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.004 | - |
| CameraCalibration_round_trip_0015 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.006 | - |
| Undistort_round_trip_0015 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0015 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.004 | - |
| CoordinateTransform_round_trip_0015 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0015 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0015 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0015 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0015 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0016 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0016 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.005 | - |
| HandEyeCalibration_round_trip_0016 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0016 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0016 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0016 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0016 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0016 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0017 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0017 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0017 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.006 | - |
| CoordinateTransform_round_trip_0017 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0017 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0017 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0017 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0017 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0018 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0018 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0018 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.005 | - |
| CoordinateTransform_round_trip_0018 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0018 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0018 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0018 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0018 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0019 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0019 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0019 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0019 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.003 | - |
| PixelToWorldTransform_round_trip_0019 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0019 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0019 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0019 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0020 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0020 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0020 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0020 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0020 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.006 | - |
| StereoCalibration_round_trip_0020 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0020 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0020 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0021 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0021 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0021 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0021 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0021 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0021 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.005 | - |
| FisheyeCalibration_round_trip_0021 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0021 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0022 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0022 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0022 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0022 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0022 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0022 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0022 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.004 | - |
| FisheyeUndistort_round_trip_0022 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| CameraCalibration_round_trip_0023 | CameraCalibration | Camera intrinsics planar round-trip | True | 0 | 0.001 | mm | 0.002 | - |
| Undistort_round_trip_0023 | Undistort | Brown-Conrady undistort round-trip | True | 0 | 0.02 | px | 0.002 | - |
| HandEyeCalibration_round_trip_0023 | HandEyeCalibration | AX=XB rigid transform round-trip | True | 0 | 0.000000001 | matrix_abs | 0.003 | - |
| CoordinateTransform_round_trip_0023 | CoordinateTransform | 2D homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| PixelToWorldTransform_round_trip_0023 | PixelToWorldTransform | Pixel/world homography round-trip | True | 0 | 0.000001 | px | 0.002 | - |
| StereoCalibration_round_trip_0023 | StereoCalibration | Stereo disparity depth round-trip | True | 0 | 0.000001 | mm | 0.002 | - |
| FisheyeCalibration_round_trip_0023 | FisheyeCalibration | Kannala-Brandt fisheye round-trip | True | 0 | 0.02 | px | 0.002 | - |
| FisheyeUndistort_round_trip_0023 | FisheyeUndistort | Fisheye undistort round-trip | True | 0 | 0.02 | px | 0.004 | - |

## Notes

- This runner uses deterministic synthetic geometry, not field data.
- It covers camera intrinsics, Brown-Conrady undistortion, fisheye projection, homography pixel/world transforms, stereo disparity, and hand-eye AX=XB consistency.
- Each operator receives at least 20 passing round-trip cases so matrix golden evidence can be aggregated with existing baselines.
