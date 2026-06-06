# RuntimePackage Manifest Dry-Run Report

- Generated UTC: `2026-06-06T16:17:44.241304+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | - | low |
| RP-RF-002 | remote_control_defect | - | medium |
| RP-RF-003 | template_measurement_combo | - | medium |
| RP-RF-004 | hole_distance | - | low |
| RP-RF-005 | terminal_color_order | - | low |
| RP-RF-006 | missing_camera | - | missing_camera_binding |
| RP-RF-007 | missing_template | - | missing_template |
| RP-RF-008 | missing_model | - | missing_model |
| RP-RF-009 | missing_output_channel | - | missing_output_channel |
| RP-RF-010 | plc_station_deny | - | plc_station_denied |
| RP-RF-011 | dangerous_path | - | dangerous_resource |
| RP-RF-012 | allowlist_mismatch | - | allowlist_mismatch |
| RP-RF-013 | multi_camera_flow | - | multi_camera_review |
| RP-RF-014 | multi_model_flow | - | multi_model_review |
| RP-RF-015 | parameter_missing | - | missing_parameter |
| RP-RF-016 | package_manifest_blocked | - | manifest_dependency_blocked |
| RP-RF-017 | workflow_editable_package_blocked | - | draft_allowed_package_blocked |
| RP-RF-018 | runtime_package_precheck_blocked | - | precheck_not_ready |
| RP-RF-019 | template_plus_hole_distance | - | medium |
| RP-RF-020 | direct_deploy_request_denied | - | deployment_intent_denied |
| RP-RF-021 | low_ipc_operator_count_exceeded | - | high |
| RP-RF-022 | multi_camera_slot_shortage | - | high |
| RP-RF-023 | unsupported_deep_learning | - | high |
| RP-RF-024 | output_channel_kind_missing | - | high |
| RP-RF-025 | plc_write_forbidden | - | plc_write_forbidden |
| RP-RF-026 | runtime_version_too_low | - | high |
| RP-RF-027 | model_type_incompatible | - | high |
| RP-RF-028 | template_dependency_missing | - | template_dependency_missing |
| RP-RF-029 | traditional_vision_release_allowed | - | low |
| RP-RF-030 | deep_learning_requires_engineer_approval | - | medium |
| RP-RF-031 | multi_station_requires_engineer_approval | - | medium |
| RP-RF-032 | release_blocked_operator_contract | - | operator_contract_missing_parameter |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
