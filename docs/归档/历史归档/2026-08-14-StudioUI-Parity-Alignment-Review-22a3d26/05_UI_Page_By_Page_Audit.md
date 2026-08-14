# UI Page-by-Page Audit

## Legacy Inventory

Legacy 正式前端发现 8 个顶层 screen：Project、Flow Editor、Image Viewer、Inspection、Results、Stations、AI、Settings。导航还通过 Toolbar、Operator Rail/Flyout、Property/Preview/Lint panels、Global Variables、Final Decision dialog、ROI/Calibration/Line Sequence workbench、Context Menu、导入/导出 dialogs、Results export menu、10 个设置 tabs 与 Status Bar 暴露约 38 个二级 surface。

## Shell / Auth / Overview

- **Next：** `/login`、`/setup`、`/overview`、Product Shell、角色/profile guards 均可达。
- **迁移结论：** `PARITY/IMPROVED`。服务健康与会话投影清晰。
- **退化：** Legacy 持久用户/FPS/当前工程/版本状态被压缩为用户与服务状态，属于 P3 上下文回归。

## Projects

- **Next：** 列表、搜索、最近工程、空白创建、打开/关闭/删除、JSON import/export、正式保存均有 owner。
- **运行证据：** 真实 WebView2 journey 创建工程、保存并进入 Workspace。
- **缺口：** full/simple Demo 与 guide 没有 Next 入口；后端仍在，Legacy fallback 不算 Next parity。

## Workspace / FlowCanvas

- **Next：** Canonical FlowCanvas、Rail/Flyout、节点编辑、连接、复制粘贴、undo/redo、Inspector、Preview、ROI、保存、正式运行、最终判定、运行包形成完整主流程。
- **运行证据：** Workspace、ROI、Final Decision、Saved、Runtime Package screenshots 非空且交互状态一致。
- **缺口：** 右键“运行到此节点”被 `nodeRunEnabled=false` 显式禁用；双击/子图 breadcrumb 无对应 owner command。这是“表面迁移完成、二级行为不完整”的最典型页面。

## Inspector / Parameters

- **Next：** 类型化字段、特殊 editor、Flow templates、Camera binding、Line Sequence、Global Variable binding 与保存链存在。
- **缺口：** 智能推荐/接受/撤销完全丢失；推荐 endpoint 成为 orphan。
- **标定：** Planar Scale/Offset 已完成；N 点基本采集/solve/candidate/asset save 存在，但 9 点模板、粘贴/JSON、备注/排序、overlay 等高级流程仍 deferred。

## Image / Preview / ROI

- **Next：** Preview 与 Formal Run 文案/状态分离；图像、ROI、像素探针、缩放/适配均可达。
- **运行证据：** ROI screenshot 显示 selected node、图像输出、编辑 ROI、结构化结果和关键输出。
- **缺口：** Legacy 独立本地图像加载以及 annotation toggle/clear 未迁移。ROI clear 或 pixel lock clear 不能冒充 annotation clear。

## Global Variables / Final Decision

- **Next：** CRUD、binding、runtime values、最终判定和正式保存链存在；变量 identity/type 校验较 Legacy 更严格。
- **缺口：** search/filter 与“定位算子”丢失；runtime values 当前文档仍是 partial evidence。

## Inspection / Run / Stop

- **Next：** `/inspection`、project inspection、正式 run/stop/reconcile 与 authenticated HTTP/SSE authority 均存在。
- **结论：** 主任务迁移并改进。
- **部分项：** 缺料超时、连续 NG 保护/恢复正确地属于 Runtime/Inspection owner，但仍缺真实设备与现场状态投影验证，不能用前端 timer 重建。

## Results

- **Next：** 列表、筛选、详情、证据、对比、诊断、分析、bulk export、取消/对账和下载均找到 owner。
- **结论：** `PARITY/IMPROVED`。此前“bulk export 缺失”候选已由 F10 当前代码排除。
- **边界：** Fixture 与本机 software evidence 不证明 Station 上报和生产 retention/soak。

## Stations

- **Next：** Station list/detail/SSE、package deploy、command owner、幂等和 unknown-outcome reconcile 已存在。
- **缺口：** Legacy 在提交 package/stop/reload/ping 前的目标/影响确认未保留；Next panel 直接调用 owner。
- **设置：** token replace/regenerate 更安全，但 Legacy reveal/copy 没有等价的受控分发替代。
- **边界：** 当前没有真实现场 Station/PLC 证据。

## Settings

- **General/Runtime/Security/PLC/TCP/Camera/AI：** 字段与保存语义核验未发现确认断链；Camera 单帧与连续调试预览已实现。
- **Storage：** 策略和磁盘占用可用，browse/立即 cleanup 缺失。
- **Database：** status/backup 可用；repair/restore/cleanup/reset 仍是 Legacy fallback，按 ADR deferred。
- **Runtime Preview Pilot：** Legacy conditional tab/console 与后端 routes 仍在，Next 没有 panel/caller。
- **结论：** 标准设置迁移完整，高风险/开发者管理能力未完整迁移。

## AI Workbench

- **Next：** plan、clarification、build、preview、apply/undo/recovery、history/diagnostics 与 AgentRun authority 已迁移。
- **合同延期：** attachment resource、CV model artifact、TemplateMatching artifact、calibration projection 与 Line Sequence AI follow-up 没有获批合同；它们不是已证实的 Legacy parity regression，但阻止宣称“全部能力生产化”。

## Diagnostics / About

- **Next：** `/diagnostics` 与 `/about` 可达，未发现功能缺口。
- **结论：** `PARITY`。

## Cross-page Interaction Audit

| Interaction | Verdict |
| --- | --- |
| Node click/select | Verified in fixture/WebView2 journey |
| Node right-click run | Confirmed missing/disabled |
| Node double-click/subgraph | Confirmed missing |
| Inspector show/edit/save | Verified in software journey |
| Toolbar save/run/package | Verified in real WebView2 journey |
| Dialog/modal | Runtime Package screenshot verified; broad keyboard matrix not run |
| ROI edit | Verified by screenshot and journey |
| File picker | Code/unit evidence; native picker matrix not independently rerun here |
| Search/filter | Projects/Operators/Results present; Global Variables missing |
| Confirmation | Project/destructive flows vary; Station admin confirmation regressed |
| Keyboard/drag/drop/splitter | Partial code/fixture coverage; not comprehensively verified at real 125% DPI |

## Unmapped

`UNMAPPED_LEGACY_CAPABILITIES=0` at capability-group granularity. Hidden legacy hosts and conditional tabs were mapped to their owning capability rather than counted as independent products.
