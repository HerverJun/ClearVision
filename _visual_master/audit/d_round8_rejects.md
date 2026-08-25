# Option D Round 8 Reject Audit

Audit date: `2026-08-17`

Result before controlled touchups: `1/13 PASS`, `12/13 REJECT`. Together with frozen Round 7 `D_02_overview`, Option D had `12/24` passed screens after `D_12_inspection` passed.

## Authority Correction

Direct inspection of CURRENT `current/r2/S05-B2.png` confirms that `流程` is a real Product Shell navigation entry in project workspace context. Earlier D07/D08 instructions incorrectly treated every top-level `流程` occurrence as invented. Round 9 corrects this: one real `流程` entry is preserved; only duplicate navigation or unverified capability rails remain forbidden.

| ID | Decision | Minimum blocking evidence |
| --- | --- | --- |
| `D_03_projects_data` | REJECT | Search is restored, but Shell loses exact `外观 浅色·紧凑`. |
| `D_04_projects_empty` | REJECT | `暂无工程` is restored, but Shell adds language and loses compact density. |
| `D_06_flow_validation_error` | REJECT | Canvas/Inspector/Preview zones are about 64% / 497px / 163px instead of the required 70% / 330px / 216px. |
| `D_07_flow_preview_roi` | REJECT | CURRENT artifact and ROI facts pass; actual dotted FlowCanvas is about 56.4% and Preview about 43.5%, outside the D target. The single real `流程` entry is not a blocker. |
| `D_08_run_ng_modal` | REJECT | Admission facts pass, but the real `流程` entry is omitted, unverified `适应画布` appears, and `近期结果` drifts to `运行结果`. |
| `D_09_results_investigation` | REJECT | Corrected facts pass, but a change-only iteration recomposes the frozen result list/detail and drops verified fields. |
| `D_10_stations_list` | REJECT | Intended changes pass, but diagnostic copy drifts from CURRENT `线序错误 · WIRE_SWAP`. |
| `D_11_station_detail` | REJECT | Intended evidence appears, but a new rail/reflow changes warning, hashes, status overview, and health facts. |
| `D_12_inspection` | PASS | Exact project context and readiness text are restored; all frozen facts remain. |
| `D_14_ai_failure_recovery` | REJECT | Rail removal passes, but authority/history/stage copy drifts from the frozen Round 7 source. |
| `D_15_operator_catalog` | REJECT | Intended first-row/Shell fixes pass, but exact rows regress, including duplicated 009 and missing 008. |
| `D_20_station_communication` | REJECT | Normal left/top structure returns, but the right Product Shell metadata is omitted. |
| `D_21_ai_model_settings` | REJECT | Untested projection passes, but `算子库` changes to `运行`. |

`D_15` and `D_20` subsequently moved to controlled Picture Layer repair using immutable `gpt-image-2` sources. Their Reject Gates were reset and require fresh review.
