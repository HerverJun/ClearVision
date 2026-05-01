# Candidate Profile Governance v1

GeneratedAtUtc: `2026-05-01T06:09:06+00:00`
ProductDefaultChange: `False`
Policy: Default-off/advisory/paused profiles may pass governance; blocked external model work is tracked separately from mainline algorithm validation.
ReleaseFieldReplayGate: `standards-signed-replay-required`

| Operator | Profile | Status | Default off | Dataset | Blockers |
|---|---|---|---|---|---|
| AnomalyDetection | mvtec_lite_v2 | default_off_candidate_ready_with_fp_tradeoff | True | `quality/public_datasets/mvtec_ad_lite` | MVTec lite is advisory only; not MVTec AD full sign-off.; Signed FP standard accepts at most +3 FP delta, <=10% normal FPR, and image precision >=0.95; release/field replay packet is still required.; MaxSide=192 is runner/evidence preprocessing; product profile enforces PatchSize/PatchStride/Coreset/Threshold but not MaxSide. |
| SurfaceDefectDetection | taxonomy_v2 | hold-current-no-targeted-improvement | True | `quality/public_datasets/kolektorsdd2` | No targeted taxonomy improvement has beaten baseline under the no-global-threshold-lowering policy. |
| EdgeDetection | recall_not_lower_v2 | paused | True | `quality/public_datasets/bsds500` | No recall-safe profile is currently selected. |
| DeepLearning | coco_yolo_external_onnx | blocked_external_model | True | `quality/public_datasets/coco2017` | Real ONNX model artifact is not present; mainline validation treats this as blocked, not failed. |
| CameraCalibration | opencv_samples_smoke | stable_smoke | False | `quality/public_datasets/opencv_calibration_samples` | - |
| AkazeFeatureMatch | default_v3 | default_off_ready_no_accuracy_delta | True | `quality/public_datasets/hpatches` | Release/field replay is still required before any default-on promotion.; Accuracy delta is neutral; keep this as an opt-in evidence/observability profile. |
| OrbFeatureMatch | replay_safe_dense_strict | default_off_ready_metric_gain_runtime_tradeoff | True | `quality/public_datasets/hpatches` | Release/field replay is still required before any default-on promotion.; Signed runtime budget must be met by release/field replay before default-on. |
