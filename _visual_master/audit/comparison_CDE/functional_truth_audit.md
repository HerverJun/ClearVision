# ClearVision C/D/E Functional Truth Audit

Audit scope: visual-reference fidelity only. Current ClearVision screenshots, current code, and current contracts are the sole authority for functions, controls, states, copy, and business data.

## Final Gate Result

- Frozen scope: `24` real screens/states per option.
- C: archived functional gate `24/24` plus targeted re-audit. Its archived schema predates the five-part Reject Gate.
- D/E: active functional gate `48/48`; five-part Reject Gate `48/48`; outputs `48/48`; Masters `6/6`.
- Generation model: exact `gpt-image-2`; fallback was not used.
- Product-owner status: awaiting selection. No visual option is approved for implementation.

## Hallucination And Drift Removed

The image model did attempt to introduce unsupported controls, facts, duplicated regions, and renamed product structure. Those attempts were rejected or corrected before this audit package was built.

### Option C

| Candidate | Rejected drift | Accepted correction |
| --- | --- | --- |
| `C_06_flow_validation_error` | Product rename, invented node-detail/result tabs, duplicate toolbar | Regenerated; duplicate chrome and invented tabs removed |
| `C_08_run_ng_modal` | Invented Flow-property panel behind the modal | Regenerated with the CURRENT workspace background structure restored |
| `C_09_results_investigation` | Duplicate left navigation rail | Regenerated with the confirmed single navigation structure |

Source: `audit/comparison/final_reaudit.md`.

### Option D

| Candidate family | Rejected drift seen in iterative gates | Accepted rule |
| --- | --- | --- |
| Flow/run states | Invented metrics, checks, node/ROI facts, duplicate navigation, and unverified canvas commands such as Fit | Preserve only CURRENT Flow controls, result identity, readiness facts, and one real navigation structure |
| Results | Unsupported comparison/retest actions, changed filters/columns, and a false missing-image state | Restore CURRENT filtering, evidence, diagnostics, and result structure |
| Station/AI | Fictional node graph, duplicate recovery rails/actions, and invented candidate/build facts | Restore CURRENT Station trace/health facts and real AI recovery stages without extra rails |
| Camera/TCP | Invented hardware telemetry, false connected profiles/network facts, and omitted real discovery/connect/listen controls | Remove unverified device claims and retain only confirmed controls and states |
| AI model/About | Unsupported model catalog controls, protected-key semantic drift, and identity fields moved to wrong roles | Restore CURRENT operation semantics, identity fields, and Product Shell context |

Sources: `audit/d_round1_rejects.md` through `audit/d_round9_rejects.md`. These are process records; the active v3 manifest and current Reject Gate are the final readiness authority. `D_21_ai_model_settings` was later replaced by a current-dominant retry, so its active `generation` and Reject Gate evidence supersede the earlier restoration/touchup provenance.

### Option E

| Candidate | Drift found | Accepted correction |
| --- | --- | --- |
| `E_10_stations_list` | False language/duplicate appearance controls and package provenance drift | Removed false controls; restored `pkg-a · 来源 r12` and CURRENT recovery copy |
| `E_17_camera_settings` | Confirmed `识别输入设备` action was missing; generated shell fragments were duplicated | Restored the existing action and compact shell without adding camera capability |
| `E_19_tcp_settings` | Duplicate profile actions | Removed only the duplicate actions using a same-surface Picture Layer patch |
| `E_11_station_detail` | Generated route/page-name pollution | Replaced by a current-dominant retry that restored product identity and removed non-user-facing route text |
| `E_02`, `E_04`, `E_09`, `E_12`, `E_22`, `E_23` | Copy, state identity, empty-state, defect wording, diagnostics identity, or brand drift | Applied bounded Picture Layer corrections from CURRENT evidence without changing layout capability |

Sources: `workflow/touchups/E_*.json` and, where a controlled Picture Layer remains active, its immutable restoration/touchup evidence in `image_prompts.json`. `E_11_station_detail` uses the later current-dominant `generation` and Reject Gate evidence instead; stale top-level restoration/touchup metadata was removed.

### Final Option E Strict Correction Batch

The final Option E pass re-opened `11` previously rejected screens and reviewed each one directly against its `CURRENT_REFERENCE`. All eleven now pass the five-part Reject Gate.

| Candidate | Rejected or re-audited drift | Final accepted boundary |
| --- | --- | --- |
| `E_02`, `E_03`, `E_04`, `E_09`, `E_12`, `E_14` | Copy, state, empty/data, result, inspection, or AI-recovery structure had previously drifted from CURRENT | Re-audited page by page; only CURRENT controls, states, data relationships, and action boundaries remain authoritative |
| `E_06_flow_validation_error` | Incorrect or extra canvas commands and validation/value drift | Restored the verified canvas command set and the real `Count = 11` validation-error state |
| `E_07_flow_preview_roi` | Extra canvas/image tools, incorrect operator categories, and ROI dock identity drift | Removed unsupported tools, restored the confirmed categories, and restored the `ROI Rectangle` contextual dock |
| `E_11_station_detail` | Invented production traceability chain, health snapshot, CPU/memory facts, and other false Station modules/data | Rejected that variant; restored the CURRENT Station-detail structure and the real `More` menu only |
| `E_18_plc_settings` | PLC fields, mapping relationship, and save-state drift | Restored the verified PLC fields, mapping, and save boundary |
| `E_20_station_communication` | Invented token input and incorrect credential presentation | Restored the real masked `******` value and token explanation; no new token field remains |

Evidence: the current Reject Gate records in `image_prompts.json` and the final touchup records under `workflow/touchups/`, including `E_06_flow_validation_final_v1.json`, `E_07_flow_preview_roi_final_v1.json`, `E_11_station_detail_truth_v2.json`, `E_18_plc_settings_final_v1.json`, and `E_20_station_communication_final_v1.json`.

## Evidence Boundary

- A generated label, number, route, node name, device value, or status is never accepted as a product fact merely because it appears in a candidate image.
- Visual reconstruction must follow target layout/materials while taking all real copy, fields, controls, and state from CURRENT/code/contracts.
- Static Chromium does not prove real WebView2 behavior, Windows 125% DPI, authenticated live endpoints, physical Camera/PLC/Station operation, release publish, or full CI.
- This audit package authorizes product-owner visual comparison only, not frontend implementation.
