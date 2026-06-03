# ClearVision Vision Engineering Agent TODO

> 目标：将当前 ClearVision AI 流程生成能力升级为 **Vision Engineering Agent**。
> 该 Agent 不开放系统级 CMD / PowerShell / Shell 权限，只调用 ClearVision 内部已有能力，辅助工程师完成视觉工程配置、流程生成、验证、调试和部署准备。

---

## 0. 背景与边界

### 0.1 当前已有基础

ClearVision 当前 AI 主链已经具备较完整的流程生成流水线：

```text
用户自然语言需求
  ↓
GenerateFlow WebMessage
  ↓
GenerateFlowMessageHandler
  ↓
AiFlowGenerationService
  ↓
会话上下文 / 意图路由 / 场景匹配 / 模板优先 / 需求摘要 / 澄清
  ↓
PromptBuilder + AiPromptComposer
  ↓
LLM 生成流程 JSON
  ↓
ResponseParser
  ↓
Validator
  ↓
AutoLayout
  ↓
DryRun
  ↓
返回 Flow / PendingParameters / MissingResources / StageTimeline
```

目前的问题不是“没有 AI 流程生成”，而是：

1. 模型仍依赖大量前置提示词注入；
2. 算子目录、知识切片、模板骨架仍有较多内容被塞入 Prompt；
3. 模型不能像 Codex 一样主动调用工具查询 ClearVision 内部状态；
4. 生成、校验、DryRun 虽然已有，但更多是固定流水线，不是显式 Agent Tool Loop；
5. 相机、PLC、Station、运行包等内部能力尚未被统一封装为 Agent 可调用工具。

---

### 0.2 本轮目标

本轮目标不是做一个通用操作系统 Agent，也不是开放 CMD 权限，而是做：

```text
Vision Engineering Agent
= LLM
+ ClearVision 内部工具注册表
+ Agent Tool Loop
+ 算子/模板/相机/流程/校验/DryRun/部署准备工具
+ 人工确认边界
```

核心效果：

```text
用户：帮我做一个华睿相机采图 + 模板匹配 + OK/NG 输出流程
  ↓
Agent 调用 list_operator_catalog / get_operator_schema
  ↓
Agent 调用 list_camera_bindings / discover_cameras
  ↓
Agent 生成流程
  ↓
Agent 调用 validate_flow
  ↓
失败则修复
  ↓
Agent 调用 dryrun_flow
  ↓
生成可应用流程 + 待确认参数 + 相机绑定草稿 / 部署准备清单
```

---

### 0.3 明确不做

本阶段明确不做以下内容：

```text
[ ] 不开放 CMD / PowerShell / shell
[ ] 不开放任意文件系统读写
[ ] 不让模型直接改 settings/config
[ ] 不让模型直接部署到 Station
[ ] 不让模型直接写 PLC / 相机生产配置
[ ] 不接 MCP
[ ] 不做多 Agent 角色系统
[ ] 不做长期记忆 / 向量库
[ ] 不做系统级安全沙箱
[ ] 不做完全无人值守上线
```

所有可能影响现场配置、运行包、Station 部署的动作，必须先生成草稿或预检查结果，由工程师确认后再应用。

---

## 1. 当前 Prompt 问题与改造方向

### 1.1 当前 Prompt 注入现状

当前 `PromptBuilder.BuildSystemPrompt()` 会拼接大量固定 Section：

```text
Section 1 - Role And Hard Rules
Section 2 - Domain Workflow Patterns
Section 3 - Template First Strategy
Section 4 - Phase 1 Operator Extensions
Section 5 - Phase 2 Operator Extensions
Section 6 - Phase 3 Operator Extensions
Section 7 - Operator Catalog
Section 8 - Connection Rules
Section 9 - Parameter Inference Guide
Section 10 - Output Format
Section 11 - Few Shot Examples
```

当前并不是“无脑全量注入 150+ 个完整算子名片”，而是：

```text
相关算子详细目录 / 知识切片
+ 全量 compact fallback catalog
+ 模板优先策略 / templateSkeletonJson
+ Phase1/2/3 规则
+ 连接规则
+ 参数推断规则
+ few-shot examples
```

也就是说，已有一定检索切片能力，但 Prompt 仍偏重。

---

### 1.2 Agent Tools 后的目标 Prompt 结构

改造后，Prompt 不再承担“资料库”职责，而只承担“规则 + 协议”职责。

目标结构：

```text
SystemPrompt:
  - 角色定义
  - 硬边界
  - 工具调用协议
  - 最终输出 schema
  - 不得编造算子/端口/参数/硬件地址

ToolRegistry:
  - list_operator_catalog
  - get_operator_schema
  - retrieve_operator_knowledge
  - match_flow_template
  - get_flow_template_skeleton
  - inspect_current_flow
  - validate_flow
  - dryrun_flow
  - list_camera_bindings
  - discover_cameras
  - capture_test_frame
  - runtime_package_precheck
```

新的工作方式：

```text
不是开局把所有资料塞给模型，
而是模型需要什么，就调用 ClearVision 内部工具查什么。
```

---

### 1.3 Prompt 改造目标

| 内容 | 当前方式 | 目标方式 |
| --- | --- | --- |
| 算子目录 | Prompt 注入相关详细目录 + 全量 compact fallback | `list_operator_catalog` / `get_operator_schema` 工具查询 |
| 算子知识卡片 | Prompt 注入知识切片 | `retrieve_operator_knowledge` 按场景查询 |
| 模板骨架 | 命中后注入 `templateSkeletonJson` | `match_flow_template` / `get_flow_template_skeleton` 查询 |
| 当前画布 | `existingFlowJson` 摘要注入 | `inspect_current_flow` 工具查询 |
| 校验 | 固定流水线执行 | `validate_flow` 工具化，Agent 可主动调用 |
| DryRun | 固定流水线执行 | `dryrun_flow` 工具化，Agent 可主动调用 |
| 相机状态 | 设置页独立接口 | `list_camera_bindings` / `discover_cameras` / `capture_test_frame` 工具化 |
| 部署准备 | Station/Package 独立链路 | `runtime_package_precheck` / `draft_runtime_package_manifest` 工具化 |

---

## 2. 总体架构 TODO

目标架构：

```text
ClearVision.Product.Core
  └── AI/Tools
      ├── IVisionAgentTool.cs
      ├── IVisionAgentToolRegistry.cs
      ├── VisionAgentToolDescriptor.cs
      ├── VisionAgentToolResult.cs
      ├── VisionAgentToolTrace.cs
      └── VisionAgentToolContext.cs

ClearVision.Product.Infrastructure
  └── AI/Agent
      ├── VisionAgentLoop.cs
      ├── VisionAgentLoopOptions.cs
      ├── VisionAgentProtocolParser.cs
      └── AgentPromptBuilder.cs

ClearVision.Product.Infrastructure
  └── AI/Tools
      ├── OperatorCatalogTool.cs
      ├── OperatorSchemaTool.cs
      ├── OperatorKnowledgeTool.cs
      ├── FlowTemplateMatchTool.cs
      ├── FlowTemplateSkeletonTool.cs
      ├── CurrentFlowInspectTool.cs
      ├── FlowValidationTool.cs
      ├── DryRunFlowTool.cs
      ├── CameraBindingsTool.cs
      ├── CameraDiscoveryTool.cs
      ├── CameraTestFrameTool.cs
      ├── RuntimePackagePrecheckTool.cs
      └── RuntimePackageManifestDraftTool.cs
```

---

## 3. P0：工具系统骨架

### TODO 3.1 新增工具抽象接口与依赖注入防线

新增：

```csharp
public interface IVisionAgentTool
{
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }
    string Category { get; }
    VisionAgentToolPermission Permission { get; }
    JsonElement ParametersSchema { get; }

    Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken);
}
```

新增：

```csharp
public interface IVisionAgentToolRegistry
{
    IReadOnlyList<VisionAgentToolDescriptor> ListTools();
    bool TryGet(string name, out IVisionAgentTool tool);

    Task<VisionAgentToolResult> ExecuteAsync(
        string name,
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken);
}
```

验收：

```text
[ ] DI 可注册工具，必须将所有 IVisionAgentTool 实例注册为 Transient 或 Scoped 生命周期，严禁 Singleton，防范生命周期捕获风险（Captive Dependency）
[ ] 可列出所有 Agent Tools
[ ] 可按名称执行工具
[ ] 工具执行异常被包装为结构化 ToolResult
[ ] 工具失败不会导致 GenerateFlow 整体崩溃
```

---

### TODO 3.2 新增工具权限枚举

新增：

```csharp
public enum VisionAgentToolPermission
{
    ReadOnly,
    Simulation,
    RuntimePreview,
    ConfigDraft,
    ConfigWrite,
    DeploymentPrepare
}
```

当前阶段默认开放：

```text
ReadOnly
Simulation
RuntimePreview
ConfigDraft
DeploymentPrepare
```

默认禁止：

```text
ConfigWrite 直接写入
DirectDeploy 直接部署
SystemCommand / Shell / CMD
```

验收：

```text
[ ] 工具描述中明确 permission
[ ] Agent Loop 执行前检查 permission
[ ] ConfigWrite 类工具默认不执行，只生成草稿
[ ] 所有越权请求返回 ToolResult(success=false, error=PermissionDenied)
```

---

### TODO 3.3 新增工具 Trace

新增：

```csharp
public sealed record VisionAgentToolTrace
{
    public string ToolName { get; init; } = string.Empty;
    public object? Arguments { get; init; }
    public bool Success { get; init; }
    public object? ResultSummary { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }
    public string Permission { get; init; } = string.Empty;
}
```

扩展 `GenerateFlowResponse` / `AiFlowGenerationResult`：

```csharp
public List<VisionAgentToolTrace>? ToolTrace { get; init; }
```

验收：

```text
[ ] 前端能收到 ToolTrace
[ ] 后端日志包含工具调用名称、耗时、成功/失败
[ ] DebugPrompt 模式下可展开完整工具调用参数
[ ] 非 Debug 模式只展示摘要，避免长 JSON 影响 UI
```

---

## 4. P1：第一批只读工具

### TODO 4.1 `list_operator_catalog`

封装：

```text
IOperatorFactory.GetAllMetadata()
IOperatorFactory.GetSupportedOperatorTypes()
```

返回：

```json
{
  "operators": [
    {
      "operatorType": "ImageAcquisition",
      "displayName": "图像采集",
      "category": "Acquisition",
      "description": "...",
      "keywords": ["camera", "image", "capture"],
      "inputCount": 0,
      "outputCount": 1,
      "parameterCount": 6
    }
  ]
}
```

规则：

```text
[ ] 默认只返回 compact catalog
[ ] 不默认返回完整参数 schema
[ ] 支持 category / keyword / topN 参数
[ ] 需要完整 schema 时必须调用 get_operator_schema
```

验收：

```text
[ ] Agent 能查询真实算子列表
[ ] 模型不再依赖 Prompt 中的全量 compact fallback catalog
[ ] 生成流程不出现不存在的 operatorType
```

---

### TODO 4.2 `get_operator_schema`

封装：

```text
IOperatorFactory.GetMetadata(OperatorType type)
```

输入：

```json
{
  "operatorType": "ImageAcquisition"
}
```

返回：

```json
{
  "operatorType": "ImageAcquisition",
  "displayName": "图像采集",
  "category": "Acquisition",
  "inputs": [],
  "outputs": [
    {
      "portName": "Image",
      "dataType": "Image",
      "required": true
    }
  ],
  "parameters": [
    {
      "paramName": "SourceType",
      "type": "enum",
      "required": true,
      "defaultValue": "Camera",
      "options": ["Camera", "File"]
    }
  ]
}
```

验收：

```text
[ ] Agent 使用某个算子前能查询该算子 schema
[ ] 参数名、端口名从 schema 中取，不再靠模型记忆
[ ] Validator 报参数错误时，Agent 会重新调用 get_operator_schema 修复
```

---

### TODO 4.3 `inspect_current_flow`

封装：

```text
request.ExistingFlowJson
conversationContext.ExistingFlowJson
AiPromptComposer.BuildReferenceFlowSummary 的部分逻辑
```

输入：

```json
{}
```

返回：

```json
{
  "hasFlow": true,
  "operatorCount": 5,
  "connectionCount": 4,
  "operators": [
    {
      "id": "op_1",
      "operatorType": "ImageAcquisition",
      "displayName": "主相机采图"
    }
  ],
  "connections": [
    "op_1.Image -> op_2.Image"
  ],
  "warnings": []
}
```

验收：

```text
[ ] 用户要求“修改当前流程”时，Agent 先读当前流程
[ ] Agent 能区分 New / Modify / Explain / ReviewPendingParameters
[ ] 不再把长 ReferenceFlowSummary 默认塞进 user prompt，改为工具按需查询
```

---

## 5. P2：Agent Loop 与工具调用协议

### TODO 5.1 新增 `AgentPromptBuilder`

新增轻量 Prompt Builder：

```text
ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/AgentPromptBuilder.cs
```

只保留：

```text
1. Role
2. Hard Rules
3. Tool Calling Protocol
4. Final Output Schema
5. Permission Boundary
```

不再默认注入：

```text
[ ] 不默认注入完整 Operator Catalog
[ ] 不默认注入 Full Catalog Fallback
[ ] 不默认注入完整 Operator Knowledge Slice
[ ] 不默认注入 templateSkeletonJson
[ ] 不默认注入大量 few-shot examples
```

建议最小系统 Prompt：

```text
You are ClearVision Vision Engineering Agent.
You help engineers generate, validate, debug, and prepare deployment for ClearVision visual inspection workflows.
Use only ClearVision internal tools listed in this session.
Do not invent operator types, port names, parameter names, camera IDs, PLC addresses, model paths, calibration files, or station IDs.
When information is missing, call tools or mark it as pending.
Never request system commands or shell execution.
Config write and deployment actions must be returned as drafts requiring user confirmation.
```

验收：

```text
[ ] PromptMode=agent_tools 时使用 AgentPromptBuilder
[ ] PromptMode=legacy_full_prompt 时继续使用 PromptBuilder
[ ] DebugPrompt 能显示实际 system prompt 长度
[ ] agent_tools 模式下 system prompt 明显短于 legacy 模式
```

---

### TODO 5.2 定义工具调用协议与原生 Function Calling 适配

协议与 API 调用机制：

1. **API 原生 Function Calling（首选）**：
   - 在 `AiApiClient` 中扩展请求体构建逻辑，将 `IVisionAgentToolRegistry` 注册的工具 schema 格式化为 API 标准的 `tools` 参数传入。
   - 对接原生 `tool_calls` 响应并自动回调对应工具，规避大模型以纯文本输出 JSON 导致的格式解析不可靠问题。
2. **自定义 JSON 协议（不支持 tools 时的备用 Fallback）**：
   - 模型输出支持两类 JSON 格式（工具调用 `tool_call` 和最终结果 `final_flow`）：

#### 工具调用

```json
{
  "kind": "tool_call",
  "toolCalls": [
    {
      "id": "call_1",
      "name": "list_operator_catalog",
      "arguments": {
        "keyword": "template matching",
        "topN": 20
      }
    }
  ]
}
```

#### 最终结果

```json
{
  "kind": "final_flow",
  "explanation": "...",
  "operators": [],
  "connections": [],
  "parametersNeedingReview": {},
  "missingResources": [],
  "pendingActions": []
}
```

验收：

```text
[ ] 优先走 API 原生 Function Calling 链路，支持并发调用多个工具
[ ] 在 fallback 模式下能正确解析自定义 `tool_call` JSON 和 `final_flow` JSON
[ ] 协议解析/提取失败时进入 retry 纠错，而不是直接崩溃
[ ] tool_call 名称必须存在于 ToolRegistry
[ ] 不存在的工具返回 UnknownTool
```

---

### TODO 5.3 新增 `VisionAgentLoop`

新增：

```text
ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/VisionAgentLoop.cs
```

职责：

```text
1. 构造轻量 system prompt，并注入工具列表 descriptor（或原生 tools 接口参数）
2. 调用 LLM
3. 解析 tool_call / final_flow（或直接读取 API 的 tool_calls 并执行）
4. 执行 ClearVision 内部工具
5. 将 tool_result 以 Role=Tool（原生）或 Role=User（自定义协议）追加进上下文
6. 再次调用 LLM
7. 循环直到 final_flow 或超出最大轮次
```

建议配置（允许更多循环轮次以跑通完整闭环）：

```json
{
  "Ai": {
    "EnableVisionAgentTools": true,
    "PromptMode": "hybrid",
    "MaxToolRounds": 5,
    "MaxToolCallsPerRound": 5,
    "MaxToolResultChars": 12000
  }
}
```

验收：

```text
[ ] 单轮工具调用可完成
[ ] 多轮工具调用可完成，在 MaxToolRounds 限制下能支持并行工具请求以节省往返延迟
[ ] 达到 MaxToolRounds 后返回 ManualRetry / FailedWithToolLimit
[ ] ToolTrace 完整记录调用链
[ ] 可通过 feature flag 回退旧链路
```

---

## 6. P3：Prompt 瘦身迁移

### TODO 6.1 拆分 Prompt 模式

新增配置：

```json
{
  "Ai": {
    "PromptMode": "legacy_full_prompt"
  }
}
```

支持：

```text
legacy_full_prompt：当前 PromptBuilder 行为
hybrid：短 Prompt + 少量核心算子 + Agent Tools
agent_tools：短 Prompt + 工具查询，不默认注入 catalog/template skeleton
```

验收：

```text
[ ] 三种模式可切换
[ ] legacy 模式现有测试全部通过
[ ] hybrid 模式作为默认试验模式
[ ] agent_tools 模式用于后续压测
```

---

### TODO 6.2 Operator Catalog 从 Prompt 迁移到 Tool

改造：

```text
PromptBuilder.GetOperatorCatalog(userDescription)
```

目标：

```text
legacy_full_prompt：保留原逻辑
hybrid：只注入核心算子 compact 信息 + 提醒可调用工具
agent_tools：不注入 catalog，只注入工具说明
```

核心算子可保留：

```text
ImageAcquisition
ResultJudgment
ResultOutput
ConditionalBranch
```

验收：

```text
[ ] agent_tools 模式下无 Full Catalog Fallback
[ ] hybrid 模式下 Full Catalog Fallback 不超过核心算子 compact 信息
[ ] 需要算子详细参数时，Agent 调用 get_operator_schema
```

---

### TODO 6.3 Operator Knowledge Slice 从 Prompt 迁移到 Tool

新增工具：

```text
retrieve_operator_knowledge
```

输入：

```json
{
  "description": "线序检测，端子颜色顺序判断",
  "topN": 12
}
```

封装：

```text
IOperatorKnowledgeRetriever.RetrieveAsync(...)
```

输出：

```json
{
  "retrievalSummary": "operator_count=12; scenario_hints=wire-sequence",
  "matchedScenarioKeys": ["wire-sequence"],
  "cards": [
    {
      "operatorType": "DeepLearning",
      "requiredResources": ["model"],
      "typicalDownstream": ["DetectionSequenceJudge"],
      "antiPatterns": ["..."],
      "knownLimitations": ["..."]
    }
  ]
}
```

验收：

```text
[ ] Prompt 不默认塞完整知识切片
[ ] 线序/模板匹配/测量/PLC 场景可按需检索知识
[ ] 知识卡片仍经过真实 metadata 校验，避免过期 schema 污染生成
```

---

### TODO 6.4 Template Skeleton 从 Prompt 迁移到 Tool

新增工具：

```text
match_flow_template
get_flow_template_skeleton
```

封装：

```text
IFlowTemplateService
IScenarioMatcher
BuildTemplatePriorityContextAsync 相关逻辑
```

`match_flow_template` 输出：

```json
{
  "matched": true,
  "templateId": "wire-sequence-v1",
  "templateName": "端子线序检测模板",
  "confidence": 0.92,
  "generationMode": "template_fill",
  "templateLockLevel": "strict"
}
```

`get_flow_template_skeleton` 输出：

```json
{
  "templateId": "wire-sequence-v1",
  "flowSkeleton": {
    "operators": [],
    "connections": []
  }
}
```

验收：

```text
[ ] 不再默认把 templateSkeletonJson 塞进 UserPrompt
[ ] Agent 命中模板后按需拉取模板骨架
[ ] strict template_fill 场景仍能保持模板骨架不被破坏
```

---

### TODO 6.5 Few-shot 瘦身

当前 few-shot 可保留到 legacy 模式。

agent_tools 模式改为：

```text
[ ] 只保留 1 个最小 JSON 输出示例
[ ] 或完全依赖 final_flow schema + parser + validator + retry
```

验收：

```text
[ ] agent_tools 模式下 few-shot 总长度显著降低
[ ] JSON 输出稳定性不低于 legacy 模式
[ ] parser failure rate 有统计
```

---

## 7. P4：校验与 DryRun 工具化

### TODO 7.1 `validate_flow`

封装：

```text
IAiFlowValidator.Validate(...)
ITemplateConstraintValidator.Validate(...)
```

输入：

```json
{
  "flow": {
    "operators": [],
    "connections": []
  },
  "templateId": "optional",
  "templateLockLevel": "optional"
}
```

输出：

```json
{
  "isValid": false,
  "errors": [
    {
      "code": "missing_required_parameter",
      "operatorTempId": "op_1",
      "operatorType": "ImageAcquisition",
      "message": "CameraBindingId is required",
      "repairHint": "Call list_camera_bindings or mark CameraBindingId as pending."
    }
  ],
  "warnings": []
}
```

验收：

```text
[ ] Agent 生成 flow 后主动调用 validate_flow
[ ] validate_flow 失败后，Agent 基于 errors 修复
[ ] 修复最多重试 MaxValidationRepairRounds
[ ] 修复失败进入 ManualRetry，不返回假成功
```

---

### TODO 7.2 `dryrun_flow`

封装：

```text
DryRunService.RunAsync(...)
```

输入（支持闭环帧注入）：

```json
{
  "flow": {},
  "testInputsMode": "empty_stub",
  "temporaryFrameId": "optional string (从之前 capture_test_frame 采图获取，将临时图片馈送给采集算子，避免仿真时缺失输入图片)"
}
```

输出：

```json
{
  "isSuccess": true,
  "durationMs": 18,
  "coveragePercentage": 100,
  "coveredBranches": 3,
  "totalBranches": 3,
  "warnings": []
}
```

验收：

```text
[ ] validate_flow 通过后调用 dryrun_flow
[ ] DryRun 异常返回结构化错误摘要
[ ] Agent 根据 DryRun 结果修复或标记 pendingParameters
[ ] DryRun 不接真实硬件，但可通过 `temporaryFrameId` 将之前物理相机抓到的图作为输入数据管道馈送给仿真环境下的采集算子，使得深度学习或模板匹配算子能进行闭环真实阈值校验，而非仅用 empty_stub 空值预演
```

---

## 8. P5：相机内部工具

### TODO 8.1 `list_camera_bindings`

封装：

```text
ICameraManager.GetBindings()
/api/cameras/bindings 的同等读取逻辑
```

输出：

```json
{
  "bindings": [
    {
      "id": "cam-main",
      "displayName": "主相机",
      "manufacturer": "Huaray",
      "serialNumber": "EF59632AAK00291",
      "ipAddress": "192.168.1.88",
      "modelName": "...",
      "interfaceType": "GigE",
      "triggerMode": "Software",
      "pixelFormat": "BayerRG8",
      "connectionStatus": "Discovered"
    }
  ]
}
```

验收：

```text
[ ] Agent 生成 ImageAcquisition 时优先使用已有绑定
[ ] 有多个相机时，Agent 不能擅自选择生产相机，必须说明选择依据或标记待确认
[ ] 无绑定时返回空列表，不报错
```

---

### TODO 8.2 `discover_cameras`

封装：

```text
CameraProviderFactory.DiscoverHuarayOnly()
CameraProviderFactory.DiscoverHikvisionOnly()
```

输入：

```json
{
  "manufacturer": "Huaray"
}
```

输出：

```json
{
  "devices": [
    {
      "manufacturer": "Huaray",
      "serialNumber": "...",
      "ipAddress": "...",
      "modelName": "...",
      "interfaceType": "GigE"
    }
  ],
  "diagnostics": {}
}
```

规则：

```text
[ ] 只发现，不保存
[ ] 只读权限
[ ] 搜索失败返回 diagnostics
```

验收：

```text
[ ] Agent 可在没有绑定时发现华睿/海康相机
[ ] 发现结果可用于 draft_camera_binding
[ ] 发现不到设备时，Agent 返回 MissingResources
```

---

### TODO 8.3 `capture_test_frame`

封装：

```text
ICameraManager.GetOrCreateByBindingAsync(...)
AcquireSingleFrameAsync()
```

输入：

```json
{
  "cameraBindingId": "cam-main"
}
```

输出：

```json
{
  "success": true,
  "width": 2448,
  "height": 2048,
  "format": "png",
  "temporaryFrameId": "agent-frame-xxx"
}
```

规则：

```text
[ ] 不把图片二进制塞给 LLM
[ ] 只返回宽高、格式、是否成功、临时帧 ID、错误摘要
[ ] 真图预览交给前端或内部缓存
[ ] 非 Software 触发模式时返回明确错误
```

验收：

```text
[ ] 已绑定相机可采测试帧
[ ] 采图失败时返回结构化错误
[ ] Agent 能根据采图失败原因提示用户检查 IP / 触发模式 / 像素格式 / SDK
```

---

### TODO 8.4 `draft_camera_binding`

输入：

```json
{
  "device": {},
  "suggestedDisplayName": "主相机",
  "triggerMode": "Software",
  "pixelFormat": "BayerRG8"
}
```

输出：

```json
{
  "draftBinding": {
    "id": "cam-main",
    "displayName": "主相机",
    "manufacturer": "Huaray",
    "serialNumber": "...",
    "ipAddress": "...",
    "triggerMode": "Software",
    "pixelFormat": "BayerRG8"
  },
  "requiresUserConfirmation": true
}
```

规则：

```text
[ ] 只生成草稿
[ ] 不直接 PUT /api/cameras/bindings
[ ] 前端展示“应用到设置草稿”按钮
[ ] 工程师确认后走现有设置保存链路
```

验收：

```text
[ ] Agent 发现相机后能生成绑定草稿
[ ] 不会直接改 AppConfig
[ ] 不会影响正在运行的相机流
```

---

## 9. P6：部署准备工具

### TODO 9.1 `check_station_status`

封装已有 Station 同步 / 健康状态能力。

输出：

```json
{
  "stations": [
    {
      "stationId": "line-1-station-a",
      "online": true,
      "version": "...",
      "lastHeartbeatUtc": "...",
      "currentPackage": "..."
    }
  ]
}
```

规则：

```text
[ ] 只读
[ ] 不下发命令
[ ] 不重启 Station
```

验收：

```text
[ ] Agent 能回答目标 Station 是否在线
[ ] 离线时给出部署阻断原因
```

---

### TODO 9.2 `runtime_package_precheck`

输入：

```json
{
  "flow": {},
  "targetStationId": "line-1-station-a"
}
```

检查项：

```text
[ ] 是否存在未确认参数
[ ] 是否缺少模型路径
[ ] 是否缺少相机绑定
[ ] 是否缺少 PLC 连接
[ ] 是否通过 validate_flow
[ ] 是否通过 dryrun_flow
[ ] 是否目标 Station 在线
[ ] 是否 Station 版本兼容
```

输出：

```json
{
  "ready": false,
  "blockingIssues": [
    "图像采集算子缺少 CameraBindingId",
    "YOLO 模型路径未确认"
  ],
  "warnings": [
    "未执行真实相机抓图验证"
  ]
}
```

验收：

```text
[ ] Agent 可以生成部署前阻断清单
[ ] 不自动导出运行包
[ ] 不自动部署到 Station
```

---

### TODO 9.3 `draft_runtime_package_manifest`

输出：

```json
{
  "packageName": "wire-sequence-line1-v1",
  "flowHash": "...",
  "requiredModels": [],
  "requiredCameraBindings": [],
  "requiredPlcConnections": [],
  "pendingApprovals": []
}
```

规则：

```text
[ ] 只生成 manifest 草稿
[ ] 不写入正式包目录
[ ] 不下发 Station
```

验收：

```text
[ ] Agent 能整理部署准备清单
[ ] 用户可看到需要补哪些资源
```

---

## 10. P7：前端 AI 面板增强

### TODO 10.1 Tool Trace 展示

在 AI 面板结果区域新增：

```text
Agent 调用记录
- list_operator_catalog ✅ 18ms
- get_operator_schema(ImageAcquisition) ✅ 5ms
- list_camera_bindings ✅ 22ms
- validate_flow ❌ 发现 1 个错误
- validate_flow ✅ 修复后通过
- dryrun_flow ✅ coverage 100%
```

验收：

```text
[ ] 普通模式展示摘要
[ ] Debug 模式可展开参数和结果
[ ] 失败工具高亮显示
[ ] ToolTrace 与 StageTimeline 不冲突
```

---

### TODO 10.2 待确认动作卡片与前端架构模块化

新增卡片类型：

```text
PendingAgentActionCard
```

示例：

```text
建议新增相机绑定：
厂商：Huaray
序列号：xxx
IP：192.168.1.88
触发模式：Software
像素格式：BayerRG8

[应用到相机设置草稿] [忽略]
```

部署准备卡片：

```text
运行包预检查未通过：
- 缺少 CameraBindingId
- 模型路径未确认
- Station line-1-station-a 离线

[打开参数审阅] [复制清单]
```

前端架构隔离要求：
由于 `aiPanel.js` (296KB) 文件已经过于庞大且为 Vanilla JS 原生开发，为了防范代码失控，严禁将跨组件的表单填充与路由跳转逻辑直接塞入 `aiPanel.js`。
- 新增 `wwwroot/src/features/ai/agentActionBridge.js` 专门负责拦截 ActionCard 事件。
- 桥接器处理跨组件（如与 Settings 相机设置页、Stations 部署页）的数据分发，并支持行内修改（Inline Edit）草稿后再应用。

验收：

```text
[ ] Agent 不能静默写配置
[ ] 所有 ConfigDraft / DeploymentPrepare 动作都展示卡片，且卡片支持用户在应用前行内微调（Inline Edit）参数
[ ] 用户确认后才通过 `agentActionBridge.js` 调用现有保存/导出链路，且设置页的表单数据能够同步刷写更新
```

---

## 11. P8：测试计划

### TODO 11.1 单元测试

新增：

```text
ClearVision.Product/tests/ClearVision.Product.Tests/AI/Tools/
```

测试文件建议：

```text
VisionAgentToolRegistryTests.cs
OperatorCatalogToolTests.cs
OperatorSchemaToolTests.cs
OperatorKnowledgeToolTests.cs
FlowTemplateToolTests.cs
CurrentFlowInspectToolTests.cs
FlowValidationToolTests.cs
DryRunFlowToolTests.cs
CameraAgentToolTests.cs
RuntimePackagePrecheckToolTests.cs
```

验收：

```text
[ ] 工具注册表测试通过，需包含 Transient / Scoped 生命周期装载与避免 Captive Dependency 的依赖注入测试
[ ] 每个工具正常结果 / 异常结果均覆盖
[ ] 工具参数 schema 快照测试
[ ] 未知工具 / 权限不足 / 参数错误有测试
```

---

### TODO 11.2 Agent Loop 集成测试

新增：

```text
VisionAgentLoopTests.cs
AiFlowGenerationServiceToolLoopTests.cs
GenerateFlowMessageHandlerToolTraceTests.cs
```

覆盖场景：

```text
[ ] 模型请求 list_operator_catalog，工具返回后模型生成 final_flow
[ ] 模型请求 get_operator_schema 后生成正确参数名
[ ] validate_flow 第一次失败，Agent 第二轮修复成功
[ ] dryrun_flow 失败后 Agent 标记 pendingParameters
[ ] 无相机绑定时 Agent 返回 MissingResources
[ ] 发现相机后 Agent 生成 draft_camera_binding，而不保存配置
[ ] runtime_package_precheck 返回部署阻断项，不执行部署
[ ] Agent 超过 MaxToolRounds 后进入 ManualRetry
```

---

### TODO 11.3 Prompt 瘦身测试

新增：

```text
AgentPromptBuilderTests.cs
PromptModeCompatibilityTests.cs
```

指标：

```text
[ ] legacy_full_prompt 长度保持可兼容
[ ] hybrid prompt 长度明显降低
[ ] agent_tools prompt 不包含 Full Catalog Fallback
[ ] agent_tools prompt 不包含 templateSkeletonJson
[ ] agent_tools prompt 不包含完整 few-shot examples
[ ] system prompt 中包含工具调用协议
```

---

## 12. 四个冲刺周期安排

## Sprint 1：工具骨架 + 算子工具 + PromptMode

目标：让 Agent 能查真实算子，Prompt 开始瘦身。

任务：

```text
[ ] 新增 IVisionAgentTool
[ ] 新增 IVisionAgentToolRegistry
[ ] 新增 ToolDescriptor / ToolResult / ToolTrace / ToolContext
[ ] 新增 VisionAgentToolPermission
[ ] 实现 list_operator_catalog
[ ] 实现 get_operator_schema
[ ] 新增 AgentPromptBuilder
[ ] 新增 PromptMode 配置：legacy_full_prompt / hybrid / agent_tools
[ ] hybrid 模式默认只注入核心算子 compact 信息
[ ] GenerateFlowResponse 增加 ToolTrace
[ ] 前端先以 JSON 面板展示 ToolTrace
[ ] 补单元测试
```

验收：

```text
用户输入“生成相机采图 + 模板匹配流程”时：
[ ] Agent 会查询算子目录和相关 schema
[ ] 生成流程中的 operatorType / portName / parameterName 来自真实 schema
[ ] hybrid 模式 prompt 明显短于 legacy 模式
[ ] 可一键切回 legacy_full_prompt
```

---

## Sprint 2：Agent Loop + 校验修复闭环

目标：让 Agent 从“一次生成”变为“生成后自检修复”。

任务：

```text
[ ] 新增 VisionAgentLoop
[ ] 定义 tool_call / final_flow 协议
[ ] 新增 VisionAgentProtocolParser
[ ] 接入 LLM 多轮 tool loop
[ ] 实现 inspect_current_flow
[ ] 实现 validate_flow
[ ] 将 validation errors 回填给模型
[ ] validate 失败自动修复最多 N 次
[ ] ToolTrace 写入 StageTimeline 或独立返回
[ ] 保留旧链路 fallback
[ ] 补集成测试
```

验收：

```text
[ ] 模型第一次生成缺失参数流程
[ ] validate_flow 返回错误
[ ] Agent 第二轮自动修复
[ ] 最终返回 Validator 通过的流程
[ ] 修改当前流程时，Agent 先 inspect_current_flow
```

---

## Sprint 3：知识/模板工具化 + DryRun 工具化

目标：将算子知识切片、模板骨架和 DryRun 从长 prompt / 固定链路迁移为工具。

任务：

```text
[ ] 实现 retrieve_operator_knowledge
[ ] 实现 match_flow_template
[ ] 实现 get_flow_template_skeleton
[ ] agent_tools 模式不再默认注入 Operator Knowledge Slice
[ ] agent_tools 模式不再默认注入 templateSkeletonJson
[ ] 实现 dryrun_flow
[ ] DryRun 结果结构化返回给模型
[ ] DryRun 失败后 Agent 修复或标记 pendingParameters
[ ] few-shot 瘦身为 0~1 个最小示例
[ ] 补 Prompt 瘦身测试
```

验收：

```text
线序检测请求：
[ ] Agent 先 match_flow_template
[ ] 再 get_flow_template_skeleton
[ ] 再 retrieve_operator_knowledge
[ ] 生成流程后 validate_flow
[ ] 通过后 dryrun_flow
[ ] prompt 中不再直接塞 templateSkeletonJson
```

---

## Sprint 4：相机工具 + 部署准备工具 + 前端动作卡片

目标：让 Agent 能调用真实 ClearVision 内部设备状态和部署准备能力，但不直接写配置/部署。

任务：

```text
[ ] 实现 list_camera_bindings
[ ] 实现 discover_cameras
[ ] 实现 capture_test_frame
[ ] 实现 draft_camera_binding
[ ] 实现 check_station_status
[ ] 实现 runtime_package_precheck
[ ] 实现 draft_runtime_package_manifest
[ ] 前端新增 Tool Trace 折叠面板
[ ] 前端新增 PendingAgentActionCard
[ ] ConfigDraft / DeploymentPrepare 全部走用户确认
[ ] 补相机工具和部署准备工具测试
```

验收：

```text
有相机绑定时：
[ ] Agent 生成 ImageAcquisition 并自动填入 CameraBindingId

无绑定但能发现相机时：
[ ] Agent 生成 draft_camera_binding
[ ] 不直接保存配置

无法发现相机时：
[ ] Agent 返回 MissingResources
[ ] 不崩溃

部署准备时：
[ ] Agent 输出 runtime_package_precheck 结果
[ ] 只生成 manifest 草稿
[ ] 不自动导出/下发 Station
```

---

## 13. Codex /goal 精简版

可直接交给 Codex 的目标指令：

```text
/goal
Upgrade ClearVision AI workflow generation into a Vision Engineering Agent with internal tool calling only.

Scope:
- Do NOT add CMD, PowerShell, shell, arbitrary filesystem, or OS-level permissions.
- Only expose ClearVision internal capabilities as Agent Tools.
- Preserve existing GenerateFlow behavior behind fallback / feature flags.
- Add PromptMode: legacy_full_prompt, hybrid, agent_tools.
- Reduce long prompt injection by moving operator catalog, operator knowledge slices, template skeletons, current flow inspection, validation, DryRun, camera bindings, and deployment precheck into tools.

Implement:
1. Core tool abstractions:
   - IVisionAgentTool
   - IVisionAgentToolRegistry
   - VisionAgentToolDescriptor
   - VisionAgentToolResult
   - VisionAgentToolTrace
   - VisionAgentToolContext
   - VisionAgentToolPermission

2. Agent loop:
   - AgentPromptBuilder
   - VisionAgentLoop
   - VisionAgentProtocolParser
   - tool_call / final_flow protocol
   - MaxToolRounds / MaxToolCallsPerRound / MaxToolResultChars
   - ToolTrace returned in GenerateFlowResponse

3. Internal tools:
   - list_operator_catalog
   - get_operator_schema
   - retrieve_operator_knowledge
   - match_flow_template
   - get_flow_template_skeleton
   - inspect_current_flow
   - validate_flow
   - dryrun_flow
   - list_camera_bindings
   - discover_cameras
   - capture_test_frame
   - draft_camera_binding
   - check_station_status
   - runtime_package_precheck
   - draft_runtime_package_manifest

4. Prompt migration:
   - legacy_full_prompt keeps current PromptBuilder behavior.
   - hybrid uses short prompt + core operator compact info + tools.
   - agent_tools uses short prompt + tool protocol only.
   - Do not inject Full Catalog Fallback, full Operator Knowledge Slice, templateSkeletonJson, or large few-shot examples in agent_tools mode.

5. Safety boundaries:
   - Agent can read internal state and run simulation.
   - Agent can generate config/deployment drafts.
   - Agent must not directly save camera/PLC settings or deploy to Station.
   - Any ConfigDraft / DeploymentPrepare action must be surfaced as a pending action card for user confirmation.

6. UI:
   - Show ToolTrace in AI panel.
   - Show pending action cards for camera binding drafts and deployment precheck results.

7. Tests:
   - Tool registry/unit tests.
   - Agent loop integration tests.
   - PromptMode compatibility tests.
   - validate_flow repair test.
   - dryrun_flow failure handling test.
   - camera tool no-device / discovered-device / existing-binding tests.
   - deployment precheck blocking issue tests.

Acceptance:
- Existing GenerateFlow tests continue to pass in legacy_full_prompt mode.
- In hybrid / agent_tools mode, the Agent can query real operator schemas, generate a flow, validate it, repair it if needed, dry-run it, and return ToolTrace.
- Prompt size is reduced because operator catalog, knowledge cards, and template skeletons are retrieved via tools instead of injected by default.
- No system-level command execution capability is introduced.
```

---

## 14. 最终验收总表

| 编号 | 验收项 | 必须通过 |
| --- | --- | --- |
| A1 | 不引入 CMD / shell / PowerShell 权限 | 是 |
| A2 | 所有 Agent Tools 都来自 ClearVision 内部服务 | 是 |
| A3 | legacy_full_prompt 模式兼容旧链路 | 是 |
| A4 | hybrid / agent_tools 模式能启用 Tool Loop | 是 |
| A5 | ToolTrace 返回前端并可展示 | 是 |
| A6 | Agent 能按需查询算子目录和 schema | 是 |
| A7 | Agent 不再依赖 Full Catalog Fallback | 是，agent_tools 模式 |
| A8 | Agent 能调用 validate_flow 并修复错误 | 是 |
| A9 | Agent 能调用 dryrun_flow 并处理失败 | 是 |
| A10 | Agent 能读取相机绑定 / 发现相机 | 是 |
| A11 | Agent 只能生成相机绑定草稿，不直接保存 | 是 |
| A12 | Agent 能做部署预检查，不直接部署 | 是 |
| A13 | Prompt 长度较 legacy 明显下降 | 是 |
| A14 | 现有 GenerateFlow 行为可 fallback | 是 |
| A15 | 单元测试、集成测试、PromptMode 测试覆盖 | 是 |

---

## 15. 关键判断

完成本 TODO 后，ClearVision AI 能力将从：

```text
自然语言 → 长 Prompt 注入 → 一次生成流程 → 校验 / DryRun
```

升级为：

```text
自然语言
  ↓
Vision Engineering Agent
  ↓
按需调用 ClearVision 内部工具
  ↓
查询算子 / 模板 / 当前流程 / 相机 / Station
  ↓
生成流程
  ↓
validate_flow 自检
  ↓
dryrun_flow 预演
  ↓
修复或标记待确认
  ↓
返回可应用流程 + ToolTrace + 待确认动作 + 部署准备清单
```

这就是 ClearVision 版的 Codex 工具调用能力：

> 不给它系统命令权限，
> 而是给它 ClearVision 内部工程工具。
> 让它从“会生成流程的 AI”升级为“会查证、会校验、会预演、会准备部署的视觉工程 Agent”。
