# Operator Knowledge Graph (方向二 P0 落地说明)

## 目标

- 把算子知识从“全量目录硬塞 Prompt”升级为“按场景检索切片”。
- 让 PromptBuilder、模板链路、Validator 的事实源向同一套结构靠拢。

## 本次实现

- 当前生成口径：158 张算子知识卡、1909 条图谱边、86 条 `USED_IN_TEMPLATE` 关系；模板来源为当前 `FlowTemplateService` 的 17 个内置模板，其中包含 9 个传统视觉/测量/匹配模板。
- 新增结构模型：
  - `OperatorKnowledgeCard`
  - `OperatorKnowledgeEdge`
  - `OperatorKnowledgeGraph`
- 新增图谱生成服务：
  - `OperatorKnowledgeGraphService`
  - 数据源：
    - `IOperatorFactory.GetAllMetadata()`
    - `FlowTemplateService` 内置模板拓扑
    - `quality/evals/reports/operator_quality_evidence_manifest.json`
- 新增检索服务：
  - `OperatorKnowledgeRetriever`
  - 输入：描述、补充上下文、附件名、场景提示
  - 输出：优先算子列表、知识卡切片、命中场景摘要
- 新增导出 Runner：
  - `quality/tools/OperatorKnowledgeGraphRunner`
  - 默认产物：
    - `docs/ai/operator-knowledge/operator_knowledge_graph.json`
    - `docs/ai/operator-knowledge/operator_knowledge_cards.json`
    - `docs/ai/operator-knowledge/operator_knowledge_graph_report.md`

## 关系类型

- `PRODUCES`：算子输出某数据类型（来自输出端口）
- `CONSUMES`：算子消费某数据类型（来自输入端口）
- `COMMONLY_PRECEDES`：模板中常见前驱关系
- `COMMONLY_FOLLOWS`：模板中常见后继关系
- `USED_IN_TEMPLATE`：算子出现在某模板/场景
- `REQUIRES_RESOURCE`：算子依赖模型或路径资源（含模板 `ScenarioPackage.RequiredResources`）
- `HAS_EVIDENCE`：算子绑定质量证据概况
- `ALIAS_OF`：算子语义别名映射

## PromptBuilder 接入策略

- 已在 `PromptBuilder` 中接入 `IOperatorKnowledgeRetriever`。
- 为兼容现有测试与回归：
  - 若通过 DI 注入检索器，使用知识图谱切片。
  - 若直接 `new PromptBuilder(factory)`，回退旧关键词裁剪逻辑。

## 运行方式

```powershell
dotnet run --project quality/tools/OperatorKnowledgeGraphRunner/OperatorKnowledgeGraphRunner.csproj
```

可选参数：

- `--graph-output <path>`
- `--cards-output <path>`
- `--report-output <path>`
- `--no-report`

## 测试覆盖

- `OperatorKnowledgeGraphTests.BuildAsync_ShouldCoverOperatorCardsAndEdges`
  - 验证卡片数量与算子元数据一致，且关键边类型齐全。
- `OperatorKnowledgeGraphTests.BuildAsync_ShouldIncludeTemplateRequiredResourceEdges`
  - 验证模板资源依赖被写入 `REQUIRES_RESOURCE`，并验证关键模板链路。
- `OperatorKnowledgeGraphTests.RetrieveAsync_ShouldPrioritizeWireSequenceOperators`
  - 验证线序场景优先检索核心算子链。
