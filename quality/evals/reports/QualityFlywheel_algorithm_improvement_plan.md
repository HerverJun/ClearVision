# Quality Flywheel Algorithm Improvement Plan

GeneratedAtUtc: `2026-04-29T00:00:00Z`

## Policy

- Strategy: evidence-driven optimization
- A/B rule: Every algorithm change must compare old/new metrics, failed case replay, performance, memory, and regression risk.
- Test rule: Validation split freezes thresholds; test split is final proof only and requires a new proof version after retuning.

## Work Queue

| Operator | Family | Current | Target | Next Action |
|---|---|---|---|---|
| PPFEstimation | general-operator | missing | contract | Add contract suite coverage and protocol/error replay cases |
| PPFMatch | matching-localization | contract | public-benchmark | Attach hpatches, semisynthetic-homography-oracle manifest, split, runner, and threshold gate |
| RansacPlaneSegmentation | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| DualModalVoting | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AnomalyDetection | ai-vision | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| DeepLearning | ai-vision | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| EdgePairDefect | ai-vision | contract | public-benchmark | Attach kolektorsdd2, mvtec_ad_lite manifest, split, runner, and threshold gate |
| SemanticSegmentation | ai-vision | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| SurfaceDefectDetection | ai-vision | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| DistanceTransform | measurement-geometry | golden | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| AngleMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| ContourMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| FFT1D | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| FrequencyFilter | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| InverseFFT1D | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionClosing | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionDilation | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionErosion | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionOpening | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionSkeleton | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionComplement | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionDifference | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionIntersection | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RegionUnion | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| GlcmTexture | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| LawsTextureFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AkazeFeatureMatch | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| GradientShapeMatch | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| LocalDeformableMatching | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| OrbFeatureMatch | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| PlanarMatching | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| PyramidShapeMatch | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| ShapeMatching | matching-localization | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| TemplateMatching | matching-localization | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| AffineTransform | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageStitching | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BlobLabeling | measurement-geometry | golden | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| ImageCompose | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageTiling | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BoxFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| CoordinateTransform | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| FisheyeCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| FisheyeUndistort | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| HandEyeCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| HandEyeCalibrationValidator | calibration | contract | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| NPointCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| PixelToWorldTransform | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| StereoCalibration | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| TranslationRotationCalibration | calibration | contract | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| Undistort | calibration | golden | public-benchmark | Attach opencv_calibration_samples, semisynthetic-calibration-oracle manifest, split, runner, and threshold gate |
| ArcCaliper | measurement-geometry | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| CaliperTool | measurement-geometry | field-substitute | field-substitute | Maintain evidence, add failure replay, and keep claim audit passing |
| CircleMeasurement | measurement-geometry | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| ContourExtrema | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GapMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GeoMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| GeometricFitting | measurement-geometry | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| LineMeasurement | measurement-geometry | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| Measurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| MinEnclosingGeometry | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| WidthMeasurement | measurement-geometry | golden | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
| BlobAnalysis | measurement-geometry | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| ContourDetection | measurement-geometry | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| EdgeDetection | general-operator | public-benchmark | field-substitute | Promote to public/semisynthetic proof, then add field-substitute replay and failure taxonomy |
| CodeRecognition | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| OcrRecognition | ai-vision | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| RoiTransform | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageAcquisition | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| AdaptiveThreshold | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| BilateralFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Filtering | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageAdd | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageBlend | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageCrop | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageDiff | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageNormalize | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageResize | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageRotate | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ImageSubtract | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| MeanFilter | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Morphology | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| PerspectiveTransform | image-processing | golden | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| Thresholding | image-processing | contract | public-benchmark | Attach semisynthetic-oracle manifest, split, runner, and threshold gate |
| ColorMeasurement | measurement-geometry | contract | public-benchmark | Attach semisynthetic-geometry-oracle, bsds500 manifest, split, runner, and threshold gate |
