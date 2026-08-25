# VisionAgent 稳定 Build 双产物分叉审计

审计日期：2026-08-03
基线提交：`7dadc853581aeed4546c5d3a567d9a4c94825ae8`

## 已确认的分叉

`WorkflowDraftBuilder.DraftAsync` 在同一次 Build 中分别调用：

```text
pipeline + parameters + connections -> BuildCanonicalDraft -> WorkflowDraft
load.CurrentFlowSnapshot + pipeline + parameters + connections -> BuildCanvasFlow -> CanvasFlow
```
两者没有共享一个带身份的 Canonical Graph。`BuildCanvasFlow` 还会读取和修改已有 `OperatorFlowDto`，并为新节点、端口和连线生成新的运行时 GUID；`BuildCanonicalDraft` 则只保存 tempId、算子类型、参数和连接文本。

## Repair 分叉

`WorkflowDraftBuilder.Repair` 基线行为只替换：

```text
DraftWorkflowResolution.WorkflowDraft
EntryOperatorTempId
AddedNodeIds
```

它没有重建 `CanvasFlow`。因此校验再次看到的是新 canonical draft，而最终结果仍可能携带 Repair 前的 CanvasFlow。

## 成功语义风险

`BuildResultAssembler` 基线使用：

```csharp
result.Success = input.CurrentDraft.CanvasFlow.Operators.Count > 0;
```

并据此重新生成 `CompletionStatus`、`BuildReadiness.CanBuild` 和空 `RemainingFields`。这会把“有节点”误当为“任务完整、可构建”。

## 基线证据

| 检查项 | 结果 |
| --- | --- |
| Canonical draft 与 Canvas projection 独立构造 | `CONFIRMED` |
| Repair 是否同步重建 Canvas projection | `NO` |
| 验证对象是否携带 artifact fingerprint | `NO` |
| ApplyGate 是否检查任务语义 | `NO` |
| 节点数是否直接影响 readiness | `YES` |

## G0 门禁

```text
STABLE_BUILD_DUAL_ARTIFACT_CONFIRMED=true
REPAIR_CANVAS_PROJECTION_LEAK_CONFIRMED=true
NODE_COUNT_READINESS_BUG_CONFIRMED=true
```
