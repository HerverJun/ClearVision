# RuntimePreview Agent Explanation Benchmark

- Generated UTC: `2026-06-06T13:40:09.986508+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Case | Scenario | Status | Risk | Explanation |
| --- | --- | --- | --- | --- |
| RP-SC-001 | wire_sequence |  | Risk: low. Metadata review can continue. | Review metadata report and keep real pilot disabled. |
| RP-SC-002 | terminal_color_order |  | Risk: low. Metadata review can continue. | Review metadata report and keep real pilot disabled. |
| RP-SC-003 | template_matching |  | Risk: low. Metadata review can continue. | Review metadata report and keep real pilot disabled. |
| RP-SC-004 | hole_distance |  | Risk: low. Metadata review can continue. | Review metadata report and keep real pilot disabled. |
| RP-SC-005 | remote_control_detection |  | Risk: low. Metadata review can continue. | Review metadata report and keep real pilot disabled. |
| RP-SC-006 | missing_camera |  | Risk: missing_camera_binding. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-007 | missing_template |  | Risk: missing_template. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-008 | missing_model |  | Risk: missing_model. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-009 | dangerous_path |  | Risk: dangerous_resource. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-010 | plc_station_deny |  | Risk: plc_station_denied. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-011 | precheck_blocked |  | Risk: precheck_not_ready. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-012 | allowlist_mismatch |  | Risk: allowlist_mismatch. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-013 | multi_operator_flow |  | Risk: medium. Metadata review can continue. | Review metadata report and keep real pilot disabled. |
| RP-SC-014 | missing_parameter |  | Risk: missing_parameter. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |
| RP-SC-015 | draft_editable_package_blocked |  | Risk: draft_allowed_package_blocked. Do not package or deploy. | Resolve metadata handle, rerun readiness, then rerun package precheck. |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
