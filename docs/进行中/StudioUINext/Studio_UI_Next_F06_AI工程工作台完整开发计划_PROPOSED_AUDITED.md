# Studio UI Next F06 AI 工程工作台完整开发计划（基于双工作树代码审计）

> 状态：`G1_DONE_G2_AWAITING_REVIEW`
> 阶段：F06 G1，合同、安全身份与唯一 Owner 地基
> 审计日期：2026-07-29
> 产品定义：**工业视觉任务工作台，工作台主导，会话辅助；不迁移旧 AI 页面结构。**
> 当前授权：G1 本地门禁、Remote CI 与 Final Gate 已通过；G2 仅进入复审等待，未获实现授权。默认入口变更和 Legacy AI 退役均未授权。

G1 当前实施证据见 [F06 G1 阶段报告](./F06_G1_AI合同安全身份与唯一Owner地基.md)、[G1 安全合同 ADR](./ADR-F06-G1-AI合同安全身份与唯一Owner.md) 与 [Handoff Artifact ADR](./ADR-F06-G1-Workspace-Handoff-Artifact.md)。B1-B5 已由本地合同/测试关闭；B6 仅批准 ADR，产品实现延期到 G4。

## 0. 审计基线、工作树保护与证据限制

### 0.1 Git 基线

| 项目 | Next 工作树 | Legacy 参考工作树 |
|---|---|---|
| 路径 | `C:\Users\HerverJun\Desktop\ClearVision-UI-Next` | `C:\Users\HerverJun\Desktop\ClearVision` |
| 分支 | `studio-ui-next` | `codex初稿` |
| Initial HEAD | `76c057b046d9f65973b76acc21194e9061f8630e` | `bea404394ac8cf403cca719c1990c426414a06c2` |
| tracking | `origin/studio-ui-next` | `origin/codex初稿` |
| ahead / behind | `0 / 0` | `0 / 0` |
| remote | `https://github.com/HerverJun/ClearVision.git` | 同左 |
| 使用方式 | 本轮只修改获准文档 | 全程严格只读 |

两个分支的 merge base 为 `e1bad492fecb6dff2c0a8f848db9ebfa18acf093`。代码审计以两个工作树当前代码为准；旧文档、旧 Goal、过去的测试数量和历史 PASS 只作线索。

### 0.2 Next 用户已有改动保护清单

以下 SHA-256 在审计开始时记录。本轮不得修改、清理、暂存或提交这些文件：

| 文件 | Initial SHA-256 |
|---|---|
| `ClearVision.Product/src/ClearVision.PlcComm/packages.lock.json` | `49b36391930d1a13d6da39c7aac480500de23f5d44dde7ae76c222a3bfc207f6` |
| `ClearVision.Product/src/ClearVision.Product.Application/packages.lock.json` | `7448aa32ad19c3398738b435d11e23068dc484c3f42a8e798157e1be7c9fc861` |
| `ClearVision.Product/src/ClearVision.Product.Contracts/packages.lock.json` | `837b83c78b6d4b45fce71a24d0a5ac40740ee2408c4723533d9938d650c8f249` |
| `ClearVision.Product/src/ClearVision.Product.Core/packages.lock.json` | `0a1bb3e3a0dacab38ee160419946680e75c08f03234ede850c0947386e985aa6` |
| `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json` | `1651b55c6a0b738bdd39cb93832b89e8f72036e5b1f29abb1909d8f9dff72bc4` |
| `ClearVision.Product/src/ClearVision.Product.Desktop/packages.lock.json` | `95bff3c80fe2e7bb41eabeb65511dfbbde85f0352079ada8c3640125b8240040` |
| `ClearVision.Product/src/ClearVision.Product.Infrastructure/packages.lock.json` | `c8be3cd408ef693b342957394c9cea23b3fe51d1661c59febca56cbee14e280d` |
| `ClearVision.Product/src/ClearVision.Product.Runtime.Abstractions/packages.lock.json` | `8eebe9ada399887f24ba89cd2774c6aa9405fd33237bf85f4f55ff1a458fa0cd` |
| `ClearVision.Product/src/ClearVision.Product.Runtime/packages.lock.json` | `02b1be0e73ac498787490957692c73b83bb596b552367bf0cf52c946e9d51c2a` |
| `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/index.html` | `295c5fa598ba083319b0d60d6ace13837063c2eff118229f9c72914e5ed69c1f` |

### 0.3 Legacy 用户已有改动保护清单

Legacy 工作树存在 12 个 modified 与 3 个 untracked 文件，均属于用户的 Preview / Observation 在研工作；AI 目录本身无 dirty 文件。本轮只读，以下 SHA-256 必须保持不变：

| 文件 | Initial SHA-256 |
|---|---|
| `Desktop/Endpoints/CalibrationDraftEndpoints.cs` | `e9a390e8170b4d15d26ddaa7ee1603aa3f6b9f3a7381012470f8897706e2c145` |
| `Desktop/Endpoints/PreviewNodeEndpoints.cs` | `a90d42fc7496ed2e5c435031659089b09b9db95472ad2a114de61031f1207c3c` |
| `Desktop/Observation/ExecutionObservationEnvelopeV1.cs` | `58d256ea37a5c00687976ed5f675049fb37a7173a1e43ae1d72505b95cd3ced1` |
| `Desktop/Observation/ExecutionObservationProjector.cs` | `80908ada42a47bf51987083a9e878c672460361f5519a3eb8508afc3678f68c3` |
| `Desktop/PreviewArtifacts/PreviewArtifactMaterializer.cs` | `1a641d7d9335e4675e5399c6179262729b9c986bc3d8d1fa85a3d4e83afa973a` |
| `wwwroot/src/features/flow-editor/operatorResultViewModel.mjs` | `f69a3cd97cf0323c41113a187828701888932903a07d5010c6c917e36638f162` |
| `wwwroot/src/features/flow-editor/previewCoordinator.js` | `5f7bad366816b494233e2bfa85f9b9c287ed26aebb311d952009461c0a769da6` |
| `wwwroot/src/features/flow-editor/previewOutputFormatter.mjs` | `2d3176e83ceae6da81ee9f07305daf99428705e85b8a60ff0cd5a9f384afcfe8` |
| `Desktop.Tests/ExecutionObservationProjectorTests.cs` | `975cda061c8df7db29cbfa98fb34807a79baf7ec5da46c1c07c35521f10cfb7a` |
| `Desktop.Tests/PreviewNodeEndpointsTests.cs` | `c1ed9055b90bdc98260b45736e04c11c0ca2a00782b7534d65496450cc628bd0` |
| `UI.Tests/tests/unit/preview-output-formatter.test.mjs` | `c80e639b9a761ba4bd0e4f10f5bd68e410cc4a457bbbda9d95b449fe15ae7f0b` |
| `UI.Tests/tests/unit/preview-panel-memory.test.mjs` | `26d4e51df93f53b2d3714841be36adbc76c4ed688c2e6acee2cbb0c0c45b94e7` |
| `Desktop/Observation/KnownFiniteCollectionAccessor.cs` | `1fe6fa59d6aa9600699849867242ac6a53ce182052a507556cca19691eeca40e` |
| `wwwroot/src/features/flow-editor/previewValueSemantics.mjs` | `ce607bb817473958dd6adab33e9a924ea9f2f8d194347fa3c6c791d6e84e4b51` |
| `ClearVision_视觉算子科学性与稳定性分析报告.md` | `27d0cfadbce2e94e5ee85f1ab2c6de7bf13d41a281b2a6f52cb62319ef6ebddb` |

表内 Legacy 路径省略共同前缀 `ClearVision.Product/src/ClearVision.Product.Desktop/`、`ClearVision.Product/tests/ClearVision.Product.` 等时，以实际 Git 状态路径为准；SHA 是保护权威。

### 0.4 本轮证据边界

```text
F06_G0_SOURCE_AUDIT=PERFORMED
F06_G0_LEGACY_WORKTREE_WRITE=NOT_PERFORMED
F06_G0_PRODUCT_CODE_CHANGE=NOT_PERFORMED
F06_G0_BUILD=NOT_RUN
F06_G0_TEST=NOT_RUN
F06_G0_BROWSER=NOT_RUN
F06_G0_WEBVIEW2=NOT_RUN
F06_G0_DPI=NOT_RUN
F06_G0_REAL_LLM=NOT_RUN
F06_G0_REAL_CAMERA_PLC_STATION=NOT_PERFORMED
```

F05 已有 Browser、真实 WebView2 Debug/Release、Windows 125% DPI、Release publish 与 Remote CI 证据仍由 [F05 完成报告](./F05_完成报告.md) 负责。本轮不重复运行，也不把历史证据改写为 F06 PASS。

### 0.5 文档漂移说明

F05 计划前部保留的是当时 G0 审计事实，例如曾记录 `/inspection` 尚不存在、Stations flag 未注入、路由未 lazy load。当前代码已完成 F05：

- `StudioUI/src/app/router.ts` 已有 `/inspection`、`/projects/:id/inspection`，并对主要 route 使用动态 import；
- `WebView2Host.BuildStudioUiFeatureFlags()` 已注入 `Studio2.StationsRead` 与 `Studio2.InspectionRun`；
- `StudioUI/bundle-budgets.json`、bundle report/gate 与 F05 Browser/WebView2 harness 已存在；
- README 和 F05 完成报告是当前 F05 状态入口，旧计划中的历史段落不能覆盖现行代码。

## 1. 审计结论与冻结决策

### 1.1 产品形态

F06 采用**组合式入口、单一 capability**：

1. `/ai` 是独立任务工作台，允许从现场需求开始，不要求先懂算子；未绑定工程时可完成 Intent、Plan，并可面向“新工程”空基线构建候选。
2. `/projects/:id/ai` 是同一 AI capability 的工程绑定入口；它以服务端 canonical Project 的 `PersistenceRevision` 与 flow fingerprint 为基线，适合改造既有工程。
3. Workspace 只提供“打开 AI”入口和 handoff 接收能力，**不在 Workspace 内同时挂载第二个 AI 面板或第二个 `AiSessionOwner`**。
4. AI 页面不直接持有或修改已挂载的 `FlowCanvas`。AI 产生候选；Workspace owner 校验并接收 staged draft；正式保存仍经现有 `workspacePersistenceOwner`。

这不是“独立页面或内嵌助手二选一”，而是“独立任务体验 + 工程上下文入口 + Workspace 权威交接”。

### 1.2 架构决策

- Legacy `AiPanel`、mixin、DOM、CSS 和 reducer 不复制到 Vue。
- 依据后端合同在 TypeScript 中重建严格 decoder、纯 reducer、projection 与 action model；Legacy reducer 只作为业务语义证据。
- 一个 route-scoped `AiSessionOwner` 是 Plan、Build、Clarification、Resource、History 与恢复的唯一 mounted owner。Plan/Build run controller 是内部子控制器，不是可独立挂载的顶层 owner。
- SSE 复用共享 `ApiTransport.getTextStream()`；AI stream adapter 持有 `AbortController`、last sequence、重连与 gap replay，并由 owner 统一 dispose。
- 后端 AgentRun/EventStore/terminal reservation/recovery 继续是运行权威；前端 reducer 只投影事件。
- AI Workspace Snapshot revision 只属于 AI Session；Project `PersistenceRevision` 才是正式工程保存并发身份，两者永不互换。
- Apply Preview 是候选差异说明，不是写入授权。handoff artifact 由后端签发/存储，Workspace owner 是接收候选并决定是否替换本地 draft 的唯一前端权威。
- 新 flag 固定为 `Studio2.AiWorkbench`，宿主配置为 `Studio:AiWorkbenchCapabilityEnabled=false`。不得复用 Legacy `Studio2.AiPanel` 的语义。

### 1.3 实现前硬阻断

| 编号 | 缺口 | 严重性 | 阻断范围 | 决策 |
|---|---|---|---|---|
| B1 | Session/Workspace Snapshot 无用户 owner/tenant 身份，认证用户知道 `sessionId` 即可 mutation | Critical | 阻断全部 F06 UI 接入 | G1 先建立 owner-bound session authority、迁移/隔离策略和双用户测试 |
| B2 | Session list/get/delete 只有 WebMessage，无 authenticated owner-bound HTTP DTO | Critical | 阻断历史、恢复，也阻断可靠 hydration | G1 新增既有 service 上的窄 HTTP 合同；不再让 Next 调 WebMessage |
| B3 | AgentRun endpoints 只有全局登录要求，无 Engineer/Admin 显式 policy | High | 阻断 Plan/Build/Cancel | G1 对 mutation/cancel 加 `CanEditProject` 等价策略；公开只读投影另行定义 |
| B4 | Plan/Build create 无 durable client operation identity；响应丢失可能产生 orphan run | Critical | 阻断可靠 create/reconcile | G1 增加 owner-scoped `clientOperationId` 与 operation lookup；`requestId` 不冒充幂等键 |
| B5 | Build 未绑定 Project `PersistenceRevision`、canonical flow hash 或稳定 handoff identity | Critical | 阻断既有工程 Build 与 handoff | G1 冻结 project baseline contract；G3 Build 强校验 |
| B6 | 无后端 handoff artifact / 一次性交接合同 | Critical | 阻断 G4 Apply/Handoff | G4 实现前必须批准并落地 artifact；否则 F06 停在 G3，不允许 localStorage/缓存替代 |
| B7 | AgentRun 仅 latest/by-id，无 owner-scoped run list | High | 阻断完整历史恢复 | G1 或 G5 增加分页、按 session 过滤的公开投影 |
| B8 | Session 完整载荷可能包含历史诊断/Reasoning，缺专用公开 DTO | High | 阻断 Session HTTP 暴露 | G1 定义 redacted summary/detail DTO 与角色边界 |
| B9 | Legacy fallback `/agent-plan` 的 snapshot update 未始终要求 expected revision | High | 阻断新旧并行期间一致性声明 | G1 冻结 Next 不调用 fallback，并补服务端并发策略或明确隔离 |

`B1-B5` 在 G1 退出前必须关闭。`B6` 必须在 G4 产品代码开始前关闭。任何阻断未获批准时，停止而不是由前端推断或降级。

## 2. Legacy AI 代码事实

审计对象为 Legacy 工作树 `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/`。当前有 29 个文件；主要规模如下：

| 文件 | 行数 | 真实职责 |
|---|---:|---|
| `aiPanelAgentWorkspace.js` | 7200 | Plan/Build 编排、workspace snapshot、readiness、答案/资源、run association、恢复与大量 UI 同步 |
| `aiPanel.js` | 2321 | 组合根、DOM、Canvas/session/result 大量字段、生命周期与 mixin 拼装 |
| `aiPanelPendingParameters.js` | 1868 | 待确认参数、建议值、批量处理与验证 |
| `aiPanelAgentRun.js` | 1441 | AgentRun 创建、replay、cancel 与事件归并 |
| `aiPanelLiveEvents.js` | 1382 | SSE/EventSource、断线、增量事件和 fallback |
| `aiPanelBuildPresentation.js` | 1117 | Build 进度、结果、验证与诊断展示 |
| `aiPanelResourceBinding.js` | 927 | 资源缺口、资源身份与绑定决策 |
| `aiPanelGenerateRequest.js` | 899 | 请求组装、Strict/Draft、附件和当前 Flow 上下文 |
| `agentWorkspaceState.js` | 894 | reducer、projection、plan/run stale 防护与旧字段兼容桥 |
| `aiPanelSessionHistory.js` | 858 | Session list/get/delete、恢复、导航 epoch |
| `aiPanelChat.js` | 831 | 会话消息与输入体验 |
| `aiPanelApplyPreview.js` | 770 | Flow diff、apply gate、Canvas apply/rollback |

### 2.1 入口与 mounted owner

- Legacy `app.js` 在默认路径创建 `AiPanel('ai-view', flowCanvasService, ...)`，把 `flowCanvasAdapter` 或 raw `flowCanvas` 注入 AI。
- 实验 `AiPanelCapabilityOwner` 受 `Studio2.AiPanel` **以及** `window.__CLEARVISION_ENABLE_EXPERIMENTAL_AI_PANEL_CAPABILITY === true` 双重条件控制；它只投影 latest AgentRun、SSE/replay 与 cancel，不等同完整 AI 工程工作台。
- flag on/off 时旧 owner 的 mounted/subscription 语义已被历史迁移治理，但该 capability 名称仍代表 Legacy AI Panel，不应成为 Next 新产品合同。
- `AiPanel` 通过 `Object.assign()` 聚合二十余个 mixin；会话、Plan、Build、资源、参数、Apply、历史、Canvas 和 DOM 状态集中在同一个大对象。

### 2.2 已成熟、必须保留的业务语义

| 能力 | 代码事实 | Next 处理 |
|---|---|---|
| Intent Router | `/agent-intent-router-runs` 先判断任务与意图 | 保留；成为任务理解投影，不展示内部 router trace |
| Strict / Draft | Plan request 与 readiness 决定严格或草稿策略 | 保留；用明确分段控件，不从按钮状态推断 |
| Plan / Clarification | PlanId/PlanHash、问题、推荐答案、确认答案和 readiness | 保留；重建 decoder/reducer/action |
| Build | BuildFromPlan、答案/资源 fingerprint、结果/validation/dry run | 保留；只消费 canonical public DTO |
| AgentRun | owner hash、事件流、replay、cancel、terminal | 原样复用后端权威；重写前端 adapter |
| Session Snapshot | revision、client mutation receipt、last-good 恢复 | 保留服务端语义，但先补用户 owner 与 HTTP DTO |
| Resource Decision | missing resource、resource identity、绑定决策 | 保留；将阻断项做成可处理任务，不堆成技术表格 |
| Pending Parameter | 建议值、需审核、确认后重建 | 保留；参数确认不直接写 Project |
| Workflow Diff / ApplyGate | Build terminal payload 已投影差异与应用条件 | 保留；变为 handoff 前审查，不直接操作 Canvas |
| Session History / Recovery | WebMessage list/get/delete 与 Snapshot 恢复 | 语义保留；传输改为 authenticated owner-bound HTTP |

### 2.3 不迁移的结构与行为

- `AiPanel` 大类、mixin 拼装、固定 DOM id、旧 CSS 覆盖体系。
- `agentWorkspaceState.js` 本身及其兼容属性桥；它与大量独立字段并存，不能证明单一状态树。
- 旧页面的对话中心布局、卡片模板、默认展开的大量 stage/tool trace/token/raw payload。
- WebMessage 的 Generate/Cancel 通道和 Session CRUD 通道。
- AI owner 直接持有 Canvas，调用 `deserialize()` 后在前端回滚的 apply 模式。
- 用 `localStorage` 的安全阻断、按钮 disabled、DOM 文案或缓存推断可应用/已保存状态。
- EventSource/fetch 不可用时退回旧同步 `/agent-plan` 的隐式行为。

### 2.4 Legacy 黄金旅程实际能力

当前 Legacy 能做到：描述需求与附件 → Intent/Plan → 少量或多轮 Clarification → Readiness → Build AgentRun → SSE/replay → 参数和资源补齐 → Validation/DryRun/ApplyGate → Flow 差异 → 直接把候选反序列化到当前 Canvas → 用户再走 Project 保存。会话可列出、恢复和删除；运行可取消；应用失败可尝试用本地旧 Flow 回滚。

这条旅程的**业务语义有价值**，但最后两步的 authority 不满足 Next 边界：候选没有 durable handoff identity，AI 与 Workspace 共享 Canvas，Project 基线没有用 `PersistenceRevision` + canonical flow hash 锁定，前端回滚也不能替代后端冲突判定。

### 2.5 失败、取消、断线、恢复与 stale 事实

- reducer 会按 `sessionId`、revision、`planId`、`planHash`、runId、event id/sequence 拒绝明显 stale/重复事件。
- Plan/Build terminal、cancel 与 recovery 已在后端使用 terminal reservation、terminal intent 和 workspace projection reconciliation。
- SSE 支持 `Last-Event-ID`、`lastEventId`、`afterSequence`；前端也保留 replay/fallback。
- Session 写入有 expected revision、client mutation receipt 和 conflict 结果；但旧 `/agent-plan` fallback 的部分 mutation 没有相同并发要求。
- Apply 失败时 Legacy 尝试本地 Flow rollback；这只能恢复 UI 草稿，不能证明 Project authority 未被其他入口推进。
- localStorage 中的阻断/折叠偏好可作为 UI preference，但不得被 Next 用作保存、应用或恢复权威。

### 2.6 两分支 AI 漂移

`76c057b0..bea40439` 在 AI/Agent 相关范围有 20 个文件差异，约 `1113 insertions / 761 deletions`。主要涉及：

- Legacy `aiPanelAgentWorkspace.js`；
- `ConversationalFlowService.cs`；
- Prompt/知识检索/参数映射/工具 catalog；
- Anomaly embedding；
- `AgentRunEndpointsTests.cs`。

`AgentRunEndpoints.cs` 本身在该比较中无 committed 差异。F06 不手工复制 Legacy 工作树文件；G1 开始前必须 `git fetch origin --prune`，逐提交审计 `origin/codex初稿` 的安全/合同修复，并按 Git 单向合入规则处理。无法证明必要性的算法或异常检测改动不应搭车进入 F06。

## 3. 后端 endpoint、身份与权威审计

### 3.1 当前 `/api/ai/**` 合同矩阵

| Endpoint | 当前用途 | 当前身份/权限 | F06 决策 |
|---|---|---|---|
| `POST /api/ai/agent-plan` | 同步 fallback Plan | 登录；Session 不 owner-bound；部分 snapshot mutation 不带 expected revision | Next 禁用；保留 Legacy 兼容，G1 补并发/隔离后再决定长期去留 |
| `POST /api/ai/sessions/{sessionId}/workspace-snapshot` | AI workspace delta + expected revision + client mutation id | 仅登录，未校验 Session owner | 复用 service 语义；G1 owner-bound + `CanEditProject` + 专用 DTO |
| `POST /api/ai/agent-plan/readiness-preview` | 根据答案/资源计算 Build readiness | 仅登录 | 复用；mutation 角色为 Admin/Engineer |
| `POST /api/ai/agent-intent-router-runs` | 任务意图分类 | 仅登录，无 run identity | 复用；纳入同一 session operation 语义或定义为可重试纯计算 |
| `POST /api/ai/agent-plan-runs` | 创建异步 Plan Run | AgentRun owner hash；无 client operation identity | G1 增加 durable idempotency/reconcile |
| `POST /api/ai/agent-runs` | 创建 Build/Generate AgentRun | AgentRun owner hash；请求有 `requestId` 但不是 durable 幂等查询身份 | G1 增加 `clientOperationId`；G3 强制 project baseline/plan/build association |
| `GET /api/ai/agent-runs/latest` | 当前用户 latest replay | owner hash | 可复用为辅助恢复，不可替代 operation reconcile/history list |
| `GET /api/ai/agent-runs/{runId}` | by-id replay | owner hash，非 owner 403 | 复用，使用公开 redacted decoder |
| `GET /api/ai/agent-runs/{runId}/events` | SSE | bearer 或 45 秒 stream token；run owner 验证 | 复用；Next 优先共享 fetch stream，必要时才使用 stream token |
| `POST /api/ai/agent-runs/{runId}/stream-token` | 原生 EventSource 一次性短 token | owner hash | 作为 fallback；token 不进入日志/store/UI state |
| `POST /api/ai/agent-runs/{runId}/cancel` | terminal reservation + cancel | owner hash；无 Engineer/Admin policy | G1 加 mutation policy；terminal 幂等语义保留 |
| `GET /api/ai/models` | 模型安全列表，Admin DTO 更丰富 | 登录；非 Admin 得 safe projection | F06 只可读模型可用性；模型管理仍属 F07 Settings |
| `POST /api/ai/reasoning-support` | 模型 family/capability 解析 | 登录 | 可作为兼容诊断；不放在主旅程 |
| `POST/PUT/DELETE /api/ai/models...` | 模型创建、更新、删除、激活、默认角色、测试 | Admin 显式 policy | F06 不实现管理 UI；保持 F07 Settings scope |

当前没有 authenticated HTTP Session list/get/delete，没有 owner-scoped run list，也没有 handoff artifact endpoint。

### 3.2 已成熟的后端权威

- AgentRun owner 由当前登录用户 ID 计算 SHA-256 owner hash；replay、cancel、token 和 stream 都验证 owner。
- EventStore 为持久化 JSONL，支持 replay、压缩、终态 reservation、terminal intent、启动恢复与投影 reconciliation。
- 事件写入前经过 `AgentRunEventRedactor`，会处理 secret、authorization、path、address、private planning marker、data image 与长 base64。
- SSE 支持序列游标和 gap replay；45 秒 stream token 用于无法带 header 的原生 EventSource。
- Build terminal payload 已包含 Flow、BuildResult、Readiness、WorkflowDiff、ApplyGate、参数、资源、Validation/DryRun 与公开诊断。
- Build association 已使用 AI Workspace Snapshot revision、mutation receipt、planId/planHash、answer/build fingerprint。
- Conversation Session store 通过临时文件 + `File.Replace`/`File.Move` 原子替换，并维护 `.last-good`；支持 revision conflict 与 mutation idempotency。

### 3.3 身份边界

| 身份 | authority | 可否互换 |
|---|---|---|
| `sessionId` + AI Snapshot revision | AI 会话草稿和 Plan/Build 投影 | 不可替代 Project revision |
| `planId` + `planHash` | 已确认计划身份 | 不可单独授权 Apply |
| `runId` + build identity/fingerprint | AgentRun 与 Build terminal 身份 | 不可单独证明 Project baseline 未变 |
| Project `PersistenceRevision` | 正式保存并发身份 | 唯一 Project save concurrency identity |
| canonical flow hash | Project Flow 内容身份 | 与 `PersistenceRevision` 配合；不能用 UI revision 替代 |
| handoff artifact id/version | AI 候选交接身份 | 只代表 staged candidate，不是已保存 Project |
| Workspace local draft revision | UI 草稿/stale 防护 | 不进入后端保存并发字段 |

## 4. Studio UI Next 可复用底座

| 底座 | 当前代码事实 | F06 使用方式 | 禁止 |
|---|---|---|---|
| Product Shell / Router | `src/app/router.ts` 有 auth、role、flag、lazy route、chunk error 恢复；`navigation.ts` 有角色/flag 过滤 | 注册两个 lazy route，扩展 safe `returnTo` | 第二 router、隐藏 DOM 代替 unmount |
| Startup Flag | `StudioOptions` → `WebView2Host.BuildStudioUiFeatureFlags()` → immutable StartupConfig | 新增独立默认 false 的 `Studio2.AiWorkbench` | 复用/重定义 `Studio2.AiPanel` |
| Auth / Session | `authLifecycleOwner`、401 recovery、route guard | AI owner 响应 session loss，停止 stream/request/mutation | 在组件中私建 token 管理 |
| `ApiTransport` | bearer、401 hook、JSON/blob、`getTextStream()`、AbortSignal | 所有 AI HTTP/SSE 的唯一 transport | 第二 fetch wrapper、raw EventSource 常驻 owner |
| Strict Decoder | Workspace/Inspection contracts 已用显式 decoder 拒绝坏载荷 | 新建 AI capability-local decoder 与 versioned DTO | `as any`、静默吞字段、DOM 解析 |
| Owner / Projection | Workspace、Inspection、Stations 有 reactive readonly projection + command + dispose | `AiSessionOwner` + pure reducer + action model | Pinia 成为业务 authority；Vue 持有命令式资源 |
| SSE 生命周期 | `inspection-run/sseAdapter.ts` + `inspectionRunOwner.ts` 已有 sequence、AbortController、reconnect、resource ledger | 复用模式，不复制业务类型 | 第二 EventBus、无界重连、dispose 后写状态 |
| Leave Guard | app-level leave owner 与 workspace bridge | 未提交 clarification、running Plan/Build、未完成 handoff 进入明确 guard | 用浏览器默认弹窗作为唯一策略 |
| Workspace save | `workspacePersistenceOwner` → `PUT /api/projects/{id}`，带 `expectedPersistenceRevision` | handoff 后用户显式保存 | AI 新建 save endpoint/client，直接写 Project |
| FlowCanvas host | `flowCanvasOwner.replaceFlow()` 包装 canonical canvas，严格 dispose | 仅 Workspace 接收 staged candidate 后调用；AI 不 import/持有 | AI 页面直接 `deserialize()` 或 mounted 双 owner |
| Canonical Project | `workspaceContracts.ts` decode/encode，Project DTO 有 `persistenceRevision`，persistence fingerprint 可复用 | 扩展独立 canonical flow hash/基线 DTO 时与现有实现一致 | 把 AI snapshot revision 当 `PersistenceRevision` |
| Bundle gate | route lazy loading、manifest report、`bundle-budgets.json`、CI fail-closed | 新增 `ai` target，先测后冻结预算 | 把大模型/旧 AI 代码打入 shell eager chunk |
| Browser/WebView2 | F05 fixture、Playwright、WebView2 harness、DPI/publish/no-Node 脚本 | 扩展 AI 专项场景，隔离端口/用户数据/DB | 用 Chromium fixture 冒充真实 WebView2/模型 |
| Design System | Quiet Precision tokens、primitives、patterns、icons | 复用 CvButton/Panel/Alert/Status/PageHeader/Modal 等 | 复制 Legacy CSS，页面私造第二套 tokens |

## 5. 产品信息架构与交互模型

### 5.1 页面结构

AI 工作台用任务状态组织，而不是按技术模块堆 tab：

| 区域 | 默认职责 | 主次 |
|---|---|---|
| 任务栏 | 当前任务标题、绑定工程、Strict/Draft、会话状态、返回工程 | 最高；保持紧凑 |
| 当前阶段 | AI 理解、当前阻断、唯一主操作 | 页面主区 |
| 方案与验证 | Plan 摘要、Build 结果、Validation、ApplyGate、差异 | 随阶段切换的工作区 |
| 会话轨迹 | 用户输入、关键 AI 结论、clarification 答案 | 辅助，不占据主视觉中心 |
| 上下文面板 | 工程基线、资源、待确认参数、历史 | 需要时展开 |
| 诊断抽屉 | stage、tool trace、event sequence、公开 failure metadata | 默认关闭，仅 Engineer/Admin 排障 |

不把“对话、方案、构建、资源、历史、工程详情”全部做成同权 tab。阶段主区一次只承担一个决定；会话和历史是上下文，不抢主操作。

### 5.2 状态组织

| 产品状态 | 默认可见 | 唯一主操作 | 进入条件 |
|---|---|---|---|
| 空闲 | 任务描述、工程绑定状态、少量示例入口 | `理解任务` | owner hydrated，无 active run |
| 理解中 | 正在识别目标/约束，允许取消 | `取消` | Intent operation 已确认创建 |
| 规划中 | 当前阶段和已知输入 | `取消规划` | Plan Run identity 已确认 |
| 待澄清 | 1 组少而关键的问题、推荐答案与影响 | `确认并继续` | canonical clarification batch |
| 方案就绪 | 任务理解、推荐方案、readiness、差异基线 | `构建并验证` | planId/hash 已确认且 readiness 允许 |
| 构建中 | 公开阶段、已完成检查、可取消 | `取消构建` | Build Run identity 已确认 |
| 待补料 | 阻断项按“资源/参数/工程基线”分组并可定位 | `重新验证` 或 `继续构建` | Build/Readiness 返回可处理 blocker |
| 待交接 | Build 结果、Validation、ApplyGate、工程差异 | `交接到工作区` | terminal succeeded + handoff eligible |
| 已交接 | 目标工程、artifact、Workspace 状态 | `打开工作区` | Workspace 接收 staged draft 成功 |
| 冲突/未知结果 | 服务端最新状态、禁止重复提交原因 | `核对状态` | network loss、revision/hash mismatch、sequence gap |

### 5.3 默认可见与渐进披露

默认显示：AI 对任务的结构化理解、工程绑定、关键假设、当前阻断、推荐方案摘要、验证结论、差异和下一步。详情抽屉显示：完整算子清单、参数映射、resource identity、validation 细项、event timeline。诊断视图才显示：runId、sequence、projection disposition、公开 tool trace、fallback 原因、redacted payload。

永不显示：system prompt、chain-of-thought、secret/token/API key、未经脱敏的路径/IP/PLC 地址、raw attachment data、后端未标记为 public 的异常。

### 5.4 核心旅程

**新工程旅程**：

```text
/ai 描述任务
→ Intent / Plan / Clarification
→ 以“新工程空基线”构建并验证候选
→ 创建新工程或选择目标
→ 若目标不是空基线，必须重新绑定基线并 Build
→ 创建 handoff artifact
→ Workspace 接收 staged draft
→ 用户审核并显式 Save
→ 保存响应刷新 PersistenceRevision
→ Formal Run / 连续检测验证
```

**既有工程旅程**：

```text
/projects/:id/ai
→ 加载服务端 canonical Project baseline
→ Intent / Plan / Clarification
→ Build 绑定 projectId + PersistenceRevision + canonical flow hash
→ Apply Preview
→ handoff artifact
→ /projects/:id/workspace 校验 artifact 与当前 server/local draft
→ 接收 staged draft
→ Save → ProjectSaveCoordinator
→ Formal Run
```

若 Workspace 有未保存 draft，进入 AI 前由现有 Leave Guard 要求先保存、放弃或留在 Workspace。F06 不跨 route 读取活跃 Canvas 内存。

## 6. 保留 / 重构 / 删除 / 延期矩阵

| 能力 | 裁决 | F06 说明 |
|---|---|---|
| Intent Router、Plan、Strict/Draft、Clarification | 保留并重构前端 | G2 核心 |
| Readiness、Resource Decision、Pending Parameter | 保留并重构前端 | G2/G3 核心 |
| AgentRun/SSE/Replay/Cancel/Terminal | 保留后端，重写 adapter | 不改运行 authority |
| Session Workspace Snapshot | 保留 service 语义，补安全/HTTP | G1 硬阻断 |
| Build Result/Validation/DryRun/ApplyGate | 保留并重新编排 | G3/G4 |
| Flow diff 与 Workspace 交接 | 保留用户价值，重建 authority | 必须 backend artifact + Workspace owner |
| Session History/Recovery | 保留并迁移到 owner-bound HTTP | G5 |
| Legacy reducer | 删除迁移意图 | 只作语义样本，TypeScript 重建 |
| Legacy AiPanel/mixin/DOM/CSS | 删除迁移意图 | 不进入 StudioUI bundle |
| WebMessage Generate/Cancel/Session CRUD | Legacy 兼容，Next 禁用 | 不新增 bridge |
| Raw tool trace/token/payload 主视图 | 删除 | 仅保留 redacted diagnostics |
| 模型 CRUD/测试/默认模型管理 | 延期 F07 Settings | F06 可读 safe availability |
| 多模型切换、shadow eval 治理 UI | 延期 | 不属于任务黄金旅程 |
| Runtime Preview pilot、部署、Station | 延期/复用其他域 | AI 不成为 Runtime/Station owner |
| 自动保存、自动运行、自动部署 | 明确不做 | 必须用户审核、Workspace Save、正式 Run |
| Operator 只读 AI 历史 | 延期 | 等独立 `CanReadAiHistory` 产品合同 |

## 7. Owner、State、Projection 与 Action 架构

### 7.1 唯一 Owner 树

```text
AiWorkbench route runtime (one mounted instance)
└─ AiSessionOwner
   ├─ session snapshot controller
   ├─ intent operation controller
   ├─ plan run controller
   ├─ build run controller
   ├─ AgentRunStreamAdapter (at most one active stream)
   ├─ handoff controller (G4 only)
   └─ readonly projection + action model
```

Plan/Build controller 只是 `AiSessionOwner` 内部互斥资源。任何时刻至多一个 active operation、一个 stream、一个 request controller 集合；route 参数或 session 变化必须 dispose 旧 owner 后再创建新 owner。

### 7.2 State 分层

| 层 | 内容 | 可持久化位置 |
|---|---|---|
| Canonical server references | owner-bound session revision、plan/run/artifact id、Project revision/hash | 后端 |
| Reducer state | 已 decode 的事件、operation outcome、clarification、readiness、build/handoff projection | route owner 内存，可从后端重建 |
| UI draft | 尚未提交的任务描述、问题选择、面板展开、筛选 | Vue state；必要时只保存非敏感 preference |
| Derived projection | phase、primary action、blockers、progress、diagnostics availability | 纯函数，不持久化 |
| Imperative resources | AbortController、stream reader、timer、unsubscribe | adapter/owner 私有，Vue/Pinia 不持有 |

不得建立 capability-global Pinia authority。若使用 Pinia，只能存壳层可丢弃 UI preference，不得存 Session/Plan/Build/Project authority。

### 7.3 Reducer stale 规则

事件进入 reducer 前必须通过 strict decoder。reducer 至少核对：

- capability generation / navigation epoch；
- authenticated user/session generation；
- sessionId 与 owner 当前 session；
- operation kind、clientOperationId、runId；
- planId + planHash；
- monotonically increasing sequence；
- terminal sequence/status；
- AI workspace revision；
- projectId + baseline persistence revision + canonical flow hash；
- build/handoff identity。

不匹配事件进入 diagnostics counter，不改变用户可见 canonical projection。

### 7.4 Action Model

Vue 组件只能调用窄 action：`describeTask`、`routeIntent`、`startPlan`、`answerClarification`、`previewReadiness`、`startBuild`、`confirmParameter`、`resolveResource`、`cancelActiveRun`、`reconcile`、`prepareHandoff`、`openWorkspace`、`switchSession`、`deleteSession`。按钮 enable/disable 来自 projection，组件不自行拼 endpoint 或判断 authority。

## 8. SSE、回放、重连与 unknown outcome

1. 创建 operation 前生成 durable `clientOperationId`，保留到服务端明确返回或 operation lookup 证明结果。
2. create 返回 runId 后先 replay，再从最后 sequence 建流，避免“响应和订阅之间”丢事件。
3. `getTextStream()` 为首选；仅在平台证据证明需要原生 EventSource 时请求一次性 stream token。
4. 每个事件按 runId + sequence 去重；发现 gap 时暂停应用后续事件，GET replay 补齐后再继续。
5. 断线采用有界退避；owner dispose、logout、route change、terminal 时立即 abort 并清 timer。
6. terminal 以后忽略非 terminal/更低 sequence 事件；冲突 terminal 进入 reconcile/diagnostics，前端不挑选“更喜欢”的状态。
7. create/cancel/snapshot/handoff 响应丢失时标记 `unknown-outcome`，禁止立即重复 mutation；用 operation lookup、run replay、session GET 或 artifact GET 核对。
8. 401 触发现有 auth lifecycle；AI owner freeze mutation、dispose stream，重新认证后用身份集合 reconcile，不恢复旧 AbortController。

## 9. AI → Workspace handoff 合同

### 9.1 推荐合同

新增后端 owner-bound、短生命周期、可审计的 `AiWorkspaceHandoffArtifactV1`。它是**候选交接工件**，不是 Project、Runtime Package 或保存结果。最少字段：

```text
artifactId / schemaVersion / expiresAt / consumedState
ownerHash
sessionId / sessionRevision
projectId / targetKind(new|existing)
planId / planHash
buildRunId / buildIdentity / submittedBuildFingerprint
baselinePersistenceRevision
baselineCanonicalFlowHash
candidateCanonicalFlowHash
candidateFlow (public canonical DTO or protected artifact reference)
workflowDiff / validationSummary / applyGate
createdAt / single-use-or-versioned-consumption receipt
```

附件、raw prompt、Reasoning、tool secret、绝对路径不进入 artifact。若候选依赖正式 Project assets，artifact 只引用经过现有 asset authority 验证的身份，不内嵌前端私有资产。

### 9.2 创建条件

- 当前用户是 Admin/Engineer，且拥有 Session/Run；
- Build terminal 为成功且 `ApplyGate` 允许；
- sessionId、planId/hash、buildRunId/build identity 全部匹配；
- existing Project 的 `PersistenceRevision` 与 canonical flow hash 仍等于 Build baseline；
- new Project 目标必须是刚创建且仍为空基线；若选择非空既有工程，必须重新绑定并 Build；
- artifact create 本身使用 `clientOperationId`，响应丢失可 lookup。

### 9.3 Workspace 接收流程

1. 路由到 `/projects/:id/workspace?handoff=<artifactId>`；`returnTo` 只携带安全内部路径。
2. Workspace runtime 先加载 canonical Project，再由**Workspace owner 的 handoff port**获取 artifact。
3. 核对 owner、projectId、过期状态、baseline revision/hash、candidate hash、ApplyGate。
4. 若 Workspace 当前 local draft 与 server baseline 不同，禁止覆盖；用户只能保留本地草稿并取消 handoff，或先保存/放弃后重新接收。
5. 校验通过后，Workspace owner 将 candidate 作为 staged local draft 交给现有 `flowCanvasOwner.replaceFlow()`；AI owner不参与 Canvas 写入。
6. staged draft 标记 dirty，但不自动保存。用户审核后显式 Save，走 `workspacePersistenceOwner → PUT /api/projects/{id} → ProjectService → ProjectSaveCoordinator`。
7. Save 成功返回新的 `PersistenceRevision` 后，artifact receipt 可标为 accepted/saved；失败或 409 保持 Project authority 不变。

### 9.4 冲突裁决

| 场景 | 处理 |
|---|---|
| Project revision/hash 已变化 | 拒绝接收，回 AI 基于新 baseline 重建 |
| Workspace 有本地 dirty draft | 不覆盖；先处理 Leave Guard/本地草稿 |
| artifact 过期/已消费 | GET artifact 返回明确状态；重新生成，不从缓存恢复 |
| candidate decoder 失败 | fail closed，报告 contract error |
| Save 409 | 使用现有 persistence reconcile；不重新 apply artifact |
| Save 响应丢失 | 现有 workspace unknown-outcome/reconcile；不让 AI判断 |
| AI Session 删除 | 已签发 artifact 的保留/撤销策略由后端合同明确，不由前端猜测 |

最终权威分层：AgentRun 决定 Build 结果；handoff service 决定 artifact 身份；Workspace owner 决定本地 staged draft 接收；ProjectSaveCoordinator 决定正式保存；Runtime/Inspection 决定正式运行与结果。

## 10. Route / Role / Flag / returnTo 矩阵

| Route / action | Admin | Engineer | Operator | Flag | Owner |
|---|---:|---:|---:|---|---|
| `/ai` | 允许 | 允许 | F06 禁止 | `Studio2.AiWorkbench` | `AiSessionOwner` |
| `/projects/:id/ai` | 允许 | 允许 | F06 禁止 | 同上 | 同一个 capability owner |
| Intent/Plan/Build/Cancel | 允许 | 允许 | 禁止 | 同上 | owner command → HTTP |
| Session list/get/delete | owner 范围允许 | owner 范围允许 | 禁止 | 同上 | session controller |
| Handoff create/consume | 允许 | 允许 | 禁止 | AI + Workspace flags 均满足 | handoff service + Workspace owner |
| AI model safe list | 允许 | 允许 | 不在 F06 route 暴露 | 无新增 | query only |
| AI model CRUD/test | Admin，F07 Settings | 禁止 | 禁止 | Settings flag | 不属于 F06 |
| 未来只读历史 | 待新 policy | 待新 policy | 待产品批准 | 待定 | 只读 projection owner |

G1 新增：

- `StudioOptions.AiWorkbenchCapabilityEnabled = false`；
- Startup flag `Studio2.AiWorkbench`；
- router meta `allowedRoles: ['Admin','Engineer']` + `requiredFeatureFlag`；
- product navigation 仅在角色和 flag 同时满足时显示；
- `resolveSafeReturnRoute()` 接受 `/ai` 与 `/projects/:id/ai`，继续拒绝 scheme、`//`、反斜杠、编码 slash 与 `..`；
- `/projects/:id/ai` 的 `id` 用现有 Project id decoder；不依赖“当前工程”内存。

现有 `Studio2.AiPanel` 保持原义、默认与 Legacy 回滚用途。禁止双写两个 flag，禁止让一个 flag 同时挂载两种 owner。

## 11. G1 目标合同矩阵

具体 URI 可在 ADR 复审中微调，但语义必须冻结：

| 合同 | 最小语义 | unknown outcome / 安全 |
|---|---|---|
| `POST /api/ai/sessions` | owner-bound session create，clientOperationId | durable lookup 或幂等响应 |
| `GET /api/ai/sessions` | owner-scoped paged summaries | 只返回 redacted summary |
| `GET /api/ai/sessions/{id}` | owner-scoped public detail + snapshot | 非 owner 404/403 策略统一，不泄漏存在性 |
| `DELETE /api/ai/sessions/{id}` | owner-scoped delete，禁止 active unsafe delete | operation identity + receipt |
| `POST .../workspace-snapshot` | owner + expected AI revision + mutation id | 409 返回 canonical latest public snapshot |
| `POST /agent-plan-runs` | required clientOperationId | owner-scoped operation lookup |
| `POST /agent-runs` | required clientOperationId + project baseline when bound | 响应丢失不得产生不可发现 orphan |
| `GET /api/ai/operations/{clientOperationId}` | plan/build/handoff create reconcile | owner scoped，最小公开状态 |
| `GET /api/ai/agent-runs` | owner-scoped paged history，可按 session | redacted public replay summary |
| `POST /api/ai/handoffs` | G4：从 eligible Build 创建 artifact | required clientOperationId |
| `GET /api/ai/handoffs/{id}` | Workspace 获取并校验 artifact | owner + target project policy |
| `POST /api/ai/handoffs/{id}/accept` | 可选 receipt，不保存 Project | 幂等 receipt；不能绕过 Save |

如果评审决定复用现有 Project operation coordinator 的通用模式，应复用其语义/实现边界；不得复制出第二个全局 operation framework。若现有抽象不适合 AI，新增 capability-local store 必须有 ADR、owner scope、retention、recovery 和冲突测试。

## 12. 安全、隐私与审计边界

- 所有 mutation、cancel、handoff 对 Admin/Engineer 应用显式 endpoint policy；仅靠“route 不显示”不算授权。
- Session/operation/run/artifact 都以不可伪造的 authenticated user identity 绑定；projectId 另走现有 Project edit policy。
- DTO 分 `PublicSummary`、`PublicDetail`、内部存储模型；禁止直接序列化 `ConversationSession` 给 Next。
- 保留后端 redactor，并为 Session/Handoff DTO 增加同等级泄漏测试。
- API key 继续只在模型配置 authority 中存在；F06 不显示、缓存、记录或透传。
- stream token 只在连接瞬间存在 adapter 局部变量，不进入 reducer、URL history、telemetry 或 error 文案。
- 任务描述和会话历史可能含产线/产品敏感信息；日志只记录 id、状态、耗时和 redacted error，不记录全文。
- DELETE session 的 retention、active run、artifact 影响必须后端定义；前端确认框不构成数据治理。
- diagnostics 只展示后端明确公开字段；“Engineer/Admin 可看”不等于可展示 chain-of-thought。

## 13. 视觉与交互原则

F06 包含**AI 页面专项视觉重构**，不包含全产品视觉重构。视觉定稿边界是：完成任务工作台的信息层级、关键状态、交互密度、空/错/加载/恢复态，以及 1920×1080、Windows 125% 和窄视口适配；不提前锁死大量像素、颜色或组件内部 CSS。

- 继续 Quiet Precision：克制、可靠、高信息密度、简体中文优先。
- H1/页面标题保持工作区尺度，不做营销 hero；首屏直接进入任务工作台。
- 同一阶段一个主按钮；取消、查看详情、返回等为次操作。
- 阻断项显示“位置、原因、可处理动作”，不只显示红色错误码。
- 任务理解与验证结论优先于聊天气泡；聊天记录不做全高主列。
- Strict/Draft 使用清楚的 segmented control；状态用已有 badge/inline alert；图标按钮复用 CvIcon 并有 tooltip。
- 资源、参数、历史使用适合扫描的列表/表格/抽屉，不堆套娃 cards。
- stage timeline 只展示用户能理解的 4-6 个阶段；内部 tool call 不进入默认 timeline。
- motion 只用于状态过渡与进度反馈，尊重 reduced motion；running 状态不得用动画造成布局位移。
- 文案不向用户解释页面设计、键盘或组件样式；操作语义直接体现在 label 和状态中。

## 14. 七个串行 Goal

F06 固定为 7 个串行 Goal，不再拆 G0.1，也不把共享 authority 并行分给多个 owner。

### G0｜一次性双工作树审计与完整计划（本轮）

- **输入基线**：Next `76c057b0`；Legacy `bea40439`；两工作树 dirty SHA 清单。
- **目标**：跟踪 Legacy AI、后端 `/api/ai/**`、Next platform/Workspace/证据链，冻结本文。
- **允许修改**：本文、`docs/进行中/StudioUINext/README.md` 入口、必要审计附录。
- **禁止**：任何产品源代码、route、合同、flag、默认入口、Legacy 工作树、测试产物。
- **证据**：Git/dirty 基线、代码引用、文档链接/状态验证；build/test/browser 均 NOT RUN。
- **退出门禁**：本文提交只含获准文档并推送；状态保持 `PROPOSED_AUDITED`。
- **下一 Goal 授权**：产品负责人批准产品形态、B1-B9 处置、G1 文件范围；否则停止。

### G1｜合同、权限、flag、route 与唯一 Owner 地基

- **输入基线**：获批 G0 SHA；先 fetch 并审计 `origin/codex初稿` AI 相关 committed drift。
- **实现目标**：

- 关闭 B1-B5：owner-bound Session、公开 DTO、显式角色策略、durable operation identity/reconcile、Project baseline contract；
- 冻结 B6 handoff ADR，不提前实现 Canvas apply；
- 新增默认 false 的 `Studio2.AiWorkbench` 真值链；
- 注册两个 lazy route、role/flag/returnTo/navigation；
- 建立 capability-local strict contracts、API adapter、pure reducer、`AiSessionOwner` 骨架与 resource ledger；
- Next 明确禁止调用 WebMessage 与同步 `/agent-plan` fallback。

- **允许修改范围**：

- 后端：`Desktop/Endpoints/AgentRunEndpoints.cs`、`EndpointPermissionGuards.cs`（仅需新增明确 policy 时）、必要的 AI Session/operation endpoint 文件；`Infrastructure/AI/ConversationalFlowService.cs`、AgentRun store/service 的最小扩展；相关 Core DTO/Services；对应 Desktop/Product tests；
- Host/shared（主协调代理独占）：`Configuration/StudioOptions.cs`、`WebView2Host.cs`、`appsettings*.json` 中受控默认值、`StudioUI/src/app/router.ts`、`routerMeta.ts`、`navigation.ts`、`ProductLayout.vue`、startup contracts、架构守卫；
- capability：新建 `StudioUI/src/capabilities/ai-workbench/` 的 contracts/api/reducer/owner/runtime/index 与最小页面；
- 文档：G1 ADR、合同矩阵与阶段报告。

- **禁止**：实现完整 Plan/Build 页面；handoff/Canvas 写入；第二 HTTP/EventBus/operation framework；修改 Project save；复用 `Studio2.AiPanel`；默认入口切换。
- **测试与证据**：双用户 Session/Run 隔离；Admin/Engineer/Operator 双向授权；operation 响应丢失与 retry；revision conflict；bad DTO fail closed；route/flag/returnTo；20-cycle owner dispose；Debug WebView2 flag smoke。所有 `.csproj` 测试串行。
- **退出门禁**：B1-B5 全关闭；B6 ADR 批准；flag off 零 mounted owner/stream/request；G1 Remote CI 通过或明确不进入 G2。
- **G2 授权**：合同和 owner skeleton 通过复审，无 unresolved security blocker。

### G2｜任务入口、Intent、Plan 与 Clarification

- **输入基线**：G1 Final SHA 与 owner-bound Session/operation 合同。
- **实现目标**：`/ai` 与 `/projects/:id/ai` 任务入口、工程绑定、Intent、Strict/Draft、Plan Run、少量关键 Clarification、readiness preview、cancel/replay/reconcile。
- **允许修改范围**：`capabilities/ai-workbench/**` 的 Plan/Clarification 叶子组件、owner action/reducer/decoder、capability-local tests；必要的 endpoint DTO bugfix 只能回到 G1 owner 复审。
- **禁止**：Build、resource binding、handoff、Canvas、Project save、模型管理、Legacy CSS。
- **测试与证据**：空闲/理解/规划/澄清/方案就绪/失败/取消/401/断线/响应丢失；session/project/run stale；Browser 两 route 深链与角色/flag；真实 endpoint deterministic rule-fallback。
- **退出门禁**：用户始终能看到“理解了什么、卡在哪里、下一步是什么”；一次仅一个 active Plan；dispose ledger 归零；不得把 rule-fallback 证据称为真实模型质量。
- **G3 授权**：Plan identity/readiness 在重开和断线后可恢复，且没有 fallback transport。

### G3｜Build、Validation、参数与资源

- **输入基线**：G2 可恢复的 canonical Plan；existing/new Project baseline 合同。
- **实现目标**：Build Run、公开阶段、Validation/DryRun、参数建议/确认、资源缺口/决策、重新 readiness/build、ApplyGate projection。
- **允许修改范围**：`ai-workbench/**` Build/resource/parameter 组件与 owner 内部 controller；必要的 Build public DTO/decoder 与 endpoint tests；不修改 Project persistence。
- **禁止**：直接 apply、handoff 伪实现、自动保存/运行、资源私有持久化、Runtime/Station 控制。
- **测试与证据**：planHash/revision/project baseline mismatch；Build create unknown outcome；cancel/terminal race；sequence gap；pending parameter/resource identity；invalid/public redacted payload；20-cycle stream lifecycle；Browser Build 状态矩阵。
- **退出门禁**：Build 只接受当前 Plan 与 baseline；terminal 可 replay 恢复；所有 blocker 可定位/处理；B6 未关闭则在这里停止。
- **G4 授权**：handoff ADR 与后端 artifact implementation scope 获明确批准。

### G4｜Apply Preview 与 Workspace handoff

- **输入基线**：G3 eligible Build；获批 `AiWorkspaceHandoffArtifactV1`；现有 Workspace persistence/Canvas owner。
- **实现目标**：Apply Preview、artifact create/lookup、Workspace 接收 port、baseline/local draft 冲突、staged candidate、显式 Save 链。
- **允许修改范围**：AI handoff endpoint/service/store/DTO/tests；`ai-workbench/**` handoff；`project-workspace/**` 中 capability-local handoff port/owner integration/页面提示；shared router 仅传安全 id；架构守卫。
- **禁止**：AI import `FlowCanvas`；AI 直接调用 `replaceFlow()`；新增 Project save endpoint；自动 Save/Run；把 artifact 当 Project/Runtime Package；修改 Runtime/Station。
- **测试与证据**：owner/project/plan/build/revision/hash/candidate fingerprint 全矩阵；artifact expiry/consume/retry；dirty Workspace 不覆盖；Save 409/unknown outcome 走现有 reconcile；AI 与 Workspace owner 不同时 mounted 写同一 capability；Browser 完整 AI→Workspace→Save 旅程；真实 WebView2 Debug 冒烟。
- **退出门禁**：正式保存 trace 只出现一次既有 Project PUT 并进入 `ProjectSaveCoordinator`；Canvas 只有 Workspace owner；artifact 不含敏感字段。
- **G5 授权**：handoff lifecycle/authority 证据通过。

### G5｜历史、恢复与 AI 专项视觉收口

- **输入基线**：G4 黄金旅程。
- **实现目标**：Session history、paged run history、恢复/删除、诊断抽屉、全部状态视觉、可访问性、1920×1080/125% 与窄视口收口。
- **允许修改范围**：`ai-workbench/**`、owner-bound history DTO/query、Design System 缺失 primitive 的最小扩展（主协调代理独占）、capability-local styles/tests、bundle budget target。
- **禁止**：模型管理、全产品视觉重构、公开 raw reasoning、改变 G4 authority、为美化引入第二 tokens。
- **测试与证据**：历史分页/删除 active session；重开/崩溃/last-good/recovery；redaction；keyboard/focus/contrast/reduced-motion；超长中文/英文/error；DPR 1/1.25/1.5/2 browser screenshot；route lazy/bundle gate。
- **退出门禁**：视觉验收只覆盖 AI 专项；所有状态无重叠/截断；diagnostics 默认关闭；resource ledger 归零；bundle 未超冻结预算。
- **G6 授权**：产品负责人通过 AI 页面专项视觉确认。

### G6｜Final Evidence、Remote CI 与准入判断

- **输入基线**：G1-G5 Final code SHA，不允许 evidence 后再修改产品代码而沿用旧结论。
- **实现目标**：全量本地门禁、隔离 Browser、真实 WebView2 Debug/Release、DPI、Release publish/no-Node、真实 endpoint、真实模型分层证据、Remote CI 与 Final Gate、完成报告。
- **允许修改范围**：测试/harness/evidence 脚本、CI、F06 报告和精确 test guard 修复；任何产品修复后重新生成全部受影响证据。
- **禁止**：默认入口变更、Legacy AI 退役、F07 Settings、把现场/真实模型 NOT RUN 写成 PASS。
- **测试与证据**：见第 15 节。
- **退出门禁**：Final code SHA 的 required Remote CI/Final Gate 成功；所有证据绑定相同 SHA；dirty 保护清单不变；明确 `DEFAULT_ENTRY_CHANGE=BLOCKED` 与 `LEGACY_AI_RETIREMENT=NOT_APPROVED`。
- **下一阶段授权**：F06 DONE 仍不自动授权 F07、默认入口或 Legacy AI 退役。

## 15. 测试、Browser、WebView2、DPI、Bundle 与 CI 门禁

### 15.1 测试分层

| 层 | 必测内容 | 不能证明 |
|---|---|---|
| Pure unit | decoder/reducer/projection/action、stale/sequence/terminal、handoff identity | HTTP/Auth/真实 DOM |
| Owner integration | request/stream/timer/dispose、unknown outcome、401 reconcile、20-cycle | 真实后端持久化 |
| Desktop endpoint | 双用户 owner、角色、idempotency、revision、artifact、redaction/recovery | WebView2/DPI |
| Product service | Conversation/AgentRun/EventStore/ProjectSaveCoordinator 不回归 | 前端 UX |
| Browser fixture | 所有产品状态、route/role/flag、视觉、handoff UI、a11y | 真实 WebView2/真实 LLM |
| Local real endpoint | 实际 ASP.NET Core、持久化、SSE/replay、rule-fallback | 模型质量 |
| Real WebView2 | Host startup injection、auth、route、stream、handoff、窗口/DPI | 现场硬件 |
| Real LLM | 受控任务集的 Plan/Build 质量、模型失败与 latency | deterministic 回归稳定性 |
| Remote CI | clean runner 的构建/测试/架构/bundle/browser | 本机 WebView2/DPI/硬件 |

### 15.2 Deterministic 与真实模型分离

- CI 与大多数 Browser 使用 deterministic fixtures；固定 event/DTO，不访问外部模型。
- 本地真实 endpoint 至少跑 rule-fallback，证明合同与持久化；报告明确 `MODEL_MODE=RULE_FALLBACK`。
- 真实 LLM 使用版本化工业场景集、固定模型配置快照、明确网络/成本/隐私批准；分别记录成功率、阻断正确性、幻觉/无效算子、参数合理性、超时与 redaction。
- 真实 LLM 失败不通过改 fixture 掩盖；fixture PASS 也不等于模型验收。
- 现有 P1 AI WebView2 脚本的 rule-fallback 只可作为旧后端证据，不直接成为 F06 UI/模型 PASS。

### 15.3 最低证据清单

```text
STUDIO_UI_LINT
STUDIO_UI_TYPECHECK
STUDIO_UI_UNIT
STUDIO_UI_PRODUCTION_BUILD
BUNDLE_VERIFY
BUNDLE_GATE
PRODUCT_AI_TARGETED_TESTS
DESKTOP_AI_ENDPOINT_TESTS
DESKTOP_ARCHITECTURE_GUARDS
BROWSER_F06_AI
REAL_ENDPOINT_RULE_FALLBACK
REAL_WEBVIEW2_DEBUG
REAL_WEBVIEW2_RELEASE
WINDOWS_DPI_100_125_150
RELEASE_PUBLISH
INDEPENDENT_NO_NODE_TARGET
REMOTE_CI
FINAL_GATE
REAL_LLM_SCENARIO_EVIDENCE
REAL_CAMERA_PLC_STATION
```

未执行项必须为 `NOT_RUN` / `NOT_PERFORMED`，不得借另一证据替代。AI 核心旅程不依赖真实相机/PLC/Station，因此这些硬件项可作为生产验收门禁而非 F06 工程完成门禁，但必须诚实报告。

### 15.4 Bundle 门禁

- `/ai` 与 `/projects/:id/ai` 共享一个 lazy capability chunk，不复制页面实现。
- G1 先生成实际 manifest；G5 才基于测量冻结 `ai` 同步闭包预算。
- shell hard max 不放宽；AI 不把 Legacy JS/CSS、模型 SDK、Canvas 内核 eager 打入 shell。
- `bundle:verify` 两次产物规范化一致；CI `bundle:gate` fail closed。
- chunk load failure 继续进入 eager 404/retry 路径，owner 不应在 chunk 未完成时创建。

## 16. 风险、停止边界与回滚

| 风险 | 影响 | 控制/停止条件 |
|---|---|---|
| Session 跨用户访问 | 数据泄漏/越权 mutation | B1 未关闭，G1 立即停止 |
| create 响应丢失产生 orphan | 重复模型成本、状态不可恢复 | 无 operation identity 不进入 G2/G3 |
| AI/Workspace 双 Canvas owner | 覆盖草稿、生命周期泄漏 | 架构守卫 + runtime owner ledger；发现即停止 G4 |
| Project 基线过期 | 旧 AI 结果覆盖人工修改 | revision+hash 双校验，必须 rebase/rebuild |
| artifact 成为第二 Project store | authority 分裂 | retention/字段/Save trace 守卫；不得存正式 Project state |
| terminal race/stale event | UI 倒退或错误 ApplyGate | terminal reservation + reducer sequence + replay |
| diagnostics 泄密 | prompt/key/path/产线信息泄漏 | public DTO + redactor +泄漏测试；发现即 release blocker |
| Legacy fallback 并发覆盖 | 新旧并行时 Session 回退 | Next 禁用 fallback，后端补并发或 Session 隔离 |
| 大 capability 拖慢首屏 | WebView2 启动/内存回归 | lazy chunk + budget + 20-cycle memory evidence |
| 视觉又变成聊天页 | 现场用户无法定位下一步 | G2/G5 产品审查以任务阶段与唯一主操作为门禁 |
| 稳定线 AI drift 未审计 | 丢失安全/算法修复或粗暴覆盖 Next | G1 前 Git 单向同步审计，shared files 由主协调代理逐项处理 |

回滚层级：

1. 关闭 `Studio:AiWorkbenchCapabilityEnabled=false` 并重启；两个 route 403/导航隐藏，AI owner/stream/request 全部为零。
2. 保持 `Studio2.AiPanel` 不变，Legacy `/index.html` 继续回退；不通过 CSS 隐藏模拟回滚。
3. 每个 Goal 独立提交；按 Goal revert。后端安全收紧的回滚会放宽权限，必须单独批准。
4. handoff 关闭时 Workspace 不接受 artifact，但现有 Workspace 保存/运行不受影响。
5. `Studio:StudioUiEnabled=false` 仍是根入口回滚；F06 不修改其默认值。

## 17. 共享文件与并行约束

以下只能由主协调代理修改：`package.json`、lockfile、Vite、bundle budgets、Router、App Shell、navigation、Design Tokens、Startup/Host flag、API contracts、HostBridge、`.csproj`、CI、feature flags、根 `AGENTS.md`、共享 ADR/基线文档。

`AgentRunEndpoints` + `ConversationalFlowService` + operation/session contract 是一个串行 authority 工作包；`AiSessionOwner` + reducer + API adapter 是一个实现 owner；Workspace handoff + persistence/Canvas integration 是一个实现 owner。不得拆给多个并行代理后合并状态树或保存链。

允许并行的仅是无共享状态的叶子：独立展示组件、纯 decoder fixture、a11y 检查、只读视觉审计。每个子任务必须有文件白名单。所有同一 `.csproj` 测试严格串行，端口、WebView2 user-data、DB、结果和 publish 目录隔离。

## 18. F06 完成定义

只有同时满足以下条件，F06 才可标记 DONE：

- B1-B9 按阶段关闭或被明确延期且不影响已交付范围；不存在前端猜测 authority。
- 两 route 共用一个 capability，Admin/Engineer 可用，Operator fail closed，flag 默认 false。
- 新工程与既有工程的 Intent→Plan→Clarification→Build→Validation→Handoff→Workspace→Save 黄金旅程完成。
- Session/Run/operation/artifact 全部 owner-bound、可恢复、可 reconcile；unknown outcome 不会触发盲重试。
- AI 不直接持有 Canvas；Workspace staged draft 与 Project Save 仍是唯一链。
- AgentRun/EventStore/Runtime/Station authority 未被复制。
- 所有敏感载荷只通过 public redacted DTO；泄漏测试通过。
- AI 专项视觉、a11y、1920×1080、125% 与窄视口通过；无重叠/截断。
- lint/typecheck/unit/build/bundle、后端定向测试、Browser、真实 WebView2 Debug/Release、DPI、publish/no-Node、Remote CI/Final Gate 绑定同一 Final code SHA。
- 真实模型与 fixture 结果分别报告；未运行的模型/硬件证据不冒充 PASS。
- 用户原有 dirty 文件 SHA 保持不变，Legacy 工作树无本轮写入。

即使 F06 DONE，以下仍保持：默认入口未变，Legacy AI 未退役，F07 Settings 未启动，生产现场验收不自动通过。

## 19. 明确不属于 F06

- AI 模型新增/编辑/删除/测试/默认角色完整管理 UI；
- Settings、Import/Export、全产品视觉重构；
- 自动保存、自动 Formal Run、自动连续检测、自动部署到 Station；
- 新 Runtime Package 格式、RuntimeHost/Station/Inspection 状态机；
- 第二 Project save、第二 EventStore、第二 Canvas、第二 HTTP/EventBus/HostBridge；
- 重写 Prompt/算法/算子科学性作为 UI 迁移附带工作；
- Operator 角色的 AI 历史浏览，除非另有产品/安全合同批准；
- 默认入口切换、Legacy AI 删除或退役；
- 把 `FrontendV2`、旧 Studio2 Goal 或 Legacy AI UI 当新前端地基。

## 20. G1 复审与实施结论

G1 已按获批范围实施。owner-bound Session、窄 HTTP/DTO、Admin/Engineer policy、durable operation identity、existing Project baseline、独立 flag、两个 route 与 route-scoped owner 已完成本地门禁；未实现 G2 产品页或 G4 Handoff 产品代码。

进入 G2 前必须复审：

1. G1 Final/Remote SHA 的 CI 与 Final Gate；
2. B1-B5 的 owner、权限、operation identity 与 Project baseline 实现；
3. B6 `ADR_APPROVED_IMPLEMENTATION_DEFERRED_TO_G4` 边界；
4. G2 只允许 Intent/Plan/Clarification，不扩权实现 Build 或 Handoff。

Remote CI 通过后，G2 仍只进入 `AWAITING_REVIEW`；未获得新的明确授权时，G2 实现保持禁止。

## 21. 当前状态

```text
F05_STATE=DONE
F06_G0_STATE=DONE
F06_PLAN_STATE=PROPOSED_AUDITED
F06_G1_STATE=DONE
F06_B1_OWNER_BOUND_SESSION=CLOSED
F06_B2_SESSION_HTTP=CLOSED
F06_B3_MUTATION_POLICY=CLOSED
F06_B4_OPERATION_IDENTITY=CLOSED
F06_B5_PROJECT_BASELINE=CLOSED
F06_B6_HANDOFF_ADR=APPROVED_IMPLEMENTATION_DEFERRED
F06_G2_ENTRY=AWAITING_REVIEW
F06_G2_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
PRODUCTION_ACCEPTANCE=BLOCKED
```
