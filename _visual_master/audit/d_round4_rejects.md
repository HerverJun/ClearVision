# Option D Round 4 Reject Gate

Audit date: `2026-08-17`  
Reference mode: `current-dominant`  
Decision: `16/16 rejected`

Round 4 corrected many core page structures, but every page retained at least one verifiable functional, state, shell, data, or option-language defect. No page was accepted for visual quality alone.

| Screen | Blocking reason |
| --- | --- |
| `D_02_overview` | Runtime facts became a bordered six-cell status dashboard instead of the restrained CURRENT projection. |
| `D_03_projects_data` | Normal populated state simultaneously renders loading, forbidden, and failed states; the sort menu is also left open. |
| `D_04_projects_empty` | Empty state remains inside a large bordered panel with a decorative icon instead of the unframed list field. |
| `D_06_flow_validation_error` | Canvas is about 49% of the screen rather than the required 65-75%; both wide permanent side regions remain open. |
| `D_07_flow_preview_roi` | Added help and notification controls and a second cancel-preview command; these are not in the selected CURRENT state. |
| `D_08_run_ng_modal` | Workspace version changed from CURRENT `1.0.0` to `1.1.0`. |
| `D_09_results_investigation` | The visible diagnostic-code input is missing; Product Shell appearance/more context drifted and help was added. |
| `D_10_stations_list` | Added a top-level Station monitor entry and language control, omitted exact appearance/more and the read-only/realtime-recovery projection. |
| `D_11_station_detail` | Added a top-level Station entry, omitted appearance/more, and changed `WIRE_SWAP` to `WIRE_SNAP`. |
| `D_12_inspection` | Rewrote readiness labels/messages and simultaneously claimed recovery complete and recovery in progress instead of the real continuous-run state. |
| `D_14_ai_failure_recovery` | Added an entire task/project/version/recycle-bin/settings sidebar and duplicated recovery actions in the right rail. |
| `D_15_operator_catalog` | Changed CURRENT row facts, including lifecycle, parameter counts, and descriptions. |
| `D_17_camera_settings` | Added `简体中文` to Product Shell and omitted the `浅色·紧凑` appearance projection. |
| `D_18_plc_settings` | Changed the current mapping row to writable and weakened the local-service Shell status. |
| `D_20_station_communication` | Added a new `语言` Product Shell entry. |
| `D_21_ai_model_settings` | Added Windows window controls and omitted local-service, appearance, and more Shell context. |

## Round 5 Policy

Round 5 uses the existing candidate only as an iteration target to retain corrected layout and style. The board also contains CURRENT and same-option architecture/Master references. CURRENT remains the sole source of functions, states, copy semantics, and data. The iteration target is not product truth.
