# F06 G6 Final Evidence 与准入判断报告

> 执行日期：2026-07-31
> 工作树：`C:\Users\HerverJun\Desktop\ClearVision-UI-Next`
> 分支：`studio-ui-next`
> Remote CI：[ClearVision CI/CD run 30618432357](https://github.com/HerverJun/ClearVision/actions/runs/30618432357)

## 1. SHA、范围与结论

```text
G5_PRODUCT_ANCHOR=2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23
G6_INITIAL_SHA=ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998
FINAL_CODE_SHA=ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998
REMOTE_CODE_SHA=ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998
DOCS_FINAL_SHA=由承载本报告的 docs-only 提交定义，实际值见交付回报
REMOTE_BRANCH_SHA=报告提交推送后定义，实际值见交付回报
```

G6 没有修改产品、测试或仓库 harness；因此全部正式本地、Browser、真实 ASP.NET Core、
WebView2、DPI、publish/no-Node 与 Remote CI 证据均绑定同一 `FINAL_CODE_SHA`。后续只允许
docs-only tracking 提交，不改变 Final Code SHA，也不把 tracking SHA 冒充产品证据 SHA。

全部 F06 工程必需门禁已通过，可以关闭 F06。当前视觉按产品负责人授权用于框架迁移收口；
视觉精修延期。真实 LLM 产品质量、真实相机/PLC/Station 与生产现场验收未执行，不属于本次
工程完成结论。

## 2. G5 后提交审计

对 `2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23..ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998`
的全部提交与改动逐项审计：

| 提交 | 范围 | 审计结论 |
|---|---|---|
| `434522a939f3f67c2aacbbe072ba1ed45fb056b7` | 新增 F06 G5 完成报告 | docs-only，允许 |
| `ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998` | recovery conflict mutation identity 与回归测试 | 小型 G5 recovery 修复，允许 |

`ffd1aa5b` 使 Plan/Build recovery conflict mutation identity 绑定 workspace update payload
fingerprint，并兼容旧 `recovery-conflict:{runId}` receipt，避免同 run 不同 payload 复用 mutation
identity。改动只涉及既有 recovery reconciliation/projector 与 `BuildFromPlanEntryParityTests`，没有新增
第二 authority 或扩大 F06 范围。

审计确认没有：

- 默认入口切换或 `StudioUiEnabled` 默认值变更；
- Legacy AI 退役；
- F07 Settings、模型管理或资源类型扩展；
- 第二 Canvas、Project Save、HTTP、EventBus、HostBridge 或持久化 authority；
- 自动保存、自动运行、自动部署或未解释的大范围重构。

开始时 `HEAD`、`origin/studio-ui-next` 与 live remote 均为 `ffd1aa5b...`，ahead/behind 为 `0/0`，
工作树 clean。该审计后的 HEAD 冻结为 G6 Initial SHA，并继续作为 Final Code SHA。

## 3. 本地工程门禁

| 门禁 | 结果 |
|---|---|
| StudioUI `npm ci` | PASS；保留既有 `6 high` npm audit 债务，未执行 `audit fix` |
| StudioUI lint | PASS |
| StudioUI typecheck | PASS |
| StudioUI full unit | PASS，106 files / 625 tests |
| StudioUI production build | PASS，397 modules |
| bundle reproducibility | PASS |
| bundle gate | PASS |
| Product AI targeted | PASS，1288/1288 |
| Product services regression | PASS，514/514 |
| Phase42 regression | PASS，143/143 |
| Product full（排除 `Category=PerformanceBudget`） | PASS，3853 passed / 2 existing skipped / 0 failed |
| Desktop endpoints | PASS，364/364 |
| AgentRun endpoint + AI/StudioUI architecture guards | PASS，109/109 |
| Desktop full | PASS，694/694 |
| diff hygiene | PASS |

Product 本地全量明确排除 performance budget，避免把本机波动或受保护 report 重写当作 Final Gate。
同一 Final Code SHA 的 clean Remote `Product Tests` 最终执行主集 `3853 passed / 2 skipped`，并执行
performance `10/10 PASS`，由 Coverage Summary 和 Final Gate 接受。

首次 Product AI 命令因外层短超时遗留的 testhost 暂时锁住 DLL，未进入用例；泄漏进程退出后，
正式重跑 `1288/1288 PASS`。失败尝试未写成产品失败或被冒充为通过。

本地证据根目录：

`.tmp/studio-ui-next/f06-g6/ffd1aa5b/`

终局统计为 112 个文件，包括 31 JSON、17 PNG、6 TRX 与 Remote 日志/清单。

## 4. Repository Browser

F06 Repository Browser 使用 `studio-ui-next` 隔离 server、独立端口、单 worker 与 list reporter，未创建或
覆盖 `playwright-report/`：

```text
f06-ai-workbench.spec.ts
f06-g4-handoff.spec.ts
f06-g5-history.spec.ts
29/29 PASS
```

覆盖 Intent、Plan、Clarification、Build、参数、Camera 资源、Validation、Handoff、Workspace staged
draft 与显式 Save；同时覆盖 cancellation、response loss、unknown outcome、replay/SSE、history/recovery、
删除阻断/reconcile、role/flag/401、chunk failure、20 次 owner 生命周期与零资源泄漏。

Browser 正式截图/JSON 绑定 `sourceSha=ffd1aa5b...`，数据源为 deterministic fixture：

```text
MODEL_MODE=RULE_FALLBACK
DATA_SOURCE=DETERMINISTIC_BROWSER_FIXTURE
```

Chromium DPR 只作为 Browser 分层证据，不用于替代 Windows DPI。

## 5. 真实 ASP.NET Core rule-fallback 黄金旅程

Debug、Release 与发布 EXE 各完成一次真实 F06 WebView2 黄金旅程。主要证据：

- `.tmp/studio-ui-next/f06-g6/ffd1aa5b/webview2/debug-100/evidence/`；
- `.tmp/studio-ui-next/f06-g6/ffd1aa5b/webview2/release-100/evidence/`；
- `.tmp/studio-ui-next/f06-g6/ffd1aa5b/publish-no-node/evidence/`。

三次正式运行均为：

```text
status=PASS
sourceSha=ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998
MODEL_MODE=RULE_FALLBACK
REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
DATA_SOURCE=REAL_ASPNETCORE_WEBVIEW2_WITH_HANDOFF_AND_RESPONSE_LOSS_FAULT_INJECTION
```

真实 authority trace 证明：

- Camera binding 写入并从 `/api/ai/resource-candidates/camera-bindings` 回读；
- Plan 经 clarification 到 ready；
- 第一次 Build 取消并保留 cancelled terminal reservation；
- 第二次 Build 注入 response loss，通过 durable operation identity reconcile，未重复创建；
- replay 与 SSE 均由真实 endpoint 提供；
- Build ready 前 Project write 为零；
- handoff create/read/consume/acknowledge 各沿用既有 authority；
- new Workspace 保持 staged draft，用户显式 Save 后仅一次 Project POST + 一次 Project PUT；
- `authorityViolations=[]`，console/page/request/http failure 均为 0，水平溢出为 0；
- AI owner 在导航到 Workspace 后卸载，未出现第二 Canvas 或 Save owner。

## 6. WebView2 Debug / Release 与 Windows DPI

### 6.1 Debug / Release 100%

| 配置 | 业务范围 | native DPI | 结果 |
|---|---|---:|---|
| Debug | 完整 F06 黄金旅程 | 96 | PASS |
| Release | 完整 F06 黄金旅程 | 96 | PASS；Release build 0 warning / 0 error |

两者窗口均为 1920×1080，真实 WinForms + WebView2，`PerMonitorV2=true`，证据内记录 native window、
JS DPR、CDP viewport、截图像素、Desktop 进程树与 clean shutdown。

### 6.2 Windows 100% / 125% / 150%

Windows 缩放通过 DisplayConfig relative scale 切换并回读，不使用 Chromium DPR 冒充：

| Windows 档位 | DisplayConfig relative | Debug native DPI / JS DPR | Release native DPI / JS DPR | 结果 |
|---|---:|---:|---:|---|
| 100% | 0 | 96 / 1.0 | 96 / 1.0 | PASS |
| 125% | 1 | 120 / 1.25 | 120 / 1.25 | PASS |
| 150% | 2 | 144 / 1.5 | 144 / 1.5 | PASS |

125% 与 150% 使用现有只读 `/overview` product-page smoke 读取 native window DPI、JS DPR、截图、
runtime errors 与 cleanup；F06 业务黄金旅程由同 SHA 的 100% Debug/Release/publish 三次真实运行负责。
通用 DPI transport 仍使用其权威 `EvidencePhase=f04` 分支，run name、目录与 `sourceSha` 明确属于本次
F06 G6；该标签只选择旧 transport 合同，不复用旧 SHA 结论。

通用 transport 的原始 JSON 均为 `status=pass`、零 runtime error、native DPI 与 JS DPR 一致、cleanup
通过；外层 PowerShell 因 Node 成功后 `$LASTEXITCODE` 为空产生一次已知 false negative。最终结论来自
对原始 JSON、startup log、进程/端口清理的逐字段 fail-closed 回读，外层误判没有被删除。

保留的不合格 125% 尝试：

- 完整黄金旅程第二次 Build 进入 `session-conflict`；
- seeded Workspace `DpiOnly` 命中 dirty 前置断言；
- 通用 smoke 直接 `/ai` 命中历史 page-owner 白名单，`EvidencePhase=f06` `/overview` 命中历史导航清单。

这些尝试不计 PASS，也不影响随后使用受支持只读 transport 得到的 native DPI 证据。全部测试结束后
DisplayConfig 已从 relative `2` 恢复到初始 relative `0`，恢复后再次查询为 `CurrentRelative=0`。

## 7. Release publish 与 no-Node

正式发布：Release、`win-x64`、self-contained，目录：

`.tmp/publish-check/studio-ui-next-f06/ffd1aa5b/publish/`

发布 EXE：

```text
SHA256=9edb47ad7004222c0d85fdb313c187f36cf708643492ce3cd900bc32a20fc617
SIZE=311672919 bytes
```

发布结果：

| 门禁 | 结果 |
|---|---|
| Release self-contained publish | PASS |
| legacy/studio index、Studio assets、Vite manifest | PASS |
| index 与 manifest 资源路径存在 | PASS |
| source map / Vite dev signature | 0 |
| forbidden package/source/Node/browser-test artifact | 0 |
| 发布目录 EXE 完整 F06 黄金旅程 | PASS |
| Desktop sanitized PATH | PASS |
| Desktop 子进程树 Node 数 | 0 |
| 外部 CDP driver | FACT_RECORDED；绝对 Node 路径，明确不在 Desktop 进程树 |
| 进程、端口、DB、WebView2 user-data 清理 | PASS |
| local isolated no-Node target | PASS |
| 另一台物理 clean machine（未安装 Node） | NOT_PERFORMED |

F06 场景进程字段位于 `runtime.native`，旧 no-Node scanner 读取 `nativeRuntime`。本轮没有修改 harness，
而是生成带 raw evidence/cleanup 路径的只读 schema projection，再由现有 scanner 复核；最终
`publishStaticScan=PASS`、`desktopChildProcessAudit=PASS`、`sanitizedPathDesktopStartup=PASS`、
`localNoNodeEvidence=PASS`。

该证据证明发布 Desktop 目标不依赖 Node 启动或运行；外部 Node 只是 CDP 取证驱动。它不等同于在另一台
完全未安装 Node 的物理机器上验收，因此后者保持 `NOT_PERFORMED`。

## 8. Remote CI 与 Final Gate

```text
RUN_ID=30618432357
EVENT=workflow_dispatch
HEAD_SHA=ffd1aa5be9d41c3cc3d17171a5afa9b18dfd0998
STATUS=completed
CONCLUSION=success
```

| Job | Job ID | 结论 |
|---|---:|---|
| Guard & Operator Catalog | 91116986736 | success |
| StudioUI Quality Gates | 91117809018 | success |
| Product Tests | 91117809033 | success |
| Desktop Tests | 91117809013 | success |
| Detection / Measurement / Data | 91117808969 | success |
| OperatorLibrary Package & Benchmark | 91117809022 | success |
| Contracts & Vision Agent | 91117808946 | success |
| Legacy UI & StudioUI Browser | 91117809026 | success |
| Coverage Summary | 91124553026 | success |
| Operator Industrial Gate | 91117809053 | success |
| Final Gate | 91124704081 | success |

workflow_dispatch 条件下 `Code Quality`、`Release Build`、`Create Release` 按设计 skipped。Final Gate 日志
明确输出九个 required jobs 全部 `success`、Industrial Gate 为预期 `success`，并以：

`All required and applicable CI jobs completed successfully.`

结束。Remote Product 主集为 `3853 passed / 2 skipped / 0 failed`，performance budget 为 `10/10 PASS`。

## 9. 真实模型、硬件与生产验收

```text
MODEL_MODE=RULE_FALLBACK
REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
REAL_LLM_SCENARIO_EVIDENCE=NOT_RUN
REAL_CAMERA_PLC_STATION=NOT_RUN
CLEAN_MACHINE_WITHOUT_NODE=NOT_PERFORMED
```

CI 中的 deterministic fixture、rule-fallback、quality suite 与 shadow-eval sample 不构成真实 LLM 产品质量
验收。F06 黄金旅程中的 Camera binding 是隔离 authority metadata，不是现场真实相机；未连接真实 PLC 或
Station。以上缺口不阻断 F06 工程完成，但继续阻断生产验收。

## 10. 受保护文件与终局卫生

任务开始与结束均核对：

- `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json` SHA-256 保持
  `0dd6fd8313538eb2b2bc817ce0ac5324e8f1e8d7243405234d818442ac462470`；
- 15 个 `packages.lock.json` 哈希保持不变；
- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/` 开始与结束均不存在；
- 未修改 package lock、Playwright report、默认配置或既有 dirty 文件；
- Browser 使用 list reporter，未生成默认 HTML report；
- publish 只写入 `.tmp/publish-check/`，证据只写入 `.tmp/studio-ui-next/`；
- Legacy 工作树未进入、未修改、未清理；
- Windows display scale 已恢复初始 100%；
- 最终产品工作树在新增本报告前为 clean。

本地 Product 测试曾重写 5 份 tracked benchmark/quality report。因为任务开始时工作树 clean，可确认是本轮
测试生成；终局已精确恢复这 5 个文件到 Final Code SHA，没有覆盖用户改动，也未纳入提交。

## 11. 最终准入结论

F06 的框架迁移与 AI 工程工作台工程范围已完成；视觉精修按用户决定延期。F06 DONE 不授权默认入口切换、
Legacy AI 退役、F07、真实模型产品质量或生产现场验收。

```text
F06_STATE=DONE
F06_G6_STATE=DONE
F06_FRONTEND_FRAMEWORK_MIGRATION=COMPLETE
F06_AI_WORKBENCH_ENGINEERING=COMPLETE
F06_AI_VISUAL_POLISH=DEFERRED
F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
F07_IMPLEMENTATION=FORBIDDEN
PRODUCTION_ACCEPTANCE=BLOCKED
```

完成后停止，不进入默认入口切换、Legacy AI 退役、F07 或额外视觉工作。
