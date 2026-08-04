# VisionAgent 旧 Planner 生产调用面审计

审计日期：2026-08-03
基线分支：`codex初稿`
基线提交：`7dadc853581aeed4546c5d3a567d9a4c94825ae8`
跟踪远端：`origin/codex初稿`，审计时与基线一致

## 基线配置

实际读取文件：`ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`

| 配置 | 基线值 | 风险 |
| --- | --- | --- |
| `AI:VisionAgent:GenerateFlow:Enabled` | `true` | 生产入口可进入 Agent GenerateFlow |
| `AI:VisionAgent:GenerateFlow:Mode` | `scripted` | 默认并不代表请求模式已被策略约束 |
| `FallbackToScriptedOnPlannerFailure` | `true` | Planner 失败可切换旧 Scripted 物化 |
| `FallbackToLegacyOnFailure` | `false` | 配置默认关闭，但代码仍保留 fail-open 分支 |
| `AI:VisionAgent:PlanPlanner:Enabled` | `true` | 正式 Plan Planner 可保留 |

历史 AI 流程数据库和外部运行包未在本次 G0 环境中接入：`PARTIAL`。本审计不把不可访问数据伪造为零。

## 入口矩阵

| ENTRYPOINT | REQUESTED_MODE | EFFECTIVE_MODE（基线） | PRODUCTION_REACHABLE | MODEL_AUTHORED_DRAFT_ALLOWED | USES_BUILD_FLOW_DTO | USES_WORKFLOW_DRAFT_BUILDER | USES_BUILD_RUN_SERVICE | VALIDATION_REQUIRED | CAN_APPLY | PERSISTED |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `GenerateFlowMessageHandler` 普通 WebMessage | `scripted` / 未知 | `scripted` | 是 | 是 | 是（Agent 路径） | 否 | 否（无 Plan） | Agent 工具检查，不是正式 Build | 由 `Success` 和 Flow 兼容路径决定 | 会话持久化 |
| `GenerateFlowMessageHandler` 普通 WebMessage | `planner` | `planner` | 是 | 是 | 是 | 否 | 否 | Planner 工具检查 | 可能 | 会话持久化 |
| `GenerateFlowMessageHandler` `BuildFromPlan` | 任意 | 请求值归一化 | 是 | 否 | 否 | 是 | 是 | 是 | 由 BuildResult/ApplyGate | AgentRun 终态 |
| `AiFlowGenerationService` | Agent 失败 | 旧 `GenerateFlow` fallback（配置可开启） | 是 | 是 | 旧解析器/物化路径 | 否 | 否 | 旧 validator | 旧成功语义 | 是 |
| `VisionAgentGenerateFlowService` | `planner` | `planner` | 是 | 是 | 是 | 否 | 否 | 工具轨迹检查 | `Flow != null` 可被认为成功 | 可选 |
| `VisionAgentBuildRunService` | `BuildFromPlan` | Plan→Build | 是 | 否 | 否 | 是 | 是 | 是 | AgentRun terminal projection | 是 |
| `VisionAgentBuildApplicationService` | `BuildFromPlan` | Plan→Build | 是 | 否 | 否 | 是 | 是 | 是 | ApplyGate | 是 |
| `VisionAgentOrchestrator` | `PlanPlanner` | 正式 Plan Planner | 是 | 不直接物化 | 否 | 否 | 否 | Plan readiness | 不产生 Flow | 会话/Plan |
| 桌面端 Agent 开发者控件 | `planner` / `tool_loop` | 基线允许选择 | 开发 UI 可达 | 是 | 是 | 否 | 否 | 受控 Agent 检查 | 由前端兼容逻辑决定 | 可选 |
| quality runners / planner tests | `planner` | 离线评测 | 否 | 允许评测 | 允许 | 否 | 否 | 测试自定义 | 不应授权生产 Apply | 测试输出 |

## 待删除或迁移清单

- `VisionAgentGenerateFlowService` 的 Planner 分支、Planner 失败后 Scripted fallback 和旧 `BuildFlowDto`。
- `AiFlowGenerationService` 的 Agent 失败后旧自由 GenerateFlow fallback。
- `GenerateFlowMessageHandler` 对未知模式的静默 `Normalize`。
- 前端开发控件中的生产可达 `planner` / `tool_loop` 选择。
- 旧自由物化所依赖的动态端口、schema 外参数和未知算子降级。

## G0 门禁

```text
OLD_PLANNER_ENTRY_MAP_COMPLETE=true
STABLE_BUILD_DUAL_ARTIFACT_CONFIRMED=true
INITIAL_CASE_FIXTURE_CREATED=true
RUNTIME_DATA_STATUS_RECORDED=true (PARTIAL)
```
