# ClearVision A/B/C Visual Audit Index

All generated images are visual references. Current ClearVision screenshots and code remain authoritative for copy, controls, routes, workflow names, state, and business data.

## Delivery Status

- Frozen real screens/states: `24`.
- Option coverage: `A=24`, `B=24`, `C=24`.
- Model: exact `gpt-image-2`; fallback: `false`.
- Functional manifest gate: passed for all `72` entries.
- Product-owner status: awaiting selection; no visual option is approved yet.

## Design Directions

- A - Precision Workstation: compact CAD/IDE order, cool light work surfaces, crisp dividers, minimal elevation.
- B - Premium Industrial: deep graphite material hierarchy, restrained tactile depth, durable installed-software character.
- C - Modern AI Engineering: cool layered work zones, slightly more breathing room, modern AI-assisted engineering without chat/SaaS behavior.

## Master Chains

- `A_FLOW_MASTER -> A_AI_MASTER -> A_SETTINGS_MASTER`
- `B_FLOW_MASTER -> B_AI_MASTER -> B_SETTINGS_MASTER`
- `C_FLOW_MASTER -> C_AI_MASTER -> C_SETTINGS_MASTER`

## Fast Review

- `abc_comparison_index.png`: overview of every CURRENT/A/B/C page comparison.
- `option_A_contact_sheet.png`, `option_B_contact_sheet.png`, `option_C_contact_sheet.png`: whole-option scans.
- `abc_master_contact_sheet.png`: all nine Master Screens.
- Individual page sheets in this directory preserve the same `CURRENT | A | B | C` order.

## Page Mapping

| Screen | Page | Current | A | B | C |
| --- | --- | --- | --- | --- | --- |
| `01_login` | Login | `current/r2/S00-B0.png` | `option_A/screens/01_login.png` | `option_B/screens/01_login.png` | `option_C/screens/01_login.png` |
| `02_overview` | Overview | `current/r2/S01-B0.png` | `option_A/screens/02_overview.png` | `option_B/screens/02_overview.png` | `option_C/screens/02_overview.png` |
| `03_projects_data` | Projects - Data | `current/r2/S02-B0.png` | `option_A/screens/03_projects_data.png` | `option_B/screens/03_projects_data.png` | `option_C/screens/03_projects_data.png` |
| `04_projects_empty` | Projects - Empty | `current/r2/S02-EMPTY.png` | `option_A/screens/04_projects_empty.png` | `option_B/screens/04_projects_empty.png` | `option_C/screens/04_projects_empty.png` |
| `05_flow_editor` | Flow Editor | `current/r2/S04-B0.png` | `option_A/screens/05_flow_editor.png` | `option_B/screens/05_flow_editor.png` | `option_C/screens/05_flow_editor.png` |
| `06_flow_validation_error` | Flow Validation Error | `current/r2/S04-B2.png` | `option_A/screens/06_flow_validation_error.png` | `option_B/screens/06_flow_validation_error.png` | `option_C/screens/06_flow_validation_error.png` |
| `07_flow_preview_roi` | Flow Preview and ROI | `current/r2/S05-B2.png` | `option_A/screens/07_flow_preview_roi.png` | `option_B/screens/07_flow_preview_roi.png` | `option_C/screens/07_flow_preview_roi.png` |
| `08_run_ng_modal` | Run Details NG Modal | `current/r2/S06-B0.png` | `option_A/screens/08_run_ng_modal.png` | `option_B/screens/08_run_ng_modal.png` | `option_C/screens/08_run_ng_modal.png` |
| `09_results_investigation` | Results Investigation | `current/r2/S07-B0.png` | `option_A/screens/09_results_investigation.png` | `option_B/screens/09_results_investigation.png` | `option_C/screens/09_results_investigation.png` |
| `10_stations_list` | Stations List | `current/r2/S08-B0.png` | `option_A/screens/10_stations_list.png` | `option_B/screens/10_stations_list.png` | `option_C/screens/10_stations_list.png` |
| `11_station_detail` | Station Detail | `current/r2/S08-B2.png` | `option_A/screens/11_station_detail.png` | `option_B/screens/11_station_detail.png` | `option_C/screens/11_station_detail.png` |
| `12_inspection` | Inspection | `current/r2/S09-B0.png` | `option_A/screens/12_inspection.png` | `option_B/screens/12_inspection.png` | `option_C/screens/12_inspection.png` |
| `13_ai_workspace` | AI Workspace | `current/r2/S11-B0.png` | `option_A/screens/13_ai_workspace.png` | `option_B/screens/13_ai_workspace.png` | `option_C/screens/13_ai_workspace.png` |
| `14_ai_failure_recovery` | AI Failure Recovery | `current/r2/S11-EXCEPTION.png` | `option_A/screens/14_ai_failure_recovery.png` | `option_B/screens/14_ai_failure_recovery.png` | `option_C/screens/14_ai_failure_recovery.png` |
| `15_operator_catalog` | Operator Catalog | `current/r2/S12-B0.png` | `option_A/screens/15_operator_catalog.png` | `option_B/screens/15_operator_catalog.png` | `option_C/screens/15_operator_catalog.png` |
| `16_system_settings` | System Settings | `current/r2/S10-B0.png` | `option_A/screens/16_system_settings.png` | `option_B/screens/16_system_settings.png` | `option_C/screens/16_system_settings.png` |
| `17_camera_settings` | Camera Settings | `current/settings/settings-camera-b0-1920x1080-light-compact.png` | `option_A/screens/17_camera_settings.png` | `option_B/screens/17_camera_settings.png` | `option_C/screens/17_camera_settings.png` |
| `18_plc_settings` | PLC Settings | `current/settings/settings-plc-b0-1920x1080-light-compact.png` | `option_A/screens/18_plc_settings.png` | `option_B/screens/18_plc_settings.png` | `option_C/screens/18_plc_settings.png` |
| `19_tcp_settings` | TCP Settings | `current/settings/settings-tcp-b0-dark-1920x1080-dark-compact.png` | `option_A/screens/19_tcp_settings.png` | `option_B/screens/19_tcp_settings.png` | `option_C/screens/19_tcp_settings.png` |
| `20_station_communication` | Station Communication | `current/settings/settings-station-1920x1080-dark-compact.png` | `option_A/screens/20_station_communication.png` | `option_B/screens/20_station_communication.png` | `option_C/screens/20_station_communication.png` |
| `21_ai_model_settings` | AI Model Settings | `current/settings/settings-ai-model-1920x1080-light-compact.png` | `option_A/screens/21_ai_model_settings.png` | `option_B/screens/21_ai_model_settings.png` | `option_C/screens/21_ai_model_settings.png` |
| `22_diagnostics` | Diagnostics | `current/r2/S13-B0.png` | `option_A/screens/22_diagnostics.png` | `option_B/screens/22_diagnostics.png` | `option_C/screens/22_diagnostics.png` |
| `23_about` | About | `current/r2/S13-B2.png` | `option_A/screens/23_about.png` | `option_B/screens/23_about.png` | `option_C/screens/23_about.png` |
| `24_forbidden` | Forbidden | `current/r2/S10-EXCEPTION.png` | `option_A/screens/24_forbidden.png` | `option_B/screens/24_forbidden.png` | `option_C/screens/24_forbidden.png` |
