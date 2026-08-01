# Vision Agent Executable Business Benchmark

- Benchmark: `vision_agent_executable_business_benchmark`
- Generated UTC: `2026-08-01T07:46:36.7643226+00:00`
- Commit SHA: `local`
- Branch: `local`
- Workflow run: `local` attempt `local`
- Mode: `offline_metadata_only`
- Cases: 120
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
| structuralValidationPassRate | 98.33% | 90.00% | True |
| dryRunPassRate | 97.50% | 85.00% | True |
| previewReadyRate | 95.65% | 70.00% | True |
| parameterCompletionRate | 76.67% | 70.00% | True |
| userApplicableRate | 98.33% | 90.00% | True |

## Resource Semantics

- Ready cases: 92
- Ready cases parameter-complete: 92
- Intentional missing-resource cases: 28
- Intentional missing-resource cases still blocked: 28
- Ready flows use canonical CameraId parameters and stable resource identities.
- Intentional missing-resource flows do not receive confirmations for the missing identity.

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
| VA-BM-037 | runtime_preview | runtime_preview_session | create_runtime_preview_session, catalog_snapshot, readiness_gate, metadata_simulation_report | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-038 | scenario_corpus | scenario_corpus | scenario_corpus.remote_control_detection, explain_model_metadata_only | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-039 | scenario_corpus | runtime_preview | scenario_corpus.terminal_color_order, render_runtime_preview_metadata | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-040 | scenario_corpus | multi_operator_flow | scenario_corpus.multi_operator_flow, operator_trace.review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-041 | package_readiness | package_readiness | package_readiness_bridge, packageCreated.false, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-042 | package_readiness | runtime_preview_session | create_runtime_preview_session, package_readiness_bridge, resource_trace.review | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-043 | governance | runtime_preview | session_replay, audit_timeline.review | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-044 | agent_explanation | missing_resource | agent_explain_missing_output, workflowDraftAllowed.true, packageBlocked.true | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-045 | agent_explanation | missing_resource | agent_explain_missing_model, request_model_source, packageBlocked.true | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-046 | redacted_flow_corpus | manifest_dry_run | redacted_flow_corpus.wire_sequence, manifest_dry_run.metadata_hash, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-047 | redacted_flow_corpus | manifest_dry_run | redacted_flow_corpus.remote_control_defect, model_dependency_trace.review, realModelFilesLoaded.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-048 | redacted_flow_corpus | manifest_dry_run | redacted_flow_corpus.missing_camera, camera_binding.required, packageReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-049 | redacted_flow_corpus | manifest_dry_run | redacted_flow_corpus.missing_template, template_dependency.required, packageReviewAllowed.false | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-050 | package_readiness_v2 | package_readiness | workflowDraftAllowed.true, packageBlocked.true, output_channel.required | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-051 | package_readiness_v2 | multi_operator_flow | dependency_trace.review, operator_contract.review, resource_contract.review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-052 | manifest_dry_run | runtime_preview_session | create_runtime_preview_session, manifestDryRunReportId.linked, manifestArtifactGenerated.false | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-053 | manifest_dry_run | missing_resource | missing_model.dependency_trace, packageReviewAllowed.false, workflowDraftAllowed.true | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-054 | governance_v3 | runtime_preview_session | governance_store.v3, lookup_by_manifestId, session_replay.metadata_only | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-055 | agent_explanation_v2 | missing_resource | agent_explanation_v2.status, manifestRisk.high, plcWriteAttempted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-056 | station_compatibility | release_review | station_compatibility.standard_profile, releaseReviewAllowed.true, metadataOnly.true | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-057 | station_compatibility | release_review | station_camera_slots_insufficient, releaseReviewAllowed.false, workflowDraftAllowed.true | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-058 | station_compatibility | release_review | station_operator_not_supported.DeepLearning, releaseReviewAllowed.false, realStationTouched.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-059 | station_compatibility | release_review | station_runtime_version_too_low, engineerAction.select_runtime_v14, metadataOnly.true | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-060 | operator_contract_validation | release_review | operator_contract.validation, requiredParameters.satisfied, forbiddenParameters.none | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-061 | operator_contract_validation | missing_resource | operator_contract_missing_parameter.TemplateId, releaseReviewAllowed.false, workflowDraftAllowed.true | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-062 | operator_contract_validation | missing_resource | operator_contract_missing_parameter.OutputChannelId, releaseReviewAllowed.false, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-063 | operator_contract_validation | release_review | engineer_approval.deep_learning_release_review, operatorContractsSatisfied.true, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-064 | pre_release_review | release_review | readiness.package.manifest.station.contract.decision, releaseReviewAllowed.true, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-065 | pre_release_review | missing_resource | workflowDraftAllowed.true, releaseReviewAllowed.false, camera_binding.required | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-066 | pre_release_review | release_review | engineer_approval.multi_station_review, requiresEngineerApproval.true, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-067 | pre_release_review | release_review | station_output_channel_kind_missing, engineerAction.remap_output, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-068 | agent_explanation_v3 | release_review | agent_explanation_v3.workflowDraftVsRelease, stationCompatibilityExplanation, nextEngineerAction | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-069 | governance_v4 | runtime_preview_session | governance_store.v4, lookup_by_reviewId, lookup_by_stationProfileId | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-070 | redacted_flow_corpus_v2 | manifest_dry_run | redacted_flow_corpus_v2.case_32, operator_contract_missing_parameter, redactionPass.true | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-071 | release_review_final | release_review | release_decision_matrix.releaseAllowed, metadataOnly.true, realResourcesTouched.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-072 | station_profile_final | release_review | station_profile.low_spec_ipc, operator_count.review, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-073 | operator_contract_final | release_review | operator_contract_coverage.pass, TemplateMatching.contract, ResultOutput.contract | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-074 | agent_explanation_final | release_review | agent_explanation_final.firstFixRecommendation, workflowDraftAllowed.true, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-075 | governance_store_final | runtime_preview_session | governance_export_final.lookupKeys, release_review_decision.stream, redactionPass.true | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-076 | review_desk_final | runtime_preview | reviewDesk.releaseAllowed.approvalRequired.blocked, adminDeveloperGate.required, domRedacted.true | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-077 | source_guard_final | missing_resource | source_guard.package_path_denied, plcWriteAttempted.false, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-078 | readability_gate_final | manifest_dry_run | report_readability_gate.pass, status.non_empty, action.non_empty | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-079 | remote_ci_evidence_final | manifest_dry_run | workflowRun.runId.non_local, artifact.digest.recorded, headSha.current | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-080 | redacted_corpus_final | release_review | redacted_flow_corpus_final.caseCount60, station_compatibility_final, pre_release_review_final | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-081 | release_review_final | release_review | release_decision_matrix.releaseAllowed, metadataOnly.true, realResourcesTouched.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-082 | station_profile_final | release_review | station_profile.low_spec_ipc, operator_count.review, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-083 | operator_contract_final | release_review | operator_contract_coverage.pass, TemplateMatching.contract, ResultOutput.contract | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-084 | agent_explanation_final | release_review | agent_explanation_final.firstFixRecommendation, workflowDraftAllowed.true, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-085 | governance_store_final | runtime_preview_session | governance_export_final.lookupKeys, release_review_decision.stream, redactionPass.true | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-086 | review_desk_final | runtime_preview | reviewDesk.releaseAllowed.approvalRequired.blocked, adminDeveloperGate.required, domRedacted.true | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-087 | source_guard_final | missing_resource | source_guard.package_path_denied, plcWriteAttempted.false, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-088 | readability_gate_final | manifest_dry_run | report_readability_gate.pass, status.non_empty, action.non_empty | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-089 | remote_ci_evidence_final | manifest_dry_run | workflowRun.runId.non_local, artifact.digest.recorded, headSha.current | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-090 | redacted_corpus_final | release_review | redacted_flow_corpus_final.caseCount60, station_compatibility_final, pre_release_review_final | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-091 | release_review_final | release_review | release_decision_matrix.releaseAllowed, metadataOnly.true, realResourcesTouched.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-092 | station_profile_final | release_review | station_profile.low_spec_ipc, operator_count.review, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-093 | operator_contract_final | release_review | operator_contract_coverage.pass, TemplateMatching.contract, ResultOutput.contract | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-094 | agent_explanation_final | release_review | agent_explanation_final.firstFixRecommendation, workflowDraftAllowed.true, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-095 | governance_store_final | runtime_preview_session | governance_export_final.lookupKeys, release_review_decision.stream, redactionPass.true | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-096 | review_desk_final | runtime_preview | reviewDesk.releaseAllowed.approvalRequired.blocked, adminDeveloperGate.required, domRedacted.true | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-097 | source_guard_final | missing_resource | source_guard.package_path_denied, plcWriteAttempted.false, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-098 | readability_gate_final | manifest_dry_run | report_readability_gate.pass, status.non_empty, action.non_empty | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-099 | remote_ci_evidence_final | manifest_dry_run | workflowRun.runId.non_local, artifact.digest.recorded, headSha.current | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-100 | redacted_corpus_final | release_review | redacted_flow_corpus_final.caseCount60, station_compatibility_final, pre_release_review_final | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-101 | release_review_final | release_review | release_decision_matrix.releaseAllowed, metadataOnly.true, realResourcesTouched.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-102 | station_profile_final | release_review | station_profile.low_spec_ipc, operator_count.review, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-103 | operator_contract_final | release_review | operator_contract_coverage.pass, TemplateMatching.contract, ResultOutput.contract | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-104 | agent_explanation_final | release_review | agent_explanation_final.firstFixRecommendation, workflowDraftAllowed.true, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-105 | governance_store_final | runtime_preview_session | governance_export_final.lookupKeys, release_review_decision.stream, redactionPass.true | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-106 | review_desk_final | runtime_preview | reviewDesk.releaseAllowed.approvalRequired.blocked, adminDeveloperGate.required, domRedacted.true | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-107 | source_guard_final | missing_resource | source_guard.package_path_denied, plcWriteAttempted.false, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-108 | readability_gate_final | manifest_dry_run | report_readability_gate.pass, status.non_empty, action.non_empty | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-109 | remote_ci_evidence_final | manifest_dry_run | workflowRun.runId.non_local, artifact.digest.recorded, headSha.current | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-110 | redacted_corpus_final | release_review | redacted_flow_corpus_final.caseCount60, station_compatibility_final, pre_release_review_final | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-111 | release_review_final | release_review | release_decision_matrix.releaseAllowed, metadataOnly.true, realResourcesTouched.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-112 | station_profile_final | release_review | station_profile.low_spec_ipc, operator_count.review, packageCreated.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-113 | operator_contract_final | release_review | operator_contract_coverage.pass, TemplateMatching.contract, ResultOutput.contract | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | get_operator_schema, validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-114 | agent_explanation_final | release_review | agent_explanation_final.firstFixRecommendation, workflowDraftAllowed.true, releaseReviewAllowed.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-115 | governance_store_final | runtime_preview_session | governance_export_final.lookupKeys, release_review_decision.stream, redactionPass.true | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | validate_flow, dryrun_flow, runtime_preview_simulate_metadata_session | True |
| VA-BM-116 | review_desk_final | runtime_preview | reviewDesk.releaseAllowed.approvalRequired.blocked, adminDeveloperGate.required, domRedacted.true | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True |
| VA-BM-117 | source_guard_final | missing_resource | source_guard.package_path_denied, plcWriteAttempted.false, deploymentExecuted.false | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-118 | readability_gate_final | manifest_dry_run | report_readability_gate.pass, status.non_empty, action.non_empty | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-119 | remote_ci_evidence_final | manifest_dry_run | workflowRun.runId.non_local, artifact.digest.recorded, headSha.current | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |
| VA-BM-120 | redacted_corpus_final | release_review | redacted_flow_corpus_final.caseCount60, station_compatibility_final, pre_release_review_final | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True |

## Field Contract

- `expectedBusinessActions`: non-tool business expectations, such as parameter completion, review, or UI state intent.
- `expectedToolCalls`: registered tool names that must execute in order.
- `actualToolCalls`: tool execution trace with permission, success, and error metadata.
- `actualValidationResult`, `actualDryRunResult`, `actualPrecheckResult`, `actualRuntimePreviewResult`: actual tool outputs used for metrics.

## Safety

- No real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, or hot load is used.
- RuntimePreview mode: `offline_metadata_only`
- Safety violations: none
