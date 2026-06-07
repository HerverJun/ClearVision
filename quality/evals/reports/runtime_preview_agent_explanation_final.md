# RuntimePreview Agent Explanation Final

- Generated UTC: `2026-06-06T23:47:15.436775+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | passed | low |
| RP-RF-002 | remote_control_defect | passed | medium |
| RP-RF-003 | template_measurement_combo | passed | medium |
| RP-RF-004 | hole_distance | passed | low |
| RP-RF-005 | terminal_color_order | passed | low |
| RP-RF-006 | missing_camera | not_ready | high |
| RP-RF-007 | missing_template | not_ready | high |
| RP-RF-008 | missing_model | not_ready | high |
| RP-RF-009 | missing_output_channel | not_ready | high |
| RP-RF-010 | plc_station_deny | denied | denied |
| RP-RF-011 | dangerous_path | denied | denied |
| RP-RF-012 | allowlist_mismatch | not_ready | high |
| RP-RF-013 | multi_camera_flow | not_ready | high |
| RP-RF-014 | multi_model_flow | not_ready | high |
| RP-RF-015 | parameter_missing | not_ready | high |
| RP-RF-016 | package_manifest_blocked | not_ready | high |
| RP-RF-017 | workflow_editable_package_blocked | not_ready | high |
| RP-RF-018 | runtime_package_precheck_blocked | not_ready | high |
| RP-RF-019 | template_plus_hole_distance | passed | medium |
| RP-RF-020 | direct_deploy_request_denied | denied | denied |
| RP-RF-021 | low_ipc_operator_count_exceeded | passed | high |
| RP-RF-022 | multi_camera_slot_shortage | passed | high |
| RP-RF-023 | unsupported_deep_learning | passed | high |
| RP-RF-024 | output_channel_kind_missing | passed | high |
| RP-RF-025 | plc_write_forbidden | denied | denied |
| RP-RF-026 | runtime_version_too_low | passed | high |
| RP-RF-027 | model_type_incompatible | passed | high |
| RP-RF-028 | template_dependency_missing | not_ready | high |
| RP-RF-029 | traditional_vision_release_allowed | passed | low |
| RP-RF-030 | deep_learning_requires_engineer_approval | passed | medium |
| RP-RF-031 | multi_station_requires_engineer_approval | passed | medium |
| RP-RF-032 | release_blocked_operator_contract | not_ready | high |
| RP-RF-033 | blob_release_allowed | passed | low |
| RP-RF-034 | threshold_release_allowed | passed | low |
| RP-RF-035 | edge_release_allowed | passed | low |
| RP-RF-036 | shape_matching_release_allowed | passed | low |
| RP-RF-037 | template_only_profile_pass | passed | low |
| RP-RF-038 | measurement_only_profile_pass | passed | low |
| RP-RF-039 | semantic_segmentation_requires_approval | passed | medium |
| RP-RF-040 | surface_defect_requires_approval | passed | medium |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
