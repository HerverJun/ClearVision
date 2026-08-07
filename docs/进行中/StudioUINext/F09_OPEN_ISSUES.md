# F09 Open Issues

## M-series reconciliation (2026-08-06)

历史 `30966530885=FAIL` 保留不变；最终计划记录 `31026167704=PASS`。当前产品范围已冻结为
`f8f581932469f7c52fe547b7bcabe8ad45d89532`，但本会话未对该 baseline 运行 Remote CI。已取得的真实
WebView2 Windows 100% 与 Release runtime 证据采集自先前 dirty audit candidate，125% 仍未执行。
因此以下 acceptance debt 仍然有效：

- `P1` 真实 WebView2 Windows 125% 和独立 no-Node 证据不能由当前 100%/本机 audit 或历史 F09 数字替代。
- `P1` Legacy 生产可达性、WebMessage compatibility 隔离和 Remote CI 当前 SHA 的复核仍需单独完成。
- `P2` 现场 Camera/PLC/Station、完整 DPI、生产 soak 和用户视觉签收仍未执行。

```text
AUDIT_HEAD=9800d6045a9f5fdfc62a166242e83529b833dc7d
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
CURRENT_WORKTREE=CLEAN_AFTER_DOCUMENTATION_COMMIT
CURRENT_PLAN_REMOTE_CI_RUN=31026167704
CURRENT_PLAN_REMOTE_CI=PASS
WEBVIEW2_100=PASS_AUDIT_DIRTY_CANDIDATE_NOT_BASELINE_SHA
WEBVIEW2_125=BLOCKED
RELEASE_PUBLISH_RUNTIME=PASS_LOCAL_MACHINE
INDEPENDENT_NO_NODE=NOT_PERFORMED
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

```text
F09_ISSUE_LEDGER_STATE=ACTIVE
LAST_REVIEWED=2026-08-05
P0_OPEN=0
P1_OPEN=1
P2_OPEN=3
P3_OPEN=0
F09_R_STATE=PARTIAL
F09_STATE=PARTIAL
FRONTEND_MIGRATION_MAINLINE=COMPLETE
F09_R1_SOURCE_SHA=029bcc3beddb20dc136839d30dfd00d2c7a51e65
F09_R2_PRODUCT_SOURCE_SHA=d1c82ba88e351a2d48bcfae7f97e047483dbba98
PRODUCT_SOURCE_FOLLOW_UP_SHAS=c83dcc114290cf73e5e8d9b91e7b49732db8ec68,1545bca25
EVIDENCE_RUN_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
AUDIT_BRANCH=audit/f09-r2-d1c82ba88
AUDIT_BRANCH_HEAD=06eddf63c488266f818bc36e1d14d6aa0f798333
OFFICIAL_REMOTE_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
FINAL_EVIDENCE_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
FINAL_DOC_SHA=7ccbe915dcee42978bb50caea53e51c7539ad18d
REMOTE_CI_RUN=30966530885
REMOTE_CI=FAIL
FINAL_GATE=FAIL
WORKTREE_STATE=DIRTY_UNRELATED_TRACKED_CHANGES
CONFIGURED_PROFILE=NEXT_DEFAULT
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
```

F09-R2 reconciliation: product source commit `d1c82ba88e351a2d48bcfae7f97e047483dbba98` closes the implementation portion of the Desktop shutdown/isolation repair; follow-up product fixes are recorded at `c83dcc114290cf73e5e8d9b91e7b49732db8ec68` and `1545bca25`. `F09-I001` remains closed by the product decision removing Operator formal-run authority. The Operator surface remains a read-only UI projection and no frontend or backend permission is widened. `F09-I002` is closed by the `r-9dd-r1` rollback drill: the same ProjectId remained compatible across Next/Legacy/Next, final `PersistenceRevision=4`, with no data loss or double owner. Remote CI run `30966530885` failed at clean-checkout `Validate Measurement Performance Report` because the committed `ColorMeasurement` entry is `FAIL`; Final Gate failed on that required job. Playwright launcher teardown, independent no-Node, full DPI, field hardware and production soak remain separate evidence boundaries.

| IssueId | Severity | Area | Symptom | Evidence | RootCauseStatus | PlannedGoal | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F09-I001 | P1 | Cutover / authorization | Operator 没有正式 admission、execute、PLC、项目写入或 `/inspection` 能力；相关请求按后端 policy 返回 `403` | `EndpointPermissionGuards.cs`; `f09-operator-pilot.cjs`; F09 产品决策 | resolved_by_product_decision | G6 | closed | 这是有意的只读产品合同。不得通过前端、feature flag 或测试夹具放宽。Operator 只读 UI 投影已通过代码级测试。 |
| F09-I002 | P1 | Host / rollback | 历史真实 rollback manifest 因 Desktop Host close/flush 超过 15 秒失败 | `.tmp/studio-ui-next/f09/rollback/r-9dd-r1/studio-ui-rollback-evidence.json`; `Invoke-StudioUiRollbackEvidence.ps1`; `MainForm.cs`; `DesktopShutdownDiagnostics.cs` | repair_implemented_runtime_verified | G5/G6 | closed | `r-9dd-r1` 已完成 Next/缺失资源/Legacy/Next 演练；同一 ProjectId、最终 `PersistenceRevision=4`、shutdown diagnostics、owner 清理和 process exit 均通过。 |
| F09-I003 | P2 | Legacy project lifecycle | Legacy demo/template 创建仍直接通过 `DemoProjectService` repository write，未提供 Next lifecycle 的 `clientOperationId` reconcile | `DemoProjectService.cs`; `projectManager.js` | known | G1/G4 | deferred | 仅保留为 Legacy fallback；不得把它称作 Next 已迁移能力。 |
| F09-I004 | P2 | Workspace lifecycle evidence | Browser full 已落盘 141 passed / 26 skipped / 0 failed；受管 Playwright launcher 的 Windows `taskkill /T /F` 未自然 teardown，端口随后已释放 | `f03-workspace.spec.ts`; `studio-ui-next-server.cjs`; `.tmp/studio-ui-next/f09/browser/full-9dd-r3/browser-full.log` | known_infrastructure_limitation | G5 | open | 这是证据基础设施问题，不等同于已证实的 Workspace owner 泄漏；需要单独治理 launcher teardown。 |
| F09-I005 | P2 | Acceptance | 独立 no-Node 目标机、完整 Windows DPI 矩阵、Station/Camera/PLC/TCP、Remote CI/Final Gate 与生产 soak 尚无本次 SHA 的完整验收证据 | F08/F09 evidence boundary | known | G5/G7 | open | 保留为 acceptance debt；当前 Release 125% 证据和 Desktop 子进程 Node descendant count 为 0 不能替代独立 no-Node、完整 DPI 或现场验证。 |
| F09-I006 | P2 | Database maintenance | database restore/repair/cleanup/global reset 未迁移至 Next | `SettingsDatabasePanel.vue`; Legacy `systemTabs.js` | known | G1/G7 | deferred | 产品决定允许 Legacy fallback。 |
| F09-I007 | fixed | Authorization | Operator 可调用 `/api/demo/create*` 创建工程 | `DemoEndpoints.cs`; `DemoEndpointsTests.cs` | fixed | G2 | fixed | 两个 POST 都要求 `CanEditProject`；定向测试已通过。 |
| F09-I008 | fixed | Profile contract | `profileAllowedRoles` 原本未被 startup 注入对象冻结 | `WebView2Host.cs`; `WebView2HostTests.cs` | fixed | G3 | fixed | 注入数组、feature flags 和根对象均为只读投影。 |
| F09-I009 | fixed | Navigation | Operators、Stations、Diagnostics、About 路由未稳定暴露在 Product Shell 的次级导航 | `ProductLayout.vue`; `appMount.spec.ts` | fixed | G2 | fixed | 根据 role/feature flags 派生 More 菜单，owner 不因隐藏入口挂载。 |
| F09-I010 | P2 | Evidence integrity | Profile、Rollback、Final runner 已在 `9dd69bd2b` 上通过，但受控场景使用显式 Profile 注入，尚未形成独立的无覆盖启动证据 | `Invoke-StudioUiWebView2Evidence.ps1`; `Invoke-StudioUiProfileEvidence.ps1`; `Invoke-StudioUiFinalEvidence.ps1`; `.tmp/studio-ui-next/f09/profiles/9dd-profile-r3/studio-ui-profile-evidence.json` | known_boundary | G5/G7 | open | 当前证据证明 `NEXT_DEFAULT` 配置投影、命名 Profile、权限边界和 owner guard；不把显式 runner profile 扩大解释为无覆盖启动或独立 no-Node 证据。 |
| F09-I011 | P1 | Remote CI / performance evidence | clean checkout 的 `Validate Measurement Performance Report` 读取已提交 report，并发现 `ColorMeasurement` 状态为 `FAIL`；run `30966530885` 的 Final Gate 因 required job failure 失败 | `measurement_performance_budget_report.json`; [run 30966530885](https://github.com/HerverJun/ClearVision/actions/runs/30966530885); [Final Gate job](https://github.com/HerverJun/ClearVision/actions/runs/30966530885/job/92188299730) | confirmed_repository_artifact_failure | G7 | open | 本地未暂存生成报告的 PASS 不能替代 clean checkout；不得放宽 gate 或将未提交的 `test_results` 纳入本轮 7 文件文档提交。需由产品/质量 owner 按既有性能报告流程修复并重新跑 Remote CI。 |

## Current cutover rule

`Studio:StartupProfile=NEXT_DEFAULT` 已配置，`STUDIO_UI_NEXT` 是当前默认 UI root，`LEGACY_FALLBACK` 仍是可用的配置级回退入口。当前本地工程证据和 rollback 已完成，但 Remote CI run `30966530885` 与 Final Gate 已失败，正式分支不得快进。独立 no-Node、完整 DPI、现场硬件和生产 soak 仍未完成，因此继续保持 `PRODUCTION_ACCEPTANCE=NOT_GRANTED`；不得把本地未提交报告或其他证据边界伪装成远端 PASS。

## Current TODO execution addendum (2026-08-07)

```text
CURRENT_AUDIT_BASELINE_HEAD=68e6e4286d008433f804ef90de00c8017184c177
CURRENT_IMPLEMENTATION_COMMIT=418406e620082fdedf46cd2a180b44a27c43d002
CURRENT_AUDIT_BRANCH=studio-ui-next
CURRENT_REMOTE_HEAD=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
CURRENT_REMOTE_RELATION=REMOTE_ANCESTOR_AHEAD_37_NO_DIVERGENCE
CURRENT_WORKTREE=DIRTY_SCOPED_CANDIDATE_BEFORE_COMMIT
LOCAL_FRONTEND_GATES=PASS
LOCAL_BROWSER_F03=PASS_59_OF_59
LOCAL_BROWSER_F04R=PASS_2_OF_2
CALIBRATION_ENDPOINT_TESTS=PASS_4_OF_4
PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

本次 audit 只关闭有当前代码和测试证据的细分操作；历史 F09 issue 不因本地 Chromium 或 unit 自动关闭。新增/拆分的合同台账如下：

| IssueId | Severity | Area | Current finding | Evidence / required owner | Status |
| --- | --- | --- | --- | --- | --- |
| CV-AUDIT-045 | P1 | Calibration authorization | formal save endpoint has `CanEditProject` and Operator `403`; draft solve endpoint has no explicit permission guard and its intended authenticated/readonly semantics remain unclear | `CalibrationDraftEndpoints.cs`; `CalibrationDraftEndpointsTests.cs`; Desktop/API owner | `OPEN_BACKEND_AUDIT` |
| CV-AUDIT-046 | P2 | Template acceptance | Template query/owner/conversion and unit coverage exist, but no complete search -> preview -> apply -> save -> reload Playwright evidence exists at this audit anchor | Template owner / UI evidence owner | `OPEN_EVIDENCE` |
| CV-AUDIT-047 | P1 | Project JSON lifecycle | no current Next import/export schema, file contract or lifecycle `clientOperationId`/reconcile endpoint was found | Project lifecycle/backend owner | `BLOCKED_BY_CONTRACT` |
| CV-AUDIT-048 | P1 | Planar calibration | no current Next formal two-dimensional scale/offset wizard and Project asset contract was found | Calibration/backend owner | `BLOCKED_BY_CONTRACT` |
| CV-AUDIT-049 | P1 | Results bulk export | trend/distribution/report queries exist, but no current full-batch JSON/CSV export contract and progress/unknown semantics were found | Results/export service owner | `BLOCKED_BY_CONTRACT` |
| CV-AUDIT-050 | P1 | AI and device command scope | AgentRun attachment/resource fields and the line-sequence/Station/settings high-risk command contracts are not sufficient for a safe Next-only owner | AgentRun, device, Station and settings backend owners | `BLOCKED_BY_CONTRACT` |

`CV-AUDIT-045` is intentionally split: formal asset permission is resolved and tested; only draft solve permission semantics remain open. `F09-I003`, `F09-I005`, `F09-I006`, `F09-I010` and `F09-I011` remain open/deferred according to the existing table. No issue was closed solely because `git fetch` showed the remote 37 commits behind; push and Remote CI are separate evidence.
