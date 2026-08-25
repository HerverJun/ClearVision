# Option D Round 3 Reject Gate

Audit date: `2026-08-17`  
Authority: CURRENT screenshot plus current ClearVision code  
Candidate scope: `19` pending D local screens generated with `current-dominant` references

## Decision Summary

- Passed: `D_19_tcp_settings`, `D_22_diagnostics`, `D_23_about`
- Rejected for round 4: `16`
- Existing passed screens left unchanged: `D_01_login`, `D_05_flow_editor`, `D_13_ai_workspace`, `D_16_system_settings`, `D_24_forbidden`

| Screen | Decision | Blocking evidence |
| --- | --- | --- |
| `D_02_overview` | Reject | Product Shell navigation was replaced by launcher entries; `概览` and `算子库` were lost. |
| `D_03_projects_data` | Reject | Added a second icon rail; omitted Shell status/appearance/more, page description, project count, and changed real project data. |
| `D_04_projects_empty` | Reject | Omitted `0个工程`; generalized the confirmed search/sort semantics; cardified the required unframed list field. |
| `D_06_flow_validation_error` | Reject | Validation was detached from the invalid field; ports/output, result/ROI/diagnostic context, and real command entries were omitted; canvas was no longer dominant. |
| `D_07_flow_preview_roi` | Reject | Added a second Preview mode and unconfirmed ROI controls; fabricated ROI/result values instead of the bound 100x100 artifact and 10/10/30/20 draft. |
| `D_08_run_ng_modal` | Reject | Changed the real project name; fabricated timestamp, duration, and technical identities; conflated completed execution with NG judgment and compressed 6/7 readiness. |
| `D_09_results_investigation` | Reject | Changed the NG filter to all, omitted the diagnostic-code field, added an unimplemented comparison command and duplicate page-size control, changed project data, and replaced the real local image evidence with a false missing-image state. |
| `D_10_stations_list` | Reject | Added a second set of connection-state quick filters; weakened the confirmed search scope and omitted the real station diagnostic subline and Shell appearance state. |
| `D_11_station_detail` | Reject | Added help and desktop window controls while omitting the normal Appearance and More Shell context. |
| `D_12_inspection` | Reject | Added desktop window controls forbidden by the current product shell; the remaining inspection structure is retained for the retry. |
| `D_14_ai_failure_recovery` | Reject | Invented an AI node canvas, edges, zoom tools, candidate counts, and unverified session/build facts. |
| `D_15_operator_catalog` | Reject | Replaced CURRENT operator rows with fabricated face/OCR/model data, omitted the full Product Shell, and omitted the real version column and 198/200 count semantics. |
| `D_17_camera_settings` | Reject | Omitted IP, interface, and serial-photoelectric test; implied an unverified active hardware frame/connection. |
| `D_18_plc_settings` | Reject | Changed heartbeat from milliseconds to seconds; added OS chrome and unverified enabled/validation states; omitted the normal Product Shell. |
| `D_20_station_communication` | Reject | Contradicted the saved/current projection with an unverified unsaved/restart-required state and added OS chrome while omitting Product Shell context. |
| `D_21_ai_model_settings` | Reject | Duplicated connection test, advanced settings, and inference-support actions across two regions and changed the real API-key-configured status. |
| `D_19_tcp_settings` | Pass | Preserves the real Client/Server profiles, connection controls, text/HEX send-response flow, exact six log columns, and disconnected/empty runtime state without a new transport or telemetry. |
| `D_22_diagnostics` | Pass | Preserves Product Shell, service/session/host facts, version/environment mapping, copy/refresh, and technical diagnostics without new controls or state. |
| `D_23_about` | Pass | Preserves normal Product Shell, product/host/backend/version/license/support identity, and the Studio versus Runtime/Station boundary without marketing or runtime controls. |

## Contract Corrections

Two round-3 instructions conflicted with CURRENT and are corrected before round 4:

1. `D_09_results_investigation` is a local result with a real black image artifact in CURRENT. A missing workstation-image message is not valid for this selected record.
2. `D_15_operator_catalog` CURRENT visibly includes the `版本` column and `198 个匹配项 / 目录共 200 项`. Round 4 preserves them; the previous instruction to remove the version column is superseded.

Generated text remains non-authoritative. The gate checks structure, function inventory, state meaning, and whether apparently authoritative data is traceable to CURRENT.
