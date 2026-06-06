# RuntimePreview Deploy Readiness Report

- Generated UTC: `2026-06-06T15:00:30.583603+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Case | Scenario | Ready | Blocked | Package created |
| --- | --- | --- | --- | --- |
| RP-SC-001 | wire_sequence | True | False | False |
| RP-SC-002 | terminal_color_order | True | False | False |
| RP-SC-003 | template_matching | True | False | False |
| RP-SC-004 | hole_distance | True | False | False |
| RP-SC-005 | remote_control_detection | True | False | False |
| RP-SC-006 | missing_camera | False | True | False |
| RP-SC-007 | missing_template | False | True | False |
| RP-SC-008 | missing_model | False | True | False |
| RP-SC-009 | dangerous_path | False | True | False |
| RP-SC-010 | plc_station_deny | False | True | False |
| RP-SC-011 | precheck_blocked | False | True | False |
| RP-SC-012 | allowlist_mismatch | False | True | False |
| RP-SC-013 | multi_operator_flow | True | False | False |
| RP-SC-014 | missing_parameter | False | True | False |
| RP-SC-015 | draft_editable_package_blocked | False | True | False |
| RP-SC-016 | package_manifest_blocked | False | True | False |
| RP-SC-017 | multi_camera_flow | False | True | False |
| RP-SC-018 | multi_model_flow | False | True | False |
| RP-SC-019 | template_plus_hole_distance | True | False | False |
| RP-SC-020 | direct_deploy_request_denied | False | True | False |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
