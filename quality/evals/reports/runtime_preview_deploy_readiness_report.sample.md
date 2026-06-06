# RuntimePreview Deploy Readiness Report

- Generated UTC: `2026-06-06T16:17:44.241304+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-SC-001 | wire_sequence | ready | - |
| RP-SC-002 | remote_control_defect | ready | - |
| RP-SC-003 | template_measurement_combo | ready | - |
| RP-SC-004 | hole_distance | ready | - |
| RP-SC-005 | terminal_color_order | ready | - |
| RP-SC-006 | missing_camera | not_ready | - |
| RP-SC-007 | missing_template | not_ready | - |
| RP-SC-008 | missing_model | not_ready | - |
| RP-SC-009 | missing_output_channel | not_ready | - |
| RP-SC-010 | plc_station_deny | denied | - |
| RP-SC-011 | dangerous_path | denied | - |
| RP-SC-012 | allowlist_mismatch | not_ready | - |
| RP-SC-013 | multi_camera_flow | not_ready | - |
| RP-SC-014 | multi_model_flow | not_ready | - |
| RP-SC-015 | parameter_missing | not_ready | - |
| RP-SC-016 | package_manifest_blocked | not_ready | - |
| RP-SC-017 | workflow_editable_package_blocked | not_ready | - |
| RP-SC-018 | runtime_package_precheck_blocked | not_ready | - |
| RP-SC-019 | template_plus_hole_distance | ready | - |
| RP-SC-020 | direct_deploy_request_denied | denied | - |
| RP-SC-021 | low_ipc_operator_count_exceeded | ready | - |
| RP-SC-022 | multi_camera_slot_shortage | ready | - |
| RP-SC-023 | unsupported_deep_learning | ready | - |
| RP-SC-024 | output_channel_kind_missing | ready | - |
| RP-SC-025 | plc_write_forbidden | denied | - |
| RP-SC-026 | runtime_version_too_low | ready | - |
| RP-SC-027 | model_type_incompatible | ready | - |
| RP-SC-028 | template_dependency_missing | not_ready | - |
| RP-SC-029 | traditional_vision_release_allowed | ready | - |
| RP-SC-030 | deep_learning_requires_engineer_approval | ready | - |
| RP-SC-031 | multi_station_requires_engineer_approval | ready | - |
| RP-SC-032 | release_blocked_operator_contract | not_ready | - |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
