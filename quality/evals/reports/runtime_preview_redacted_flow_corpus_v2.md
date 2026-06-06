# RuntimePreview Redacted Flow Corpus v2

- Generated UTC: `2026-06-06T16:17:44.241304+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | release_allowed | low |
| RP-RF-002 | remote_control_defect | requires_engineer_approval | medium |
| RP-RF-003 | template_measurement_combo | requires_engineer_approval | medium |
| RP-RF-004 | hole_distance | release_allowed | low |
| RP-RF-005 | terminal_color_order | release_allowed | low |
| RP-RF-006 | missing_camera | release_blocked | missing_camera_binding |
| RP-RF-007 | missing_template | release_blocked | missing_template |
| RP-RF-008 | missing_model | release_blocked | missing_model |
| RP-RF-009 | missing_output_channel | release_blocked | missing_output_channel |
| RP-RF-010 | plc_station_deny | release_blocked | plc_station_denied |
| RP-RF-011 | dangerous_path | release_blocked | dangerous_resource |
| RP-RF-012 | allowlist_mismatch | release_blocked | allowlist_mismatch |
| RP-RF-013 | multi_camera_flow | release_blocked | multi_camera_review |
| RP-RF-014 | multi_model_flow | release_blocked | multi_model_review |
| RP-RF-015 | parameter_missing | release_blocked | missing_parameter |
| RP-RF-016 | package_manifest_blocked | release_blocked | manifest_dependency_blocked |
| RP-RF-017 | workflow_editable_package_blocked | release_blocked | draft_allowed_package_blocked |
| RP-RF-018 | runtime_package_precheck_blocked | release_blocked | precheck_not_ready |
| RP-RF-019 | template_plus_hole_distance | requires_engineer_approval | medium |
| RP-RF-020 | direct_deploy_request_denied | release_blocked | deployment_intent_denied |
| RP-RF-021 | low_ipc_operator_count_exceeded | release_blocked | high |
| RP-RF-022 | multi_camera_slot_shortage | release_blocked | high |
| RP-RF-023 | unsupported_deep_learning | release_blocked | high |
| RP-RF-024 | output_channel_kind_missing | release_blocked | high |
| RP-RF-025 | plc_write_forbidden | release_blocked | plc_write_forbidden |
| RP-RF-026 | runtime_version_too_low | release_blocked | high |
| RP-RF-027 | model_type_incompatible | release_blocked | high |
| RP-RF-028 | template_dependency_missing | release_blocked | template_dependency_missing |
| RP-RF-029 | traditional_vision_release_allowed | release_allowed | low |
| RP-RF-030 | deep_learning_requires_engineer_approval | requires_engineer_approval | medium |
| RP-RF-031 | multi_station_requires_engineer_approval | requires_engineer_approval | medium |
| RP-RF-032 | release_blocked_operator_contract | release_blocked | operator_contract_missing_parameter |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
