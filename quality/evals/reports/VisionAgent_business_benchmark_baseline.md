# Vision Agent Business Benchmark

- Benchmark: `vision_agent_business_benchmark`
- Generated UTC: `2026-06-05T00:00:00Z`
- Mode: `offline_metadata_only`
- Cases: 36
- Accepted: True
- JSON: `quality/evals/reports/VisionAgent_business_benchmark_baseline.json`

## Metrics

| Metric | Actual | Minimum | Passed |
| --- | ---: | ---: | --- |
| generationSuccessRate | 100.00% | 95.00% | True |
| structuralValidationPassRate | 100.00% | 95.00% | True |
| dryRunPassRate | 77.78% | 75.00% | True |
| previewReadyRate | 71.43% | 70.00% | True |
| parameterCompletionRate | 75.00% | 75.00% | True |
| userApplicableRate | 100.00% | 90.00% | True |

## Task Set

| Case | Category | Type | Operators | Tools | Pending | Preview | Precheck |
| --- | --- | --- | --- | --- | --- | --- | --- |
| VA-BM-001 | wire_sequence | generate | ImageAcquisition, RoiManager, DeepLearning, ResultJudgment, ResultOutput | match_flow_template, get_flow_template_skeleton, list_camera_bindings, validate_flow, dryrun_flow | - | - | ready_with_warnings |
| VA-BM-002 | wire_sequence | modify_existing_flow | ImageAcquisition, RoiManager, DeepLearning, ResultJudgment, ResultOutput | inspect_current_flow, propose_flow_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-003 | wire_sequence | missing_resource | ImageAcquisition, RoiManager, DeepLearning, ResultJudgment, ResultOutput | list_camera_bindings, validate_flow, dryrun_flow, runtime_package_precheck | cameraBinding.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-004 | wire_sequence | runtime_preview | ImageAcquisition, RoiManager, DeepLearning, ResultJudgment, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | - | ready | not_requested |
| VA-BM-005 | wire_sequence | parameter_completion | ImageAcquisition, RoiManager, DeepLearning, ResultJudgment, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-006 | template_matching | generate | ImageAcquisition, TemplateMatching, PositionCorrection, ResultJudgment, ResultOutput | match_flow_template, get_flow_template_skeleton, get_operator_schema, validate_flow, dryrun_flow | - | - | ready_with_warnings |
| VA-BM-007 | template_matching | missing_resource | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | templatePath.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-008 | template_matching | parameter_completion | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-009 | template_matching | modify_existing_flow | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, propose_flow_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-010 | template_matching | runtime_preview | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | - | ready | not_requested |
| VA-BM-011 | hole_distance | generate | ImageAcquisition, CircleMeasurement, CircleMeasurement, MeasureDistance, ResultJudgment, ResultOutput | retrieve_operator_knowledge, get_operator_schema, validate_flow, dryrun_flow | - | - | ready_with_warnings |
| VA-BM-012 | hole_distance | missing_resource | ImageAcquisition, CircleMeasurement, CircleMeasurement, MeasureDistance, ResultJudgment, ResultOutput | retrieve_operator_knowledge, validate_flow, dryrun_flow, runtime_package_precheck | calibration.review, runtimePackagePrecheck.review | - | ready_with_warnings |
| VA-BM-013 | hole_distance | parameter_completion | ImageAcquisition, CircleMeasurement, CircleMeasurement, MeasureDistance, ResultJudgment, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-014 | hole_distance | modify_existing_flow | ImageAcquisition, CircleMeasurement, CircleMeasurement, MeasureDistance, ResultJudgment, ResultOutput | inspect_current_flow, propose_flow_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-015 | hole_distance | precheck | ImageAcquisition, CircleMeasurement, CircleMeasurement, MeasureDistance, ResultJudgment, ResultOutput | validate_flow, dryrun_flow, runtime_package_precheck | runtimePackagePrecheck.review | - | ready_with_warnings |
| VA-BM-016 | missing_resources | missing_resource | ImageAcquisition, DeepLearning, ResultJudgment, ResultOutput | retrieve_operator_knowledge, validate_flow, dryrun_flow, runtime_package_precheck | modelPath.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-017 | missing_resources | missing_resource | ImageAcquisition, TemplateMatching, ResultOutput | list_camera_bindings, validate_flow, dryrun_flow, runtime_package_precheck | cameraBinding.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-018 | missing_resources | missing_resource | ImageAcquisition, TemplateMatching, ResultOutput | validate_flow, dryrun_flow, runtime_package_precheck | outputChannel.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-019 | missing_resources | missing_resource | ImageAcquisition, ResultJudgment, PlcResultOutput | validate_flow, dryrun_flow, runtime_package_precheck | plcParameters.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-020 | missing_resources | missing_resource | ImageAcquisition, TemplateMatching, ResultOutput | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | templatePath.required, runtimePackagePrecheck.review | - | blocked_missing_resource |
| VA-BM-021 | modify_existing_flow | modify_existing_flow | ImageAcquisition, TemplateMatching, DeepLearning, ResultJudgment, ResultOutput | inspect_current_flow, get_operator_schema, propose_flow_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-022 | modify_existing_flow | modify_existing_flow | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, propose_flow_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-023 | modify_existing_flow | runtime_preview | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | - | ready | not_requested |
| VA-BM-024 | modify_existing_flow | modify_existing_flow | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, propose_flow_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-025 | parameter_completion | parameter_completion | ImageAcquisition, TemplateMatching, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-026 | parameter_completion | parameter_completion | ImageAcquisition, DeepLearning, ResultJudgment, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-027 | parameter_completion | parameter_completion | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-028 | parameter_completion | parameter_completion | ImageAcquisition, TemplateMatching, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-029 | parameter_completion | parameter_completion | ImageAcquisition, TemplateMatching, ResultOutput | inspect_current_flow, propose_parameter_patch, validate_flow, dryrun_flow | - | - | ready |
| VA-BM-030 | runtime_preview | runtime_preview | ImageAcquisition, TemplateMatching, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | - | ready | not_requested |
| VA-BM-031 | runtime_preview | runtime_preview | ImageAcquisition, ImageAcquisition, ImageCompose, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | entryOperatorTempId.required | blocked | not_requested |
| VA-BM-032 | runtime_preview | runtime_preview | ImageAcquisition, TemplateMatching, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | developerHiddenUi.disabled | blocked | not_requested |
| VA-BM-033 | runtime_preview | runtime_preview | ImageAcquisition, TemplateMatching, ResultOutput | inspect_current_flow, runtime_preview_metadata, validate_flow | - | ready | not_requested |
| VA-BM-034 | precheck | precheck | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | validate_flow, dryrun_flow, runtime_package_precheck | runtimePackagePrecheck.review | - | ready |
| VA-BM-035 | precheck | precheck | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | validate_flow, dryrun_flow, runtime_package_precheck | stationStatus.review, runtimePackagePrecheck.review | - | blocked_station_offline |
| VA-BM-036 | precheck | precheck | ImageAcquisition, TemplateMatching, ResultJudgment, ResultOutput | validate_flow, runtime_package_precheck | dryrun.required, runtimePackagePrecheck.review | - | blocked_dryrun_missing |

## Safety

- RuntimePreview stays offline/metadata-only.
- No real camera SDK, Station, image file, model file, PLC write, package creation, or hot load is used.
- Safety violations: none
