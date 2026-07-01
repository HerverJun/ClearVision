# RuntimePreview Scenario Corpus

- Generated UTC: `2026-06-07T05:24:30.303399+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-SC-001 | wire_sequence | passed | low |
| RP-SC-002 | remote_control_defect | passed | medium |
| RP-SC-003 | template_measurement_combo | passed | medium |
| RP-SC-004 | hole_distance | passed | low |
| RP-SC-005 | terminal_color_order | passed | low |
| RP-SC-006 | missing_camera | not_ready | missing_camera_binding |
| RP-SC-007 | missing_template | not_ready | missing_template |
| RP-SC-008 | missing_model | not_ready | missing_model |
| RP-SC-009 | missing_output_channel | not_ready | missing_output_channel |
| RP-SC-010 | plc_station_deny | denied | plc_station_denied |
| RP-SC-011 | dangerous_path | denied | dangerous_resource |
| RP-SC-012 | allowlist_mismatch | not_ready | allowlist_mismatch |
| RP-SC-013 | multi_camera_flow | not_ready | multi_camera_review |
| RP-SC-014 | multi_model_flow | not_ready | multi_model_review |
| RP-SC-015 | parameter_missing | not_ready | missing_parameter |
| RP-SC-016 | package_manifest_blocked | not_ready | manifest_dependency_blocked |
| RP-SC-017 | workflow_editable_package_blocked | not_ready | draft_allowed_package_blocked |
| RP-SC-018 | runtime_package_precheck_blocked | not_ready | precheck_not_ready |
| RP-SC-019 | template_plus_hole_distance | passed | medium |
| RP-SC-020 | direct_deploy_request_denied | denied | deployment_intent_denied |
| RP-SC-021 | low_ipc_operator_count_exceeded | passed | high |
| RP-SC-022 | multi_camera_slot_shortage | passed | high |
| RP-SC-023 | unsupported_deep_learning | passed | high |
| RP-SC-024 | output_channel_kind_missing | passed | high |
| RP-SC-025 | plc_write_forbidden | denied | plc_write_forbidden |
| RP-SC-026 | runtime_version_too_low | passed | high |
| RP-SC-027 | model_type_incompatible | passed | high |
| RP-SC-028 | template_dependency_missing | not_ready | template_dependency_missing |
| RP-SC-029 | traditional_vision_release_allowed | passed | low |
| RP-SC-030 | deep_learning_requires_engineer_approval | passed | medium |
| RP-SC-031 | multi_station_requires_engineer_approval | passed | medium |
| RP-SC-032 | release_blocked_operator_contract | not_ready | operator_contract_missing_parameter |
| RP-SC-033 | blob_release_allowed | passed | low |
| RP-SC-034 | threshold_release_allowed | passed | low |
| RP-SC-035 | edge_release_allowed | passed | low |
| RP-SC-036 | shape_matching_release_allowed | passed | low |
| RP-SC-037 | template_only_profile_pass | passed | low |
| RP-SC-038 | measurement_only_profile_pass | passed | low |
| RP-SC-039 | semantic_segmentation_requires_approval | passed | medium |
| RP-SC-040 | surface_defect_requires_approval | passed | medium |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
