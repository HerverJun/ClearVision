# F06 G5 历史、恢复与 AI 专项视觉收口报告

## 1. SHA 与范围

```text
INITIAL_SHA=4cd8cd97fcc053cbb3ce4012776cb31395f80662
G4_PRODUCT_SHA=fc97b46cc32022e4b92294e65bc327c39e93aa5a
IMPLEMENTATION_SHA=2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23
FINAL_PRODUCT_SHA=2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23
REMOTE_SHA=2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23
BROWSER_SOURCE_SHA=2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23
REMOTE_CI_RUN=30603908251
```

G5 产品代码由单一提交 `2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23`
（`feat(studio-ui): close F06 G5 AI history and diagnostics`）冻结。Implementation、Final Product、
Remote CI 与正式 Browser 证据均绑定同一 SHA。承载本报告的后续 docs-only 提交不是产品证据 SHA。

本阶段只完成 Session/Run 历史、恢复与删除、公开诊断、AI 工作台视觉与可访问性收口、生命周期门禁和
AI lazy chunk 预算冻结。未进入 G6，未切换默认入口，未退役 Legacy AI，也未修改 G4 artifact、Workspace
或 Project Save authority。

## 2. Session / Run 历史合同

- `GET /api/ai/sessions` 与 `GET /api/ai/agent-runs` 均从 authenticated owner 解析身份，只返回当前 owner；
  跨 owner 的详情查询继续使用不可枚举语义，Operator 返回 `403`，匿名返回 `401`。
- 两个列表都支持 `offset`、`limit`，默认 `limit=25`，服务端限制为 `1..100`。Run 列表另支持
  `sessionId` 过滤；运行摘要按 `UpdatedAt`、`CreatedAt` 倒序稳定投影。
- Session 摘要只包含恢复所需的 lifecycle、Project 绑定、revision 与更新时间。Run 摘要只包含
  Plan/Build/unknown 类型、公开状态、title、summary、first-fix、recovery state、序列和事件计数。
- 当前会话可直接继续；历史 Session 通过匹配 route 恢复后继续。Run 页可按当前 Session 过滤，并保持
  Session 与 Run 两套分页状态互不覆盖。
- Run 公开文本必须同时满足 `MetadataOnly && RedactionPass`，之后仍经 `AgentRunEventRedactor`；未通过时
  返回固定公开占位，不透出原始文本。前端 strict decoder 拒绝未声明字段。
- `sessionId`、`runId` 只用于恢复、过滤和关联合同，历史与诊断 UI 默认不渲染内部 ID。
- 前端只保存当前页、选择态和请求 generation 等可丢弃 UI 投影。Session 恢复始终重新读取服务端
  canonical Snapshot；没有使用 `localStorage`、`sessionStorage` 或前端缓存充当 authority。

## 3. 恢复、切换与安全删除

- 工程绑定 Session 只能导航到 `/projects/:projectId/ai?sessionId=...` 恢复；独立 Session 导航到
  `/ai?sessionId=...`。恢复后的状态以 `GET /api/ai/sessions/{sessionId}` 返回的 canonical Snapshot 为准。
- Session 或 route 切换前先 dispose 当前 AI owner；request、SSE stream、timer、subscription 归零后才允许
  导航。Session 与 Run 分页分别使用 generation，dispose 或换代后的迟到响应不能发布到新 owner。
- 删除要求 `expectedRevision + clientMutationId`，并以 owner、`session_delete`、mutation identity 与 payload
  fingerprint 预留回执。同 identity 可重放，不创建第二条删除写入。
- 以下任一条件存在时返回公开 `409` 并 fail closed：待确认 operation、active Plan/Build、available 或
  consuming artifact，以及 consumed artifact 对应的 Workspace staged draft。
- 删除成功只删除当前 owner 的 Session；测试明确断言不会发出 Project DELETE，也不会级联删除 Project。
- 删除响应丢失时，前端先按原 mutation identity 查询 `session_delete` operation，再读取 canonical Session
  判断删除是否完成；reconcile 始终复用原 identity，禁止盲目重复 DELETE。

公开删除阻断码为：

```text
session_active_operation_conflict
session_active_run_conflict
session_active_artifact_conflict
session_staged_draft_conflict
```

## 4. 公开诊断与 Redaction

公开诊断抽屉默认关闭，并与历史抽屉互斥打开。可展示字段被限制为：阶段 timeline、public error code、
recovery 状态、Session revision、Project baseline、Plan、Build、artifact 摘要、blocker、warning 与
first-fix recommendation。

模板、contract、decoder、endpoint 与 Browser sentinel 检查共同禁止 reasoning、chain-of-thought、system
prompt、token/usage、密钥、owner hash、内部异常与 stack、绝对路径、IP、PLC 地址、raw attachment、
raw payload 和私有 tool payload。诊断模板不使用 `v-html`，默认不显示 Session/Run/Plan/Build/artifact
内部 ID。正式 32 份 JSON 证据的 `sensitiveLeaks` 总数为 0。

## 5. 视觉与可访问性收口

- 保持 Quiet Precision 和工作台式布局，仅使用现有 design tokens 与 StudioUI 组件边界；未引入第二套
  tokens、Legacy CSS 或新的视觉体系。
- Intent、Plan、Clarification、Build、参数、资源、Validation、Handoff、Workspace staged draft、Save，
  以及 empty、loading、offline/service unavailable、expired、forbidden、failed、cancelled、unknown outcome
  和 recovery 状态统一说明当前阶段、阻断与下一步；每阶段最多一个填充型主操作。
- 历史与诊断使用同一 drawer shell；减少嵌套卡片和重复说明，内部 ID 默认隐藏。`1366x768` compact 下
  主状态、阻断和主操作保持可见，长中文、英文和公开错误文本可换行且无文档或抽屉水平溢出。
- Drawer 使用 `role=dialog`、`aria-modal`、标题/描述关联和可访问名称；打开后聚焦关闭按钮，Tab/Shift+Tab
  焦点陷阱、Escape 关闭、关闭后恢复触发器焦点。触发器维护 `aria-expanded`。
- `prefers-reduced-motion: reduce` Browser 场景确认抽屉最大 transition 不超过 1 ms。颜色继续使用既有已审计
  语义 token；没有另起色板。最终主观对比度与视觉节奏仍等待用户截图确认。

## 6. 生命周期

- Unit 覆盖 20 次 create/dispose，以及 Build replay、SSE、请求和 owner 资源回收；每次 dispose 后
  `requestCount=streamCount=timerCount=subscriptionCount=0`，`disposed=true`。
- Browser 覆盖 20 次 `/about -> /ai -> history -> diagnostics -> /about` mount/unmount 循环，每次 AI route
  只有一个 mounted owner，离开后 mounted AI write authority owner 数为 0。
- 工程绑定 Session 恢复后再切换独立 Session，覆盖 `/projects/:id/ai -> /ai`；连续 Session 切换和迟到分页
  响应均不污染新 Snapshot。
- logout、受保护请求 `401` 会在返回登录页前释放 owner；AI chunk load failure 不挂载 owner，并进入公开
  资源恢复状态。History/diagnostics 抽屉本身不创建第二 EventBus、HTTP client、SSE 或写 authority。

## 7. AI Lazy Chunk 预算

最终 production build 实测并冻结：

| 项目 | 实测 | 冻结上限 |
|---|---:|---:|
| AI route JS | 179,907 B | 包含于 AI synchronous closure |
| AI route CSS | 45,600 B | 包含于 AI synchronous closure |
| AI synchronous closure | 962,257 B | 970,000 B |
| Shell synchronous closure | 833,639 B | 850,000 B |
| Shell hard initial max | 963,630 B | 963,630 B（未放宽） |

`/ai` 与 `/projects/:id/ai` 继续引用同一个 `AiWorkbenchPage.vue` lazy import，因此共用同一 AI route chunk。
Architecture guard 同时确认 Shell eager closure 与 AI lazy closure没有引入 Legacy AI、模型 SDK、
FlowCanvas/ImageCanvas 或 Canvas 内核。`hardInitialMaxBytes=963630` 保持不变。

## 8. Browser 证据

正式证据目录：

`.tmp/studio-ui-next/f06-g5/implementation-2ce5d53f/browser/`

共有 32 PNG + 32 JSON；全部 JSON 的 `sourceSha` 均为 Implementation SHA，数据源为确定性 Browser fixture，
`MODEL_MODE=RULE_FALLBACK`，不代表真实模型质量验证。矩阵覆盖 `1920x1080`、`1366x768`，compact、
comfortable，以及 Chromium DPR `1`、`1.25`、`1.5`、`2`。

自动检查结果：文档水平溢出 0、嵌套 drawer 水平溢出 0、敏感字段泄漏 0、console error 0、page error 0。
F06 Browser 全套为 `29/29 PASS`。空 Session/Run 历史在 default-closed 场景同一测试中打开抽屉并断言；
长历史、Session/Run 独立分页、恢复、切换、删除阻断与 reconcile 均有 Browser 断言。

Plan -> Build -> Handoff -> Workspace staged draft -> explicit Save 沿用并回归 G4 唯一 authority 路径：existing
Project 只产生一次 Project PUT；new Project 显式保存时才产生 Project POST，随后一次 Project PUT。

### 待用户确认的关键截图索引

| 索引 | 场景 | PNG |
|---:|---|---|
| 01 | Intent 空态，1920 comfortable | `ai-idle-unbound-1920x1080-comfortable-dpr-1.png` |
| 02 | Clarification 长中文，1366 compact | `ai-project-clarifying-long-cn-1366x768-compact-dpr-1.png` |
| 03 | Plan ready 与主操作 | `ai-plan-ready-unbound-1920x1080-comfortable-dpr-1.png` |
| 04 | 参数、资源、Validation | `ai-build-parameters-pending-1920x1080-comfortable-dpr-1.png`、`ai-build-resources-pending-1920x1080-comfortable-dpr-1.png` |
| 05 | Apply Preview / Handoff gate | `ai-build-ready-readonly-gate-1920x1080-comfortable-dpr-1.png` |
| 06 | Existing / new Workspace staged draft | `g4-existing-staged-unsaved-1920x1080-comfortable-dpr-1.png`、`g4-new-staged-unsaved-1366x768-compact-dpr-1.png` |
| 07 | 长历史与分页，1366 compact | `g5-history-long-paged-1366x768-compact-dpr-1.png` |
| 08 | 工程 Session 恢复与独立 Session 切换 | `g5-session-restored-project-1366x768-compact-dpr-1.png`、`g5-session-switched-unbound-1366x768-compact-dpr-1.png` |
| 09 | 删除阻断 | `g5-session-delete-blocked-1366x768-compact-dpr-1.png` |
| 10 | 公开诊断打开并脱敏 | `g5-diagnostics-open-redacted-1920x1080-comfortable-dpr-1.png` |
| 11 | reduced motion 与 1366 主操作可见 | `g5-compact-reduced-motion-1366x768-compact-dpr-1.png` |
| 12 | service unavailable / expired / Operator / flag-off | `ai-service-unavailable-1366x768-comfortable-dpr-1.png`、`g5-ai-session-expired-1366x768-compact-dpr-1.png`、`g5-ai-operator-forbidden-1366x768-compact-dpr-1.png`、`g5-ai-flag-off-forbidden-1366x768-compact-dpr-1.png` |
| 13 | DPR 1 / 1.25 / 1.5 / 2 | `g5-dpr-1-1366x768-compact-dpr-1.png`、`g5-dpr-1.25-1366x768-compact-dpr-1_25.png`、`g5-dpr-1.5-1366x768-compact-dpr-1_5.png`、`g5-dpr-2-1366x768-compact-dpr-2.png` |

Chromium DPR 不等于真实 Windows DPI。Debug WebView2 125% 视觉检查为 `NOT_PERFORMED`；Release、完整 DPI
矩阵与 publish/no-Node 属于 G6，本阶段未执行。Codex 应用 Browser 的 `UNAVAILABLE_TOOL_ENVIRONMENT`
不影响仓库 Playwright、正式 SHA 绑定证据或 Remote Browser gate。

## 9. 本地工程门禁

| 门禁 | 结果 |
|---|---|
| G5 targeted StudioUI unit | PASS，11 files / 61 tests；其中 G5 architecture guard 3/3 |
| Clean StudioUI full unit | PASS，106 files / 625 tests |
| StudioUI lint | PASS |
| StudioUI typecheck | PASS |
| Production build | PASS |
| Bundle reproducibility / verify / gate | PASS |
| AgentRun endpoint class | PASS，75/75；覆盖 owner、角色、分页、恢复、删除阻断、reconcile 与 redaction |
| Desktop endpoint regression | PASS，364/364 |
| Product services regression | PASS，514/514 |
| Phase42 regression | PASS，143/143 |
| Clean Desktop full | PASS，694/694 |
| F06 Repository Browser | PASS，29/29 |

本地 Product full 按串行约束多次执行，但分别停在不同的既有 performance-budget 抖动点，因此不记为本地
full PASS。每个涉及的分组/测试随后独立通过；没有据此伪报本地全量成功。最终权威使用同一
Implementation SHA 的干净 Remote `Product Tests`，其 coverage 与 Product performance budget 均成功，
并被 `Final Gate` 接受。

## 10. Remote CI 与 Final Gate

Remote CI：[ClearVision CI/CD run 30603908251](https://github.com/HerverJun/ClearVision/actions/runs/30603908251)

```text
event=workflow_dispatch
headSha=2ce5d53f55d6c8f73b70961223e2cb2f1a0d6c23
status=completed
conclusion=success
Product Tests job=91072666441 success
Coverage Summary job=91076699679 success
Final Gate job=91076799284 success
```

Final Gate 所需的 `Guard & Operator Catalog`、`StudioUI Quality Gates`、`Product Tests`、`Desktop Tests`、
`Detection / Measurement / Data`、`OperatorLibrary Package & Benchmark`、`Contracts & Vision Agent`、
`Legacy UI & StudioUI Browser` 与 `Coverage Summary` 全部成功；本次额外的 `Operator Industrial Gate` 也成功。
`Code Quality`、`Release Build`、`Create Release` 按 workflow_dispatch/非 Release 条件预期 skipped，不属于失败。

## 11. 受保护文件

以下既有 dirty/untracked 内容未纳入 Implementation 提交，也不得纳入 docs-only 收口提交：

- `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`；
- 8 个既有 `packages.lock.json`；
- `ClearVision.Product/test_results/` 下 10 个既有 benchmark/performance/quality report；
- 未跟踪的 `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/`。

产品实现未修改受保护 `appsettings.json`、lockfile 或 Playwright report。测试过程中被本地 Product full
重写的受保护报告已恢复到任务既有状态，并保持未暂存、未提交。

## 12. 边界与停止状态

G4 artifact、Workspace staged draft、Project POST/PUT、Workspace persistence 与 `ProjectSaveCoordinator`
仍是唯一权威链路。G5 没有新增第二 Project save endpoint、第二 HTTP 基础设施、第二 EventBus、第二 Canvas
内核或第二 HostBridge。模型管理、Settings、资源类型扩展、默认入口切换、Legacy AI 退役、F07 与 G6
均未进入。

工程自动化门禁已经完成；下一步仅等待用户依据第 8 节截图索引完成视觉确认。在确认前禁止进入 G6。

```text
F06_G5_ENGINEERING_STATE=DONE
F06_SESSION_RUN_HISTORY=COMPLETE
F06_RECOVERY_DELETE=COMPLETE
F06_AI_VISUAL_AUTOMATED_GATE=PASS
F06_AI_VISUAL_CONFIRMATION=AWAITING_USER
F06_G6_ENTRY=BLOCKED_PENDING_USER_VISUAL_CONFIRMATION
F06_G6_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
```
