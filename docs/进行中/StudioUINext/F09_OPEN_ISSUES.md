# F09 Open Issues

```text
F09_ISSUE_LEDGER_STATE=ACTIVE
LAST_REVIEWED=2026-08-04
P0_OPEN=0
P1_OPEN=2
P2_OPEN=5
P3_OPEN=0
```

| IssueId | Severity | Area | Symptom | Evidence | RootCauseStatus | PlannedGoal | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F09-I001 | P1 | Cutover / authorization | `NEXT_DEFAULT` 不能切换：Operator 仅有受认证的只读结果/工作站能力，`/inspection` 被禁止，正式 admission、execute、PLC 和工程写入均为后端 403；不存在计划要求的 Operator “批准运行或连续检测 -> 结果 -> 退出/重启”完整路径 | `EndpointPermissionGuards.cs`; `f09-operator-pilot.cjs`; F09 §12.3/12.6 | known | G6 | open | 这是有意保护，不可通过前端、feature flag 或测试夹具放宽。当前配置必须保持 `NEXT_DEFAULT_CANDIDATE`。 |
| F09-I002 | P1 | Host / rollback | 历史真实 rollback manifest 已失败：Desktop Host close/flush 超过 15 秒；缺少最终 F09 候选 SHA 的成功 candidate -> failure injection -> legacy fallback -> candidate 演练 | `.tmp/studio-ui-next/f09/rollback/f09-rollback-ba2389d/studio-ui-rollback-evidence.json`; `Invoke-StudioUiRollbackEvidence.ps1` | known | G5/G6 | open | 不是“未运行”。必须先修复或得到最终候选 SHA 的 PASS，回退失败会阻断正式默认入口。 |
| F09-I003 | P2 | Legacy project lifecycle | Legacy demo/template 创建仍直接通过 `DemoProjectService` repository write，未提供 Next lifecycle 的 `clientOperationId` reconcile | `DemoProjectService.cs`; `projectManager.js` | known | G1/G4 | deferred | 仅保留为 Legacy fallback；`DemoEndpoints` 已增加 `CanEditProject`，不得把它称作 Next 已迁移能力。 |
| F09-I004 | P2 | Workspace lifecycle evidence | F03 已在外部管理静态服务下得到 54/54 行为 PASS；受管 Playwright launcher 曾在测试 PASS 后挂起，进程内 server 修复尚需在最终候选 SHA 复证 clean exit/端口释放 | `f03-workspace.spec.ts`; `studio-ui-next-server.cjs`; F03 审计 | known | G5 | open | 这是验证基础设施/证据缺口，不等同于已证实的 Workspace owner 泄漏。 |
| F09-I005 | P2 | Acceptance | 独立 no-Node 目标机、真实 Windows 125% DPI、真实 Station/Camera/PLC/TCP、Remote CI 与生产 soak 尚无本次 SHA 证据 | F08/F09 evidence 边界 | known | G5/G7 | open | 可形成 evidence debt，不能当作本地 Browser 或静态构建的替代。 |
| F09-I006 | P2 | Database maintenance | database restore/repair/cleanup/global reset 未迁移至 Next | `SettingsDatabasePanel.vue`; Legacy `systemTabs.js` | known | G1/G7 | deferred | 产品决定允许 Legacy fallback。 |
| F09-I007 | fixed | Authorization | Operator 可调用 `/api/demo/create*` 创建工程 | `DemoEndpoints.cs`; `DemoEndpointsTests.cs` | known | G2 | fixed | 两个 POST 都要求 `CanEditProject`；2026-08-04 定向 64/64 PASS。 |
| F09-I008 | fixed | Profile contract | `profileAllowedRoles` 原本未被 startup 注入对象冻结 | `WebView2Host.cs`; `WebView2HostTests.cs` | known | G3 | fixed | 注入数组、feature flags 和根对象均为只读投影。 |
| F09-I009 | fixed | Navigation | Operators、Stations、Diagnostics、About 路由未稳定暴露在 Product Shell 的次级导航 | `ProductLayout.vue`; `appMount.spec.ts` | known | G2 | fixed | 根据 role/feature flags 派生 More 菜单，owner 不因隐藏入口挂载。 |
| F09-I010 | P2 | Evidence integrity | 现有 WebView2 evidence 的 `sourceSha` 来自提交 HEAD，但此前运行时工作树包含未提交改动；Final 脚本通过显式 Profile 覆盖运行，尚未证明无覆盖的配置默认启动 | `Invoke-StudioUiWebView2Evidence.ps1`; `Invoke-StudioUiProfileEvidence.ps1`; `Invoke-StudioUiFinalEvidence.ps1` | known | G5/G7 | open | 最终候选必须先提交，再用新鲜 build 复跑；Operator `/stations` 已加入场景，也须在该次复跑中验证。 |

## Cutover rule

在 `F09-I001` 和 `F09-I002` 关闭前，禁止将配置改为 `NEXT_DEFAULT`。当前 F09 只能报告为 `PARTIAL`，且 `NEXT_UI_DEFAULT_ENTRY=NOT_ENABLED`。`P2` 可保留到最终 evidence debt，但必须在最终报告中逐项列出。
