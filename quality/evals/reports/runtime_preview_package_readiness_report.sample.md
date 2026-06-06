# RuntimePreview Package Readiness Report

- Generated UTC: `2026-06-06T16:17:44.241304+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | - | - |
| RP-RF-002 | remote_control_defect | - | - |
| RP-RF-003 | template_measurement_combo | - | - |
| RP-RF-004 | hole_distance | - | - |
| RP-RF-005 | terminal_color_order | - | - |
| RP-RF-006 | missing_camera | - | - |
| RP-RF-007 | missing_template | - | - |
| RP-RF-008 | missing_model | - | - |
| RP-RF-009 | missing_output_channel | - | - |
| RP-RF-010 | plc_station_deny | - | - |
| RP-RF-011 | dangerous_path | - | - |
| RP-RF-012 | allowlist_mismatch | - | - |
| RP-RF-013 | multi_camera_flow | - | - |
| RP-RF-014 | multi_model_flow | - | - |
| RP-RF-015 | parameter_missing | - | - |
| RP-RF-016 | package_manifest_blocked | - | - |
| RP-RF-017 | workflow_editable_package_blocked | - | - |
| RP-RF-018 | runtime_package_precheck_blocked | - | - |
| RP-RF-019 | template_plus_hole_distance | - | - |
| RP-RF-020 | direct_deploy_request_denied | - | - |
| RP-RF-021 | low_ipc_operator_count_exceeded | - | - |
| RP-RF-022 | multi_camera_slot_shortage | - | - |
| RP-RF-023 | unsupported_deep_learning | - | - |
| RP-RF-024 | output_channel_kind_missing | - | - |
| RP-RF-025 | plc_write_forbidden | - | - |
| RP-RF-026 | runtime_version_too_low | - | - |
| RP-RF-027 | model_type_incompatible | - | - |
| RP-RF-028 | template_dependency_missing | - | - |
| RP-RF-029 | traditional_vision_release_allowed | - | - |
| RP-RF-030 | deep_learning_requires_engineer_approval | - | - |
| RP-RF-031 | multi_station_requires_engineer_approval | - | - |
| RP-RF-032 | release_blocked_operator_contract | - | - |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
