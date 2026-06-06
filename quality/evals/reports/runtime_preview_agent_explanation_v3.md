# RuntimePreview Agent Explanation v3

- Generated UTC: `2026-06-06T16:17:44.241304+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | True | Review metadata manifest and keep real pilot gate closed. |
| RP-RF-002 | remote_control_defect | True | Request DeepLearning release review approval. |
| RP-RF-003 | template_measurement_combo | True | Review combined template, measurement, and DeepLearning contracts. |
| RP-RF-004 | hole_distance | True | Confirm measurement unit and tolerance source. |
| RP-RF-005 | terminal_color_order | True | Confirm template ownership and output channel mapping. |
| RP-RF-006 | missing_camera | True | Bind an allowlisted metadata camera before package review. |
| RP-RF-007 | missing_template | True | Assign an allowlisted TemplateId; do not use file paths. |
| RP-RF-008 | missing_model | True | Bind ModelId from catalog; do not load a model file. |
| RP-RF-009 | missing_output_channel | True | Choose OutputChannelId before package review. |
| RP-RF-010 | plc_station_deny | True | Remove PLC/Station intent; this console cannot write or deploy. |
| RP-RF-011 | dangerous_path | True | Replace path-like metadata with a catalog TemplateId. |
| RP-RF-012 | allowlist_mismatch | True | Review allowlist diff and confirm the catalog handle. |
| RP-RF-013 | multi_camera_flow | True | Confirm both camera bindings are catalog allowlisted. |
| RP-RF-014 | multi_model_flow | True | Confirm all ModelIds and output aggregation before package review. |
| RP-RF-015 | parameter_missing | - | Complete required operator parameters and rerun readiness. |
| RP-RF-016 | package_manifest_blocked | True | Resolve manifest dependencies; no package may be created. |
| RP-RF-017 | workflow_editable_package_blocked | True | Keep editing the workflow; do not start release review yet. |
| RP-RF-018 | runtime_package_precheck_blocked | True | Rerun readiness after model metadata is resolved. |
| RP-RF-019 | template_plus_hole_distance | True | Request approval for medium-risk measurement release review. |
| RP-RF-020 | direct_deploy_request_denied | True | Use only metadata review; direct deployment remains forbidden. |
| RP-RF-021 | low_ipc_operator_count_exceeded | True | Split the workflow or target a higher-capacity IPC profile. |
| RP-RF-022 | multi_camera_slot_shortage | True | Choose a Station profile with enough camera binding slots. |
| RP-RF-023 | unsupported_deep_learning | True | Move the flow to a DeepLearning-capable Station profile. |
| RP-RF-024 | output_channel_kind_missing | True | Remap ResultOutput to a Station-supported output channel kind. |
| RP-RF-025 | plc_write_forbidden | True | Remove PLC write intent and keep output metadata-only. |
| RP-RF-026 | runtime_version_too_low | True | Select a Runtime 1.4.0 Station profile before release review. |
| RP-RF-027 | model_type_incompatible | True | Use a supported detection model or target a segmentation-capable Station profile. |
| RP-RF-028 | template_dependency_missing | True | Bind TemplateId metadata and rerun manifest dry-run. |
| RP-RF-029 | traditional_vision_release_allowed | True | Release review simulator can allow this metadata-only traditional flow. |
| RP-RF-030 | deep_learning_requires_engineer_approval | True | Obtain DeepLearning release approval before allowing release review. |
| RP-RF-031 | multi_station_requires_engineer_approval | True | Obtain multi-station release approval before allowing release review. |
| RP-RF-032 | release_blocked_operator_contract | - | Fix TemplateMatching TemplateId before rerunning release review. |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
