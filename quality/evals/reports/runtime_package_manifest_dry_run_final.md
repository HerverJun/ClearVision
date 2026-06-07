# RuntimePackage Manifest Dry-Run Final

- Generated UTC: `2026-06-06T23:47:15.436775+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | true | low |
| RP-RF-002 | remote_control_defect | true | medium |
| RP-RF-003 | template_measurement_combo | true | medium |
| RP-RF-004 | hole_distance | true | low |
| RP-RF-005 | terminal_color_order | true | low |
| RP-RF-006 | missing_camera | false | missing_camera_binding |
| RP-RF-007 | missing_template | false | missing_template |
| RP-RF-008 | missing_model | false | missing_model |
| RP-RF-009 | missing_output_channel | false | missing_output_channel |
| RP-RF-010 | plc_station_deny | false | plc_station_denied |
| RP-RF-011 | dangerous_path | false | dangerous_resource |
| RP-RF-012 | allowlist_mismatch | false | allowlist_mismatch |
| RP-RF-013 | multi_camera_flow | false | multi_camera_review |
| RP-RF-014 | multi_model_flow | false | multi_model_review |
| RP-RF-015 | parameter_missing | false | missing_parameter |
| RP-RF-016 | package_manifest_blocked | false | manifest_dependency_blocked |
| RP-RF-017 | workflow_editable_package_blocked | false | draft_allowed_package_blocked |
| RP-RF-018 | runtime_package_precheck_blocked | false | precheck_not_ready |
| RP-RF-019 | template_plus_hole_distance | true | medium |
| RP-RF-020 | direct_deploy_request_denied | false | deployment_intent_denied |
| RP-RF-021 | low_ipc_operator_count_exceeded | true | high |
| RP-RF-022 | multi_camera_slot_shortage | true | high |
| RP-RF-023 | unsupported_deep_learning | true | high |
| RP-RF-024 | output_channel_kind_missing | true | high |
| RP-RF-025 | plc_write_forbidden | false | plc_write_forbidden |
| RP-RF-026 | runtime_version_too_low | true | high |
| RP-RF-027 | model_type_incompatible | true | high |
| RP-RF-028 | template_dependency_missing | false | template_dependency_missing |
| RP-RF-029 | traditional_vision_release_allowed | true | low |
| RP-RF-030 | deep_learning_requires_engineer_approval | true | medium |
| RP-RF-031 | multi_station_requires_engineer_approval | true | medium |
| RP-RF-032 | release_blocked_operator_contract | false | operator_contract_missing_parameter |
| RP-RF-033 | blob_release_allowed | true | low |
| RP-RF-034 | threshold_release_allowed | true | low |
| RP-RF-035 | edge_release_allowed | true | low |
| RP-RF-036 | shape_matching_release_allowed | true | low |
| RP-RF-037 | template_only_profile_pass | true | low |
| RP-RF-038 | measurement_only_profile_pass | true | low |
| RP-RF-039 | semantic_segmentation_requires_approval | true | medium |
| RP-RF-040 | surface_defect_requires_approval | true | medium |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
