# RuntimePreview Deploy Readiness Report

- Generated UTC: `2026-06-06T23:47:15.436775+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-SC-001 | wire_sequence | ready | metadata-only |
| RP-SC-002 | remote_control_defect | ready | metadata-only |
| RP-SC-003 | template_measurement_combo | ready | metadata-only |
| RP-SC-004 | hole_distance | ready | metadata-only |
| RP-SC-005 | terminal_color_order | ready | metadata-only |
| RP-SC-006 | missing_camera | not_ready | metadata-only |
| RP-SC-007 | missing_template | not_ready | metadata-only |
| RP-SC-008 | missing_model | not_ready | metadata-only |
| RP-SC-009 | missing_output_channel | not_ready | metadata-only |
| RP-SC-010 | plc_station_deny | denied | metadata-only |
| RP-SC-011 | dangerous_path | denied | metadata-only |
| RP-SC-012 | allowlist_mismatch | not_ready | metadata-only |
| RP-SC-013 | multi_camera_flow | not_ready | metadata-only |
| RP-SC-014 | multi_model_flow | not_ready | metadata-only |
| RP-SC-015 | parameter_missing | not_ready | metadata-only |
| RP-SC-016 | package_manifest_blocked | not_ready | metadata-only |
| RP-SC-017 | workflow_editable_package_blocked | not_ready | metadata-only |
| RP-SC-018 | runtime_package_precheck_blocked | not_ready | metadata-only |
| RP-SC-019 | template_plus_hole_distance | ready | metadata-only |
| RP-SC-020 | direct_deploy_request_denied | denied | metadata-only |
| RP-SC-021 | low_ipc_operator_count_exceeded | ready | metadata-only |
| RP-SC-022 | multi_camera_slot_shortage | ready | metadata-only |
| RP-SC-023 | unsupported_deep_learning | ready | metadata-only |
| RP-SC-024 | output_channel_kind_missing | ready | metadata-only |
| RP-SC-025 | plc_write_forbidden | denied | metadata-only |
| RP-SC-026 | runtime_version_too_low | ready | metadata-only |
| RP-SC-027 | model_type_incompatible | ready | metadata-only |
| RP-SC-028 | template_dependency_missing | not_ready | metadata-only |
| RP-SC-029 | traditional_vision_release_allowed | ready | metadata-only |
| RP-SC-030 | deep_learning_requires_engineer_approval | ready | metadata-only |
| RP-SC-031 | multi_station_requires_engineer_approval | ready | metadata-only |
| RP-SC-032 | release_blocked_operator_contract | not_ready | metadata-only |
| RP-SC-033 | blob_release_allowed | ready | metadata-only |
| RP-SC-034 | threshold_release_allowed | ready | metadata-only |
| RP-SC-035 | edge_release_allowed | ready | metadata-only |
| RP-SC-036 | shape_matching_release_allowed | ready | metadata-only |
| RP-SC-037 | template_only_profile_pass | ready | metadata-only |
| RP-SC-038 | measurement_only_profile_pass | ready | metadata-only |
| RP-SC-039 | semantic_segmentation_requires_approval | ready | metadata-only |
| RP-SC-040 | surface_defect_requires_approval | ready | metadata-only |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
