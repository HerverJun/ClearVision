# F06 G4 Apply Preview 与 Workspace Handoff 完成报告

## 1. SHA 与范围

```text
INITIAL_SHA=c76806ca8cf409af81a7bd7d8b2471f047abffa9
FINAL_PRODUCT_SHA=fc97b46cc32022e4b92294e65bc327c39e93aa5a
REMOTE_SHA=fc97b46cc32022e4b92294e65bc327c39e93aa5a
REMOTE_CI_RUN=30572670889
```

G4 产品范围由三个提交构成：

| 产品提交 | 职责 |
|---|---|
| `f294319b02548ffc5b2368a72a8f114403aabbeb` | 实现 Apply Preview、owner-bound Handoff endpoint/store、Workspace receive port、existing/new Project staged draft、显式保存接入及对应合同与测试。 |
| `8ee0bf6fd25f0e092336e47bee34ef0e3eef4070` | 修复 Workspace Shell 的 Canvas 几何保持，并补 F03 Workspace 回归。 |
| `fc97b46cc32022e4b92294e65bc327c39e93aa5a` | 修复 new Project staged draft 的 Preview 可读性，并补 G4 Handoff Browser 回归。 |

本 docs-only 收口提交不是产品 SHA。G4 产品、正式 WebView2 证据与 Remote CI 始终绑定
`fc97b46cc32022e4b92294e65bc327c39e93aa5a`。

## 2. Authority 结论

- Apply Preview 只消费 canonical terminal Build；Build 必须有单一成功终态，且后端 ApplyGate 同时满足
  handoff eligible、Canvas apply ready 与 runtime draft ready。
- create 时复核 authenticated owner、Session、Plan、Build、operation receipt、Session/answer/resource revision、
  new/existing Project baseline 与 candidate flow fingerprint；公开候选还必须通过 metadata-only 与 redaction 检查。
- 已实现 create、按 artifact/Build/operation lookup、consume、acknowledge、reject。create 以 owner + operation
  identity 幂等；consume/acknowledge 使用同一 operation 两阶段确认，一次性接收且同 identity 可重放。
- create 响应丢失通过 operation/Build lookup 协调；consume、acknowledge 或正式保存结果未知时保留原 identity
  reconcile，禁止盲目重复创建 artifact 或重复保存。
- AI route 在导航到 Workspace 前 dispose。AI 不持有 Canvas；Workspace 是唯一 staged draft、FlowCanvas 与写入口 owner。
- Workspace 在 reserve 和 staging 两侧检查 dirty 状态；已有未保存草稿时不覆盖。existing Project 的
  `PersistenceRevision` 或 canonical flow hash 变化会 fail closed，要求基于最新 baseline 重新 Build。
- new Project 在交接时保持 staged unsaved，不预建正式 Project，也不伪造 Project id。
- artifact store 只保存短期候选与 consume audit，不成为 Project store。consume receipt 的
  `projectSaved=false` 明确表示接收不等于保存。
- 正式保存只走既有 Project POST/PUT、Workspace persistence 与 `ProjectSaveCoordinator`；没有 AI Project save
  endpoint，没有自动保存、自动运行或自动部署。

## 3. Artifact 参数

| 参数 | 实际值 |
|---|---:|
| Artifact TTL | 30 分钟 |
| 终态审计保留 | 24 小时 |
| 单 owner active 上限 | 16 |
| 全局 active 上限 | 256 |
| 总存储上限 | 512 |
| candidate Flow payload 上限 | 2,097,152 bytes（2 MiB） |

状态机：

```text
available → consuming → consumed
available/consuming → rejected
available/consuming → expired
```

持久化由 `AiWorkspaceHandoffArtifactStore` 负责：公开、脱敏后的记录写入
`ai_workspace_handoff_artifacts.json`，默认复用 AI conversation store root，也可用
`CV_AI_HANDOFF_STORE_ROOT` 隔离。实现使用同目录临时文件、durable flush 与原子 replace/move，启动时重载；
`consuming` 可恢复，同 operation 继续协调。加载或持久化失败时 fail closed，不回退到 localStorage 或前端缓存。

## 4. New / Existing Project

### Existing Project

- artifact 固定 `projectId + PersistenceRevision + canonical flow hash`；create、lookup、consume 时均由服务端重载
  当前 Project baseline 复核。
- Workspace dirty 或 revision/hash 不一致时不 reserve、不替换 Canvas；用户必须返回 AI 基于最新工程重新 Build。
- 接收成功只形成 staged unsaved draft；用户检查后显式使用既有保存命令，正式写入走既有 Project PUT。
- PUT 的 409 与 unknown outcome 沿用 Workspace persistence 的既有 reconcile，不新增保存 identity 或 endpoint。

### New Project

- Build 和 artifact 使用 `targetKind=new`，显式保存前 `projectId=null`；交接过程不创建正式 Project。
- 用户显式保存时才调用既有 Project POST 创建身份，随后由既有 Workspace persistence 通过 Project PUT 保存
  staged flow；没有 AI 预建工程或后台自动保存。
- consume receipt 与 Project create/save receipt 分离；前者永远不证明 Project 已保存。

## 5. 测试与正式证据

本节记录 G4 产品评审采用的既有结果；docs-only 收口没有重新运行或重生成产品测试、JSON、PNG 或报告。

| 门禁 | 结果与证据 |
|---|---|
| F03 定向回归 | PASS；`StudioUiF03G5Workspace_ShouldKeepOnePersistenceOwnerAndExactProjectPutBoundary` 通过，TRX 为 `.tmp/test_results/f06-g4-desktop-architecture/f06-g4-desktop-architecture.trx`；Workspace Canvas 几何回归包含在最终仓库 Browser 验证中。 |
| F06 Repository Playwright Browser | PASS，18/18。 |
| StudioUI lint | PASS，0 warning。 |
| StudioUI typecheck | PASS。 |
| bundle gate / reproducibility | PASS。 |
| Debug Desktop build | PASS。 |
| Product full | PASS，3,862 executed / 3,862 passed，另有 2 个既有 skipped。 |
| Desktop full | PASS，689/689；使用不覆盖受保护本地配置的最终结果。 |
| Real Debug Desktop / WebView2 | PASS。 |

正式 WebView2 主证据：

`.tmp/studio-ui-next/f06-g4-webview2/fc97b46c-final/evidence/studio-ui-webview2-f06-g4-f06-g4-debug-fc97b46c-final.json`

```text
status=PASS
sourceSha=fc97b46cc32022e4b92294e65bc327c39e93aa5a
DATA_SOURCE=REAL_ASPNETCORE_WEBVIEW2_WITH_HANDOFF_AND_RESPONSE_LOSS_FAULT_INJECTION
MODEL_MODE=RULE_FALLBACK
REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
```

该 JSON 记录 1 次 handoff create、1 次 read、1 次 consume、1 次 acknowledge，以及既有
`POST /api/projects` 1 次、`PUT /api/projects/{id}` 1 次；`authorityViolations=0`。console error、page error、
request failure、HTTP failure 与 horizontal overflow 均为 0。真实窗口为 1920x1080，WebView2 client capture
为 1904x1016；这不是完整 Windows DPI 矩阵声明。

清理证据：

`.tmp/studio-ui-next/f06-g4-webview2/fc97b46c-final/evidence/studio-ui-webview2-f06-g4-debug-fc97b46c-final-cleanup.json`

`passed=true`；Desktop process 已退出、WebView2 已断开，HTTP 5095 与 CDP 9365 已释放，WebView2 user-data、
Conversation/AgentRun store、测试数据库与 runtime root 已清理，外部 Node driver 不在 Desktop process tree，
环境已恢复。

Remote CI [run 30572670889](https://github.com/HerverJun/ClearVision/actions/runs/30572670889) 的
`headSha=fc97b46cc32022e4b92294e65bc327c39e93aa5a`，run conclusion 为 `success`。Final Gate 要求的九个 job
`Guard & Operator Catalog`、`StudioUI Quality Gates`、`Product Tests`、`Desktop Tests`、
`Detection / Measurement / Data`、`OperatorLibrary Package & Benchmark`、`Contracts & Vision Agent`、
`Legacy UI & StudioUI Browser`、`Coverage Summary` 全部 `success`；`Final Gate=success`。本次 workflow_dispatch
的 `Operator Industrial Gate=success`；Release/Create Release/Code Quality 按条件为 skipped，Final Gate 按预期接受。

正式证据口径：

```text
REPOSITORY_PLAYWRIGHT_BROWSER=PASS_18_OF_18
REAL_DEBUG_WEBVIEW2=PASS
REMOTE_STUDIOUI_BROWSER=PASS
REMOTE_FINAL_GATE=PASS
CODEX_APP_BROWSER=UNAVAILABLE_TOOL_ENVIRONMENT
CODEX_APP_BROWSER_BLOCKS_G4=NO
```

## 6. 非产品工具环境说明

```text
CODEX_APP_BROWSER_STATUS=UNAVAILABLE
ERROR=failed to write kernel assets ... os error 3
CLASSIFICATION=CODEX_APPLICATION_TOOL_ENVIRONMENT
PRODUCT_BROWSER_EVIDENCE_AFFECTED=NO
REAL_WEBVIEW2_EVIDENCE_AFFECTED=NO
REMOTE_BROWSER_EVIDENCE_AFFECTED=NO
G4_BLOCKING=NO
```

Codex 应用 Browser 未通过，也未在本轮被修复、重置或重复初始化；它不是 ClearVision 产品、仓库
Playwright、真实 WebView2 或 Remote CI 门禁。

## 7. 受保护文件

以下 dirty 内容保持任务开始时原状，本轮未修改、未暂存、未提交：

- `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`；
- 8 个 NuGet lockfile：
  `ClearVision.Product/src/ClearVision.PlcComm/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Application/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Contracts/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Core/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Desktop/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Infrastructure/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Runtime.Abstractions/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Runtime/packages.lock.json`；
- 未跟踪的既有 `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/`；
- `ClearVision.Product/test_results/` 下既有 10 个 dirty benchmark/performance/quality report：
  `calibration_operator_benchmark_report.md`、`detection_performance_budget_report.json`、
  `detection_performance_budget_report.md`、`measurement_operator_benchmark_report.md`、
  `measurement_performance_budget_report.json`、`measurement_performance_budget_report.md`、
  `operator_benchmark_report.md`、`preprocessing_benchmark_report.md`、`preprocessing_quality_report.md`、
  `stage2_specialized_performance_report.md`。

## 8. 权威计划与最终状态

权威计划中的早期 `F06_G2_ENTRY` 属于对应历史 Goal 的退出记录；当前 G4 状态以 G4 完成报告和已实现 ADR
为权威。计划第 21 节是独立 live 状态，已按本报告做最小更新；G1/G2 历史复审章节不重写。

```text
F06_G4_STATE=DONE
F06_HANDOFF_ARTIFACT=COMPLETE
F06_WORKSPACE_STAGED_DRAFT=COMPLETE
F06_PROJECT_SAVE_AUTHORITY=PRESERVED
F06_AUTOMATIC_SAVE_RUN_DEPLOY=FORBIDDEN
F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
F06_G5_ENTRY=AWAITING_REVIEW
F06_G5_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
```

G4 至此收口。G5 未开始，默认入口变更与 Legacy AI 退役仍未批准。
