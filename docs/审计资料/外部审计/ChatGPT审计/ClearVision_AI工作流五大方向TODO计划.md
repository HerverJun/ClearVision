# ClearVision AI 工作流五大方向 TODO 计划

> 日期：2026-04-30  
> 范围：仅覆盖以下五个方向，不扩展到其他产品方向。  
> - 产品优化方向一：做强“模板优先”，不要让 LLM 自由发挥  
> - 产品优化方向二：把算子库变成“AI 可理解的工业知识图谱”  
> - 产品优化方向三：把“用户输入”从一句话升级为需求澄清闭环  
> - 产品优化方向四：把 UX 从“聊天框”升级为“AI 工程师工作台”（重点）  
> - 产品优化方向五：工程架构与 LLM 策略优化

---

## 0. 当前项目基线与本计划定位

### 0.1 已有基础

ClearVision 当前已经不是简单的“LLM 生成 JSON”项目，而是具备一条相对完整的 AI 工作流生成链路：

```text
用户自然语言/附件
  -> AI Panel
  -> GenerateFlowMessageHandler
  -> AiFlowGenerationService
  -> PromptBuilder
  -> FlowTemplateService / Scenario Package
  -> LLM Provider
  -> Parse JSON
  -> AiFlowValidator
  -> DryRunService
  -> Pending Parameters / Missing Resources / Manual Retry / Prompt Trace
  -> 前端人工确认
  -> 应用到流程编辑器
```

当前可直接复用的能力包括：

- 桌面端基于 WinForms + WebView2，本地后端端点承载流程编辑与运行能力。
- 算子体系已有 155 个正式算子，并统一到 Operator 元数据、端口、参数、质量证据框架。
- `FlowTemplateService` 已经具备内置模板与 `ScenarioPackageBinding`，包含端子线序、包装箱外观检测、空调内机外观检测、空调外机外观检测、遥控器漏装检测、两器铜孔间距检测等模板。
- `AiFlowGenerationService` 已具备模板优先入口、附件能力判断、PromptTrace、DryRun、推荐模板、待确认参数、缺失资源、ManualRetry 等结果字段。
- `AiFlowValidator` 已覆盖算子类型、端口存在性、端口类型兼容、DAG 环路、重复输入、参数默认值、参数范围等校验。
- 前端 `aiPanel.js` 已经不是单纯聊天框，而是 `ai-workspace` 结构，包含对话、历史、附件、生成算子清单、待补信息、参数补录与审核、PromptTrace、应用到环境等基础区域。
- 模型配置已支持 OpenAI compatible、Anthropic、Azure OpenAI、Ollama native 等协议，并具备 `RoleBindings`、能力声明、视觉输入能力、JSON Mode、Tool Call、Reasoning 等能力字段。

### 0.2 本计划的产品目标

把 ClearVision 从“会调用 LLM 的视觉流程生成器”升级成：

```text
面向工业视觉检测的 AI 工程师工作台：
先识别场景，优先套模板，基于算子知识图谱约束生成，
必要时主动澄清需求，生成后可验证、可预演、可人工接管、可沉淀成模板。
```

### 0.3 不做的事

- 不继续扩张算子数量。
- 不把 LLM 作为最终决策者。
- 不把合成数据、替代 replay 或本地测试结果包装成真实现场验收。
- 不优先做“大而全聊天助手”。本轮只围绕模板、算子知识、需求澄清、工程师工作台、LLM 架构收敛。
- 不重做整套前端，而是在现有 `AiPanel`、`flowCanvas`、GenerateFlow 消息契约上增强。

### 0.4 总体验收指标

| 指标 | 当前问题 | 目标口径 |
|---|---|---|
| 模板命中率 | 当前模板优先主要依赖线序关键词，其他内置模板尚未形成强匹配闭环 | 5 个核心场景的标准提示词 100% 命中正确模板候选 Top1 |
| LLM 自由发挥比例 | 非线序场景容易走自由生成 | 高频场景默认模板优先，自由生成仅作为 fallback |
| 首轮结构合法率 | 依赖 Prompt + Validator | 模板优先场景首轮 `AiFlowValidator.IsValid` ≥ 90% |
| 待确认参数闭环率 | 已有 PendingParameters，但产品动作仍需强化 | 生成后必须能在工作台中补录、确认、复审、应用 |
| 工作台可解释性 | 结果字段丰富，但部分信息隐藏或分散 | 用户能看到：为什么匹配该模板、缺什么、哪里有风险、DryRun 如何 |
| Prompt 成本与稳定性 | 当前可能向模型塞入较重上下文 | 按场景检索算子知识切片，减少无关算子干扰 |
| 现场可信边界 | 质量矩阵显示真实现场验证仍是缺口 | 工作台明确标记“功能可用但未完成现场工业验证”，不误导用户 |

---

## 1. 执行路线图

### Phase 0：止血式产品收敛（建议优先）

目标：让现有 AI 生成链路从“能生成”变成“可控生成”。

- [ ] 方向一：把现有模板匹配从线序关键词扩展为数据驱动的 `ScenarioMatcher`。
- [ ] 方向三：生成前新增 `RequirementBrief`，缺关键信息时先澄清，不急着调用 LLM。
- [ ] 方向四：把现有 `AiPanel` 的右侧结果区升级为工作台状态机，展示模板命中、缺资源、待确认参数、Validator、DryRun。
- [ ] 方向五：把 `AiFlowGenerationService` 中的生成链路拆成可测试 stage，但先不大规模重构外部 API。

### Phase 1：知识与评测闭环

目标：让 LLM 看到的不是“155 个算子大清单”，而是按场景检索出的工业知识切片。

- [ ] 方向二：生成 Operator Knowledge Graph，并让 PromptBuilder 使用相关算子子图。
- [ ] 方向一：建立模板 golden prompts 测试集与模板命中率评测。
- [ ] 方向三：把澄清问题、用户回答、参数补录写入会话上下文。
- [ ] 方向五：模型路由按任务角色拆分：意图识别、澄清、生成、修复、视觉附件理解。

### Phase 2：工程师工作台闭环

目标：让用户不只是“采纳一段 AI 结果”，而是完成一次可审查的视觉工程配置流程。

- [ ] 方向四：支持模板候选比较、生成结果 diff、分阶段应用、撤销、保存为模板、导出工程报告。
- [ ] 方向二：把质量矩阵证据、Known limitations、工业验证状态并入工作台风险提示。
- [ ] 方向五：离线/断网时进入模板向导降级模式，不阻塞用户配置高频场景。

---

# 方向一：做强“模板优先”，不要让 LLM 自由发挥

## 1.1 产品判断

ClearVision 已经有内置模板，但当前模板优先逻辑还偏窄：`AiFlowGenerationService` 里主要通过“线序、端子、接线顺序、排针顺序”等关键词触发 `template-first`。而 `FlowTemplateService` 实际已经内置了多个场景模板，说明产品基础已经具备，但还没有形成完整的“场景识别 -> 模板候选 -> 模板约束生成 -> 模板验证 -> 模板沉淀”闭环。

本方向的关键不是“多写几个 Prompt”，而是让 LLM 在模板骨架内补参数、补资源、补解释，而不是自由决定拓扑。

## 1.2 TODO 清单

### P0-1.1：新增数据驱动的 ScenarioMatcher，替换硬编码关键词匹配

目标：把 `_templateFirstKeywords` 和 `_wireTemplateHints` 从硬编码线序规则，升级为可配置、可测试的场景匹配器。

建议新增/调整文件：

- `Acme.Product/src/Acme.Product.Core/Entities/ScenarioDefinition.cs`
- `Acme.Product/src/Acme.Product.Infrastructure/AI/ScenarioMatcher.cs`
- `Acme.Product/tests/Acme.Product.Tests/AI/ScenarioMatcherTests.cs`

TODO：

- [ ] 定义 `ScenarioDefinition`：`scenarioKey`、`scenarioName`、`industry`、`keywords`、`synonyms`、`negativeKeywords`、`intentTypes`、`objectTypes`、`defectTypes`、`measurementTargets`、`requiredResources`、`templateId/templateName`。
- [ ] 从 `FlowTemplateService.CreateBuiltInTemplates()` 生成内置场景定义，不再只靠线序关键词。
- [ ] 为以下模板补齐场景匹配特征：
  - [ ] `wire-sequence-terminal`：端子线序检测。
  - [ ] 包装箱外观检测。
  - [ ] 空调内机外观检测。
  - [ ] 空调外机外观检测。
  - [ ] 遥控器漏装检测。
  - [ ] 两器铜孔间距检测。
- [ ] `ScenarioMatcher.Match(description, additionalContext, attachments)` 返回 TopN 候选，包含 `confidence`、`matchReason`、`matchedFields`、`missingSignals`。
- [ ] `AiFlowGenerationService.BuildTemplatePriorityContextAsync()` 改为调用 `ScenarioMatcher`，不再只判断线序关键词。

验收标准：

- [ ] 输入“检测包装箱破损、压痕、标签异常”，Top1 命中包装箱外观检测模板。
- [ ] 输入“空调内机面板划伤和缝隙检测”，Top1 命中空调内机外观检测模板。
- [ ] 输入“附件区域判断遥控器有没有漏装”，Top1 命中遥控器漏装检测模板。
- [ ] 输入“测量两器铜孔之间的距离是否合格”，Top1 命中两器铜孔间距检测模板。
- [ ] 输入“端子线序黑蓝顺序检测”，Top1 命中端子线序检测模板。
- [ ] 单测覆盖：中文同义词、英文关键词、行业词缺失、歧义场景、低置信度 fallback。

---

### P0-1.2：模板优先生成时，LLM 只能“补全”，不能随意改拓扑

目标：对高置信度模板命中的场景，把模板骨架作为主事实源，LLM 只负责补参数、解释、待确认项和缺资源。

建议新增字段：

- `AiRecommendedTemplateInfo.TemplateVersion`
- `AiRecommendedTemplateInfo.ScenarioKey`
- `AiGeneratedFlowJson.GenerationMode`：`template_fill` / `template_adapt` / `free_generate`
- `AiGeneratedFlowJson.TemplateLockLevel`：`strict` / `relaxed` / `none`

TODO：

- [ ] 当 `ScenarioMatcher.confidence >= 0.75` 时进入 `template_fill`。
- [ ] `template_fill` 模式下，传给 LLM 的指令明确：不得删除模板必需算子，不得替换核心拓扑，不得创造模板外算子。
- [ ] `template_fill` 模式下，优先把模板 `FlowJson` 转为 `AiGeneratedFlowJson` 初稿，再让 LLM 对参数、说明、待确认项做补全。
- [ ] 如果用户明确要求“换一种方案/不要用模板”，才进入 `template_adapt` 或 `free_generate`。
- [ ] 返回结果里展示模板命中原因、模板版本、场景 Key、锁定等级。

验收标准：

- [ ] 端子线序模板生成结果必须保留 `ImageAcquisition -> DeepLearning -> BoxFilter -> BoxNms -> DetectionSequenceJudge -> ResultOutput` 主干。
- [ ] 包装箱/空调外观检测必须保留 `ImageResize -> DeepLearning -> BoxFilter -> BoxNms -> ResultJudgment -> ResultOutput` 主干。
- [ ] 铜孔间距检测必须保留 `Filtering -> EdgeDetection -> GapMeasurement -> ResultJudgment -> ResultOutput` 主干。
- [ ] LLM 不能把模板主干替换成无关算子组合。

---

### P0-1.3：增加 Template Gate，把模板约束接入 Validator

目标：不仅让 Prompt 要求模板优先，还要在校验层证明模板没有被破坏。

建议新增/调整文件：

- `Acme.Product/src/Acme.Product.Infrastructure/AI/TemplateConstraintValidator.cs`
- `Acme.Product/tests/Acme.Product.Tests/AI/TemplateConstraintValidatorTests.cs`

TODO：

- [ ] 从 `FlowTemplate.ScenarioPackage.Constraints` 或模板 JSON 中抽取约束：必需算子、必需连线、必需资源、可调参数、禁止替换的节点。
- [ ] 在 `AiFlowValidator.Validate()` 后增加模板约束校验。
- [ ] 模板约束失败时返回结构化错误：`template_required_operator_missing`、`template_required_connection_missing`、`template_required_resource_missing`。
- [ ] 模板约束错误进入 `ManualRetry` 或自动修复队列。

验收标准：

- [ ] 手动删除模板中的 `ResultOutput`，Template Gate 报错。
- [ ] 手动删除线序模板中的 `DetectionSequenceJudge`，Template Gate 报错。
- [ ] `DeepLearning.ModelPath` 缺失时，不视为结构失败，但进入 `MissingResources` 与参数补录。

---

### P1-1.4：建立模板命中与生成质量评测集

目标：用测试证明模板优先策略稳定，而不是凭感觉判断。

建议新增目录：

- `quality/evals/ai_generation/template_prompts/`
- `quality/evals/ai_generation/reports/`

TODO：

- [ ] 每个内置模板至少准备 20 条 prompt：标准描述、口语描述、缺字段描述、含错误术语描述、含附件描述。
- [ ] 评测项包括：模板 Top1 命中、核心拓扑保持、Validator 通过、待确认参数完整、缺资源完整。
- [ ] 输出 Markdown 报告：`template_match_eval_report.md`。
- [ ] 将轻量评测加入 CI 或本地固定回归脚本。

验收标准：

- [ ] 核心模板 Top1 命中率 ≥ 95%。
- [ ] 模板优先场景核心拓扑保持率 ≥ 95%。
- [ ] 模板优先场景 Validator 首轮通过率 ≥ 90%。

---

### P1-1.5：在 AI 工程师工作台中加入模板候选区

目标：让用户知道系统为什么用了某个模板，也能手动切换模板。

TODO：

- [ ] 在 `aiPanel.js` 右侧新增“模板候选”卡片。
- [ ] 展示 Top3：模板名、行业、匹配置信度、命中词、缺失信息、模板版本。
- [ ] 用户可选择：`使用推荐模板`、`换一个模板`、`不用模板自由生成`。
- [ ] 选择结果写入 GenerateFlow 的 `hint` 或新增 `templateSelection` 字段。

验收标准：

- [ ] 用户输入歧义需求时，可看到多个模板候选。
- [ ] 用户强制选择模板后，后端按该模板生成。
- [ ] 生成结果中保留用户选择过的模板版本。

---

### P2-1.6：模板生命周期管理

目标：让用户能把成功方案沉淀为模板，而不是每次从零生成。

TODO：

- [ ] 在工作台中增加“保存为模板”。
- [ ] 生成模板时自动提取：场景关键词、待确认参数、缺资源、核心拓扑、版本号。
- [ ] 模板新增 `status`：`draft` / `approved` / `deprecated`。
- [ ] 模板版本升级时保留迁移记录。
- [ ] 支持把现场调试后的参数固化为“客户模板”或“产线模板”。

验收标准：

- [ ] 用户从 AI 生成结果保存模板后，下次相似需求可命中该模板。
- [ ] 模板升级不破坏旧会话记录。
- [ ] 废弃模板不再作为默认候选，但历史可追溯。

---

# 方向二：把算子库变成“AI 可理解的工业知识图谱”

## 2.1 产品判断

ClearVision 的算子体系已经很强：155 个正式算子、端口、参数、质量矩阵、算子名片、证据等级都已有基础。但 LLM 真正需要的不是“把所有算子塞进 Prompt”，而是：

```text
在当前场景下，哪些算子适合？
它们的输入输出如何衔接？
哪些参数必须确认？
哪些算子只是备选？
哪些场景不要用它？
它的证据等级与工业验证边界是什么？
```

因此，本方向的核心是把算子库从“人看的文档 + 运行时元数据”升级为“LLM 可检索、Validator 可复用、工作台可解释”的知识图谱。

## 2.2 TODO 清单

### P0-2.1：定义 Operator Knowledge Card JSON Schema

目标：为每个算子生成 AI 可理解的结构化知识卡片。

建议新增目录：

- `docs/ai/operator-knowledge/cards/`
- `docs/ai/operator-knowledge/operator_knowledge_schema.json`
- `docs/ai/operator-knowledge/operator_knowledge_graph.json`

建议每个算子卡片包含：

```json
{
  "operatorType": "DeepLearning",
  "displayName": "深度学习",
  "category": "AI检测",
  "aliases": ["YOLO", "目标检测", "缺陷检测"],
  "intentTags": ["defect_detection", "object_detection"],
  "scenarioTags": ["appearance_inspection", "wire_sequence"],
  "inputs": [],
  "outputs": [],
  "parameters": [],
  "requiredResources": ["ModelPath"],
  "typicalUpstream": ["ImageResize", "ImageAcquisition"],
  "typicalDownstream": ["BoxFilter", "BoxNms", "ResultJudgment"],
  "antiPatterns": [],
  "knownLimitations": [],
  "evidence": {
    "contract": "Yes/No",
    "golden": "Yes/No",
    "dataset": "Yes/Partial/No",
    "fieldReplay": "Yes/No",
    "industrialStatus": "功能可用但未完成现场工业验证"
  }
}
```

TODO：

- [ ] 从 `OperatorMetadata` 生成基础字段：类型、显示名、分类、输入端口、输出端口、参数、默认值、范围、是否必填。
- [ ] 从算子名片补充：算法摘要、已知限制、适用场景、误用场景。
- [ ] 从 `operator_quality_matrix.md` 补充证据状态、QScore、工业验证状态。
- [ ] 每个算子至少具备：`intentTags`、`requiredResources`、`typicalUpstream`、`typicalDownstream`。
- [ ] 对 AI 相关算子优先补齐：`DeepLearning`、`SemanticSegmentation`、`AnomalyDetection`、`SurfaceDefectDetection`、`BoxFilter`、`BoxNms`、`ResultJudgment`、`DetectionSequenceJudge`。

验收标准：

- [ ] 155 个正式算子都有 Knowledge Card。
- [ ] 生成工具能检查卡片与运行时元数据是否一致。
- [ ] 缺端口、缺参数、未知算子会导致生成工具失败。

---

### P0-2.2：生成 Operator Knowledge Graph 边关系

目标：让系统知道算子之间的工业组合关系，而不仅是端口能不能连。

建议边类型：

- `PRODUCES`：算子输出某类数据。
- `CONSUMES`：算子消费某类数据。
- `COMMONLY_PRECEDES`：常见上游关系。
- `COMMONLY_FOLLOWS`：常见下游关系。
- `USED_IN_TEMPLATE`：出现在某模板中。
- `REQUIRES_RESOURCE`：依赖模型、标定、相机、PLC、文件路径等资源。
- `HAS_EVIDENCE`：绑定质量证据。
- `ALIAS_OF`：legacy alias 或语义别名。
- `NOT_RECOMMENDED_WITH`：不建议组合。

TODO：

- [ ] 由端口类型自动生成基础 `PRODUCES/CONSUMES`。
- [ ] 由模板 JSON 自动生成 `COMMONLY_PRECEDES/COMMONLY_FOLLOWS`。
- [ ] 由 `ScenarioPackageBinding.RequiredResources` 生成 `REQUIRES_RESOURCE`。
- [ ] 由质量矩阵生成 `HAS_EVIDENCE`。
- [ ] 输出 `operator_knowledge_graph.json`。

验收标准：

- [ ] 查询 `DeepLearning` 能返回常见下游：`BoxFilter`、`BoxNms`、`ResultJudgment`、`DetectionSequenceJudge`。
- [ ] 查询 `GapMeasurement` 能返回上游：`EdgeDetection`，下游：`ResultJudgment`、`ResultOutput`。
- [ ] 查询某模板能返回其核心算子链。

---

### P0-2.3：PromptBuilder 改为“按场景取知识切片”

目标：减少无关算子干扰，降低 Prompt 成本，提高生成稳定性。

TODO：

- [ ] `PromptBuilder.BuildSystemPrompt(userDescription)` 内部调用 `OperatorKnowledgeRetriever`。
- [ ] 检索输入包括：场景候选、意图、模板、当前流程、用户附件元信息。
- [ ] 返回相关算子子集，而不是默认塞入全部 155 个算子。
- [ ] 高置信度模板场景只发送模板涉及算子 + 少量备选算子。
- [ ] 低置信度自由生成场景才发送更大的候选集。

验收标准：

- [ ] 线序场景 Prompt 中优先出现 `DeepLearning`、`BoxFilter`、`BoxNms`、`DetectionSequenceJudge`、`ResultOutput`。
- [ ] 铜孔间距场景 Prompt 中优先出现 `Filtering`、`EdgeDetection`、`GapMeasurement`、`ResultJudgment`、`ResultOutput`。
- [ ] 与当前全量目录相比，模板场景 Prompt 中算子目录 token 体量明显下降。

---

### P1-2.4：把知识图谱作为 Validator 与工作台的共同事实源

目标：避免 Prompt、Validator、UI 三套规则各说各话。

TODO：

- [ ] `AiFlowValidator` 在端口/参数校验之外，读取知识图谱中的 `antiPatterns` 与 `requiredResources`。
- [ ] `AiPanel` 的算子清单卡片展示：算子角色、证据等级、已知限制、缺资源。
- [ ] 工作台中点击某算子，显示“为什么选择它”和“它通常接在哪些算子后面”。
- [ ] 对质量矩阵中“未完成现场工业验证”的算子显示风险提示。

验收标准：

- [ ] 用户能在工作台中看到 DeepLearning 缺 `ModelPath` 的资源提示。
- [ ] 用户能看到 TemplateMatching/CaliperTool 等算子的证据状态与现场验证边界。
- [ ] UI 展示的参数范围与 Validator 使用的参数范围一致。

---

### P1-2.5：知识图谱生成质量门禁

目标：把“算子知识可被 AI 使用”变成可验证资产。

TODO：

- [ ] 新增 `OperatorKnowledgeGraphGenerator`。
- [ ] 新增 `OperatorKnowledgeGraphTests`。
- [ ] CI 检查：155 个正式算子必须都有卡片。
- [ ] CI 检查：卡片中的端口/参数必须与 `OperatorMetadata` 一致。
- [ ] CI 检查：卡片中的 `operatorType` 必须可被 `Enum.TryParse<OperatorType>` 解析。

验收标准：

- [ ] 新增算子但未生成知识卡片时，测试失败。
- [ ] 修改参数名但未更新知识卡片时，测试失败。
- [ ] PromptBuilder 只能使用通过校验的知识卡片。

---

### P2-2.6：支持“替代方案解释”

目标：让 AI 工程师工作台不只是给答案，还能解释为什么不用另一个算子。

TODO：

- [ ] 对同一意图返回候选算子排序。
- [ ] 对候选给出理由：数据类型、模板历史、证据状态、参数复杂度、是否需要资源。
- [ ] 在工作台中展示“本次选择 DeepLearning，而不是传统 BlobAnalysis 的原因”。
- [ ] 支持用户切换“传统方案 / AI 方案 / 混合方案”。

验收标准：

- [ ] 外观缺陷场景可解释 AI 检测与传统阈值/Blob 的取舍。
- [ ] 测量场景可解释 CaliperTool、GapMeasurement、LineMeasurement 的取舍。

---

# 方向三：把“用户输入”从一句话升级为需求澄清闭环

## 3.1 产品判断

当前 `GenerateFlowRequestPayload` 已有 `Description`、`Hint`、`SessionId`、`ExistingFlowJson`、`Mode`、`Attachments` 等字段，但用户入口仍以自然语言为主。工业视觉检测的真实需求通常必须明确：检测对象、缺陷类别、相机来源、模型文件、ROI、阈值、PLC/输出方式、标定状态、OK/NG 判定逻辑。直接把一句话交给 LLM，容易让模型补出看似合理但现场不可用的参数。

因此，生成前应增加“需求澄清”，生成后通过 `PendingParameters` 与 `MissingResources` 做参数闭环。

## 3.2 TODO 清单

### P0-3.1：新增 RequirementBrief 数据结构

目标：把用户的一句话解析成可确认的工程需求卡。

建议新增文件：

- `Acme.Product/src/Acme.Product.Core/DTOs/RequirementBriefDto.cs`
- `Acme.Product/src/Acme.Product.Infrastructure/AI/RequirementBriefExtractor.cs`

建议字段：

```text
RequirementBrief
- sceneType：appearance_defect / measurement / code_reading / wire_sequence / missing_part / calibration / other
- industry：线束装配 / 空调制造 / 包装终检 / 通用制造 ...
- objectName：产品/部件名
- defectTypes：划伤、破损、脏污、漏装、错序 ...
- measurementTargets：孔距、间距、圆心距离 ...
- imageSource：camera / file / unknown
- triggerMode：software / hardware / continuous / unknown
- outputTarget：ResultOutput / PLC / Database / unknown
- aiModelRequired：true/false/unknown
- modelResource：ModelPath / LabelsPath / missing
- roiRequirement：none / region / unknown
- calibrationRequirement：none / pixel_to_world / hand_eye / unknown
- decisionRule：OK/NG 判定逻辑
- confidence：需求理解置信度
- missingFields：缺失字段
```

TODO：

- [ ] 先用规则 + ScenarioMatcher 生成基础 `RequirementBrief`。
- [ ] 只有复杂/歧义需求才调用轻量 LLM 做补充解析。
- [ ] `RequirementBrief` 写入会话上下文，后续 Modify/Review 模式复用。
- [ ] 附件元信息进入 `RequirementBrief`，例如图片数量、分辨率、是否可发送给模型。

验收标准：

- [ ] “检测空调内机面板划伤”解析出：`sceneType=appearance_defect`、`industry=空调制造`、`defectTypes=[划伤]`。
- [ ] “测量两个孔的圆心距离”解析出：`sceneType=measurement`、`measurementTargets=[孔距/圆心距离]`。
- [ ] “端子线序黑蓝顺序检测”解析出：`sceneType=wire_sequence`、`expectedSequence=[黑, 蓝]`。

---

### P0-3.2：新增 ClarificationEngine，缺关键信息先问问题

目标：在关键字段缺失时，不直接调用生成模型。

建议新增文件：

- `Acme.Product/src/Acme.Product.Infrastructure/AI/ClarificationEngine.cs`
- `Acme.Product/tests/Acme.Product.Tests/AI/ClarificationEngineTests.cs`

TODO：

- [ ] 定义澄清级别：`required` / `recommended` / `optional`。
- [ ] 每个模板定义最小必需字段，例如：
  - 线序：期望顺序、模型路径、ROI、排序方向。
  - 外观检测：缺陷类型、模型路径、ROI、OK/NG 判定。
  - 漏装检测：目标类别、期望数量、ROI、模型路径。
  - 铜孔间距：测量方向、合格范围、是否需要像素到物理单位换算。
- [ ] 生成前若缺 `required` 字段，返回 `ClarificationRequired`，而不是返回 Flow。
- [ ] 每次最多问 3 个最关键问题，避免用户被长表单吓退。
- [ ] 用户回答后合并到 `RequirementBrief`，再继续模板匹配/生成。

建议新增响应字段：

```text
GenerateFlowResponse
- ClarificationRequired: bool
- RequirementBrief: object
- ClarificationQuestions: [
    { field, question, options, required, reason }
  ]
```

验收标准：

- [ ] 用户只说“检测缺陷”，系统先问产品/缺陷类型/是否有模型，而不是直接生成。
- [ ] 用户说“用 YOLO 检测包装箱破损”，系统可以继续生成，但把 `ModelPath` 放入缺资源。
- [ ] 用户说“测铜孔间距”，系统询问合格范围或单位换算需求。

---

### P0-3.3：前端增加“需求卡片”

目标：把澄清从聊天来回问，变成工程化表单 + 可编辑卡片。

建议调整文件：

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js`
- 对应 CSS 文件。

TODO：

- [ ] 在 AI 工作台右侧或顶部新增“需求卡片”。
- [ ] 展示系统已识别字段：场景、行业、检测对象、缺陷类型、输出目标、资源状态。
- [ ] 缺失字段用黄色/红色标记。
- [ ] 支持用户用表单补充，不必须继续打字。
- [ ] 补充后自动更新 `hint` 或新增 `requirementBrief` payload。

验收标准：

- [ ] 用户输入一句话后，需求卡片自动生成。
- [ ] 用户能直接在卡片中选择“包装箱外观检测 / 遥控器漏装 / 铜孔间距”。
- [ ] 用户补充字段后再次生成，不丢失会话上下文。

---

### P1-3.4：把 PendingParameters / MissingResources 变成后置澄清闭环

目标：当前已有 `PendingParameters` 和 `MissingResources`，要把它们从提示信息升级为可完成的工程动作。

TODO：

- [ ] `PendingParameters` 每项绑定算子、参数定义、默认值、范围、输入控件类型。
- [ ] `MissingResources` 每项绑定处理动作：选择模型文件、选择标签文件、配置相机、配置 PLC、选择 ROI。
- [ ] 用户补齐后调用现有 `review_pending_parameters` 模式复核。
- [ ] 参数确认后在工作台中显示“已确认”，并允许再次编辑。

验收标准：

- [ ] `DeepLearning.ModelPath` 缺失时，用户能通过文件选择器补齐。
- [ ] `BoxFilter.RegionX/Y/W/H` 缺失时，用户能通过 ROI 选择或数字输入补齐。
- [ ] 补齐参数后，`review_pending_parameters` 能保持流程结构稳定，仅更新参数。

---

### P1-3.5：澄清记忆与默认值沉淀

目标：让用户不用每次重复填写相同产线信息。

TODO：

- [ ] 会话级记忆：当前会话中的模型路径、相机、PLC、ROI、阈值。
- [ ] 模板级默认值：某模板常用参数默认值。
- [ ] 产线级默认值：某工位相机/PLC/输出路径。
- [ ] 用户可选择“仅本次使用 / 保存为模板默认 / 保存为产线默认”。

验收标准：

- [ ] 同一会话中第二次生成相似流程时，不重复询问已确认的模型路径。
- [ ] 保存为模板默认后，下次命中该模板时自动带入默认参数，但仍标记为可复核。

---

### P2-3.6：附件驱动的澄清

目标：让用户上传图片后，系统能根据图片元信息减少问题数量。

TODO：

- [ ] 利用现有附件分析能力展示图片数量、分辨率、是否发送成功、是否因模型不支持视觉而降级。
- [ ] 如果模型支持视觉输入，先让模型做“场景观察摘要”，但不直接做最终判定。
- [ ] 图片观察结果进入 `RequirementBrief`，例如“疑似包装箱外观场景”。
- [ ] 若模型不支持图片，明确提示“附件仅用于元信息”。

验收标准：

- [ ] 上传图片后，工作台显示附件状态与模型视觉能力。
- [ ] 不支持视觉模型时，系统不会假装看过图片。

---

# 方向四：把 UX 从“聊天框”升级为“AI 工程师工作台”（重点）

## 4.1 产品判断

`aiPanel.js` 当前已经有 `ai-workspace`、左侧对话、右侧结果、生成算子清单、待补信息、参数补录、PromptTrace、应用按钮，说明你已经走出了“单聊天框”的第一步。下一步不应重写，而是把它升级为真正的工程工作台：

```text
不是让用户看 AI 聊天，
而是让用户完成一次工程配置：
识别需求 -> 选择模板 -> 补参数 -> 验证 -> 预演 -> 应用 -> 保存模板/报告。
```

## 4.2 工作台目标布局

建议在现有左右两栏基础上演进为“主工作区 + 工程侧栏 + 底部流水线状态”：

```text
┌─────────────────────────────────────────────────────────────┐
│ 顶部：AI 工程师工作台状态条                                  │
│ 场景识别 | 模板匹配 | 需求澄清 | 生成 | 校验 | DryRun | 应用   │
├───────────────┬───────────────────────────┬─────────────────┤
│ 左：对话/输入  │ 中：流程预览/画布/Diff      │ 右：工程审核面板 │
│ 附件/快捷示例  │ 当前流程 vs AI 方案         │ 模板/参数/风险   │
│ 需求卡片入口   │ 节点高亮/连线检查           │ Validator/DryRun │
├───────────────┴───────────────────────────┴─────────────────┤
│ 底部：事件时间线 / 模型调用 / 生成日志 / ManualRetry          │
└─────────────────────────────────────────────────────────────┘
```

如果短期不改三栏，也可以先在现有右侧 `ai-result-pane` 中以 Tab 形式实现：

```text
[需求] [模板] [流程] [参数] [验证] [应用] [调试]
```

## 4.3 TODO 清单

### P0-4.1：把 AiPanel 升级为显式状态机

目标：让工作台状态清晰，而不是靠 `isGenerating/currentResult/pendingManualRetry` 等变量隐式组合。

建议新增状态：

```text
idle
clarifying
matching_template
generating
parsing
validating
dry_running
reviewing_parameters
ready_to_apply
applying
applied
failed
cancelled
```

TODO：

- [ ] 新增 `AiWorkbenchState` 常量或状态管理模块。
- [ ] 把当前 `isGenerating`、`isCancellingGenerate`、`currentResultVersion`、`appliedResultVersion` 等状态映射到统一状态机。
- [ ] 顶部显示工作台阶段条，当前阶段高亮，失败阶段显示原因。
- [ ] 所有 GenerateFlowProgress 进入阶段时间线，而不是只追加聊天文本。
- [ ] `ManualRetry`、`ClarificationRequired`、`ValidationFailed`、`DryRunSkipped` 都映射为明确状态。

验收标准：

- [ ] 用户能一眼看到当前处于“模板匹配 / 生成 / 校验 / 待补参数 / 可应用”的哪个阶段。
- [ ] 取消生成后状态回到 `cancelled`，不会误显示可应用。
- [ ] 生成失败但上一版结果仍可应用时，状态条明确区分“本轮失败 / 右侧保留上一版”。

---

### P0-4.2：新增“模板匹配”工作台卡片

目标：把模板优先策略产品化，让用户信任系统不是乱生成。

TODO：

- [ ] 在右侧结果区新增“模板匹配”卡片。
- [ ] 展示：推荐模板名、行业、版本、场景 Key、置信度、命中关键词、缺失信号。
- [ ] 支持 Top3 模板候选切换。
- [ ] 支持“强制使用此模板 / 允许轻微改造 / 不使用模板”三种模式。
- [ ] 模板卡片展示核心拓扑摘要，例如：`ImageAcquisition -> DeepLearning -> BoxNms -> ResultJudgment -> ResultOutput`。

验收标准：

- [ ] 线序场景显示端子线序模板及其版本。
- [ ] 包装箱外观检测显示包装箱模板，并展示需要 `DeepLearning.ModelPath`。
- [ ] 用户切换模板后，重新生成使用新模板。

---

### P0-4.3：新增“需求卡片 + 缺口清单”工作台卡片

目标：把方向三的需求澄清在 UX 中落地。

TODO：

- [ ] 右侧增加“需求卡片”。
- [ ] 字段包括：检测对象、缺陷/测量目标、输入源、触发方式、输出方式、模型资源、ROI、判定逻辑。
- [ ] 字段状态：已识别、待确认、缺失、用户已确认。
- [ ] 缺失字段直接生成可点击动作：`选择模型文件`、`绘制 ROI`、`配置 PLC`、`填写阈值`。
- [ ] 需求卡片可折叠，避免占据过多空间。

验收标准：

- [ ] “检测包装箱破损”至少显示检测对象=包装箱、缺陷=破损、模型资源=缺失。
- [ ] 用户在需求卡片中补充字段后，Prompt/Hint 能拿到更新后的结构化上下文。

---

### P0-4.4：升级“生成的算子清单”为工程审核视图

目标：让用户不仅看到算子名字，还能看到每个算子的工程角色与风险。

TODO：

- [ ] 每个算子显示：角色、输入输出摘要、关键参数、资源状态、证据状态、风险标签。
- [ ] 对模板核心算子打上“模板锁定”标记。
- [ ] 对待确认参数打上黄色标记。
- [ ] 对缺资源打上红色标记。
- [ ] 点击算子时，在画布中高亮对应节点和上下游连线。
- [ ] 支持“为什么选它”解释：来自模板、来自知识图谱、来自用户需求、来自 LLM 补全。

验收标准：

- [ ] `DeepLearning` 节点显示 `ModelPath` 缺失和 `TargetClasses`。
- [ ] `BoxNms` 节点显示 `IouThreshold/ScoreThreshold` 可调。
- [ ] `ResultJudgment` 节点显示 OK/NG 判定逻辑。

---

### P0-4.5：新增 Validator / DryRun 控制台

目标：把校验结果从后台日志变成用户可见的工程证据。

TODO：

- [ ] 新增“验证”Tab 或卡片。
- [ ] 展示 `AiFlowValidator` 的结构化错误/警告：算子不存在、端口错误、类型不兼容、环路、参数越界、缺源算子、缺输出算子。
- [ ] 展示 DryRun 结果：是否成功、覆盖率、分支数、跳过原因。
- [ ] 对每个问题提供动作：自动修复、回到参数编辑、重新生成、忽略警告。
- [ ] ManualRetry 展示原始问题摘要、修复目标、草稿，而不是只追加到输入框。

验收标准：

- [ ] 端口错误能在工作台中定位到具体连线。
- [ ] 参数越界能显示旧值、新值和原因。
- [ ] DryRun 异常时，用户能看到“已跳过/失败”的结构化原因。

---

### P0-4.6：应用流程改为“分阶段应用 + 可撤销”

目标：降低用户把 AI 结果直接应用到画布的风险。

TODO：

- [ ] `应用到环境` 前增加预览确认：将新增/修改/删除哪些节点和连线。
- [ ] 对当前画布与 AI 方案做 diff：新增节点、删除节点、参数变更、连线变更。
- [ ] 应用后保存上一版快照，支持撤销。
- [ ] 应用后显示“已应用版本号/会话号/模板号”。
- [ ] 应用后可一键“保存为模板”。

验收标准：

- [ ] 用户应用前能看到流程变更摘要。
- [ ] 应用后能撤回到应用前画布。
- [ ] 同一生成结果不可重复误应用，或重复应用时给出明确提示。

---

### P1-4.7：参数补录组件工程化

目标：把参数补录从文本输入升级为工业配置控件。

TODO：

- [ ] 根据参数类型渲染控件：数字、枚举、布尔、文件、路径、ROI、相机绑定、PLC 地址。
- [ ] 文件参数支持文件选择器，例如 `ModelPath`、`LabelsPath`。
- [ ] ROI 参数支持从图像或画布中选择。
- [ ] PLC/Modbus 参数支持地址、寄存器、功能码校验。
- [ ] 参数修改后实时更新当前 Flow DTO，并可提交 `review_pending_parameters`。

验收标准：

- [ ] 用户不需要手写模型路径字符串。
- [ ] 用户不需要手工输入 ROI 四个数字也能完成区域选择。
- [ ] 参数补录完成后，待确认状态自动消失或转为已确认。

---

### P1-4.8：附件与多模态能力面板

目标：让用户清楚知道图片有没有被模型看到。

TODO：

- [ ] 附件区展示缩略图、名称、大小、分辨率、发送状态。
- [ ] 如果模型不支持视觉输入，显示“已降级文本模式”。
- [ ] 如果图片过大/格式不支持，显示跳过原因。
- [ ] 在 PromptTrace 中保留附件摘要，但普通模式不展示敏感路径。

验收标准：

- [ ] 模型不支持图片时，用户明确知道图片没有进入模型。
- [ ] 附件被跳过时，用户能看到原因。

---

### P1-4.9：调试信息分层展示

目标：PromptTrace 对开发有价值，但普通用户不应被原始 Prompt 淹没。

TODO：

- [ ] 普通模式只显示工程摘要：模板、模型、阶段耗时、校验结果。
- [ ] Debug 模式才显示 SystemPrompt、UserPrompt、Capabilities、附件报告。
- [ ] PromptTrace 中敏感信息脱敏：本地路径、API Key、客户文件名、内部网络地址。
- [ ] Reasoning/Thinking 不作为普通用户可见主内容，改为“工程摘要”。

验收标准：

- [ ] `?debugPrompt=1` 或本地开关打开时可见 PromptTrace。
- [ ] 普通用户界面不展示长 Prompt 和原始推理内容。
- [ ] 不在 UI 中泄露 API Key 或敏感路径。

---

### P1-4.10：历史会话升级为工程记录

目标：AI 历史不是聊天记录，而是工程方案记录。

TODO：

- [ ] 历史列表显示：场景、模板、生成状态、是否应用、是否保存模板。
- [ ] 会话详情显示：需求卡片、模板、参数补录、校验、DryRun、应用版本。
- [ ] 支持按模板/行业/状态筛选历史。
- [ ] 支持从历史方案重新生成、复制为模板、导出报告。

验收标准：

- [ ] 用户能找到“上次包装箱外观检测方案”。
- [ ] 用户能看到该方案有没有应用到画布。
- [ ] 历史方案能恢复到工作台进行修改。

---

### P2-4.11：工作台指标埋点

目标：用数据判断工作台是否真的提高效率。

TODO：

- [ ] 记录模板命中率、用户改选模板次数、澄清问题完成率。
- [ ] 记录 Validator 首轮通过率、DryRun 成功率、ManualRetry 触发率。
- [ ] 记录应用率、撤销率、保存模板率。
- [ ] 生成本地 Markdown 或 JSON 报告，不上传外部服务。

验收标准：

- [ ] 一次完整生成链路可输出工作台事件报告。
- [ ] 指标可按模板/模型/场景聚合。

---

### P2-4.12：方案对比与替代路线

目标：让用户能比较“模板方案 / 自由生成方案 / 传统视觉方案 / AI 检测方案”。

TODO：

- [ ] 支持生成 2~3 个候选方案，但模板优先方案默认排第一。
- [ ] 对比维度：算子数量、资源需求、参数待确认、证据状态、现场验证风险、执行复杂度。
- [ ] 用户可选择其中一个应用到画布。

验收标准：

- [ ] 外观检测场景可比较 AI 检测方案与传统阈值/Blob 方案。
- [ ] 测量场景可比较 Caliper/GapMeasurement/LineMeasurement 方案。

---

# 方向五：工程架构与 LLM 策略优化

## 5.1 产品判断

当前 `AiFlowGenerationService` 承担了较多职责：准备上下文、模板匹配、Prompt 构建、模型调用、解析、校验、DryRun、结果组装、会话记录等。短期可运行，但未来要做模板优先、知识图谱、澄清闭环、工作台，就需要把生成链路拆成可测试、可观察、可替换的阶段。

此外，模型配置已经有 `RoleBindings`、`Priority`、`Capabilities`、`SupportsVisionInput`、`SupportsJsonMode` 等字段，但还需要真正落地“按任务选择模型”的策略。

## 5.2 TODO 清单

### P0-5.1：把 AiFlowGenerationService 拆成阶段化 Pipeline

目标：不改变外部 GenerateFlow API 的前提下，把内部拆成可测试 stage。

建议阶段：

```text
1. RequirementBriefExtractor
2. ClarificationEngine
3. ScenarioMatcher
4. TemplateContextBuilder
5. OperatorKnowledgeRetriever
6. PromptContextBuilder
7. LlmGenerationClient
8. ResponseParser
9. AiFlowValidator
10. TemplateConstraintValidator
11. AutoRepairEngine
12. DryRunExecutor
13. ResultAssembler
14. ConversationRecorder
```

TODO：

- [ ] 新增 `AiGenerationPipelineContext`，贯穿 request、brief、scenario、template、prompt、raw response、validation、dryrun、result。
- [ ] 先抽出纯函数/服务，不急着改对外接口。
- [ ] 每个 stage 输出结构化诊断，供工作台时间线展示。
- [ ] `GenerateFlowMessageHandler` 保持现有消息契约，但逐步增加澄清字段。

验收标准：

- [ ] 每个 stage 能单独单测。
- [ ] 一次生成可输出完整 stage timeline。
- [ ] 失败时能定位是澄清、模板、LLM、解析、校验还是 DryRun 阶段。

---

### P0-5.2：模型路由策略按任务角色拆分

目标：不要所有任务都用同一个模型。

建议策略：

| 任务 | 模型要求 | 可用能力字段 |
|---|---|---|
| 意图识别/场景匹配 | 快、低成本、可本地规则优先 | `RoleBindings=generation/validation` 或新增 `classification` |
| 需求澄清问题生成 | 便宜、中文表达好 | `SupportsSystemPrompt` |
| 工作流 JSON 生成 | JSON 稳定、遵守 Schema | `SupportsJsonMode=true` |
| 图片理解 | 支持视觉输入 | `SupportsVisionInput=true` |
| 修复/解释 | 推理能力强 | `Reasoning.Mode` / `SupportsReasoningStream` |
| 离线降级 | 不依赖外部 LLM | 模板向导 |

TODO：

- [ ] 扩展 `RoleBindings`：`classification`、`clarification`、`generation`、`repair`、`vision`、`validation`、`fallback`。
- [ ] `ActiveAiModelSelector` 支持按任务角色选择模型。
- [ ] 当当前模型 `SupportsVisionInput=false` 时，附件自动降级但工作台明确提示。
- [ ] 当模型不支持 JSON Mode 时，PromptBuilder 使用更强格式约束，Parser 开启修复流程。
- [ ] 加入 fallback 顺序：主模型失败 -> 备用模型 -> 模板向导。

验收标准：

- [ ] `reasoner` 类模型不会被用于图片输入。
- [ ] 视觉场景自动选择支持图片的模型。
- [ ] 主模型超时后能进入备用模型或模板降级。

---

### P0-5.3：结构化输出强约束

目标：降低非法 JSON、乱字段、乱算子类型的概率。

TODO：

- [ ] 为 `AiGeneratedFlowJson` 增加 `schemaVersion`。
- [ ] 生成 JSON Schema：包括 `operators`、`connections`、`parametersNeedingReview`、`recommendedTemplate`、`pendingParameters`、`missingResources`。
- [ ] 若模型 `SupportsJsonMode=true`，优先使用 JSON Mode。
- [ ] 若模型支持 Tool Call，探索以“生成工作流草案工具”形式返回结构化对象。
- [ ] Parser 对非法 JSON 做最小修复，但修复记录必须进入诊断。

验收标准：

- [ ] 模型返回 Markdown 包裹 JSON 时能修复或进入 ManualRetry。
- [ ] 生成结果缺 `operators` 时明确失败。
- [ ] `schemaVersion` 不兼容时明确提示。

---

### P0-5.4：Prompt 版本与评测闭环

目标：每次 Prompt 修改都能被评测，而不是凭感觉优化。

当前可利用能力：`IPromptVersionManager` 已在 DI 中注册。

TODO：

- [ ] 每个场景模板绑定 `promptVersion`。
- [ ] 每次生成记录：`promptVersion`、`templateVersion`、`operatorKGVersion`、`modelId`、`modelCapabilities`。
- [ ] 建立 `ai_generation_eval`：输入 prompt、预期模板、预期核心算子、预期待确认参数。
- [ ] Prompt 更新后自动跑轻量评测。

验收标准：

- [ ] 修改 PromptBuilder 后能看到模板命中率是否下降。
- [ ] 生成失败能追溯到具体 promptVersion。
- [ ] 工作台调试页能显示本次使用的版本信息。

---

### P1-5.5：自动修复策略从“重试”升级为“定向修复”

目标：Validator 发现问题后，不再只让 LLM 重生成整个流程，而是生成 patch。

TODO：

- [ ] 新增 `AiFlowPatch`：添加算子、删除连线、修正端口、修正参数。
- [ ] Validator 错误转成 patch 提示。
- [ ] 对常见错误做确定性修复：参数越界、缺默认值、缺输出算子、重复输入端口。
- [ ] 对复杂错误调用 repair 模型，只允许返回 patch。
- [ ] Patch 应用后再次 Validator。

验收标准：

- [ ] 参数越界无需重新调用 LLM，可自动 clamp 并记录。
- [ ] 缺 ResultOutput 可自动建议补齐。
- [ ] 端口错误进入定向修复，而不是全流程重生成。

---

### P1-5.6：Prompt 压缩与上下文缓存

目标：减少每次都发送大量重复知识。

TODO：

- [ ] Operator Knowledge Card 按场景切片。
- [ ] 模板骨架用 hash 标识，工作台可展示完整模板，LLM 只拿必要内容。
- [ ] 常用场景 PromptContext 缓存到本地。
- [ ] 会话内复用 `RequirementBrief`、模板候选、知识切片。

验收标准：

- [ ] 高频模板场景的 Prompt 明显短于全量算子目录 Prompt。
- [ ] 同一会话二次修改不重复发送全部无关上下文。

---

### P1-5.7：PromptTrace 与安全边界

目标：调试可用，但不泄露敏感信息。

TODO：

- [ ] PromptTrace 仅 Debug/开发态可见。
- [ ] 对 API Key、本地绝对路径、客户文件名、内网地址做脱敏。
- [ ] Reasoning 内容不默认返回给普通 UI；只展示工程摘要。
- [ ] 日志中不要记录完整 Prompt 与图片路径，或仅在开发态记录。

验收标准：

- [ ] 普通用户无法看到完整 PromptTrace。
- [ ] PromptTrace 中不存在 API Key。
- [ ] 附件路径在 UI 中可脱敏显示。

---

### P1-5.8：生成链路可观测性

目标：为产品优化提供数据。

TODO：

- [ ] 每次生成记录 stage 耗时：澄清、匹配、Prompt、LLM、解析、Validator、DryRun。
- [ ] 记录模型、模板、场景、token 估算、失败原因。
- [ ] 工作台展示本次生成诊断摘要。
- [ ] 本地生成 Markdown/JSON 报告。

验收标准：

- [ ] 能定位“慢在模型调用还是 DryRun”。
- [ ] 能统计某模型的 JSON 失败率。
- [ ] 能统计某模板的首轮 Validator 通过率。

---

### P2-5.9：离线/断网模板向导降级

目标：LLM 不可用时，用户仍能完成高频场景配置。

TODO：

- [ ] 当 AI 服务不可用，工作台进入“模板向导模式”。
- [ ] 用户选择模板 -> 填参数 -> Validator -> DryRun -> 应用。
- [ ] 不调用 LLM 也能生成模板 Flow。
- [ ] 离线模式明确标记“未使用 LLM”。

验收标准：

- [ ] 断网时仍能配置端子线序模板。
- [ ] 断网时仍能配置铜孔间距模板。
- [ ] 断网模式不会显示 AI 生成解释，而是显示模板说明。

---

### P2-5.10：Scenario Package 版本化与资产校验

目标：让模板不只是流程 JSON，而是包含模型、规则、标签、样本、FAQ 的场景资产包。

当前 `FlowTemplate` 已有 `ScenarioPackageBinding` 和 `ScenarioPackageManifest` 基础，可以继续扩展。

TODO：

- [ ] 建立场景包目录结构：

```text
scenario-packages/
  wire-sequence-terminal/
    manifest.json
    templates/
    rules/
    labels/
    samples/
    faq.md
```

- [ ] Manifest 记录资产版本、checksum、必需资源、约束条件。
- [ ] 模板加载时校验 manifest。
- [ ] 工作台显示场景包版本与缺失资产。
- [ ] 保存为模板时可选择是否归入场景包。

验收标准：

- [ ] 线序模板能显示 package version、template version、model/rule/label 资产状态。
- [ ] 缺少必需标签文件时进入 MissingResources。
- [ ] 场景包升级可追溯。

---

## 9. 建议的任务优先级总表

| 优先级 | 任务 | 方向 | 主要收益 | 建议负责人 |
|---|---|---|---|---|
| P0 | ScenarioMatcher | 方向一 | 让模板优先从线序扩到全部内置高频模板 | AI 后端 |
| P0 | Template Lock + Template Gate | 方向一 | 限制 LLM 自由发挥，保证拓扑稳定 | AI 后端 / 质量 |
| P0 | RequirementBrief + ClarificationEngine | 方向三 | 生成前先补关键需求，减少无效生成 | AI 后端 / 产品 |
| P0 | AiWorkbench 状态机 | 方向四 | 让用户看懂生成阶段和结果状态 | 前端 |
| P0 | 模板匹配卡片 + 需求卡片 + 验证卡片 | 方向四 | 把现有字段变成可操作 UX | 前端 |
| P0 | Generation Pipeline 拆分 | 方向五 | 降低 `AiFlowGenerationService` 复杂度 | AI 后端 |
| P1 | Operator Knowledge Graph | 方向二 | 让 LLM 从算子清单升级为场景知识检索 | AI 后端 / 文档生成 |
| P1 | 模板评测集 | 方向一/五 | 用数据证明 Prompt 和模板策略稳定 | 质量 |
| P1 | 参数补录控件工程化 | 方向四 | 降低用户配置难度 | 前端 / 运行时 |
| P1 | 模型角色路由 | 方向五 | 降本增稳，视觉/生成/修复分工 | AI 后端 |
| P2 | 离线模板向导 | 方向五 | LLM 不可用时仍可落地高频场景 | 产品 / 前端 |
| P2 | 场景包版本化 | 方向一/五 | 模板从流程升级为工业资产包 | 架构 / 质量 |

---

## 10. 建议第一轮落地切片

第一轮不要同时做完所有内容，建议按“一个端到端场景”落地。

推荐选择：`端子线序检测` 或 `包装箱外观检测`。

### 第一轮目标

```text
用户输入一句话
  -> 系统识别场景
  -> 展示模板候选
  -> 生成需求卡片
  -> 缺字段先澄清
  -> 模板锁定生成
  -> Validator + Template Gate
  -> DryRun
  -> 参数补录
  -> 应用到画布
  -> 保存为模板或导出报告
```

### 第一轮具体 TODO

- [ ] 为选择的场景补齐 `ScenarioDefinition`。
- [ ] 为选择的场景补齐 Operator Knowledge Cards 子图。
- [ ] 写 20 条模板命中评测 prompt。
- [ ] 工作台新增模板匹配卡片。
- [ ] 工作台新增需求卡片。
- [ ] 工作台新增验证卡片。
- [ ] 生成结果必须展示 `templateVersion`、`scenarioKey`、`missingResources`、`pendingParameters`。
- [ ] 应用前展示 diff。
- [ ] 应用后可撤销。

第一轮验收标准：

- [ ] 该场景模板 Top1 命中率 ≥ 95%。
- [ ] 该场景核心拓扑保持率 ≥ 95%。
- [ ] 该场景 Validator 首轮通过率 ≥ 90%。
- [ ] 用户能在不手写 JSON 的情况下补齐关键参数并应用到画布。

---

## 11. 风险与注意事项

### 11.1 不要把“模板优先”做成死板模板

模板优先不是不允许调整，而是默认不允许 LLM 自由破坏主拓扑。正确做法是：

```text
模板主干锁定 + 参数可调 + 局部可替换 + 明确用户授权后再改拓扑
```

### 11.2 不要把知识图谱做成文档大工程

第一版知识图谱只服务三个目标：

- 帮 Prompt 选相关算子。
- 帮 Validator 检查误用。
- 帮工作台解释为什么这么选。

不要一开始追求复杂图数据库，本地 JSON 足够。

### 11.3 不要让澄清变成冗长表单

澄清必须“少问关键问题”。每次最多 3 个问题，且能跳过非关键字段。否则用户会觉得还不如自己搭流程。

### 11.4 工作台不要堆信息

信息分层：

```text
普通用户：模板、缺什么、能不能应用、风险是什么。
工程用户：参数、端口、Validator、DryRun、证据状态。
开发调试：PromptTrace、模型能力、原始诊断。
```

### 11.5 质量矩阵要进入产品提示，但不要夸大

质量矩阵显示很多算子已有证据，但真实工业现场验证仍然是缺口。工作台应明确展示“功能可用但未完成现场工业验证”，避免把本地基线或替代 replay 说成产线签收。

---

## 12. 推荐新增文档与代码入口

建议新增文档：

- `docs/进行中/当前计划/ClearVision-AI工作流模板优先与工程师工作台TODO-2026-04-30.md`
- `docs/ai/operator-knowledge/operator_knowledge_schema.md`
- `docs/ai/operator-knowledge/operator_knowledge_graph.md`
- `docs/ai/scenario-packages/README.md`
- `docs/ai/evals/template_match_eval_report.md`

建议新增/调整代码：

- `ScenarioDefinition.cs`
- `ScenarioMatcher.cs`
- `RequirementBriefDto.cs`
- `RequirementBriefExtractor.cs`
- `ClarificationEngine.cs`
- `OperatorKnowledgeRetriever.cs`
- `TemplateConstraintValidator.cs`
- `AiGenerationPipelineContext.cs`
- `AiGenerationPipeline.cs`
- `AiWorkbenchState` 前端状态模块

建议新增测试：

- `ScenarioMatcherTests.cs`
- `ClarificationEngineTests.cs`
- `TemplateConstraintValidatorTests.cs`
- `OperatorKnowledgeGraphTests.cs`
- `AiWorkbenchStateTests.js` 或前端轻量测试
- `TemplatePromptEval` 固定回归脚本

---

## 13. 最终产品表达建议

建议以后对外描述 ClearVision 的核心竞争力时，不要只说：

> 调用 LLM 生成视觉检测工作流。

建议改成：

> ClearVision 是一个面向工业视觉检测的 AI 工程师工作台。它不是让 LLM 自由发挥，而是先做场景识别和模板优先匹配，再基于算子知识图谱生成受约束的流程草案，并通过 Validator、Template Gate、DryRun 和人工参数确认完成闭环。AI 负责加速工程配置，系统负责约束、校验、预演和可追溯。
