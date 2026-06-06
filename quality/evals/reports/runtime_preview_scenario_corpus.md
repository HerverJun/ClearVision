# RuntimePreview Scenario Corpus

- Generated UTC: `2026-06-06T15:00:30.583603+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Case | Scenario | Status | Risk | Explanation |
| --- | --- | --- | --- | --- |
| RP-SC-001 | wire_sequence | passed | low | Line sequence check is package-ready after metadata camera and template handles are allowlisted. |
| RP-SC-002 | terminal_color_order | passed | low | Terminal color order inspection uses the same metadata camera with a different judgment rule. |
| RP-SC-003 | template_matching | passed | low | Template matching positioning is ready when TemplateId is catalog-backed. |
| RP-SC-004 | hole_distance | passed | low | Hole distance measurement can run metadata preview and package precheck without real image input. |
| RP-SC-005 | remote_control_detection | passed | low | Remote controller inspection uses ModelId metadata and does not load a model file. |
| RP-SC-006 | missing_camera | not_ready | missing_camera_binding | Camera binding is absent, so preview/package are blocked while the draft remains editable. |
| RP-SC-007 | missing_template | not_ready | missing_template | Template source is unresolved; engineer must bind TemplateId before package readiness. |
| RP-SC-008 | missing_model | not_ready | missing_model | Model metadata is unresolved; no model file is loaded and package stays blocked. |
| RP-SC-009 | dangerous_path | denied | dangerous_resource | External path-like metadata is denied and redacted before any artifact is produced. |
| RP-SC-010 | plc_station_deny | denied | plc_station_denied | PLC or Station intent is denied; no PLC write and no Station access are attempted. |
| RP-SC-011 | precheck_blocked | not_ready | precheck_not_ready | Runtime package precheck blocks packaging because replay/readiness metadata is incomplete. |
| RP-SC-012 | allowlist_mismatch | not_ready | allowlist_mismatch | Workflow references a metadata handle outside the pilot allowlist. |
| RP-SC-013 | multi_operator_flow | passed | medium | Multi-operator measurement flow is previewable as metadata and requires only review before real pilot. |
| RP-SC-014 | missing_parameter | not_ready | missing_parameter | A required operator parameter is missing; workflow remains editable but package is blocked. |
| RP-SC-015 | draft_editable_package_blocked | not_ready | draft_allowed_package_blocked | The workflow draft can still be edited even though package readiness is blocked by missing resources. |
| RP-SC-016 | package_manifest_blocked | not_ready | manifest_dependency_blocked | Manifest dry-run blocks package review because dependency metadata is incomplete. |
| RP-SC-017 | multi_camera_flow | not_ready | multi_camera_review | Two-camera workflow requires both camera metadata handles to be allowlisted. |
| RP-SC-018 | multi_model_flow | not_ready | multi_model_review | Multiple model dependencies require catalog ownership review before package review. |
| RP-SC-019 | template_plus_hole_distance | passed | medium | Template positioning and hole distance measurement can be reviewed as a metadata manifest. |
| RP-SC-020 | direct_deploy_request_denied | denied | deployment_intent_denied | Direct Station release intent is denied; no package or deployment is created. |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
