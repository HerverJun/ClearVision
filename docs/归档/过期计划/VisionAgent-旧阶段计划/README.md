---
title: "Vision Agent 旧阶段计划"
doc_type: "index"
status: "deprecated"
topic: "Vision Agent"
created: "2026-08-25"
updated: "2026-08-25"
---

# Vision Agent 旧阶段计划

本目录保存已经不再执行的旧 Vision Agent 方案。它们仍有历史设计价值，但不能作为当前实现、发布或现场能力的声明。

## 替代入口

- [旧阶段闭环与实施证据](../../已关闭事项/2026-07-01-VisionAgent-恢复治理阶段归档/闭环说明.md)
- [Studio 2.0 当前入口](../../../进行中/Studio2/README.md)
- [测试与覆盖率治理当前计划](../../../进行中/当前计划/测试治理/ClearVision_T01_测试与覆盖率治理总体计划_PROPOSED_AUDITED.md)

## 已过期文档

- [Vision Engineering Agent TODO（最终评审加强版）](./ClearVision_Vision_Engineering_Agent_TODO_Final_Review.md)：清单未回填，旧工具调用与 Planner 架构后来被 Studio 2.0 和稳定 Build 路径替代。
- [Real RuntimePreview RFC](./VisionAgent_Real_RuntimePreview_RFC_20260606.md)：状态仍为 Draft，未授权真实 RuntimePreview、相机、Station、PLC 或部署能力。
- [RuntimePreview Pilot Gate](./VisionAgent_RuntimePreview_Pilot_Gate.md)：旧 Pilot 门禁快照，不再是当前 release gate。
- [Real LLM Shadow Dry Run](./VisionAgent_Real_LLM_Shadow_Dry_Run.md)：依赖的旧 Shadow Eval runner 已随 legacy planner 退役，不再作为可执行说明。

如需重新启用其中任何方向，应在 `docs/进行中/当前计划/` 新建 active 计划，重新核对当前代码、权限边界和验证入口，不直接修改这些历史正文。
