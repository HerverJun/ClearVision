# Option D Round 6 Reject Audit

Audit date: `2026-08-17`

Scope: the fourteen Option D screens regenerated in Round 6. Each candidate was checked against its exact CURRENT screenshot, the Round 6 iteration instruction, and the Option D Reject Gate. Generated text, image content, and business values were not accepted as product truth.

Result: `0/14 PASS`, `14/14 REJECT`.

| ID | Decision | Minimum blocking evidence |
| --- | --- | --- |
| `D_02_overview` | REJECT | Recent project omits `上次打开 2026/07/15 11:00`; user role `工程师` is missing; runtime and launcher copy drift from CURRENT. |
| `D_03_projects_data` | REJECT | Adds a second left navigation rail including `设置`; user identity drifts from `fixture-engineer / 工程师`; `1 个工程` and exact page copy are missing. |
| `D_04_projects_empty` | REJECT | Shell omits `浅色·紧凑`; header, search placeholder, count spacing, and empty-state copy drift from CURRENT. |
| `D_06_flow_validation_error` | REJECT | Canvas is about 72.6% and Inspector about 485px instead of the instructed 68-72% / 300-340px; `数量` becomes `数值1`; Shell/run state and verified commands drift. |
| `D_07_flow_preview_roi` | REJECT | Canvas falls to about 35% behind permanent picker/Inspector rails; adds top-level `流程`; the Preview image is not the bound CURRENT 100x100 artifact and lacks its red patch/crack evidence. |
| `D_08_run_ng_modal` | REJECT | Modal facts are substantially correct, but `算子库` becomes `资产库`, a permanent unverified capability rail remains, and `正式运行完成，判定NG` / `正式运行` wording drifts. |
| `D_09_results_investigation` | REJECT | `本机结果` becomes `本机结果库`; timestamp changes from 2026 to 2024; `有效判定信号` value becomes `NG` instead of `有`; user identity drifts. |
| `D_10_stations_list` | REJECT | Shell adds icon/Windows controls and loses exact appearance/more context; `结果待同步` and `相机：已就绪` drift; page role copy changes. |
| `D_11_station_detail` | REJECT | `连接恢复中` becomes `连接中或断开`; `线序错误` is omitted beside `WIRE_SWAP`; user identity and recovery warning copy drift. |
| `D_12_inspection` | REJECT | Recovery contradiction is fixed, but readiness labels, project context `在线瓶盖检测 · 保存修订 12`, and user identity drift despite a change-only instruction. |
| `D_14_ai_failure_recovery` | REJECT | Core recovery composition passes, but Product Shell user and appearance drift from `f06-engineer / 工程师` and `浅色·紧凑`. |
| `D_15_operator_catalog` | REJECT | Adds an eighth `可见范围` table column; CURRENT operator descriptions/identifiers are rewritten, including a duplicate wrong identifier for row 009. |
| `D_20_station_communication` | REJECT | Required `深色·紧凑` is fixed, but Windows minimize/maximize/close and a `收起` control are newly added. |
| `D_21_ai_model_settings` | REJECT | Test action uniqueness and user role are fixed, but Shell renames `工程` to `工具` and `算子库` to `脚手架`. |

No candidate was promoted. Round 7 must preserve every verified region and change only the listed blockers.
