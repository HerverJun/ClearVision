# F09 Open Issues

```text
F09_ISSUE_LEDGER_STATE=ACTIVE
LAST_REVIEWED=2026-08-04
P0_OPEN=0
P1_OPEN=1
P2_OPEN=5
P3_OPEN=0
CONFIGURED_PROFILE=NEXT_DEFAULT
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
```

F09-R reconciliation: `F09-I001` is closed by the product decision removing Operator formal-run authority. The Operator surface remains a read-only UI projection and no frontend or backend permission is widened. `F09-I002` remains open because the repaired Desktop close/flush path has not yet been exercised by a real WebView2 rollback drill. WebView2, DPI, publish/no-Node, Remote CI and field validation remain acceptance debt.

| IssueId | Severity | Area | Symptom | Evidence | RootCauseStatus | PlannedGoal | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F09-I001 | P1 | Cutover / authorization | Operator 没有正式 admission、execute、PLC、项目写入或 `/inspection` 能力；相关请求按后端 policy 返回 `403` | `EndpointPermissionGuards.cs`; `f09-operator-pilot.cjs`; F09 产品决策 | resolved_by_product_decision | G6 | closed | 这是有意的只读产品合同。不得通过前端、feature flag 或测试夹具放宽。Operator 只读 UI 投影已通过代码级测试。 |
| F09-I002 | P1 | Host / rollback | 历史真实 rollback manifest 因 Desktop Host close/flush 超过 15 秒失败；当前修复尚未在最终候选 SHA 上做真实 WebView2 演练 | `.tmp/studio-ui-next/f09/rollback/f09-rollback-ba2389d/studio-ui-rollback-evidence.json`; `Invoke-StudioUiRollbackEvidence.ps1`; `MainForm.cs`; `DesktopShutdownDiagnostics.cs` | repair_implemented_evidence_not_run | G5/G6 | open | 诊断阶段和隔离 unattended 门禁已实现。未完成最终候选 drill 前不得写成 `ROLLBACK_DRILL=PASS`。 |
| F09-I003 | P2 | Legacy project lifecycle | Legacy demo/template 创建仍直接通过 `DemoProjectService` repository write，未提供 Next lifecycle 的 `clientOperationId` reconcile | `DemoProjectService.cs`; `projectManager.js` | known | G1/G4 | deferred | 仅保留为 Legacy fallback；不得把它称作 Next 已迁移能力。 |
| F09-I004 | P2 | Workspace lifecycle evidence | F03 外部管理静态服务得到代码候选 54/54；受管 Playwright launcher 的 Windows `taskkill /T /F` 可能返回 access denied，clean exit/端口释放仍需复证 | `f03-workspace.spec.ts`; `studio-ui-next-server.cjs` | known | G5 | open | 这是证据基础设施问题，不等同于已证实的 Workspace owner 泄漏。 |
| F09-I005 | P2 | Acceptance | 独立 no-Node 目标机、真实 Windows 125% DPI、Station/Camera/PLC/TCP、Remote CI 与生产 soak 尚无本次 SHA 证据 | F08/F09 evidence boundary | known | G5/G7 | open | 保留为 acceptance debt，不能当作本地 Browser 或静态构建的替代。 |
| F09-I006 | P2 | Database maintenance | database restore/repair/cleanup/global reset 未迁移至 Next | `SettingsDatabasePanel.vue`; Legacy `systemTabs.js` | known | G1/G7 | deferred | 产品决定允许 Legacy fallback。 |
| F09-I007 | fixed | Authorization | Operator 可调用 `/api/demo/create*` 创建工程 | `DemoEndpoints.cs`; `DemoEndpointsTests.cs` | fixed | G2 | fixed | 两个 POST 都要求 `CanEditProject`；定向测试已通过。 |
| F09-I008 | fixed | Profile contract | `profileAllowedRoles` 原本未被 startup 注入对象冻结 | `WebView2Host.cs`; `WebView2HostTests.cs` | fixed | G3 | fixed | 注入数组、feature flags 和根对象均为只读投影。 |
| F09-I009 | fixed | Navigation | Operators、Stations、Diagnostics、About 路由未稳定暴露在 Product Shell 的次级导航 | `ProductLayout.vue`; `appMount.spec.ts` | fixed | G2 | fixed | 根据 role/feature flags 派生 More 菜单，owner 不因隐藏入口挂载。 |
| F09-I010 | P2 | Evidence integrity | runner 已拒绝脏工作树、顶层 `-NoBuild` 和 caller-supplied Desktop EXE，但尚未用最终干净 SHA 证明无显式 Profile 覆盖时的启动结果 | `Invoke-StudioUiWebView2Evidence.ps1`; `Invoke-StudioUiProfileEvidence.ps1`; `Invoke-StudioUiFinalEvidence.ps1` | known | G5/G7 | open | 最终候选必须先提交，再复跑 Profile、Rollback、Final 和未覆盖启动证据。 |

## Current cutover rule

`Studio:StartupProfile=NEXT_DEFAULT` 已配置，`STUDIO_UI_NEXT` 是当前默认 UI root，`LEGACY_FALLBACK` 仍是可用的配置级回退入口。该配置事实不等于生产接受；在 `F09-I002` 和剩余最终证据完成前，继续保持 `PRODUCTION_ACCEPTANCE=NOT_GRANTED`。不得把未运行的 WebView2、DPI、publish、Remote CI 或 rollback 结果伪装成 PASS。
