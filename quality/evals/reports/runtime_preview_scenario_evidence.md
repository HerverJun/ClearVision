# RuntimePreview Scenario Evidence

- Generated UTC: `2026-06-06T12:31:57.847051+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`

| Case | Scenario | Expected | Actual | Risk |
| --- | --- | --- | --- | --- |
| RP-SE-001 | wire_sequence | passed | passed |  |
| RP-SE-002 | template_matching | passed | passed |  |
| RP-SE-003 | hole_distance | passed | passed |  |
| RP-SE-004 | remote_control_detection | passed | passed |  |
| RP-SE-005 | missing_resource | not_ready | not_ready | deployment_blocked_metadata_only |
| RP-SE-006 | dangerous_path | denied | denied | dangerous_resource_denied |
| RP-SE-007 | station_plc_deny | denied | denied | dangerous_resource_denied |
| RP-SE-008 | precheck_not_ready | not_ready | not_ready | deployment_blocked_metadata_only |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, packaging, deployment, hot-load, or Real RuntimePreview adapter.
