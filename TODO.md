# ClearVision Studio 2.0 开发 TODO

<!-- DOC_AUDIT_STATUS_START -->
## 文档审计状态（自动更新）
- 审计日期：2026-07-02
- 完成状态：未完成
- 任务统计：总计 0，已完成 0，未完成 0，待办关键词命中 5
- 判定依据：检测到待办关键词（TODO/待办/未完成/TBD/FIXME/WIP）
<!-- DOC_AUDIT_STATUS_END -->

> 文档版本：V1.1（仓库安装回填版）
> 审计日期：2026-07-01
> 目标仓库：`HerverJun/ClearVision`
> 目标分支：`codex初稿`
> GitHub 审计参考快照：`f4d392e2147adf175a2f8faa7d7c09b3d906ba8a`
> G00 Initial SHA：`58c7569958f3bf8ab627f5c5b76ff0a77cc86914`
> G00 完成 SHA：`3481d5a35f47bbf1f58c3f042cff6a679e720e0c`
> 结构：**本文件只做薄账本；每个 Goal 的详细上下文在独立执行卡。**

## 1. Codex 每轮读取规则

每轮只读取：

1. 根目录 `AGENTS.md`；
2. 本文件的“架构红线”“当前执行项”“本轮固定协议”；
3. 当前 Goal 对应的一张执行卡；
4. 执行卡列出的代码锚点；
5. 上位总纲仅在发生架构争议时按需读取。

禁止一次加载全部 Goal 卡；禁止同时执行两个 Goal。

## 2. 架构红线

- 保留 WinForms + WebView2 + ASP.NET Core Desktop；不引入 Electron。
- Station 独立运行；不依赖 Vue、Node 或 Studio。
- 不重写 `FlowCanvas`；V2 复用并扩展现有 `FlowCanvasAdapter`。
- 新 V2 typed API 必须包裹现有 `httpClient`，不得重做 auth、端口发现和网络错误策略。
- 同一 capability 任一时刻只有一个 mounted owner、一个订阅集合、一个写入口。
- Pinia 不得成为 Project、Flow、Variables、Agent 的业务权威。
- `flowRevision` 是 UI 本地 revision；`PersistenceRevision` 是后端持久化 authority，二者不得混淆。
- 正式工程资产只经 Application Service 与 `ProjectSaveCoordinator`。
- `ExecutionObservationEnvelopeV1` 无持久化、只读、可丢弃，不替代执行结果。
- Scene/Geometry 复用现有 `ImageCanvas`；不得再造第二图像渲染内核。
- `CalibrationBundleV2` 是唯一正式标定产物。
- 不重构 AgentRun、EventStore、Workspace Snapshot、terminal/recovery 权威。
- Feature Flag 必须登记创建、owner、关闭行为、cutover 和删除 Goal。
- 旧实现不能仅被 CSS 隐藏；flag on 时必须不挂载、不订阅、不运行 timer。

出现违反红线的必要前提时，状态改为 `BLOCKED`，输出 `BLOCKED_ARCHITECTURE_DEVIATION`，不得自行扩大重构。

## 3. 当前执行项

- 当前 Goal：`G08`
- 当前卡片：`docs/进行中/Studio2/goals/G08.md`
- 当前阶段：`Observation`
- 总状态：`READY`
- 审计参考 SHA：`f4d392e2147adf175a2f8faa7d7c09b3d906ba8a`
- G00 Initial SHA：`58c7569958f3bf8ab627f5c5b76ff0a77cc86914`
- G00 完成 SHA：`3481d5a35f47bbf1f58c3f042cff6a679e720e0c`
- 当前基线报告：[`docs/进行中/Studio2/baseline/G00-基线冻结报告-2026-07-01.md`](docs/进行中/Studio2/baseline/G00-基线冻结报告-2026-07-01.md)
- 状态权威与恢复边界：[`docs/进行中/Studio2/状态权威与恢复边界.md`](docs/进行中/Studio2/状态权威与恢复边界.md)
- Vision Agent 恢复治理阶段归档：[`docs/归档/已关闭事项/2026-07-01-VisionAgent-恢复治理阶段归档/闭环说明.md`](docs/归档/已关闭事项/2026-07-01-VisionAgent-恢复治理阶段归档/闭环说明.md)

## 4. 本轮固定协议

1. `git fetch origin`。
2. 确认分支为 `codex初稿`、工作树干净、本地 HEAD 与 `origin/codex初稿` 一致。
3. 若不一致，停止并输出 `BLOCKED_REMOTE_DIVERGED`；不得强推或悄悄 rebase。
4. 读取当前卡片和代码锚点；先核实真实代码，再修改。
5. 在工作树中把当前状态改为 `IN_PROGRESS`，回填开始时间、Initial SHA 和修改白名单。
6. 只做当前卡片；遇到旁支问题记技术债。
7. 按 `AGENTS.md` 串行运行测试；禁止同一测试项目并行。
8. 回填当前卡片与本账本；未运行写 `NOT RUN`，人工验证未做写 `NOT PERFORMED`。
9. `git diff --check`，确认无生成物、密钥、临时文件。
10. 提交前再次 `git fetch origin`；远端前进则停止 `BLOCKED_REMOTE_DIVERGED`。
11. 一个 Goal 原则上一个提交；提交并 push。
12. 核对本地 HEAD、`origin/codex初稿`、GitHub 远端 SHA。
13. 当前改为 `DONE`，下一项改为 `READY`，更新“当前执行项”和“最近完成记录”。
14. 完整 CI 必须以 PR checks、workflow_dispatch 或实际支持该分支的 workflow 作为证据；普通分支 push 不等于完整 CI。

## 5. Goal 状态总览

状态：`LOCKED / READY / IN_PROGRESS / BLOCKED / DONE / DEFERRED`

| ID | 阶段 | Goal | 状态 | 前置 | 执行卡 | 完成 SHA |
|---|---|---|---|---|---|---|
| G00 | Foundation | 归档旧阶段并冻结可复现基线 | DONE | 无 | `docs/进行中/Studio2/goals/G00.md` | `3481d5a35f47bbf1f58c3f042cff6a679e720e0c` |
| G01 | Foundation | ADR、状态权威与迁移白名单 | DONE | G00 | `docs/进行中/Studio2/goals/G01.md` | 见 G01 完成提交与最终报告 |
| G02A | Foundation | FrontendV2 构建与发布底座 | DONE | G01 | `docs/进行中/Studio2/goals/G02A.md` | 见 G02A 完成提交与最终报告 |
| G02B | Foundation | V2 挂载、HostBridge 与现有通信适配 | DONE | G02A | `docs/进行中/Studio2/goals/G02B.md` | 见 G02B 完成提交与最终报告 |
| G03 | Foundation | Workspace Shell MVP | DONE | G02B | `docs/进行中/Studio2/goals/G03.md` | 见 G03 完成提交与最终报告 |
| G04A | Foundation | V2 Flow 编辑端口与本地 stale 防护 | DONE | G03 | `docs/进行中/Studio2/goals/G04A.md` | 见 G04A 完成提交与最终报告 |
| G04B | Foundation | V2 单请求工程保存与持久化身份 | DONE | G04A | `docs/进行中/Studio2/goals/G04B.md` | 见 G04B 完成提交与最终报告 |
| G05A | Observation | Execution Observation 投影与身份 | DONE | G04B | `docs/进行中/Studio2/goals/G05A.md` | 见 G05A 完成提交与最终报告 |
| G05B | Observation | Preview Artifact 生命周期与安全读取 | DONE | G05A | `docs/进行中/Studio2/goals/G05B.md` | 见 G05B follow-up 完成提交与最终报告 |
| G06 | Observation | 节点预览结果检查器 MVP | DONE | G05B | `docs/进行中/Studio2/goals/G06.md` | 见 G06 follow-up 完成提交与最终报告 |
| G07A | Observation | Canonical ResultPath 解析器 | DONE | G06 | `docs/进行中/Studio2/goals/G07A.md` | 见 G07A follow-up 完成提交与最终报告 |
| G07B | Observation | 字段级全局变量绑定 V1 | DONE | G07A | `docs/进行中/Studio2/goals/G07B.md` | 见 G07B follow-up 完成提交与最终报告 |
| G08 | Observation | Visual Scene V1（只读投影） | READY | G07B | `docs/进行中/Studio2/goals/G08.md` |  |
| G09A | Geometry/Spatial | Geometry 纯数学内核与矩形等价迁移 | LOCKED | G08 | `docs/进行中/Studio2/goals/G09A.md` |  |
| G09B | Geometry/Spatial | Circle、Annulus 与 Arc 编辑 | LOCKED | G09A | `docs/进行中/Studio2/goals/G09B.md` |  |
| G09C | Geometry/Spatial | Polygon 与 PointSequence 编辑 | LOCKED | G09B | `docs/进行中/Studio2/goals/G09C.md` |  |
| G10A | Geometry/Spatial | Spatial Context 数学与 sidecar 契约 | LOCKED | G09C | `docs/进行中/Studio2/goals/G10A.md` |  |
| G10B | Geometry/Spatial | RoiManager Crop 空间传播 | LOCKED | G10A | `docs/进行中/Studio2/goals/G10B.md` |  |
| G10C | Geometry/Spatial | PixelToWorld 与 Scene 空间投影 | LOCKED | G10B | `docs/进行中/Studio2/goals/G10C.md` |  |
| G11A | Vertical Product | Circle Search V2 kernel、契约与数据集 | LOCKED | G10C | `docs/进行中/Studio2/goals/G11A.md` |  |
| G11B | Vertical Product | Circle Search Tool、Geometry 与 Scene | LOCKED | G11A | `docs/进行中/Studio2/goals/G11B.md` |  |
| G11C | Vertical Product | Circle Search 连续预览、性能与兼容收口 | LOCKED | G11B | `docs/进行中/Studio2/goals/G11C.md` |  |
| G12A | Vertical Product | NPoint CalibrationSolver 抽取与 parity | LOCKED | G11C | `docs/进行中/Studio2/goals/G12A.md` |  |
| G12B | Vertical Product | N 点标定工作台 draft 与可视化 | LOCKED | G12A | `docs/进行中/Studio2/goals/G12B.md` |  |
| G13A | Vertical Product | Project 正式资产权威与保存恢复 | LOCKED | G12B | `docs/进行中/Studio2/goals/G13A.md` |  |
| G13B | Vertical Product | Runtime Package 可选 Calibration/Spatial 扩展 | LOCKED | G13A | `docs/进行中/Studio2/goals/G13B.md` |  |
| G13C | Vertical Product | Station/Runtime 标定加载与 PixelToWorld E2E | LOCKED | G13B | `docs/进行中/Studio2/goals/G13C.md` |  |
| G14A | Productization | 正式 Inspection 历史投影与分页 | LOCKED | G13C | `docs/进行中/Studio2/goals/G14A.md` |  |
| G14B | Productization | 结果对比、基线与 Scene 回放 | LOCKED | G14A | `docs/进行中/Studio2/goals/G14B.md` |  |
| G14C | Productization | Evidence manifest、导出与留存策略 | LOCKED | G14B | `docs/进行中/Studio2/goals/G14C.md` |  |
| G15.1 | Productization | Property Panel capability 迁移 | LOCKED | G14C | `docs/进行中/Studio2/goals/G15_1.md` |  |
| G15.2 | Productization | Preview Panel capability 迁移 | LOCKED | G15.1 | `docs/进行中/Studio2/goals/G15_2.md` |  |
| G15.3 | Productization | Global Variables capability 迁移 | LOCKED | G15.2 | `docs/进行中/Studio2/goals/G15_3.md` |  |
| G15.5 | Productization | Settings capability 迁移 | LOCKED | G15.3 | `docs/进行中/Studio2/goals/G15_5.md` |  |
| G15.8 | Productization | Project 页面 capability 迁移 | LOCKED | G15.5 | `docs/进行中/Studio2/goals/G15_8.md` |  |
| G15.6 | Productization | Inspection capability 迁移 | LOCKED | G15.8 | `docs/进行中/Studio2/goals/G15_6.md` |  |
| G15.7 | Productization | Results/Review capability 迁移 | LOCKED | G15.6 | `docs/进行中/Studio2/goals/G15_7.md` |  |
| G15.4 | Productization | AI Panel 外壳与展示 capability 迁移 | LOCKED | G15.7 | `docs/进行中/Studio2/goals/G15_4.md` |  |
| G16 | Release | 性能、发布与唯一入口收口 | LOCKED | G15.4 | `docs/进行中/Studio2/goals/G16.md` |  |

## 6. 阶段 Gate

### Foundation Gate

- V2 build/publish 可复现，无 Node 运行依赖。
- flag off 不初始化 V2；flag on 只有一个 V2 root。
- FlowCanvas 只有一个实例且未重写。
- V2 Flow 编辑与保存路径明确，ProjectSave/Agent 无回退。

### Observation Gate

- 当前节点 Summary/Detail/Artifact 可安全查看。
- Artifact 有 TTL/容量/生命周期，SSE 不携带大载荷。
- canonical ResultPath 在 preview/正式运行一致。
- Scene 与字段双向定位且 fail-soft。

### Geometry/Spatial Gate

- Rectangle/Circle/Annulus/Arc/Polygon/PointSequence 分阶段可编辑。
- draft/commit 边界清楚，无第二 command bus。
- ROI local->Full->World2D 可组合并有 round-trip 测试。

### Vertical Product Gate

- Circle Search V2 算法、Tool、Scene、Inspector、连续预览闭环。
- NPoint solver parity 和九点工作台完成。
- ProjectSave->重启->Runtime Package->Station PixelToWorld 闭环。

### Release Gate

- 每个 capability 只有一个生产 owner。
- 旧 app.js 不再作为第二业务 composition root；保留的底层库无 top-level 业务副作用。
- clean clone build/publish、无 Node 启动、DPI/分辨率/性能/内存通过。
- 旧工程、旧 package、Station、Agent、Project Save 回归通过。

## 7. 最近完成记录

| 日期 | Goal | Initial SHA | Final SHA | 测试/CI | 结论 |
|---|---|---|---|---|---|
| 2026-07-02 | G07B follow-up | `b83fcbeab63941ba1f6f053b441f3ee2b15d8907` | 见 G07B follow-up 完成提交与最终报告 | UI focused global-variable/Inspector/selectionStore unit PASS（57 tests）；Desktop targeted G05A/G05B/G06/G07A/G07B PASS（71 tests）；Product targeted G07A/G07B PASS（95 tests）；Playwright field-binding focused PASS（2 tests）；UI `test:unit` PASS（586 tests）；`test:preview-smoke` PASS；Playwright `node-preview.spec.ts` PASS（11 tests）；Desktop full serial PASS（453 tests）；Product failed performance filters isolated PASS（4 tests）after load-sensitive full run；Product full serial final isolated PASS（3128 passed, 4 skipped）；Desktop Debug build PASS；Station Debug/Release build PASS；Desktop Release publish PASS；build/publish V2 asset audit PASS；publish no Node/source/dev artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 收紧字段绑定 stale/TOCTOU 防线：`bindPreviewField` 在打开选择器前、选择返回后、替换确认后、保存前统一 `validatePreviewFieldBindingContext`；非法/缺失 flowRevision fail closed；mutation owner 独立验证 addressable/scalar/truncated/artifact/canonical metadata/selectionStore；nested ResultPath 不再用根端口结构类型误判不兼容，legacy 与 explicit root 仍按根类型校验；延迟选择器竞态不发 PUT 且保留旧 SourceBinding；未执行 G08。 |
| 2026-07-02 | G07B | `295363ecfad05711ecb7d214513a6aa090fd64ed` | 见 G07B 完成提交与最终报告 | UI focused global-variable/Inspector unit PASS（51 tests）；Desktop targeted Observation/Architecture PASS（32 tests）；Product targeted ResultPath/ProjectVariables/FlowExecution PASS（95 tests）；Playwright field-binding focused PASS（1 test）；UI `test:unit` PASS（580 tests）；`test:preview-smoke` PASS；Playwright `node-preview.spec.ts` PASS（10 tests）；`ClearVision.Product.Tests` full serial PASS（3128 passed, 4 skipped）；Desktop full serial PASS（453 tests）；Desktop Debug build PASS；Station Debug/Release build PASS；Desktop Release publish PASS；build/publish V2 asset audit PASS；publish no Node/source/dev artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 增加字段级全局变量绑定 V1：SourceBinding 前端 round-trip 保留 optional ResultPath 字段且不迁移 legacy root；Observation 投影后端权威 `bindableVariableTypes`；Inspector 仅对当前 identity 的可绑定 scalar canonical metadata 显示入口并通过注入 callback 出口；GlobalVariablePanel 为唯一 UI mutation owner，经现有 ProjectManager 保存链完成已有变量绑定、替换、取消和失败回滚；未执行 G08。 |
| 2026-07-02 | G07A follow-up | `746bbe52c4405b63d474508efd49813f895fad41` | 见 G07A follow-up 完成提交与最终报告 | Product targeted ResultPath/ProjectVariables/FlowExecution PASS（95 tests）；Desktop targeted Observation/Preview/Architecture PASS（60 tests）；Desktop endpoint isolation PASS（37 tests）；`ClearVision.Product.Tests` full PASS（3128 passed, 4 skipped）；Desktop full serial PASS（453 tests）；Desktop Debug build PASS；Station Debug/Release build PASS；Desktop Release publish PASS；build/publish V2 asset audit PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 修复 Observation canonical metadata 仅在生产 `ResultPathResolver` 可从真实 output-port 根值解析到同一 scalar 时输出；非字符串 dictionary key 与格式化冲突保持只读显示；SourceBinding nested ResultPath schema 不再误用根结构类型产生 `GV017`；运行时 version/path 配对 fail closed；stable-ID 全集合唯一；未执行 G07B。 |
| 2026-07-02 | G07A | `9244979e3dde5534bf92121c374c34e7e5126f31` | 见 G07A 完成提交与最终报告 | Product directed ResultPath/ProjectVariables/FlowExecution PASS（86 tests）；Desktop directed Observation/Preview/Architecture PASS（57 tests）；`ClearVision.Product.Tests` full PASS（3119 passed, 4 skipped）；Desktop full serial PASS（450 tests）；Desktop Debug build PASS；Station Debug/Release build PASS；Desktop Release publish PASS；build/publish V2 asset audit PASS；publish no Node/source/dev artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 建立 Core/shared Canonical ResultPath V1 parser/formatter/resolver；SourceBinding additive nullable `ResultPathVersion`/`ResultPath`，缺失兼容 `$`；FlowExecutionService 在 output port 根值上先解析 ResultPath 再执行 Expression 和变量转换；Observation 为唯一映射到 declared output port 的 scalar leaf 生成 canonical metadata；stable-ID selector 仅通过显式 adapter 支持；未执行 G07B。 |
| 2026-07-02 | G06 follow-up | `414dc70d7644326140e97e966af976a6f54413bd` | 见 G06 follow-up 完成提交与最终报告 | Desktop targeted serial tests PASS（74 tests）；UI focused Inspector/Coordinator unit PASS（25 tests）；Playwright `node-preview.spec.ts` PASS（9 tests：legacy 6，Inspector 3）；UI `test:unit` PASS（575 tests）；`test:preview-smoke` PASS；Desktop Debug build PASS；Release publish PASS；build/publish V2 asset audit PASS；publish no Node/source/dev artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 收口 G06 follow-up：`Studio:NodePreviewInspectorEnabled` 决策收敛到 frozen `featureFlags` 单一来源；flag off 不创建 Inspector/selectionStore，flag on 不构造 legacy Overlay；Artifact 文本预览按声明长度和实际 Blob slice 双重上限读取；renderer 改为确定优先级和大小写归一化；补齐快速节点切换 stale、大文本 Artifact、flag mutation 与架构守卫；未执行 G07A。 |
| 2026-07-02 | G06 | `52f7c07dfe2842e5ec17649e8b0de203edcaf5ea` | 见 G06 完成提交与最终报告 | UI focused unit PASS（19 tests）；Playwright `node-preview.spec.ts` PASS（6 tests）；Desktop targeted serial tests PASS（73 tests）；UI `test:unit` PASS（569 tests）；`test:preview-smoke` PASS；Desktop Debug build PASS；Release publish PASS；build/publish V2 asset audit PASS；publish no Node/source/dev artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 收口节点预览结果检查器 MVP：新增默认关闭 `Studio:NodePreviewInspectorEnabled` 与 flag ledger；flag on 时仅挂载 `NodePreviewInspector`，legacy overlay 不构造/不订阅/无 timer；Summary/Detail/Artifact、受控 renderer、搜索/copy/分页增量、selectionStore 与 stale artifact 读取均经现有 coordinator；未执行 G07A。 |
| 2026-07-02 | G05B follow-up | `1c8f1c25194d8e2d169ff710bfa3c604e262bf92` | 见 G05B follow-up 完成提交与最终报告 | Desktop targeted tests PASS，60 tests；FlowExecutionService targeted PASS，13 tests；UI `test:unit` PASS，563 tests；`test:preview-smoke` PASS；Desktop Debug build PASS；Release publish PASS；build/publish V2 asset audit PASS；publish no Node/source/dev artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 收口 G05B Artifact follow-up：Store public API 不再暴露 pending bytes，commit 使用正式 bytes copy 和 SHA-256；batch 先预检/规划再原子替换 owner、淘汰旧项并插入全部当前批次；取消/异常未 commit batch rollback；前端 artifact URL 与服务端 artifact 释放覆盖 uncached、live camera bypass、cache replacement/eviction、node switch、stale/partial read/destroy；下一项为 G06，未执行 G06。 |
| 2026-07-02 | G05B | `633635569249a53b1e55bce98410e5e5a5d4e5cf` | 见 G05B 完成提交与最终报告 | Desktop targeted tests PASS，67 tests；FlowExecutionService targeted PASS，13 tests；UI `test:unit` PASS，559 tests；`test:preview-smoke` PASS；Desktop Debug build PASS；Release publish PASS；build/publish V2 asset audit PASS；publish no Node artifacts PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 建立 Desktop-only Preview Artifact 生命周期：独立 `PreviewArtifactStore`、`PreviewArtifactMaterializer`、opaque bearer artifactId、TTL/容量/checksum/DELETE/revoke/dispose；preview `ArtifactMode=references` 不再把大图塞入主 JSON；旧 Base64 兼容保留为 G16 删除债；下一项为 G06，未执行 G06。 |
| 2026-07-02 | G05A follow-up | `18cd539cccca3b3ec7d570768661fc88ce9a8ec1` | 见 G05A follow-up 完成提交与最终报告 | `ExecutionObservationProjectorTests` + `PreviewNodeEndpointsTests` + `Studio2ArchitectureGuardTests` + `BuildFromPlanArchitectureGuardTests` targeted PASS，59 tests；UI `preview-coordinator-memory.test.mjs` PASS，7 tests；UI `test:unit` PASS，557 tests；`preview-regression.smoke.mjs` PASS；Desktop Debug build PASS；Release publish PASS；build/publish V2 asset audit PASS；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 收口 G05A Observation fail-soft：未知对象不再调用 `ToString()`、任意 public getter 或任意自定义 enumerable；`Detail` 使用最终 UTF-8 byte 硬上限；legacy outputData 和 metrics 输入均有界；endpoint adversarial 场景保持 HTTP 200 且普通 `Score`/`Seen` 可读；当前 Goal 仍为 G05B，未执行 G05B。 |
| 2026-07-02 | G05A | `ef676b193f6f955db56ac79c4fc13190916d92cb` | 见 G05A 完成提交与最终报告 | `ExecutionObservationProjectorTests` + `PreviewNodeEndpointsTests` + `Studio2ArchitectureGuardTests` + `BuildFromPlanArchitectureGuardTests` targeted PASS，54 tests；UI `preview-coordinator-memory.test.mjs` PASS，7 tests；UI `test:unit` PASS，557 tests；`preview-regression.smoke.mjs` PASS；Desktop Debug build PASS；Release publish PASS；build/publish V2 asset audit PASS；`git diff --check` PASS；文本编码 PASS；diff hygiene PASS；文档审计执行但生成性副作用未提交；完整 GitHub CI NOT RUN；真实 WebView2 NOT PERFORMED | 建立 `ExecutionObservationEnvelopeV1` 与 Desktop 边界 `ExecutionObservationProjector`，preview success/failure additive 返回 Observation；旧 outputData 安全降级且 detection metrics 兼容；NodePreviewCoordinator 发送 sequence/flowRevision 并在 Observation identity mismatch 时丢弃响应；未执行 G05B、未实现 Artifact Store/ResultPath parser/Inspector UI。 |
| 2026-07-02 | G04B follow-up | `7e089a17cbaf4862cb0ea63839131563feae8120` | 见 G04B follow-up 完成提交与最终报告 | FrontendV2 lint/typecheck/unit/build PASS，8 files/43 unit tests；`studioProjectPersistencePort.test.ts` PASS，11 tests；Project persistence/concurrency + Repository + ProjectService + ProjectSaveCoordinator targeted PASS，39 tests；ProjectGlobalVariableEndpoints + Studio2/BuildFromPlan Architecture Guard targeted PASS，43 tests；`app-infrastructure.test.mjs` PASS，20 tests；Playwright `studio2-flow-editor-port.spec.ts` PASS，3 tests；Desktop build PASS；Desktop publish PASS；build/publish V2 asset audit PASS；完整 GitHub CI NOT RUN；真实 WebView2 人工启动 NOT PERFORMED | 收口保存时 EF tracked stale revision：新增 `IProjectRepository.GetByIdForUpdateAsync`，`ProjectSaveCoordinator` 的 expected revision、commit-intent apply 和 recovery apply 均使用数据库当前 tracked 实体；补齐双 DbContext/双 service scope 竞争测试；`StudioProjectPersistencePort` open intent 在发请求前生效，旧响应不污染 snapshot，save 响应校验 `saved.id`；未执行 G05A。 |
| 2026-07-02 | G04B | `b4936ce22f380b00b5ff9e211c95219b10863b46` | 见 G04B 完成提交与最终报告 | FrontendV2 lint/typecheck/unit/build PASS，8 files/40 unit tests；ProjectService + ProjectSaveCoordinator targeted PASS，29 tests；ProjectGlobalVariableEndpoints + Studio2ArchitectureGuard targeted PASS，36 tests；`app-infrastructure.test.mjs` PASS，20 tests；Playwright `studio2-flow-editor-port.spec.ts` PASS，3 tests；Desktop build PASS；Desktop publish PASS；build/publish V2 asset audit PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 人工启动 NOT PERFORMED | 建立 `StudioProjectPersistencePort` 与 `studio2.projectPersistencePort`，V2 单次调用既有 `PUT /api/projects/{id}` 提交 metadata/Flow/GlobalVariables；后端使用 `ExpectedPersistenceRevision`/`PersistenceRevision` 与 `ProjectSaveCoordinator` 判定并发，`PSV011` 映射 409；旧 Project 页面、`projectManager.saveProject()`、`/flow` 兼容入口保留；未执行 G05A。 |
| 2026-07-02 | G04A follow-up | `59544fa7d6384d06828c8eaa3b3d183de9c96dcd` | 见 G04A follow-up 完成提交与最终报告 | FrontendV2 lint/typecheck/unit/build PASS；`canvas-core.test.mjs` PASS，25/25；`ai-agent-ui-contract.test.mjs` PASS，351/351；committed Playwright `studio2-flow-editor-port.spec.ts` PASS，2/2；Studio2 + BuildFromPlan architecture guard PASS，17/17；Desktop build PASS；Release publish PASS；build/publish V2 asset audit PASS；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 人工启动 NOT PERFORMED | 收口 Flow Editor Port request sequence authority、单一 allocator、节点拖拽 `moveNode` revision 和 dirty draft stale 行为；未执行 G04B、未新增 Project 保存/API/后端持久化身份，当前 Goal 仍为 G04B。 |
| 2026-07-01 | G04A | `33c8276332fe6543036d260bfb39f50a815c1c17` | 见 G04A 完成提交与最终报告 | FrontendV2 lint/typecheck/unit/build PASS；`canvas-core.test.mjs` PASS，23/23；`ai-agent-ui-contract.test.mjs` PASS，351/351；committed Playwright `studio2-flow-editor-port.spec.ts` PASS，2/2；Desktop build PASS；Release publish PASS；build/publish V2 asset audit PASS；Studio2 + BuildFromPlan architecture guard PASS，16/16；`git diff --check` PASS；完整 GitHub CI NOT RUN；真实 WebView2 人工启动 NOT PERFORMED | 新增 `StudioFlowEditorPort`，V2 只公开 `studio2.flowEditorPort`；补齐 snapshot/select/replace/patch/subscribe/dispose、本地 stale disposition 和最小参数 draft/commit；修复 hosted adapter 旧实例二次 dispose；未执行 G04B、未保存 Project、未改 Agent/Station/Runtime。 |
| 2026-07-01 | G03 | `b359a35aafc7c37ec24bb4f27f1f4364040495d2` | 见 G03 完成提交与最终报告 | FrontendV2 npm ci/lint/typecheck/unit/build PASS；`canvas-core.test.mjs` PASS，21/21；Desktop build PASS；Release publish PASS；build/publish V2 asset audit PASS；Studio2 + BuildFromPlan architecture guard PASS，16/16；浏览器级 1366×768、1920×1080、2560×1440 screenshot PASS；flag off/on 回归 PASS；WebView2 人工启动 NOT PERFORMED；完整 GitHub CI NOT RUN | 建立 V2 Workspace Shell MVP、Flow/Tool/Review 模式、唯一 hosted FlowCanvas 创建链和 lifecycle 并发 mount 修复；未迁移业务模块；下一项为 G04A。 |
| 2026-07-01 | G02B | `656645d3a653eee238fe41f1764e1254f8c879a3` | 见 G02B 完成提交与最终报告 | FrontendV2 npm ci/lint/typecheck/unit/build PASS；Desktop build PASS；Desktop.Tests build PASS；G02B targeted + Studio2/BuildFromPlan architecture guard PASS，30/30；Release publish PASS；build/publish V2 asset audit PASS；WebView2 人工启动 NOT PERFORMED；完整 CI NOT RUN | 新增 `Studio:WorkspaceV2Enabled` 默认 false；flag off 只导航 `/index.html`，flag on 只导航 `/v2/index.html`；`/v2` 静态资产独立映射输出目录；V2 测试岛复用 legacy httpClient/WebMessageBridge/EventBus/ServiceRegistry，未迁移业务 capability；下一项为 G03。 |
| 2026-07-01 | G02A follow-up | `d89c7d6e1c6c2b083f780f7957e75d29505a2ef2` | 见 G02A 收口修复提交与最终报告 | FrontendV2 npm ci/lint/typecheck/unit/build PASS；clean Debug build + repeat incremental build PASS；Release publish PASS；build/publish V2 asset audit PASS；Studio2 + BuildFromPlan architecture guard PASS，13/13；CI YAML lint/order check PASS；Markdown links/encoding/diff hygiene PASS；完整 CI NOT RUN | 修复 `/v2/` public base、收敛 MSBuild/CI 唯一 production build、调整 HostBridge/AgentRun guard 为长期白名单规则；当前 Goal 仍为 G02B，未执行 G02B。 |
| 2026-07-01 | G02A | `ff9a9430c0eb84e9b9f8e88beac9140c987bd4e8` | 见 G02A 完成提交与最终报告 | FrontendV2 npm ci/lint/typecheck/unit/build PASS；Desktop build PASS；Desktop publish PASS；Studio2 + BuildFromPlan architecture guard PASS；发布内容审计 PASS；完整 CI NOT RUN | 建立 `Desktop/FrontendV2` 独立 Vue 3/TypeScript/Vite/Pinia 构建底座，MSBuild/CI 复制发布资产到 `wwwroot/v2/`；下一项为 G02B。 |
| 2026-07-01 | G01 | `789e9ec643390f5a79c68cfa6c4b401c1a679be3` | 见 G01 完成提交与最终报告 | Desktop.Tests build PASS；Studio2ArchitectureGuardTests + BuildFromPlanArchitectureGuardTests targeted PASS；链接/编码/diff hygiene 待提交前验证；完整 CI NOT RUN | 建立 Studio 2.0 架构 ADR、capability 迁移白名单、Feature Flag 台账和最小自动架构守卫；下一项为 G02A。 |
| 2026-07-01 | G00 | `58c7569958f3bf8ab627f5c5b76ff0a77cc86914` | `3481d5a35f47bbf1f58c3f042cff6a679e720e0c` | Desktop build PASS；Product/Desktop targeted tests PASS；services regression PASS；UI unit PASS；链接/编码/diff hygiene PASS | Vision Agent 恢复治理阶段归档，Studio 2.0 基线冻结；下一项为 G01。 |

## 8. 通用最终汇报格式

- Goal / 结果：`DONE`、`BLOCKED_*` 或 `DEFERRED`
- Initial SHA / Final SHA / 远端 SHA
- 修改文件与核心行为变化
- active owner、唯一写入口、Legacy 是否仍 mounted/订阅
- API / Project format / Runtime / Station / AgentRun 影响
- 测试命令、通过/失败/未运行数量
- 截图、benchmark、artifact 路径
- 技术债与明确未完成项
- 已回填的 TODO/card 路径
- 下一 Goal（只报告，不提前实施）

## 9. 固定启动提示词

```text
读取 AGENTS.md、根目录 TODO.md 的架构红线/当前执行项/本轮协议，以及当前 Goal 指向的唯一执行卡。先 fetch 并确认本地 HEAD 与 origin/codex初稿 一致；不一致则 BLOCKED_REMOTE_DIVERGED。只执行当前 Goal，不读取和实施其他 Goal。先审计执行卡列出的真实代码锚点，再按清单实现。遵守单一 active owner、单一写入口、ProjectSaveCoordinator 权威、FlowCanvas/ImageCanvas 复用和 AgentRun 不重构红线。测试必须按 AGENTS.md 串行执行。完成后回填执行卡与 TODO.md，提交、push，并核对本地/跟踪分支/GitHub SHA；完整 CI 只能以真实 PR checks 或 workflow run 为证据。
```
