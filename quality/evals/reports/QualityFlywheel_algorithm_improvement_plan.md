# Quality Flywheel Algorithm Improvement Plan

GeneratedAtUtc: `2026-04-29T00:00:00Z`

## Policy

- Strategy: evidence-driven optimization
- A/B rule: Every algorithm change must compare old/new metrics, failed case replay, performance, memory, and regression risk.
- Test rule: Validation split freezes thresholds; test split is final proof only and requires a new proof version after retuning.

## Work Queue

| Operator | Family | Current | Target | Next Action |
|---|---|---|---|---|
| ImageAcquisition | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AffineTransform | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BilateralFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| FFT1D | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Filtering | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| FrequencyFilter | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageAdd | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageBlend | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageCompose | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageCrop | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageNormalize | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageResize | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageRotate | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageStitching | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageSubtract | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageTiling | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| InverseFFT1D | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| MeanFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| PerspectiveTransform | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AdaptiveThreshold | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BinaryImageToRegion | image-processing | missing | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BlobAnalysis | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| DistanceTransform | measurement-geometry | golden | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| Morphology | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RectangleRegion | image-processing | missing | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionClosing | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionComplement | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionDifference | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionDilation | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionErosion | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionIntersection | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionOpening | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionSkeleton | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionUnion | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Thresholding | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BlobLabeling | measurement-geometry | golden | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| CodeRecognition | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ContourDetection | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| GlcmTexture | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageDiff | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| LawsTextureFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AkazeFeatureMatch | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| ContourExtrema | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GradientShapeMatch | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| LocalDeformableMatching | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| OrbFeatureMatch | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| PlanarMatching | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| PyramidShapeMatch | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| RoiTransform | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ShapeMatching | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| TemplateMatching | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| DualModalVoting | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| EdgePairDefect | ai-vision | contract | public-benchmark | Attach kolektorsdd2, mvtec_ad_lite manifest, split, runner, and threshold gate |
| SurfaceDefectDetection | ai-vision | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| AngleMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| ArcCaliper | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| CaliperTool | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| CircleMeasurement | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| ColorMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| ContourMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GapMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GeoMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GeometricFitting | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| LineMeasurement | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| Measurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| MinEnclosingGeometry | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| WidthMeasurement | measurement-geometry | golden | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| CoordinateTransform | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| FisheyeCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| FisheyeUndistort | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| HandEyeCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| HandEyeCalibrationValidator | calibration | contract | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| NPointCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| PixelToWorldTransform | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| TranslationRotationCalibration | calibration | contract | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| Undistort | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| AnomalyDetection | ai-vision | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| DeepLearning | ai-vision | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| OcrRecognition | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| PPFMatch | matching-localization | contract | public-benchmark | Attach hpatches, semisynthetic-homography-oracle manifest, split, runner, and threshold gate |
| RansacPlaneSegmentation | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BoxFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
