# Option D Round 5 Reject Gate

Audit date: `2026-08-17`  
Reference mode: `iteration-board`  
Passed: `D_17_camera_settings`, `D_18_plc_settings`  
Rejected: `14`

| Screen | Decision | Remaining blocker |
| --- | --- | --- |
| `D_02_overview` | Reject | Runtime band fixed, but Shell again promoted launcher entries and omitted `算子库` / compact appearance state. |
| `D_03_projects_data` | Reject | State collision fixed, but Shell added language, lost More/compact state, and the project name drifted. |
| `D_04_projects_empty` | Reject | Illustration removed, but the bordered container remained and a second navigation rail appeared. |
| `D_06_flow_validation_error` | Reject | Canvas remains about 47% rather than 65-75%. |
| `D_07_flow_preview_roi` | Reject | ROI core is correct, but the page gained a new Flow top navigation item, extra capability rail, and Windows controls. |
| `D_08_run_ng_modal` | Reject | Version fixed, but the six metrics, 6/7 checks, run/device state, and technical identity were rewritten. |
| `D_09_results_investigation` | Reject | Diagnostic input fixed, but the local project name was replaced and Product Shell service context was omitted. |
| `D_10_stations_list` | Reject | Core structure fixed, but package/source row facts were rewritten (`兼容 v12` instead of CURRENT `来源 r12`). |
| `D_11_station_detail` | Reject | WIRE_SWAP fixed, but normal Shell navigation disappeared and Windows controls appeared. |
| `D_12_inspection` | Reject | Labels fixed, but the same screen claims recovery is both complete and in progress. |
| `D_14_ai_failure_recovery` | Reject | Sidebar removed, but `核对删除结果` remains duplicated in main and right rail. |
| `D_15_operator_catalog` | Reject | Row facts fixed, but an unconfirmed `语言 中文` Shell entry was added. |
| `D_17_camera_settings` | Pass | Exact camera identity, acquisition/trigger/test/preview/resource structure and CURRENT Shell context are preserved. |
| `D_18_plc_settings` | Pass | Millisecond heartbeat, protocols, mismatch, unchecked writable state, mapping columns, two save boundaries, and Shell are preserved. |
| `D_20_station_communication` | Reject | Language removed, but appearance lost CURRENT `深色·紧凑`. |
| `D_21_ai_model_settings` | Reject | Window controls removed, but connection test is duplicated across the main form and contextual rail. |

`D_07` and `D_08` regressed broadly under iteration-target guidance, so their next retry omits the rejected target and returns to CURRENT-dominant references.
