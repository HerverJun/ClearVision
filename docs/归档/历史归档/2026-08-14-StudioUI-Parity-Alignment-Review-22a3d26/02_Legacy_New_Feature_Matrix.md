# Legacy -> Studio UI Next 功能矩阵

主状态互斥；`Backend orphan` 列是可重叠特征。`Equivalent = PARITY + MOVED + IMPROVED`。证据均针对当前工作树，文档状态不能覆盖当前代码事实。

| ID | Capability | Legacy entry / evidence | Next entry / evidence | Primary status | Orphan | 结论 |
| --- | --- | --- | --- | --- | --- | --- |
| A01 | 登录、会话、首次设置 | `wwwroot/src/features/auth/` | `/login`, `/setup`; `router.ts` | PARITY | - | 权限与会话 owner 可达 |
| A02 | Shell、导航、角色/profile | `index.html:80-98` | `ProductLayout.vue`; router guards | PARITY | - | 新 IA 保留主要入口 |
| A03 | 工程列表、搜索、最近工程 | `projectView.js` | `/projects`; `ProjectsPage.vue` | PARITY | - | 列表任务完整 |
| A04 | 空白工程创建/打开/关闭/删除 | `projectManager.js` | project lifecycle owner | MOVED | - | 重定位到 Projects/Workspace |
| A05 | 正式保存、revision、冲突/对账 | Legacy save handlers | `ProjectSaveCoordinator` projection | IMPROVED | - | 保存身份与未知结果更明确 |
| A06 | 工程 JSON 导入/导出 | Project dialogs | F10 `PROJECT_IMPORT_EXPORT=DONE` | IMPROVED | - | 旧 blocker 已解阻 |
| A07 | 示例/演示工程创建 | `projectView.js:475,525`; `projectManager.js:161-186` | 无 Next route/control | ENTRY_MISSING | yes | endpoint 仍在，Next 用户不可达 |
| A08 | Workspace 组合壳 | Flow/Image/Property panels | `/projects/:id/workspace` | MOVED | - | 多面板合并为单 owner 组合 |
| A09 | Canonical FlowCanvas 内核 | `flowCanvas.js` | `canonicalFlowCanvas.ts` | IMPROVED | - | 复用内核并收窄生命周期 |
| A10 | 节点编辑、端口、连接校验 | Canvas commands | `flowCanvasOwner.ts:79-100` | IMPROVED | - | owner 命令边界更清楚 |
| A11 | 复制/粘贴、撤销/重做 | Canvas toolbar/context menu | owner commands | PARITY | - | 高频编辑仍可达 |
| A12 | 算子 Rail/Flyout 添加 | operator palette/flyout | `OperatorRail.vue`, `OperatorFlyout.vue` | IMPROVED | - | 搜索与画布组合更紧密 |
| A13 | 算子目录与搜索 | operator library | `/operators`; workspace rail | PARITY | - | 独立浏览和工作区入口并存 |
| A14 | Inspector 类型化参数编辑 | `propertyPanel.js` | `InspectorPanel.vue` | PARITY | - | 常规字段编辑与保存链存在 |
| A15 | 文件/颜色/路径特殊编辑器 | Legacy special editors | shared `FilePickerPort` + inspector | IMPROVED | - | 统一 Host picker 边界 |
| A16 | Flow Templates | template selector | template owner -> canonical draft | IMPROVED | - | 不建立第二保存链 |
| A17 | 智能参数推荐/接受/撤销 | `propertyPanel.js:442,2358,2449-2456` | 无调用；endpoint `ApiEndpoints.cs:1672` | BACKEND_ORPHANED | yes | 完整交互闭环丢失 |
| A18 | 节点手动/自动预览 | Preview panel | `PreviewPanel.vue`; preview owner | PARITY | - | Preview 与 Formal Run 分离 |
| A19 | 图像预览工作区 | 独立 Image Viewer | Workspace `ImageViewport.vue` | MOVED | - | 主图像任务已整合 |
| A20 | ROI 几何编辑 | ROI editor/workbench | image/ROI owner | PARITY | - | 运行截图验证可达 |
| A21 | 像素探针、缩放/适配 | `imageViewer.js` | pixel probe projection | IMPROVED | - | 状态投影更窄 |
| A22 | Line Sequence 预览/推荐/Apply | wire sequence assist | `LineSequenceOwner`; F10 `DONE` | IMPROVED | - | 预览和 Apply 进入唯一草稿 owner |
| A23 | N 点高级标定工作流 | calibration workbench | Workspace calibration draft | PARTIAL | - | 9 点模板、导入/导出、overlay 等延期 |
| A24 | Planar Scale/Offset 标定 | calibration flow | F10 `PLANAR_CALIBRATION=DONE` | IMPROVED | - | 软件闭环已实现；asset projection 另有延期 |
| A25 | 全局变量定义与绑定 | global variable panel | `GlobalVariablesWorkbench.vue` | IMPROVED | - | 类型/identity 校验增强 |
| A26 | 全局变量搜索、筛选、定位算子 | `globalVariablePanel.js:226,463,1142` | 无对应 search/locate control | MISSING | - | CRUD 在，但二级效率入口丢失 |
| A27 | 全局变量运行值 | Legacy runtime values | runtime value owner | PARTIAL | - | 代码存在，当前证据仍为 partial evidence |
| A28 | 最终判定 | final decision dialog/panel | workspace final-decision owner | PARITY | - | 黄金路径截图验证 |
| A29 | 正式运行、停止、未知结果对账 | legacy run controls | formal run owner | IMPROVED | - | authenticated HTTP/SSE authority 保留 |
| A30 | 连续检测页面 | inspection view | `/inspection`, project inspection | MOVED | - | 入口与 owner 重构 |
| A31 | 缺料/连续 NG 保护与恢复 | Legacy inspection guards | Runtime/Inspection projection | PARTIAL | - | 策略正确重定位，真实现场投影未闭环 |
| A32 | 结果列表、筛选、详情 | results view | `/results`; results owner | PARITY | - | 核心追溯任务存在 |
| A33 | 证据、对比、详情诊断 | result panels | results evidence/compare | MOVED | - | 重组到 Results 页面 |
| A34 | 结果分析与批量导出 | export dropdown/analysis | F10 `RESULTS_BULK_EXPORT=DONE` | IMPROVED | - | job/reconcile 模型增强 |
| A35 | Runtime Package 导出 | project export | runtime package owner | IMPROVED | - | 真实 WebView2 黄金路径验证 |
| A36 | Station 列表、详情、SSE | station workspace | `/stations`; station owners | IMPROVED | - | 只读与管理投影分离 |
| A37 | Station package/command owner | monitor actions | `stationAdminCommandOwner.ts` | IMPROVED | - | 幂等/reconcile owner 已存在 |
| A38 | Station 命令/部署确认 | `stationMonitorView.js:942-1025` | `StationAdminPanel.vue:187-193` 直接调用 | MISSING | - | 高风险确认和操作上下文退化 |
| A39 | General/Runtime/Security 设置 | settings tabs | `/settings` panels | PARITY | - | 标准设置合同完整 |
| A40 | Storage 策略与磁盘占用 | system storage tab | `SettingsStoragePanel.vue:113-205` | IMPROVED | - | 策略和占用读取可用 |
| A41 | Storage 浏览路径/立即清理 | browse/cleanup buttons | 无 Host browse/cleanup control | MISSING | - | 经 ADR/计划明确延期 |
| A42 | Database 状态与安全备份 | database tab | `SettingsDatabasePanel.vue` | PARITY | - | 非破坏性子集完整 |
| A43 | Database repair/restore/cleanup/reset | `settingsApi.js:11-15`; `systemTabs.js` | 只排除，endpoint 仍在 | BACKEND_ORPHANED | yes | Legacy fallback；未退休 |
| A44 | PLC/TCP/Camera/AI 标准设置 | settings tabs | settings capability panels | PARITY | - | 字段语义核验未发现缺口 |
| A45 | Station token 替换/轮换 | station settings | masked replace/regenerate | IMPROVED | - | 不在浏览器持有明文 |
| A46 | Station token reveal/copy | `stationTab.js:149-160,309-359` | 无 reveal/copy | MISSING | - | 运维便利能力未等价迁移 |
| A47 | Camera 绑定与采集单帧 | camera tab | `SettingsCameraPreviewSection.vue` | IMPROVED | - | “单帧缺失”候选已排除 |
| A48 | Camera 标定入口 | camera calibration | Workspace NPoint owner | MOVED | - | 正确重定位，不算缺失 |
| A49 | AI plan/build/apply/undo/recovery | AI panel/dialog | `/ai`; AI workbench owners | IMPROVED | - | AgentRun authority 保留 |
| A50 | AI 历史、诊断与 handoff | AI diagnostics | workbench history/diagnostics | IMPROVED | - | 生命周期与诊断更明确 |
| A51 | Runtime Preview Pilot 管理台 | pilot console/settings tab | 无 Next panel；routes 仍在 | BACKEND_ORPHANED | yes | 明确 deferred Legacy fallback |
| A52 | 服务健康与当前会话 | legacy status bar | system status/session projection | IMPROVED | - | 健康和登录身份更可靠 |
| A53 | 持久 FPS/工程/版本/内存上下文 | `index.html:804,837,857,860` | ProductLayout 未持续展示 | MISSING | - | P3 信息上下文退化 |
| A54 | Overview/Diagnostics/About | dashboard/help | `/overview`, `/diagnostics`, `/about` | PARITY | - | 支持面完整 |
| A55 | 独立本地图像文件加载 | `features/image-viewer/imageViewer.js:273` | ImageViewport 仅 artifact/blob | MISSING | - | Workspace 预览不等价于独立载入 |
| A56 | 图像 annotation toggle/clear | `features/image-viewer/imageViewer.js:670,676` | 无对应 UI/owner API | MISSING | - | ROI clear 不是 annotation clear |
| A57 | Canvas 右键运行到节点/调试预览 | `flowCanvas.js:2715,2851-2871` | `canonicalFlowCanvas.ts:711` 禁用 | MISSING | - | 明确交互回归 |
| A58 | Canvas 双击节点/子图 breadcrumb | `flowCanvas.js:2385`; `app.js:539-556,2150` | 无 dblclick/subgraph command | MISSING | - | 明确交互语义丢失 |
| A59 | 默认切换与 rollback 生产接受 | WebView root | `StartupProfile=NEXT_DEFAULT` | PARTIAL | - | 本机证据通过，生产验收未授予 |
| A60 | `/operators/{type}/preview` UI 归属 | 无 Legacy caller 已建立 | 无 Next caller 已建立 | NOT_VERIFIED | - | 不能据 endpoint 单独判 orphan |
| A61 | `/images/upload` UI 归属 | 无 Legacy caller 已建立 | 无 Next caller 已建立 | NOT_VERIFIED | - | 同上 |
| A62 | 真实 Windows 125% DPI | Legacy desktop | 未执行真实 125% | NOT_VERIFIED | - | 浏览器 DPR 不能替代 |
| A63 | 独立无 Node 目标机 | deployed desktop | 未执行 | NOT_VERIFIED | - | build/WebView2 不替代 |
| A64 | Camera/PLC/TCP/Station/AI 现场链路 | real devices/services | 未执行真实现场验收 | NOT_VERIFIED | - | 软件 fixture 不替代硬件 |
| A65 | Remote CI、生产 soak、产品签收 | release process | 未完成/未授予 | NOT_VERIFIED | - | Legacy retirement 仍未批准 |

## 复算

```text
LEGACY_CAPABILITIES=65
AUDITED=65
MAPPED=65
PARITY=14
MOVED=6
IMPROVED=22
EQUIVALENT=42
PARTIAL=4
MISSING=9
ENTRY_MISSING=1
BACKEND_ORPHANED_PRIMARY=3
BACKEND_ORPHANED_CHARACTERISTIC=4
NOT_VERIFIED=6
DEAD_UI=0
INTENTIONALLY_RETIRED=0
UNMAPPED_LEGACY_CAPABILITIES=0
```
