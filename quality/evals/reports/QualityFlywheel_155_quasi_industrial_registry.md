# Quality Flywheel Governed-Population Quasi-Industrial Proof Registry

GeneratedAtUtc: `2026-04-29T00:00:00Z`

## Summary

- Operators: 158
- Population fingerprint: `sha256:4cd53973dd918e3669dc06e2ae1e901b440810e9021ac974fcce1038b719896a`
- Exposure: public=156, internal=1, legacy=4, disabled=1
- Core20 operators: 20
- Target met: 94
- Gap open: 64
- Real industrial validation complete: 0
- Claim boundary: quasi-industrial public/substitute evidence only; real field sign-off remains pending.

## Public Dataset Plan

| Dataset | Status | Records | Proof Use |
|---|---|---:|---|
| kolektorsdd2 | planned | 3335 | public industrial surface defect benchmark |
| mvtec_ad_lite | available-local | 444 | public industrial anomaly detection benchmark subset |
| bsds500 | planned | 500 | public boundary and segmentation benchmark |
| opencv_calibration_samples | planned | 13 | public calibration sample images and parameter files |
| coco2017 | planned | 5000 | public object detection benchmark bridge |
| hpatches | planned | 116 | public homography and feature matching benchmark |

## Operator Gaps

| Operator | Family | Current | Target | Status | Next Action |
|---|---|---|---|---|---|
| ImageAcquisition | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AffineTransform | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BilateralFilter | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| FFT1D | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Filtering | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| FrequencyFilter | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageAdd | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageBlend | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageCompose | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageCrop | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageNormalize | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageResize | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageRotate | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageStitching | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageSubtract | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageTiling | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| InverseFFT1D | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| MeanFilter | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| PerspectiveTransform | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AdaptiveThreshold | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BinaryImageToRegion | image-processing | missing | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| DistanceTransform | measurement-geometry | golden | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| Morphology | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RectangleRegion | image-processing | missing | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionClosing | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionComplement | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionDifference | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionDilation | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionErosion | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionIntersection | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionOpening | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionSkeleton | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionUnion | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Thresholding | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BlobLabeling | measurement-geometry | golden | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| CodeRecognition | ai-vision | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| GlcmTexture | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageDiff | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| LawsTextureFilter | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ContourExtrema | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| RoiTransform | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| DualModalVoting | ai-vision | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| EdgePairDefect | ai-vision | contract | public-benchmark | gap-open | Attach kolektorsdd2, mvtec_ad_lite manifest, split, runner, and threshold gate |
| AngleMeasurement | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| ColorMeasurement | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| ContourMeasurement | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GapMeasurement | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GeoMeasurement | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| Measurement | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| MinEnclosingGeometry | measurement-geometry | contract | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| WidthMeasurement | measurement-geometry | golden | public-benchmark | gap-open | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| CoordinateTransform | calibration | golden | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| FisheyeCalibration | calibration | golden | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| FisheyeUndistort | calibration | golden | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| HandEyeCalibration | calibration | golden | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| HandEyeCalibrationValidator | calibration | contract | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| NPointCalibration | calibration | golden | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| PixelToWorldTransform | image-processing | golden | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| TranslationRotationCalibration | calibration | contract | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| Undistort | calibration | golden | public-benchmark | gap-open | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| OcrRecognition | ai-vision | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| PPFMatch | matching-localization | contract | public-benchmark | gap-open | Attach hpatches, semisynthetic-homography-oracle manifest, split, runner, and threshold gate |
| RansacPlaneSegmentation | ai-vision | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BoxFilter | image-processing | contract | public-benchmark | gap-open | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |

## Audit Boundary

- Public benchmark and semisynthetic evidence may support quasi-industrial claims.
- Real production-site validation is blocked until own field data and sign-off are attached.
