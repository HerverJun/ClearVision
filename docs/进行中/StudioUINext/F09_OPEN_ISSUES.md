# F09 Open Issues

```text
F09_ISSUE_LEDGER_STATE=ACTIVE
LAST_REVIEWED=2026-08-05
P0_OPEN=0
P1_OPEN=0
P2_OPEN=3
P3_OPEN=0
F09_R_STATE=LOCAL_EVIDENCE_COMPLETE
F09_STATE=ENGINEERING_DONE_WITH_ACCEPTANCE_DEBT
FRONTEND_MIGRATION_MAINLINE=COMPLETE
F09_R1_SOURCE_SHA=029bcc3beddb20dc136839d30dfd00d2c7a51e65
F09_R2_PRODUCT_SOURCE_SHA=d1c82ba88e351a2d48bcfae7f97e047483dbba98
PRODUCT_SOURCE_FOLLOW_UP_SHAS=c83dcc114290cf73e5e8d9b91e7b49732db8ec68,1545bca25
EVIDENCE_RUN_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
AUDIT_BRANCH=audit/f09-r2-d1c82ba88
AUDIT_BRANCH_HEAD=PENDING_AUDIT_PUSH
OFFICIAL_REMOTE_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5
FINAL_EVIDENCE_SHA=9dd69bd2bde44e8ea5b7285bfd18f47e02f95007
FINAL_DOC_SHA=PENDING_DOCUMENTATION_COMMIT
REMOTE_CI=AWAITING
FINAL_GATE=AWAITING
WORKTREE_STATE=DIRTY_UNRELATED_TRACKED_CHANGES
CONFIGURED_PROFILE=NEXT_DEFAULT
EFFECTIVE_DEFAULT_UI_ROOT=STUDIO_UI_NEXT
```

F09-R2 reconciliation: product source commit `d1c82ba88e351a2d48bcfae7f97e047483dbba98` closes the implementation portion of the Desktop shutdown/isolation repair; follow-up product fixes are recorded at `c83dcc114290cf73e5e8d9b91e7b49732db8ec68` and `1545bca25`. `F09-I001` remains closed by the product decision removing Operator formal-run authority. The Operator surface remains a read-only UI projection and no frontend or backend permission is widened. `F09-I002` is closed by the `r-9dd-r1` rollback drill: the same ProjectId remained compatible across Next/Legacy/Next, final `PersistenceRevision=4`, with no data loss or double owner. Playwright launcher teardown, independent no-Node, full DPI, field hardware, production soak and Remote CI/Final Gate remain separate evidence boundaries.

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

## Current cutover rule

`Studio:StartupProfile=NEXT_DEFAULT` 已配置，`STUDIO_UI_NEXT` 是当前默认 UI root，`LEGACY_FALLBACK` 仍是可用的配置级回退入口。当前本地工程证据已完成，`F09-I002` rollback 已关闭，但审计分支推送、Remote CI/Final Gate、独立 no-Node、完整 DPI、现场硬件和生产 soak 仍未完成，因此继续保持 `PRODUCTION_ACCEPTANCE=NOT_GRANTED`。不得把未运行的证据边界伪装成 PASS。
