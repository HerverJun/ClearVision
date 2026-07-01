# ClearVision Studio 2.0 TODO

> 状态来源：本文件是仓库内 Studio 2.0 薄账本。详细证据见 `docs/进行中/Studio2/`。
> 最近更新：2026-07-01
> 目标分支：`codex初稿`
> 执行包审计参考 SHA：`f4d392e2147adf175a2f8faa7d7c09b3d906ba8a`
> G00 实际 Initial SHA：`58c7569958f3bf8ab627f5c5b76ff0a77cc86914`

## 当前执行项

- 当前状态：`G00 DONE`
- 当前阶段：`Foundation`
- 当前基线报告：[`docs/进行中/Studio2/baseline/G00-基线冻结报告-2026-07-01.md`](docs/进行中/Studio2/baseline/G00-基线冻结报告-2026-07-01.md)
- 状态权威与恢复边界：[`docs/进行中/Studio2/状态权威与恢复边界.md`](docs/进行中/Studio2/状态权威与恢复边界.md)
- 下一执行项：`G01`
- G01 状态：`READY`
- G01 执行边界：本轮没有执行 G01；下一轮必须重新读取当轮执行卡和真实代码锚点后再开始。

## 架构红线

- 保留 WinForms + WebView2 + ASP.NET Core Desktop；Station 独立运行，不依赖 Vue、Node 或 Studio。
- Studio 2.0 不得建立第二套 AgentRun、Project、Flow、GlobalVariables 或 ProjectSave 权威。
- `AgentRunEventStore` 是 Vision Agent 运行事件权威；Terminal Projector 只能将终态投影回 `ConversationSession`。
- `VisionAgentBuildProjectionJournal` 负责 Build terminal 投影幂等、恢复和冲突判断，不替代 AgentRun 事件流。
- `VisionAgentWorkspaceSnapshot` 是会话内 Plan/Build 工作台状态快照，不是正式工程保存。
- `ProjectService` + `ProjectSaveCoordinator` 是 Project/Flow/GlobalVariables 正式保存入口。
- 前端 `localStorage`、DOM、未来 Pinia store 和执行包文件都不得成为业务权威。
- G00 只做归档、基线冻结和文档说明，不修改生产运行行为。

## Goal 状态

| ID | 阶段 | Goal | 状态 | 基线/完成 SHA | 仓库文档 |
| --- | --- | --- | --- | --- | --- |
| G00 | Foundation | 归档旧阶段并冻结可复现基线 | DONE | 本提交 HEAD，见最终报告 | [`G00.md`](docs/进行中/Studio2/goals/G00.md) |
| G01 | Foundation | ADR、状态权威与迁移白名单 | READY |  | 下一轮读取 G01 执行卡后实施 |

## 最近完成记录

| 日期 | Goal | Initial SHA | Final SHA | 验证 | 结论 |
| --- | --- | --- | --- | --- | --- |
| 2026-07-01 | G00 | `58c7569958f3bf8ab627f5c5b76ff0a77cc86914` | 本提交 HEAD，见最终报告 | Desktop build PASS；Product/ Desktop targeted tests PASS；services regression PASS；UI unit PASS；链接/编码/diff hygiene PASS | Vision Agent 恢复治理阶段归档，Studio 2.0 基线冻结；下一项为 G01 |
