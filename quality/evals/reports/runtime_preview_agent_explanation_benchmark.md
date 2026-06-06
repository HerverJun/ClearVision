# RuntimePreview Agent Explanation Benchmark

- Generated UTC: `2026-06-06T15:00:30.583603+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Case | Scenario | Status | Risk | Explanation |
| --- | --- | --- | --- | --- |
| RP-RF-001 | wire_sequence |  | Risk: low. Metadata review can continue. | Review metadata manifest and keep real pilot gate closed. |
| RP-RF-002 | remote_control_defect |  | Risk: low. Metadata review can continue. | Confirm ModelId catalog ownership before real pilot. |
| RP-RF-003 | template_measurement_combo |  | Risk: medium. Metadata review can continue. | Review combined template and measurement contract. |
| RP-RF-004 | hole_distance |  | Risk: low. Metadata review can continue. | Confirm measurement unit and tolerance source. |
| RP-RF-005 | terminal_color_order |  | Risk: low. Metadata review can continue. | Confirm template ownership and output mapping. |
| RP-RF-006 | missing_camera |  | Risk: missing_camera_binding. Do not package or deploy. | Bind an allowlisted metadata camera before package review. |
| RP-RF-007 | missing_template |  | Risk: missing_template. Do not package or deploy. | Assign an allowlisted TemplateId; do not use file paths. |
| RP-RF-008 | missing_model |  | Risk: missing_model. Do not package or deploy. | Bind ModelId from catalog; do not load a model file. |
| RP-RF-009 | missing_output_channel |  | Risk: missing_output_channel. Do not package or deploy. | Choose OutputChannelId before package review. |
| RP-RF-010 | plc_station_deny |  | Risk: plc_station_denied. Do not package or deploy. | Remove PLC/Station intent; this console cannot write or deploy. |
| RP-RF-011 | dangerous_path |  | Risk: dangerous_resource. Do not package or deploy. | Replace path-like metadata with catalog TemplateId. |
| RP-RF-012 | allowlist_mismatch |  | Risk: allowlist_mismatch. Do not package or deploy. | Review allowlist diff and confirm catalog handle. |
| RP-RF-013 | multi_camera_flow |  | Risk: multi_camera_review. Do not package or deploy. | Confirm both camera bindings are catalog allowlisted. |
| RP-RF-014 | multi_model_flow |  | Risk: multi_model_review. Do not package or deploy. | Confirm all ModelIds and output aggregation. |
| RP-RF-015 | parameter_missing |  | Risk: missing_parameter. Do not package or deploy. | Complete required operator parameters and rerun readiness. |
| RP-RF-016 | package_manifest_blocked |  | Risk: manifest_dependency_blocked. Do not package or deploy. | Resolve manifest dependencies; no package may be created. |
| RP-RF-017 | workflow_editable_package_blocked |  | Risk: draft_allowed_package_blocked. Do not package or deploy. | Keep editing; do not start release review yet. |
| RP-RF-018 | runtime_package_precheck_blocked |  | Risk: precheck_not_ready. Do not package or deploy. | Rerun readiness after metadata is resolved. |
| RP-RF-019 | template_plus_hole_distance |  | Risk: medium. Metadata review can continue. | Review dependency trace before requesting real pilot. |
| RP-RF-020 | direct_deploy_request_denied |  | Risk: deployment_intent_denied. Do not package or deploy. | Use metadata review only; direct deployment remains forbidden. |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
