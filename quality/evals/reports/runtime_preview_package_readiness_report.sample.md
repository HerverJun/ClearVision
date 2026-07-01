# RuntimePreview Package Readiness Final

- Generated UTC: `2026-06-07T05:24:30.303399+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | true | low |
| RP-RF-002 | remote_control_defect | true | low |
| RP-RF-003 | template_measurement_combo | true | low |
| RP-RF-004 | hole_distance | true | low |
| RP-RF-005 | terminal_color_order | true | low |
| RP-RF-006 | missing_camera | false | high |
| RP-RF-007 | missing_template | false | high |
| RP-RF-008 | missing_model | false | high |
| RP-RF-009 | missing_output_channel | false | high |
| RP-RF-010 | plc_station_deny | false | high |
| RP-RF-011 | dangerous_path | false | high |
| RP-RF-012 | allowlist_mismatch | false | high |
| RP-RF-013 | multi_camera_flow | false | high |
| RP-RF-014 | multi_model_flow | false | high |
| RP-RF-015 | parameter_missing | false | high |
| RP-RF-016 | package_manifest_blocked | false | high |
| RP-RF-017 | workflow_editable_package_blocked | false | high |
| RP-RF-018 | runtime_package_precheck_blocked | false | high |
| RP-RF-019 | template_plus_hole_distance | true | low |
| RP-RF-020 | direct_deploy_request_denied | false | high |
| RP-RF-021 | low_ipc_operator_count_exceeded | true | low |
| RP-RF-022 | multi_camera_slot_shortage | true | low |
| RP-RF-023 | unsupported_deep_learning | true | low |
| RP-RF-024 | output_channel_kind_missing | true | low |
| RP-RF-025 | plc_write_forbidden | false | high |
| RP-RF-026 | runtime_version_too_low | true | low |
| RP-RF-027 | model_type_incompatible | true | low |
| RP-RF-028 | template_dependency_missing | false | high |
| RP-RF-029 | traditional_vision_release_allowed | true | low |
| RP-RF-030 | deep_learning_requires_engineer_approval | true | low |
| RP-RF-031 | multi_station_requires_engineer_approval | true | low |
| RP-RF-032 | release_blocked_operator_contract | false | high |
| RP-RF-033 | blob_release_allowed | true | low |
| RP-RF-034 | threshold_release_allowed | true | low |
| RP-RF-035 | edge_release_allowed | true | low |
| RP-RF-036 | shape_matching_release_allowed | true | low |
| RP-RF-037 | template_only_profile_pass | true | low |
| RP-RF-038 | measurement_only_profile_pass | true | low |
| RP-RF-039 | semantic_segmentation_requires_approval | true | low |
| RP-RF-040 | surface_defect_requires_approval | true | low |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
