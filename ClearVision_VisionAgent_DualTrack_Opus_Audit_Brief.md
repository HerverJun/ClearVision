# ClearVision Vision Agent 双轨/多轨架构独立复审报告

> 用途：交给 Claude Code（建议使用 Claude Opus）在本地真实仓库中进行独立、逐链路、证据化复审。
> 本文不是要求模型照抄既有结论，而是提供**待验证假设、审计边界、证据标准和交付格式**。
> 第一阶段只做审计，不修改代码、不提交、不推送。

---

## 0. 审计任务指令

请在 ClearVision 本地真实仓库中，对 Vision Agent、旧版 AI GenerateFlow、Plan/Build、DryRun、校验、流程草稿、结果契约、事件终态和前端应用链路进行一次独立架构审计。

重点回答：

1. 除已暴露的两套 DryRun 外，是否还存在两套或多套**语义相同、实现不同、结果可能冲突**的生产方案？
2. 哪些属于合理分层，哪些属于兼容层，哪些已经形成真实双主链？
3. 哪些旧代码只是存在，哪些仍被正式 DI 注册，哪些能够从真实产品入口到达？
4. 哪些冲突会导致错误门禁、错误 UI、状态覆盖、结果漂移、画布内容与后端审计结果不一致？
5. 应如何分阶段统一，且不破坏当前 Plan → BuildFromPlan → AgentRun → projector → Session → Canvas 主链？

不要因为文件名中出现 `Legacy`、`Obsolete`、`Fallback` 就直接下结论。必须证明运行时可达性或不可达性。

---

## 1. 审计基线

已知参考基线：

- 仓库：`HerverJun/ClearVision`
- 分支：`codex初稿`
- 上一次已知远端 SHA：`79dc9d026ff7fef47841cc4afb9959a2e1a4bae8`
- 对应提交：`Productize plan strict draft readiness preview`

开始前必须执行并记录：

```powershell
git status --short --branch
git fetch origin
git rev-parse HEAD
git rev-parse origin/codex初稿
git log -8 --oneline --decorate
```

若本地或远端已不是上述 SHA，以实际 SHA 为准，并在报告开头注明差异。不得为了匹配旧基线而 reset、checkout 或覆盖用户工作区。

同时读取：

```text
AGENTS.md
README.md
docs/进行中/当前计划/ClearVision_Vision_Engineering_Agent_TODO_Final_Review.md
```

测试必须遵守 `AGENTS.md`：

- 同一 `.csproj` 不得并行启动多个 `dotnet test`；
- 优先使用 `./scripts/run-dotnet-test-serial.ps1`；
- 当前 PowerShell 中使用 `& "./scripts/run-dotnet-test-serial.ps1" ...`；
- 不得使用 `powershell.exe -File` 包裹；
- 已成功构建后，后续优先 `-NoBuild -NoRestore`。

---

## 2. 当前用户实测现象

用户已经完成以下真实手测：

1. Plan 阶段可切换 Strict / Draft；
2. Draft 可在部分需求或资源待补时进入 Build；
3. Build 页面出现：
   - `DryRun 失败`
   - `分支覆盖 0/0（0.0%）`
   - 多个工具被显示为“失败”
4. 同一页面又存在 Build contract accepted、Build readiness accepted、可编辑草稿等成功信号。

初步静态分析认为：

- Agent 的 `dryrun_flow` 返回结构级元数据预演结果；
- 旧版 `DryRunService` 返回真实执行/Stub 型结果；
- 前端按旧字段读取新结果，字段缺失后默认成失败；
- ToolTrace 与 ToolEvidence 状态字段也可能被混读。

Opus 必须重新独立验证这一判断，不能把它作为既定事实。

---

## 3. 审计判定分类

每一处候选重复方案必须归入以下一种：

### A. 冲突双轨

满足以下多数特征：

- 处理同一业务输入；
- 产出同一类业务结论；
- 规则实现不同；
- 可能同时从正式入口到达；
- 结果可能互相覆盖或不一致；
- 前端无法可靠辨别来源。

结论：需要收敛为唯一权威实现。

### B. 合理分层

例如：

- Plan 阶段是否允许 Build；
- Build 后是否允许 Apply / Runtime / Deploy。

它们名称相近，但解决不同阶段问题，不应机械合并。

结论：保留，但必须重新命名或明确契约边界。

### C. 显式兼容层

旧实现仍为历史数据、测试或旧入口服务，但：

- 有清晰入口；
- 不会被普通主链隐式调用；
- 结果有版本/类型判别；
- 不会覆盖 canonical 状态。

结论：可以暂留，但必须隔离。

### D. 注册但不可达遗留

类仍在产品 DI 中，正常产品入口没有调用链。

结论：迁出正式 DI 或删除，防止后续误用。

### E. 测试/文档专用

不参与产品运行。

结论：不视为架构冲突，但应避免文档继续描述为正式路径。

---

## 4. 必须真实追踪的入口

至少完整追踪以下调用链，不得只搜索类名：

### 4.1 普通 AI 输入

```text
AI 面板输入
→ 前端 dispatch
→ WebMessage 或 HTTP
→ GenerateFlowMessageHandler / AgentRun endpoint
→ AiFlowGenerationService
→ 旧 GenerateFlow 或 VisionAgent GenerateFlow
→ 返回/终态
→ 前端 currentResult / workbench
```

确认哪些条件决定：

- `UseVisionAgentGenerateFlow`
- `AgentGenerateFlowMode`
- `GenerateFlowMode`
- `RequirementMode`
- template strategy
- fallback

### 4.2 Plan

检查：

```text
POST /api/ai/agent-plan
POST /api/ai/agent-plan-runs
```

两者是否都是正式可达入口，是否共享同一 planner、同一事件语义、同一终态投影；同步直接返回和 AgentRun 事件返回是否会形成双结果源。

### 4.3 BuildFromPlan

追踪：

```text
前端 _startBuildFromCurrentPlan
→ _dispatchGenerateRequest
→ GenerateFlowMessageHandler 或 /api/ai/agent-runs
→ IVisionAgentBuildRunService
→ IVisionAgentBuildApplicationService
→ IVisionAgentBuildOrchestrator
→ AgentRun terminal
→ IVisionAgentBuildTerminalProjector
→ ConversationSession
→ 前端 Apply
```

确认 Build 是否确实只有一个业务决策入口和一个终态事实源。

### 4.4 前端终态

检查同一 Build 是否可能同时被以下来源应用：

- `run.completed` SSE；
- EventSource；
- replay；
- WebMessage `GenerateFlowResult`；
- Session history reload；
- `currentResult` fallback。

必须区分“多种传输容错”与“多个权威终态源”。多个 transport 可以存在，但只能有一个 canonical terminal identity 和幂等应用机制。

---

## 5. 待验证的高风险假设

以下是前期静态摸查形成的候选问题。请逐项证明、否定或修正。

---

### H01：DryRun 存在两套不兼容契约

候选代码：

```text
ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/DryRun/DryRunService.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Tools/DryRunFlowTool.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Tools/FlowValidationTool.cs
ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelToolTrace.js
ClearVision.Product/src/ClearVision.Product.Core/DTOs/VisionAgentBuildContracts.cs
ClearVision.Product/src/ClearVision.Product.Contracts/Messages/AiGenerationMessages.cs
```

重点核查：

- 旧结果是否为 `IsSuccess / CoveredBranches / TotalBranches / CoveragePercentage`；
- Agent 结果是否为 `dryRunSucceeded / executedOperators / skippedOperators / blockingIssues / missingResources`；
- 二者是否进入同一个 `DryRunResult: object?`；
- UI 是否在字段缺失时默认 `false/0`；
- `ValidationPreview.dryRun` 与顶层 `DryRunResult` 是否重复表示同一检查；
- 当前 Build 中 dryrun 是结构模拟、Stub 执行、样本帧回放还是仅报告拼装。

输出必须明确：

```text
真实执行型 DryRun
结构元数据模拟
样本帧回放
运行包 Manifest 预检
Station compatibility
```

分别由谁负责，是否应该保留为独立概念。

---

### H02：ToolTrace 与 ToolEvidence 存在两套状态契约

候选字段：

```text
success: bool
status: string
errorCode
warningCode
applyImpact
deploymentImpact
permission
durationMs
```

候选代码：

```text
VisionAgentGenerateFlowService.MapTrace
VisionAgentBuildContracts
BuildToolRunner
BuildResultAssembler
aiPanelToolTrace.js
aiPanelAgentRun.js
aiPanelAgentWorkspace.js
```

重点核查：

- `success=false`、字段缺失、`Status=completed` 在 UI 中分别如何处理；
- `tool.call.*`、`tool_call.*`、Build evidence timeline 是否是三种不同事件体系；
- 是否存在同一工具同时出现在右侧 AgentRun 和左侧 Build 审计，但状态映射不同；
- “执行工具”和“构建证据步骤”是否被错误共用“工具调用”名称。

---

### H03：正式流程生成存在多条产品主链

候选路径：

```text
AiFlowGenerationService 原有 LLM 生成
VisionAgentGenerateFlowService scripted
VisionAgentGenerateFlowService planner
VisionAgentLoop / tool_loop
Plan → BuildFromPlan
```

候选配置：

```text
AI:VisionAgent:GenerateFlow:Enabled
Mode
FallbackToScriptedOnPlannerFailure
FallbackToLegacyOnFailure
UseVisionAgentGenerateFlow
AgentGenerateFlowMode
```

必须回答：

- 普通用户默认走哪一条；
- Plan/Build 是否已经是正式唯一产品路径；
- 旧直接 GenerateFlow 是否仍会被普通入口调用；
- scripted 是合理 deterministic fallback，还是第二套业务 builder；
- planner 失败后 fallback 是否改变 validator、draft builder 或结果合同；
- legacy fallback 是否实际可达，还是仅有配置字段。

---

### H04：需求理解、成熟度和澄清存在重复规则源

候选组件：

```text
RequirementBriefExtractor
AiTurnRouter
ClarificationEngine
ScenarioMatcher
VisionAgentSemanticExtractorService
VisionAgentRequirementMaturityGate
VisionAgentPlanPlannerService
VisionAgentPlanFieldPolicy
VisionAgentPlanRequirementOverlay
VisionAgentPlanReadinessEvaluator
```

检查维度：

- inspection object；
- task type；
- image source；
- acceptance criteria；
- output target；
- strategy；
- calibration；
- safety；
- missing fields；
- strict/draft；
- CanPlan / CanBuild。

必须构造至少 5 条相同输入，通过单元级调用或现有测试证明各组件输出是否一致。例如：

1. 固定平面螺钉孔定位并输出机器人坐标；
2. 相机拍摄遥控器，判断是否漏装；
3. 使用 YOLO 检测电机四角螺钉；
4. 读取条码并写 PLC；
5. 仅说“帮我做一个高级视觉方案”。

如果多个组件语义不同，应指出谁是 canonical，谁只是候选信息提供者。

---

### H05：结构校验可能存在多套规则内核

候选组件：

```text
FlowLinter
AiFlowValidator
VisionAgentFlowDraftValidator
FlowValidationTool
RuntimePackagePrecheckTool
WorkflowDraftBuilder 内部校验
画布 Apply 前校验
```

至少对以下规则做矩阵：

| 规则 | FlowLinter | AiFlowValidator | Agent Validator | Runtime/Apply |
|---|---|---|---|---|
| 空流程 | | | | |
| 未知算子 | | | | |
| 重复 tempId/Id | | | | |
| 缺失源/目标算子 | | | | |
| 端口不存在 | | | | |
| 端口类型不兼容 | | | | |
| 输入重复绑定 | | | | |
| 环路/DAG | | | | |
| 必填参数 | | | | |
| 参数范围 | | | | |
| 缺相机/模型/模板 | | | | |
| PLC/外部输出安全 | | | | |

不能只比较方法名称，必须比较实际结果严重级别：

```text
blocking
warning
pending resource
auto-repair
ignored
```

---

### H06：工作流草稿构建存在多个 builder

候选实现：

```text
AiFlowGenerationService ConvertToFlowDto / AutoLayout
VisionAgentGenerateFlowService BuildFlowDto
WorkflowDraftBuilder
OperatorPipelineSelector + ParameterMappingService
前端 _normalizeWorkflowDraftForCanvas
前端 _buildCanvasFlowFromOperatorPipeline
前端 _buildLinearCanvasConnections
```

重点核查：

- 是否有未知算子 fallback 到默认算子；
- 是否按端口名猜数据类型；
- 是否自动补线性连线；
- 前端补出的 Flow 是否经过后端同一 validator；
- Build 成功时后端是否保证返回 canonical `OperatorFlowDto`；
- `Flow`、`WorkflowDraft`、`OperatorPipeline` 三者谁是权威产物；
- 前端 fallback 是只处理旧历史记录，还是当前正常 Build 也可能进入。

这是 P0 风险：前端不得把未经后端验证的推断拓扑当成已审计草稿应用到画布。

---

### H07：结果 DTO 存在顶层与嵌套双写

候选对象：

```text
AiFlowGenerationResult
GenerateFlowResponse
VisionAgentBuildResult
ConversationTurnPayload
AgentRun terminal payload
```

重点字段：

```text
Flow
BuildResult
BuildReadiness
PendingParameters
MissingResources
ValidationPreview
DryRunResult
WorkflowDiff
ApplyGate
ToolEvidenceTimeline
FirstFixRecommendation
RequirementMaturity
DecisionTrace
```

制作“字段所有权矩阵”：

| 字段 | canonical owner | 顶层 | BuildResult | terminal | Session | UI fallback |
|---|---|---|---|---|---|---|

判定：

- 同一事实是否被复制；
- 复制值是否可能不一致；
- 恢复/replay 时是否丢字段；
- `object?` 是否导致鸭子类型解析；
- 是否有 `contractVersion` 和 `resultKind` 可判别来源；
- 前端是否通过“哪个有值就用哪个”决定权威数据。

---

### H08：Build 终态可能有多个到达通道

候选链：

```text
GenerateFlowMessageHandler 组装 GenerateFlowResponse
GenerateFlowAgentRunCreated
AgentRun SSE fetch stream
EventSource
replay polling
run.completed
VisionAgentBuildTerminalProjector
Session history
```

必须区分：

- 传输层 fallback：允许多种；
- 业务终态源：只能一个；
- UI 应用：必须按 runId/planId/planHash/sequence 幂等。

检查：

- 同一 run 是否可能被 `_displayResult` 两次；
- SSE 完成后 WebMessage 完整结果是否再次覆盖；
- replay 合成 terminal 是否可能与真实 terminal 重复；
- currentResult、pendingVisionPlan、session flow 谁会被最后写入；
- stale guard 是否覆盖 requestId、runId、planId、planHash、sequence；
- cancel/timeout/fallback 是否可能留下旧流继续写 UI。

---

### H09：Plan 可能同时存在直接接口与 AgentRun 接口

候选 endpoint：

```text
POST /api/ai/agent-plan
POST /api/ai/agent-plan-runs
```

检查：

- 前端真实调用哪一个；
- 另一个是否被测试、兼容入口或其他代码调用；
- 两条入口是否产生完全相同的 Plan hash、public events、failure contract；
- 直接接口是否绕过 AgentRun event store、redaction、cancel 和 replay；
- 是否应将直接接口降为内部 query，或让所有产品 Plan 都通过 PlanRun。

---

### H10：模式名称存在多义冲突

至少梳理：

```text
GenerateFlowMode
RequirementMode
AgentGenerateFlowMode
TemplateSelection.Mode
AIWorkflowOptions.StrictMode
RuntimePreview mode
workspace mode
```

说明每个模式控制的维度：

- 回合操作；
- 需求完整度；
- Agent 执行策略；
- 模板策略；
- 校验强度；
- 运行预览权限；
- UI 阶段。

检查 `mode_mismatch` 是否把“没有请求 Tool Loop”错误表达成异常。

建议统一命名方向，但第一阶段不要直接改代码。

---

### H11：Legacy 服务仍被正式产品 DI 注册

候选：

```text
AIWorkflowService
AIGeneratedFlowParser
AIPromptBuilder
DryRunService
DryRunStubRegistry
StubRegistryBuilder
legacyGlobals.js
```

逐个判定：

```text
生产可达
注册但不可达
测试专用
历史数据兼容
文档残留
可删除
```

尤其检查：

```text
AddVisionRuntimeServices
AddAiFlowGeneration
Desktop composition root
WebView2 message registration
Minimal API endpoint mapping
```

不能仅因 `[Obsolete]` 就认为安全；正式 DI 注册本身就是误用风险。

---

### H12：RuntimePreview、PackagePrecheck、StationCompatibility 是否职责重叠

候选：

```text
RuntimePreview*Service
RuntimePackageManifestDryRunService
RuntimePreviewPackageReadinessBridge
RuntimePreviewStationCompatibilityDryRunService
RuntimePreviewPreReleaseReviewService
RuntimePackagePrecheckTool
```

这部分可能是合理分层，也可能存在“同一数据被多次包装、均称为 readiness/dryrun”的问题。

必须画出：

```text
输入
→ 实际执行内容
→ 是否读取图像
→ 是否访问硬件
→ 是否使用 Stub
→ 是否创建 artifact/report
→ 是否影响 CanvasApplyReady
→ 是否影响 DeploymentReady
```

---

## 6. 建议使用的代码搜索

可根据实际目录调整，但应覆盖：

```powershell
rg -n "DryRunResult|dryRunSucceeded|CoveragePercentage|CoveredBranches|TotalBranches" ClearVision.Product
rg -n "ToolTrace|ToolEvidenceTimeline|success|Status|WarningCode|ApplyImpact|DeploymentImpact" ClearVision.Product
rg -n "UseVisionAgentGenerateFlow|AgentGenerateFlowMode|FallbackToScripted|FallbackToLegacy" ClearVision.Product
rg -n "RequirementBriefExtractor|RequirementMaturityGate|ClarificationEngine|FieldPolicy|ReadinessEvaluator" ClearVision.Product
rg -n "FlowLinter|AiFlowValidator|VisionAgentFlowDraftValidator|validate_flow" ClearVision.Product
rg -n "BuildFlowDto|ConvertToFlowDto|WorkflowDraftBuilder|buildLinearCanvasConnections|buildCanvasFlowFromOperatorPipeline" ClearVision.Product
rg -n "BuildResult|BuildReadiness|ValidationPreview|ApplyGate|WorkflowDiff|MissingResources|PendingParameters" ClearVision.Product
rg -n "GenerateFlowResult|run.completed|_applyAgentRunResultPayload|_displayResult|RecordAssistantResponse" ClearVision.Product
rg -n "MapPost\\(\"/api/ai/agent-plan|agent-plan-runs|agent-runs" ClearVision.Product
rg -n "\\[Obsolete|Legacy compatibility|legacy" ClearVision.Product
rg -n "AddScoped<.*AI|AddSingleton<.*AI|AddScoped<.*DryRun|AddScoped<.*Validator" ClearVision.Product
```

同时使用：

```powershell
git grep -n "目标符号"
git log --all --oneline -- "目标文件"
git blame -L <start>,<end> "目标文件"
```

利用 Git 历史确认某实现是迁移中的临时兼容，还是后来新增的正式方案。

---

## 7. 动态验证要求

本轮以审计为主，但必须尽量用现有测试或最小可复现调用证明关键结论。

### 7.1 最低验证

至少执行并记录真实输出：

```powershell
npm run test:agent-ui-contract
git diff --check
```

根据仓库实际脚本，再运行 Desktop AgentRun endpoint/architecture guard 相关 preset。

.NET 测试必须串行。若需要多个测试类，应合并到一次：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "<实际测试 csproj>" `
  -FullyQualifiedName TestClassA,TestClassB,TestClassC `
  -NoBuild `
  -NoRestore
```

不得写“应当通过”冒充“已通过”。Windows/WebView2/Playwright 因环境不能运行时，要明确写明未运行原因。

### 7.2 建议新增但暂不提交的最小探针

第一阶段允许在临时目录或测试草稿中做探针，但不得提交：

1. 同一合法 Flow 分别送入各 validator，比较严重级别；
2. 含环路 Flow 比较各 validator；
3. 缺相机资源 Flow 比较 Strict/Draft 与 DryRun；
4. 将 Agent dryrun payload 送入现有前端 normalizer，复现 `false/0/0`；
5. 将 `Status=completed`、无 `success` 的 ToolEvidence 送入 normalizer；
6. 模拟同一 run 同时收到 SSE terminal 和 WebMessage terminal，检查是否重复应用；
7. BuildResult 只含 WorkflowDraft、不含 Flow，检查前端是否自行重建拓扑。

所有探针必须删除，不得污染工作区。

---

## 8. 证据标准

每条发现必须给出：

```text
Finding ID
严重级别
分类（冲突双轨/合理分层/兼容层/不可达遗留/测试专用）
业务语义
入口
完整调用链
权威数据源
另一套实现
具体文件与行号
DI 注册证据
前端消费证据
测试证据
可复现条件
实际风险
建议处置
置信度
```

必须证明以下三件事：

1. **存在**：代码、契约或注册确实存在；
2. **可达**：从正式入口可到达，或明确证明不可达；
3. **冲突**：两套实现解决的是同一业务决策，并可能产生不同结果。

缺少第 2 或第 3 项，不得直接标记为 P0 双轨。

---

## 9. 必须输出的矩阵

### 9.1 方案清单

| ID | 领域 | 实现 A | 实现 B/C | 正式可达 | 同一语义 | 结果冲突 | 分类 | 优先级 |
|---|---|---|---|---|---|---|---|---|

### 9.2 权威来源

| 业务事实 | 当前候选来源 | 应保留权威来源 | 其他实现处置 |
|---|---|---|---|
| Plan 是否允许 Build | | | |
| Canonical requirement | | | |
| Flow 结构是否合法 | | | |
| 缺失资源 | | | |
| 结构模拟结果 | | | |
| 样本帧回放结果 | | | |
| CanvasApplyReady | | | |
| DeploymentReady | | | |
| Build terminal | | | |
| Session current flow | | | |

### 9.3 结果契约所有权

| 字段 | 写入者 | 复制位置 | 消费者 | 版本判别 | 冲突风险 | 建议 |
|---|---|---|---|---|---|---|

### 9.4 入口与终态

| 入口 | 用途 | 后端服务 | 运行记录 | 终态源 | UI 应用函数 | 是否应保留 |
|---|---|---|---|---|---|---|

---

## 10. 期望的统一目标架构

请基于真实代码复审后修正下面的目标，而不是机械接受：

```text
用户输入
  ↓
Canonical Turn Router
  ↓
CanonicalRequirementSnapshot
  ↓
Plan Planner
  ↓
Canonical PlanReadinessPreview
  ↓
Build Application Service
  ↓
CanonicalWorkflowDraftBuilder
  ↓
CanonicalWorkflowContractValidator
  ↓
ValidationBundle
  ├─ StructureValidationReport
  ├─ StructureSimulationReport
  ├─ SampleFrameReplayReport（授权后）
  ├─ RuntimePackagePrecheckReport
  └─ StationCompatibilityReport
  ↓
PostBuildApplyGate
  ↓
AgentRun Terminal Envelope
  ↓
Terminal Projector
  ├─ Conversation Session Projection
  └─ Frontend/Canvas Projection
```

原则：

1. 同一业务事实只有一个权威计算源；
2. 多 transport 可以存在，但业务 terminal 只有一个；
3. 前端不得重建权威流程拓扑；
4. UI 不得通过字段缺失推断失败；
5. 所有公共 union 结果必须带 `contractVersion` 与 `resultKind`；
6. `object?` 只允许出现在明确隔离的扩展 metadata 中；
7. Legacy 不得成为普通用户主链的隐式 fallback；
8. Plan Build authorization 与 Post-build ApplyGate 保持分层；
9. 结构模拟、样本回放、包预检、Station 兼容必须使用不同名字和 DTO；
10. Session 是投影，AgentRun/EventStore 是运行事实来源。

---

## 11. 复审后的处置计划要求

不要直接给一个“大重构”。请按以下批次提出方案：

### Phase 0：止血

只修已经造成错误展示或错误授权的问题：

- DryRun 契约判别；
- Tool status 映射；
- 未知状态不伪装为失败；
- 重复卡片与诊断隔离；
- 不改主业务链。

### Phase 1：公共契约收敛

- 版本化 terminal envelope；
- typed validation reports；
- 去除顶层/嵌套双写；
- ToolExecutionRecord 统一；
- 为旧结果提供显式 adapter。

### Phase 2：权威内核收敛

- canonical validator；
- canonical workflow builder；
- canonical requirement snapshot；
- 禁止前端拓扑重建。

### Phase 3：入口与终态收敛

- 正式产品统一 Plan → Build；
- Build terminal 唯一事实源；
- 直接 GenerateFlow 进入显式 compatibility；
- direct Plan endpoint 与 PlanRun 明确边界。

### Phase 4：Legacy 清理与防回归

- 移出正式 DI；
- 删除无调用遗留；
- 更新文档；
- architecture guard；
- contract tests；
- migration notes。

每个 Phase 必须给：

- 涉及文件；
- 不允许修改的边界；
- 迁移兼容策略；
- 测试；
- 回滚点；
- 完成判据。

---

## 12. 禁止事项

第一阶段复审中禁止：

- 修改任何生产代码；
- 自动格式化大量文件；
- 提交或推送；
- reset、clean、checkout 覆盖用户修改；
- 因为新方案看起来更现代就直接删除旧方案；
- 把合理分层误判为重复；
- 只看单元测试，不追踪真实产品入口；
- 只报告类数量，不分析业务语义；
- 将未运行测试写成通过；
- 新建第三套 wrapper、adapter 或 evaluator 来“统一”；
- 建议前端继续通过 duck typing 永久兼容所有历史 DTO。

---

## 13. 最终交付格式

请输出一份 Markdown 审计报告，建议命名：

```text
docs/进行中/当前计划/ClearVision_VisionAgent_DualTrack_Opus_Audit.md
```

报告顺序：

1. 审计环境与实际 SHA；
2. 执行摘要；
3. 当前真实主链图；
4. 已确认冲突；
5. 被否定的初步假设；
6. 合理分层；
7. 注册但不可达遗留；
8. 结果契约所有权矩阵；
9. validator 差异矩阵；
10. flow builder 差异矩阵；
11. 入口和终态矩阵；
12. P0/P1/P2/P3 风险；
13. 分阶段统一方案；
14. 测试与命令真实输出；
15. 未能验证的项目；
16. 最终判定。

执行摘要必须直接回答：

```text
已确认多少处冲突双轨？
多少处合理分层？
多少处正式可达 Legacy？
最危险的前三处是什么？
当前 DryRun 红色失败是否纯 UI Bug，还是同时存在真实业务失败？
现在是否适合直接大重构？
第一轮最小安全修复范围是什么？
```

---

## 14. 供 Opus 使用的开场提示词

```text
你正在 ClearVision 本地真实仓库中执行一次独立架构审计。请先读取 AGENTS.md 和本报告，检查 git 状态、实际分支和 SHA。第一阶段只审计，不修改代码、不提交、不推送。

目标不是验证既有结论，而是从真实入口、DI、调用链、DTO、事件终态、前端消费和测试证据出发，确认 Vision Agent 演进后是否形成了 DryRun、流程生成、需求理解、validator、flow builder、结果合同、ToolTrace、Plan endpoint、Build terminal 等双轨/多轨冲突。

每条结论必须区分：冲突双轨、合理分层、显式兼容层、注册但不可达遗留、测试/文档专用。必须给出文件与行号、入口到实现的调用链、可达性、冲突条件和现有测试证据。不要因为类名含 Legacy 或 Obsolete 就直接判断不可达。

请严格按照报告中的矩阵和交付格式输出审计结果，并记录所有真实执行过的命令与测试结果。未运行的测试必须明确标注。不要创建第三套抽象来掩盖问题，也不要在完成审计前动手重构。
```

---

## 15. 当前预判，仅供复审对照

在 Opus 独立复审前，当前预判如下：

### 高置信候选

1. DryRun 新旧契约混入同一个弱类型字段；
2. ToolTrace `success` 与 ToolEvidence `status` 不兼容；
3. BuildResult 数据同时存在顶层和嵌套复制；
4. AgentRun terminal 与 GenerateFlowResult 可能形成双结果到达面；
5. Legacy AI/DryRun 服务仍在正式 Desktop DI 中注册；
6. 前端存在从 WorkflowDraft/OperatorPipeline 推断画布 Flow 的 fallback。

### 中等置信候选

1. 旧 GenerateFlow 与 Plan/Build 仍是两条普通用户可达产品链；
2. RequirementBrief/Maturity/Semantic/FieldPolicy 存在多处重复决策；
3. FlowLinter、AiFlowValidator、Agent validator 对同一规则严重级别可能不同；
4. `/agent-plan` 与 `/agent-plan-runs` 形成 Plan 双入口；
5. scripted/planner/tool-loop 不只是执行策略差异，而可能携带不同 builder/validator。

### 不应提前判为冲突

1. Plan BuildReadiness 与 Build 后 ApplyGate；
2. AgentRun EventStore 与 ConversationSession；
3. 结构模拟、真实帧回放、运行包预检和 Station 兼容；
4. fetch SSE、EventSource 和 replay 作为传输容错。

这些可以并存，但必须确认其命名、契约和权威边界足够清晰。
