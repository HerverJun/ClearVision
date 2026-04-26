# Quality Flywheel 90-Day Closeout

GeneratedAtUtc: `2026-04-26T15:45:00+00:00`

## Matrix Snapshot

| Metric | Value |
| --- | ---: |
| Total operators | 155 |
| A level | 155 |
| B level | 0 |
| C level | 0 |
| Golden evidence Yes | 55 |
| Golden evidence No | 100 |
| Cards with TODO | 0 |
| C-level without golden evidence | 0 |

Source: `quality/evals/reports/operator_quality_matrix.md`

## 90-Day Goal Status

| Goal | Status | Evidence |
| --- | --- | --- |
| C-level operators cleared | Closed | `RegionMorphology_baseline.json`, `ArcCaliper_baseline.json`, `CLevel_contract_baseline.json` |
| At least 10 B/high-value operators promoted to A | Closed | Matrix now has `A=155`, `B=0`; original P3 B=19 are covered by `P3CoreContracts_baseline.json` |
| A-level core operators have evidence reports | Closed for 90-day core scope | Matching, measurement, frequency, AI detection, and calibration evidence listed below |
| Each 90-day core operator has card + golden/baseline + known failure/boundary evidence | Closed for 90-day core scope | Evidence and failure-contract index listed below |

The 90-day core scope follows the TODO's first-stage target: C-level rescue, P1 matching/measurement/frequency upgrades, P1/P2 AI detection, and A-level evidence reinforcement for TemplateMatching, DeepLearning, AnomalyDetection, and calibration-family operators.

## Evidence Index

| Core area | Operators | Baseline / report | Cases | Public / alternative data | Boundary or failure evidence |
| --- | --- | --- | ---: | --- | --- |
| Region / Morphology rescue | RegionUnion, RegionIntersection, RegionDifference, RegionComplement, RegionOpening, RegionClosing, RegionDilation, RegionErosion, RegionSkeleton | `RegionMorphology_baseline.json`, `RegionMorphology_before_after_report.md` | 900 | Synthetic masks | `quality/triage/failure_reports/RegionMorphology_failure_triage.md` |
| Remaining C-level closure | ArcCaliper, Comment, ContourExtrema, PhaseClosure | `ArcCaliper_baseline.json`, `CLevel_contract_baseline.json` | 97 | Synthetic / contract | wrong polarity, low texture, wraparound arc, empty/invalid contract cases |
| Matching / measurement P1 | CaliperTool, TemplateMatching, GradientShapeMatch | `CaliperTool_baseline.json`, `TemplateMatching_baseline.json`, `GradientShapeMatch_baseline.json` | 351 | Synthetic | triage reports for CaliperTool / TemplateMatching / GradientShapeMatch |
| TemplateMatching public bridge | TemplateMatching | `TemplateMatching_public_bridge_baseline.json` | 24 | HPatches-style synthetic homography bridge | homography, illumination, viewpoint, ROI-constrained bridge cases |
| Frequency | FFT1D, InverseFFT1D, FrequencyFilter | `FFT1D_baseline.json`, `InverseFFT1D_baseline.json`, `FrequencyFilter_baseline.json` | 351 | Synthetic oracle | FFT/IFFT/frequency-filter triage reports |
| Feature matching | AkazeFeatureMatch, OrbFeatureMatch | `FeatureMatch_contract_baseline.json` | 44 | Synthetic | blank scene/template, missing template, validation boundary cases |
| Pyramid shape matching | PyramidShapeMatch | `PyramidShapeMatch_contract_baseline.json` | 24 | Synthetic | blank scene/template, missing template, scaled-area rejection, validation boundaries |
| AI detection | AnomalyDetection, DeepLearning, SemanticSegmentation, EdgePairDefect, DualModalVoting | `AnomalyDetection_mvtec_baseline.json`, `DeepLearning_contract_baseline.json`, `DeepLearning_runtime_benchmark_baseline.json`, `SemanticSegmentation_contract_baseline.json`, `EdgePairDefect_contract_baseline.json`, `DualModalVoting_contract_baseline.json` | 251 | MVTec AD Lite for AnomalyDetection | model/label mismatch, missing input, degenerate line, voting missing-input and validation contracts |
| Calibration | CameraCalibration, FisheyeCalibration, HandEyeCalibration, HandEyeCalibrationValidator, StereoCalibration, PixelToWorldTransform, CoordinateTransform, Undistort, FisheyeUndistort | `CalibrationGeometry_round_trip_baseline.json`, `HandEyeCalibrationValidator_contract_baseline.json` | 216 | Synthetic geometry | AX=XB perturbation, invalid calibration JSON, missing poses, wrong bundle kind, round-trip tolerance checks |
| P3 original B-level closure | Comparator, LogicGate, StringFormat, ArrayIndexer, JsonExtractor, MathOperation, TypeConvert, ResultJudgment, TimerStatistics, VariableRead, VariableWrite, VariableIncrement, CycleCounter, Delay, ForEach, ImageAcquisition, MitsubishiMcCommunication, OmronFinsCommunication, SiemensS7Communication | `P3CoreContracts_baseline.json` | 386 | Contract / mock external IO | parameter validation, unsupported modes, missing input, mock communication failure contracts |

## Known Failure / Boundary Index

| Operator group | Locked failure or boundary contract |
| --- | --- |
| GradientShapeMatch | low-feature templates return `[InvalidTemplate]`; low contrast, strong background, blur, occlusion, ROI, and rotation stress cases are captured in baseline/triage |
| TemplateMatching | low texture returns `IsMatch=false` or explicit failure reason; fixed-scale and rotation boundaries are locked as non-match; ROI/mask constraints prevent out-of-region high responses |
| CaliperTool | wrong polarity and ExpectedCount failures are locked as `[NoFeature]`; low texture, outside sampling, zero span, and wraparound arcs are covered |
| FFT / IFFT / FrequencyFilter | shape, length, cutoff clamp/swap, conjugate symmetry, and round-trip reconstruction boundaries are covered |
| FeatureMatch / PyramidShapeMatch | blank scene/template, missing template, invalid thresholds, scale/rotation boundaries, and origin semantics are covered |
| DeepLearning | output tensor selection fail-closed, label mismatch, missing label contract, NMS IoU boundaries, target class parsing, runtime provider fallback, and batch pressure are covered |
| SemanticSegmentation | missing image/model and bad mean/std failure contracts are covered |
| EdgePairDefect | blank edge image, missing image, degenerate line, tolerance boundary, and sampling variants are covered |
| DualModalVoting | missing modal input, no valid inputs, invalid strategies, invalid weights, and confidence clamping are covered |
| Calibration family | synthetic intrinsics/homography/stereo/fisheye/AX=XB round-trip tolerances are covered; validator bad JSON/wrong kind/missing pose/count mismatch contracts are covered |
| P3 core | invalid operator parameters, unsupported modes, missing variables, empty arrays, type conversion boundaries, mock device failures, and delay/timer bounds are covered |

## Deferred To 6-Month Backlog

The matrix still contains 100 operators without golden evidence. They are not part of the 90-day core closure; they remain in the 6-month goals:

- `155` operators all get basic contract tests.
- `50` core operators get golden tests.
- `20` core vision operators get public or semi-synthetic dataset validation.
- Field failure sample ingestion becomes stable.

Current high-value unclosed P2 rows without golden evidence are tracked by the matrix as backlog, including `CalibrationLoader`, `DetectionSequenceJudge`, `LocalDeformableMatching`, `NPointCalibration`, `PlanarMatching`, `ShapeMatching`, `SurfaceDefectDetection`, and `TranslationRotationCalibration`.

## Closeout Decision

The 90-day target is considered closed because the first-stage scope now has:

- No C-level or B-level operators in the matrix.
- No card TODOs.
- Golden/baseline reports for the selected 90-day core chain.
- Known failure or boundary contracts recorded in runner baselines, triage reports, or this closeout index.
- Remaining no-golden operators explicitly moved into the 6-month baseline expansion track.
