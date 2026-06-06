# Vision Agent Executable Business Benchmark

- Benchmark: `vision_agent_executable_business_benchmark`
- Generated UTC: `2026-06-06T09:52:07.2834541+00:00`
- Commit SHA: `local`
- Branch: `local`
- Workflow run: `local` attempt `local`
- Mode: `offline_metadata_only`
- Cases: 36
- Accepted: True
- JSON: `quality/evals/reports/VisionAgent_business_benchmark_baseline.json`

## Executable Design

- Each case executes registered Vision Agent tools through `VisionAgentToolRegistry`.
- `expectedToolCalls` contains only registered tool names.
- Business-only expectations such as parameter completion, review, or UI intent are stored in `expectedBusinessActions`.
- RuntimePreview remains offline metadata-only through the existing stub tools and offline adapter.

## Metrics

| Metric | Actual | Minimum | Passed |
| --- | ---: | ---: | --- |
| generationSuccessRate | 100.00% | 95.00% | True |
| structuralValidationPassRate | 97.22% | 90.00% | True |
| dryRunPassRate | 94.44% | 85.00% | True |
| previewReadyRate | 83.33% | 70.00% | True |
| parameterCompletionRate | 80.56% | 70.00% | True |
| userApplicableRate | 97.22% | 90.00% | True |

## Task Set

| Case | Category | Type | Business Actions | Expected Tools | Actual Tools | Passed |
| --- | --- | --- | --- | --- | --- | --- |
| VA-BM-001 | wire_sequence | generate | select_template, choose_mock_camera_binding | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-002 | wire_sequence | modify_existing_flow | inspect_existing_flow, update_judgment_rule | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-003 | wire_sequence | missing_resource | request_camera_binding, keep_workflow_draft | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-004 | wire_sequence | runtime_preview | render_runtime_preview_metadata | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-005 | wire_sequence | parameter_completion | complete_output_channel | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-006 | template_matching | generate | select_template, review_min_score | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-007 | template_matching | missing_resource | request_template_source, keep_workflow_draft | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-008 | template_matching | parameter_completion | complete_roi_parameters | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-009 | template_matching | modify_existing_flow | inspect_existing_flow, update_score_threshold | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-010 | template_matching | runtime_preview | render_runtime_preview_metadata | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-011 | hole_distance | generate | retrieve_measurement_guidance, review_calibration | retrieve_operator_knowledge, get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | retrieve_operator_knowledge, get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-012 | hole_distance | missing_resource | calibration.review, keep_metadata_only | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-013 | hole_distance | parameter_completion | complete_hole_rois | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-014 | hole_distance | modify_existing_flow | inspect_existing_flow, update_tolerance | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-015 | hole_distance | precheck | runtimePackagePrecheck.review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-016 | missing_resources | missing_resource | request_model_source, keep_workflow_draft | retrieve_operator_knowledge, validate_flow, dryrun_flow, runtime_package_precheck | retrieve_operator_knowledge, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-017 | missing_resources | missing_resource | request_camera_binding, keep_workflow_draft | list_operator_catalog, validate_flow, dryrun_flow, runtime_package_precheck | list_operator_catalog, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-018 | missing_resources | missing_resource | request_output_channel, keep_workflow_draft | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-019 | missing_resources | missing_resource | request_plc_metadata, metadata_only_plc_review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-020 | missing_resources | missing_resource | request_template_source, keep_workflow_draft | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-021 | modify_existing_flow | modify_existing_flow | inspect_existing_flow, add_model_branch | inspect_current_flow, get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-022 | modify_existing_flow | modify_existing_flow | inspect_existing_flow, replace_template_source | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-023 | modify_existing_flow | runtime_preview | inspect_existing_flow, render_runtime_preview_metadata | inspect_current_flow, validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | inspect_current_flow, validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-024 | modify_existing_flow | modify_existing_flow | inspect_existing_flow, preserve_connections | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-025 | parameter_completion | parameter_completion | complete_camera_id | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-026 | parameter_completion | parameter_completion | complete_model_id | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-027 | parameter_completion | parameter_completion | complete_template_id | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-028 | parameter_completion | parameter_completion | complete_output_channel_id | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-029 | parameter_completion | parameter_completion | disable_conflicting_file_path | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-030 | runtime_preview | runtime_preview | render_runtime_preview_metadata | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-031 | runtime_preview | runtime_preview | entryOperatorTempId.required | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-032 | runtime_preview | runtime_preview | developerHiddenUi.disabled | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-033 | runtime_preview | runtime_preview | render_metadata_without_binary | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-034 | precheck | precheck | runtimePackagePrecheck.review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-035 | precheck | precheck | stationStatus.review, runtimePackagePrecheck.review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-036 | precheck | precheck | dryrun.required, runtimePackagePrecheck.review | validate_flow, runtime_package_precheck | validate_flow, runtime_package_precheck | True |

## Field Contract

- `expectedBusinessActions`: non-tool business expectations, such as parameter completion, review, or UI state intent.
- `expectedToolCalls`: registered tool names that must execute in order.
- `actualToolCalls`: tool execution trace with permission, success, and error metadata.
- `actualValidationResult`, `actualDryRunResult`, `actualPrecheckResult`, `actualRuntimePreviewResult`: actual tool outputs used for metrics.

## Safety

- No real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, or hot load is used.
- RuntimePreview mode: `offline_metadata_only`
- Safety violations: none
