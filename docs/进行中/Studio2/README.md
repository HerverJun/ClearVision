# Studio 2.0

> 状态：Release 阶段
> 已完成：G00-G15.4
> 当前执行项：G16（`BLOCKED`）
> 状态权威：仓库根 [`TODO.md`](../../../TODO.md)
> 入口更新：2026-08-25

本目录保存 Studio 2.0 的当前执行入口、基线、架构约束和 Goal 卡。根 `TODO.md` 是 Goal 状态的唯一权威；本文只提供导航，不复制完整执行账本。

## 当前入口

- 当前 Goal：[`G16`](goals/G16.md)
- 根账本记录的阻断边界：生产唯一入口切换与完整 release evidence 尚未闭环。
- 基线报告：[`baseline/G00-基线冻结报告-2026-07-01.md`](baseline/G00-基线冻结报告-2026-07-01.md)
- 状态权威与恢复边界：[`状态权威与恢复边界.md`](状态权威与恢复边界.md)
- Studio 2.0 架构边界 ADR：[`architecture/Studio2-架构边界-ADR.md`](architecture/Studio2-架构边界-ADR.md)
- capability 迁移与 Feature Flag 台账：[`architecture/Studio2-capability-迁移台账.md`](architecture/Studio2-capability-迁移台账.md)

## 阶段总览

| 阶段 | Goal | 状态 |
| --- | --- | --- |
| Foundation | G00-G04B | DONE |
| Observation | G05A-G08 | DONE |
| Geometry / Spatial | G09A-G10C 及 follow-up | DONE |
| Vertical Product | G11A-G13C 及 follow-up | DONE |
| Productization | G14A-G15.4 | DONE |
| Release | G16 | BLOCKED |

已完成卡片继续保留在 [`goals/`](goals/) 中，作为当前 G16 的前置执行记录。G16 关闭后，再将整套 Studio 2.0 Goal 卡和闭环说明作为一个批次归档，避免在项目仍 active 时拆散依赖链。

## 使用规则

- 每轮只读取根 `AGENTS.md`、根 `TODO.md` 的必要段落、当前 Goal 卡和该卡列出的代码锚点。
- 禁止同时执行两个 Goal，禁止跳过前置 Goal。
- 如果卡片、本文与仓库当前代码事实冲突，以代码事实和根 `TODO.md` 为准，并先修正文档状态。

## 历史归档

- [Vision Agent 恢复治理阶段闭环](../../归档/已关闭事项/2026-07-01-VisionAgent-恢复治理阶段归档/闭环说明.md)
- [Vision Agent 被替代旧计划](../../归档/过期计划/VisionAgent-旧阶段计划/README.md)
