# RuntimePreview Redacted Flow Corpus

- Generated UTC: `2026-06-06T15:00:30.583603+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Case | Scenario | Status | Risk | Explanation |
| --- | --- | --- | --- | --- |
| RP-RF-001 | wire_sequence | passed | low | Verify harness wire order before release. |
| RP-RF-002 | remote_control_defect | passed | low | Detect missing buttons and label defects. |
| RP-RF-003 | template_measurement_combo | passed | medium | Locate fixture by template and measure a downstream feature. |
| RP-RF-004 | hole_distance | passed | low | Measure distance between two holes. |
| RP-RF-005 | terminal_color_order | passed | low | Check terminal color sequence. |
| RP-RF-006 | missing_camera | not_ready | missing_camera_binding | Camera handle is absent from the pilot catalog. |
| RP-RF-007 | missing_template | not_ready | missing_template | TemplateMatching has no TemplateId metadata handle. |
| RP-RF-008 | missing_model | not_ready | missing_model | DeepLearning operator has unresolved model metadata. |
| RP-RF-009 | missing_output_channel | not_ready | missing_output_channel | ResultOutput lacks a safe output channel id. |
| RP-RF-010 | plc_station_deny | denied | plc_station_denied | User intent includes PLC or Station release action. |
| RP-RF-011 | dangerous_path | denied | dangerous_resource | Template dependency tries to point at an external path. |
| RP-RF-012 | allowlist_mismatch | not_ready | allowlist_mismatch | Workflow handle is not allowlisted for pilot. |
| RP-RF-013 | multi_camera_flow | not_ready | multi_camera_review | Two camera metadata handles feed one decision. |
| RP-RF-014 | multi_model_flow | not_ready | multi_model_review | Two model metadata handles are required. |
| RP-RF-015 | parameter_missing | not_ready | missing_parameter | A key operator parameter is missing. |
| RP-RF-016 | package_manifest_blocked | not_ready | manifest_dependency_blocked | Manifest dry-run blocks release review. |
| RP-RF-017 | workflow_editable_package_blocked | not_ready | draft_allowed_package_blocked | Draft is editable while package review is blocked. |
| RP-RF-018 | runtime_package_precheck_blocked | not_ready | precheck_not_ready | Runtime package precheck risk blocks release. |
| RP-RF-019 | template_plus_hole_distance | passed | medium | Template positioning and hole distance share one camera. |
| RP-RF-020 | direct_deploy_request_denied | denied | deployment_intent_denied | User asks to release to Station directly. |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
