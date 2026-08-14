# ClearVision Studio 新旧前端功能迁移完整性审计

审计日期：2026-08-14  
Legacy：`codex初稿 @ e76c74e392bb14ffe02ef9ea9c7a614cb8987f04`（clean）  
Next：`studio-ui-next @ 22a3d26a00a2d3b8098165aab5489ce54f5bc95b`（ahead 7，dirty working tree）  
审计对象：上述 Next HEAD 加当前未提交工作树快照；不是 clean `22a3d26` 的审计。  
结论：`FRONTEND_PARITY=PARTIAL`

## 结论

新版已经承载主要工程闭环：登录、工程生命周期、Canonical FlowCanvas、参数编辑、预览/ROI、正式保存、正式运行/停止/对账、结果、运行包、Station、标准设置和 AI 工作台均有可达实现。真实 WinForms/WebView2 100% DPI 黄金路径也已通过。

但它还不能作为 Legacy 的完整功能替代。65 个能力/验收组中，42 个达到等价、重定位或改进，4 个部分迁移，13 个存在确认缺口，6 个仍缺真实环境或调用者证据。确认缺口集中在迁移时容易遗漏的二级入口与高级工作台，而不是顶层页面消失。

## 必答结论

1. **是否基本完整迁移：** 主体工程工作流已迁移，但完整功能迁移尚未完成。
2. **确认功能缺口：** 13 个主状态缺口组：`MISSING=9`、`ENTRY_MISSING=1`、`BACKEND_ORPHANED=3`。另有 `PARTIAL=4`。
3. **完全无 Next 入口：** 示例工程创建、Canvas 运行到节点、Canvas 双击/子图、智能参数推荐、独立图像加载/标注控制、Station 确认与 token 复制、存储浏览/立即清理、持续状态上下文等。
4. **后端仍在但 UI 断开：** 4 组具有该特征：示例工程、算子智能推荐、数据库高级维护、Runtime Preview Pilot。Demo 的主状态记为 `ENTRY_MISSING`，所以不在主状态计数中重复。
5. **表面完成但行为不完整：** FlowCanvas、Inspector、Global Variables、ImageViewport、Station Admin、Storage、N 点标定和持续检测保护。
6. **优先修复：** 先恢复 Canvas 调试/子图语义与 Inspector 推荐闭环，再补 Station 高风险命令确认；随后处理明确延期但仍需 Legacy fallback 的管理能力。
7. **系统性模式：** 顶层 route/owner 迁移较完整，二级交互、确认语义、辅助检索和开发者/管理员工作台被弱化；“后端/owner 存在”曾被当成“用户能力已迁移”。
8. **能否完整替代 Legacy：** 不能。`LEGACY_RETIREMENT=NOT_APPROVED`，且 Windows 125%、独立 no-Node、现场设备、Remote CI 和生产 soak 尚未验证。

## 覆盖统计

| 指标 | 数量 | 说明 |
| --- | ---: | --- |
| Legacy top-level screens | 8 | 7 个导航 view，加非导航 Image Viewer |
| Legacy secondary surfaces | 38 | Dialog、Flyout、Workbench、Context Menu、设置 tab、Toolbar、Status Bar 等 |
| Legacy capability/audit groups | 65 | 包含 59 个功能组和 6 个验收/反向审计组 |
| Audited / Mapped | 65 / 65 | `UNMAPPED_LEGACY_CAPABILITIES=0` |
| Equivalent | 42 | `PARITY=14`、`MOVED=6`、`IMPROVED=22` |
| Partial | 4 | 功能或证据闭环不完整 |
| Missing | 9 | 主状态，不含 Entry Missing/Backend Orphaned |
| Entry missing | 1 | 示例工程创建 |
| Dead UI | 0 | 未确认按钮存在但完全无 handler 的死 UI |
| Backend orphaned | 4 | 特征计数；其中 3 行以该状态为主，Demo 行主状态为 Entry Missing |
| Not verified | 6 | 真实环境/调用者证据缺口 |

## 严重度

确认的 13 个缺口：`P0=0`、`P1=4`、`P2=8`、`P3=1`。明确延期并不等于迁移完成，也不等于 intentionally retired；本轮没有任何能力获得 `INTENTIONALLY_RETIRED` 判定。

## Top Blockers

1. FlowCanvas 右键“运行到此节点/调试预览”被 Next 显式禁用。
2. FlowCanvas 双击节点与子图 breadcrumb 语义未迁移。
3. Inspector 智能推荐、接受、撤销链路消失，后端 endpoint 仍存在。
4. Station 命令/部署已有 owner，但旧版的高风险确认与终态上下文未保留。
5. Legacy fallback 仍承载 Demo、Database Advanced、Runtime Preview Pilot；Next 不能宣称完整替代。

## 判定边界

- `MIGRATED` 文档状态只证明 Next 代码、route、owner 存在，不自动等于运行或产品验收。
- Legacy fallback 不计为 Next UI 入口。
- Fixture Chromium、真实 WebView2、真实 Windows DPI、no-Node 和现场设备是不同证据层级。
- 本轮只审计和取证；未修改产品源码、未提交、未推送、未切分支。

## 后续执行

科学分波次、单 Owner、分层验收的对齐台账见 [功能对齐 TODO 计划](08_Parity_Alignment_TODO.md)。
