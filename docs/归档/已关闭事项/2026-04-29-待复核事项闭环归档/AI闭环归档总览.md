---
title: "AI闭环归档总览"
doc_type: "closed-summary"
status: "closed"
topic: "AI闭环"
created: "2026-03-21"
updated: "2026-04-29"
closed_at: "2026-04-29"
source_docs:
  - "docs/归档/历史资料/needs-review来源/拆分稿/AI闭环/LLM闭环修复框架.md"
---

# AI闭环归档总览

## 归档结论

AI 这条待复核线按“工作流生成主链”口径关闭。

当前问题不再是“AI 主链有没有闭环”，而是“场景级 preview / autotune / follow-up 是否另起专项继续收口”。生成侧结构化闭环已经落地，后续不再把线序场景调参问题混入 `AI闭环` 这个历史待复核项。

当前可以确认：

- 生成侧结构化闭环已经落地
- 线序场景的 preview / autotune / AI follow-up 闭环应拆到场景专项或质量治理计划中重新确认

## 已落地的闭环层

- `AiFlowValidator` 已输出结构化诊断
- `AiFlowGenerationService` 已支持定向重试而不是机械重生
- `GenerateFlowResponse` 已回传：
  - `FailureSummary`
  - `LastAttemptDiagnostics`
  - `PendingParameters`
  - `MissingResources`
- AI 面板已支持取消生成、待确认参数和缺失资源展示

这部分已经不是阻塞线序检测的主要问题。

## 拆分边界

不再挂在本待复核项下的内容：

- 节点级 preview 与诊断码收口
- 场景级自动调参
- AI follow-up 只改参数、不改结构

这些工作原本指向旧 active 文档：

- `docs/active/线序检测闭环下一步清单.md` 当前工作区未保留，见 [`旧计划缺失引用说明`](../../过期计划/旧计划缺失引用说明.md)。

如果后续要重启线序专项闭环，应新建 `docs/进行中/当前计划/` 下的专项文档，而不是重新打开本归档项。

## 证据入口

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/AiFlowGenerationService.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/AiFlowValidator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/GenerateFlowMessageHandler.cs`
- `ClearVision.Product/src/ClearVision.Product.Contracts/Messages/AiGenerationMessages.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/AI/GenerateFlowMessageHandlerTests.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/AI/AiFlowGenerationServiceManualRetryTests.cs`

## 后续边界

本归档不声明线序业务样本、生产模型资产、GPU/TensorRT 或 field replay 已完成。那些属于场景验收和质量证据层，应进入当前计划或质量飞轮后续阶段。
