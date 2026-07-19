# Studio UI Next F04 设计稿接入前置基线

> 用途：Stitch 流程工作台设计稿定稿后，Codex 实施前的唯一入口清单。
> 本轮范围：功能映射、组件/Owner/合同映射、状态矩阵、视觉验收基础设施。
> 本轮禁止：修改视觉、tokens、布局、FlowCanvas 节点表现，补齐 P0 功能，改变保存/运行/预览/属性/画布/后端合同，或根据未完成设计稿猜测视觉细节。
> 代码取证：稳定版 `codex初稿`=`4386d8f3537e80084802567b41d96414b0ddacd0`；Next 审计起点 `studio-ui-next`=`5d25c4985968145aea9c1ca44afa7cad8bbade87`。

## 1. 结论与实施原则

稳定版流程主界面不是单一 Canvas，而是“产品导航与工程命令 + Operator Rail/Flyout + Inspector + canonical FlowCanvas + Preview + 最终判定 + 全局变量 + 状态栏”的组合；工程、检测、追溯、监控、AI、设置是与流程页分离的产品页面。设计稿不能只覆盖中央画布，也不能把缺失能力画成已实现功能。

Studio UI Next 已建立单一 ProductRuntime、WorkspaceRuntime 和 capability-local Owner：Product Shell、工程生命周期、canonical FlowCanvas、Inspector、Preview/Image/ROI、保存、正式运行、结果页与受控工作站只读页均有明确承载。主要缺口是：Operator Flyout、最终判定可见编辑器、全局变量入口、独立检测页、AI 页和设置页；Canvas 也尚未把权威节点执行状态投影成完整视觉矩阵。

后续接入遵守四条规则：

1. Stitch 只替换表现层与信息层级，不替换 Owner、API、Project/Flow 合同、Canvas 内核、保存链或运行链。
2. 一个 capability 继续只有一个 mounted owner、一个订阅集合和一个写入口；Flyout、弹窗或折叠态不能复制状态树。
3. 缺失能力先按本文标记，不用静态 DOM、假按钮或本地持久化伪装完成。
4. Browser fixture、真实 WebView2、Windows 125% 和产品视觉批准是不同证据，互不替代。

## 2. 真实功能与组件映射

状态标签仅使用：`已保留`、`已重定位`、`实现方式已变化`、`明确延后`、`当前缺失`、`不应进入流程页`。

| 稳定版真实能力 | 稳定版入口/Owner | Studio UI Next 当前承载 | 状态 | 接入结论 |
| --- | --- | --- | --- | --- |
| 顶部产品导航：工程、流程、检测、追溯、监控、AI、设置 | `index.html` `.main-nav`；`viewManager.js` | `ProductLayout.vue` + `navigation.ts` + `router.ts`；工作区时收缩为产品 Rail | `已保留`、`已重定位`、`实现方式已变化` | Next 正式导航现有概览、工程、算子库、检测结果、诊断、关于；工作站按 profile 显示。检测、AI、设置路由仍缺失。 |
| 工程级命令：最终判定、保存、运行、全局变量 | 顶部 toolbar；`FinalDecisionPanel`、GlobalVariables owner、toolbar commands | `WorkspaceShell.vue` 提供保存、正式运行、停止、核对、结果入口；外观/会话在 Product Shell | `已重定位`、`实现方式已变化`；最终判定/全局变量 `当前缺失` | 不得把 Preview 当正式运行；不得为最终判定或变量新建第二保存入口。 |
| Operator 分类 Rail + Flyout + 搜索/点击/拖拽添加 | `#operator-rail`、`#operator-group-flyout`；`OperatorPaletteShell` + `OperatorLibraryPanel` | `OperatorRail.vue` 以单一侧栏承载搜索、分类 select、列表、点击和 HTML drag | Rail `已保留`；Flyout `当前缺失`；整体 `实现方式已变化` | Stitch 若恢复 Flyout，必须作为同一 `FlowCanvasOwner.catalog` 的表现子组件，不得新建 catalog store 或 add command。 |
| 左侧属性检查器 | `#property-panel`；`PropertyPanelCapabilityOwner` + `PropertySidebarController` | 右侧 `InspectorPanel.vue` + `ParameterEditor.vue`；`inspectorOwner.ts` | `已保留`、`已重定位`、`实现方式已变化` | 节点、连线、多选、端口、参数、依赖、必填、校验已承载；文件选择器/相机绑定等专用编辑器 `明确延后`。 |
| 中央 FlowCanvas | `#flow-canvas`；`FlowCanvas` + `FlowEditorInteraction` + adapter | `FlowCanvasSurface.vue` + `flowCanvasOwner.ts` + `platform/canvas/canonicalFlowCanvas.ts`；Vite alias 复用稳定版内核 | `已保留`、`实现方式已变化` | 节点/端口/连线/Minimap、序列化、选择、拖拽、连接校验继续使用 canonical 内核。不得另造 Canvas。 |
| 右侧预览工作台 | `#preview-panel`；Preview owner/coordinator、ImageCanvas、ROI | 下方 `PreviewPanel.vue` + `ImageViewport.vue`；`PreviewWorkbenchOwner` 组合 Preview/Image/ROI owners | `已保留`、`已重定位`、`实现方式已变化` | Preview 是可丢弃调试投影；图像、结构化结果、artifact、诊断、ROI、stale/error/cancel 已保留。 |
| 最终判定 | 顶部入口 + `FinalDecisionPanel`；校验 endpoint；保存进入 Project save | `workspaceContracts.ts` 保留 `decisionConfiguration/finalDecisionBinding`；Formal Run admission 消费权威配置 | `当前缺失` | Next 没有可见入口/编辑 Owner。设计稿可预留区域，但实施必须先复用既有合同与保存链，不能只做弹窗外壳。 |
| 全局变量 | 顶部入口 + `GlobalVariablesCapabilityOwner`/store/panel | `workspaceContracts.ts` 解码 GlobalVariables；Workspace 保存合同不把前端状态变成变量权威 | `明确延后`、可见能力 `当前缺失` | 不得用 Pinia/localStorage 补成变量权威；未来需接回现有 Application Service/ProjectSaveCoordinator。 |
| 底部状态栏：用户、就绪/运行、资源/FPS、工程、版本 | `.status-bar` | `WorkspaceShell.vue` 底栏显示保存、正式运行、兼容性、Owner 技术状态；用户/服务在 Product topbar，工程名/版本在 Workspace toolbar | `已重定位`、`实现方式已变化`；内存/FPS `当前缺失` | Stitch 应保留持续状态位置，但不要把技术 Owner 计数升级为主视觉；资源/FPS 是否恢复需产品决定。 |
| 工程页 | `project-view`；Project page owner | `/overview`、`/projects`、`/projects/:id`、`/projects/:id/workspace` | `已保留`、`已重定位`、`实现方式已变化`、`不应进入流程页` | 创建、打开、重命名、删除、冲突/unknown outcome 使用独立页面和唯一 lifecycle owner。 |
| 检测页 | `inspection-view`；Inspection owner/controller/SSE | Workspace 仅保留正式运行命令；无独立检测页面 | `当前缺失`、`不应进入流程页` | 不得把连续运行、现场控制和检测统计塞进 Flow Workspace；等待独立 capability。 |
| 追溯/结果页 | `results-view`；Results owner | `/results`，名称改为“检测结果” | `已保留`、`已重定位`、`实现方式已变化`、`不应进入流程页` | Workspace 只提供当前工程结果入口和完成后的 handoff。 |
| 监控/Station | `stations-view`；Station monitor view | `/stations`、`/stations/:id`，`Studio2.StationsRead` profile-gated，只读 | `已保留`、`实现方式已变化`、`不应进入流程页` | 不得在流程页复制 Station 状态或命令模型。 |
| AI | `ai-view`；AI panel capability owner | 无产品路由/页面 | `明确延后`、`当前缺失`、`不应进入流程页` | 不把 AI 会话、AgentRun 或 workspace snapshot 权威嵌入流程页。 |
| 设置 | `settings-view`；Settings capability owner | 仅外观偏好位于 Product topbar；无设备/系统设置页 | `已重定位`（外观）、其余 `当前缺失`、`不应进入流程页` | 相机、PLC、TCP、Station、AI、用户/安全等设置等待独立页面。 |

## 3. 设计稿接入组件清单

| 区域 | 当前组件与样式入口 | 数据/业务 Owner | 允许调整的视觉层 | 不得改变的合同 | 主要测试影响 | 顺序 |
| --- | --- | --- | --- | --- | --- | --- |
| Product Shell | `app/layouts/ProductLayout.vue`、`product-layout.css`、`app/base.css` | `ProductRuntime`：session、systemStatus、preferences、projectLifecycle、leaveGuard、workspace | DOM 分组、标签、图标、间距、表面层级、响应式投影 | Router meta/guard、profile 可见性、Auth 生命周期、Leave Guard、唯一 ProductRuntime | `router.spec.ts`、`product-navigation-contract.test.cjs`、`f04-auth.spec.ts`、`f04-project-lifecycle.spec.ts` | 1 |
| 顶部导航与工程命令 | Product topbar；`WorkspaceShell.vue` toolbar/scoped style | `projectLifecycleCommandOwner`、`workspacePersistenceOwner`、`runCommandOwner` | 命令排序、分组、图标、文案层级、状态位置 | `canSave/canRun/canStop/canReconcile`、PersistenceRevision、unknown outcome、Results handoff | Workspace page/persistence/run unit；`f03-workspace.spec.ts` | 2 |
| Operator Rail / Flyout | `flow/OperatorRail.vue` scoped style；Flyout 当前不存在 | `FlowCanvasOwner.catalog`；添加命令为 `flowOwner.commands.addOperator` | Rail/Flyout 外壳、分类展示、搜索布局、列表行、拖动态、浮层定位 | `/operators/library` 合同、catalog projection、点击/拖拽 payload、单一 add command、unmount/dispose | operators contracts/queries；flowCanvasOwner；F03 Operator Rail E2E | 3 |
| Inspector | `InspectorPanel.vue`、`ParameterEditor.vue`、`WorkspacePaneHeader.vue` scoped style | `InspectorOwner`；参数依赖/校验来自 metadata contracts | 分组、字段排版、帮助/错误层级、端口摘要、状态徽标、长中文策略 | patch commands、dependency rules、required/nullable、validation、draft guard、专用 editor 延后状态 | inspectorOwner/validation/registry unit；F03 Inspector E2E | 4 |
| FlowCanvas 节点/端口/连线/Minimap | `FlowCanvasSurface.vue`；canonical `wwwroot/src/core/canvas/flowCanvas.js`；`flow-editor-layout.css`/canvas variables | `FlowCanvasOwner` + `CanonicalFlowCanvasHost` | 仅 draw/presentation：节点几何显示、文字截断、状态纹理、端口/连线/Minimap 对比；toolbar 可单独调整 | 序列化、命中测试、pointer、选择、连接兼容、环路防护、undo/redo、mutation gate、dispose | canonicalFlowCanvas unit、canvas foundation/performance、F03 wiring/selection E2E、DPI/WebView2 | 7（Stitch 定稿后） |
| Preview | `PreviewPanel.vue`、`ImageViewport.vue`、`WorkspacePaneHeader.vue` scoped style | `PreviewWorkbenchOwner` → Preview/Image/ROI owners | 标题栏、图像/详情比例、状态/错误层级、artifact/diagnostic 排版、折叠表现 | Preview identity/generation、Abort/cancel、stale、artifact digest/cleanup、ImageCanvas、ROI patch/undo | preview/image/roi unit；F03 Preview E2E | 5 |
| splitter | `CvSplitter.vue` + `FlowWorkspace.vue` | `WorkspaceLayoutOwner`；localStorage 仅为可丢弃 UI 偏好 | grip、hover/focus/drag 反馈、命中区视觉 | 248–420 Inspector、160–420 Preview、38 折叠、键盘/Home/End/Enter、storage schema | `splitter.spec.ts`、workspaceLayoutOwner、F03 splitter E2E | 2 |
| 状态栏 | `WorkspaceShell.vue` statusbar/scoped style；Product service/user status | persistence/run/compatibility projections；systemStatus/session | 信息优先级、标签密度、溢出/tooltip、技术状态折叠 | 保存/运行双轴、错误/冲突/unknown outcome、Owner 计数真实性 | Workspace shell/persistence/run；F04 visual evidence metadata | 2 |
| 弹窗与浮层 | `CvModal.vue`、`CvToastRegion.vue`、Product appearance `<details>`；未来 Flyout/最终判定/变量入口复用现有 primitive | 触发 capability 的原 Owner；Modal 本身只管理焦点 | 尺寸、分区、滚动、锚点、backdrop、层级与响应式 | focus trap/restoration、Escape、destructive initial focus、Leave Guard、单一写入口 | modal/toast unit；F04 Auth/Project/Leave Guard E2E | 6 |

推荐顺序的含义：先固定 Shell/Workspace chrome 和尺寸，再接 Rail、Inspector、Preview，随后接浮层；FlowCanvas 节点、端口、连线、Minimap 必须等待 Stitch 定稿后最后实施，避免重新打开布局和 Owner 边界。

## 4. 状态验收矩阵

| 代表状态 | 权威来源/当前事实 | 当前自动覆盖 | Stitch 后验收要求 |
| --- | --- | --- | --- |
| 未打开工程 | Projects route/Project lifecycle owner；不是伪造空 Workspace | `projects-empty` | 清楚提供创建/打开入口；不显示可执行保存/运行状态。 |
| 空流程 | 解码后的真实 Project，Flow operators/connections 为空 | `workspace-empty`；四档 layout matrix | Rail、Canvas、Inspector、Preview 均可达；提示下一步，不新增营销式空白页。 |
| 普通节点选中 | canonical selection + Inspector projection | `workspace-node-selected-success` | 节点、Inspector、工具栏选择态一致；不只靠颜色。 |
| 长中文节点名称 | Project/Flow fixture 使用真实长中文字段 | Prompt 3 Inspector 248/296/420 截图 | 节点标题、Inspector、tooltip 不重叠、不丢关键语义。节点内部策略等待 Stitch。 |
| 多节点与复杂连线 | canonical 100/150 确定性 Flow | `workspace-multi-node-selected`、`workspace-complex-flow-100-150` | Canvas 仍是主工作区；Minimap、连线密度、选择反馈可辨识。 |
| 连线选中 | canonical selectedConnection + Inspector connection mode | `workspace-connection-selected` | 连线与端点均可识别；删除/断开动作明确且受 mutation gate 约束。 |
| 端口兼容/不兼容 | canonical `validateConnection` 与稳定版 tooltip/highlight | 逻辑测试已覆盖；产品截图 `明确延后` | Stitch 后新增稳定 hover/drag 截图；不得改变类型兼容、占用、重复、环路规则。 |
| 节点禁用 | canonical `disabled/isEnabled` | Canvas/Inspector 行为测试覆盖 | 禁用仍可选择和检查；与错误、未执行分开。 |
| 节点运行中/成功/失败 | Project operator `executionStatus`；Inspector 已展示；canonical canvas 有 draw API，但 Next owner 未绑定 | Inspector 成功状态截图；Canvas 完整矩阵 `当前缺失` | 先明确权威投影与刷新时机，再做 Canvas 常驻/瞬时状态；不能从 Formal Run 总状态猜节点状态。 |
| 节点业务 NG | 正式结果的 DecisionOutcome；不是通用节点 ExecutionStatus | `当前缺失` | 只有获得节点级权威 decision 投影时才显示在节点；否则业务 NG 保留在最终判定/结果层。 |
| 参数必填、禁用、依赖、校验错误 | metadata constraints + Inspector validation | required/dependency unit；disabled、inline validation 截图 | 标签、原因、依赖变化、错误定位都靠近字段；不得隐藏关键参数。 |
| Preview 等待 | PreviewOwner `idle` | 基础 Workspace 场景 | 明确“等待选择/可预览条件”，不与无输出混淆。 |
| Preview 加载/成功/过期/安全拦截/失败/取消 | PreviewOwner phase + request identity/generation | loading、success、blocked、business/network failure、cancelled 截图；stale 逻辑测试 | 七类状态文案、动作和图像/结果保留策略均不同；stale 不可继续读取 artifact。 |
| 图像/无图像 | ImageCanvasOwner phase | success image、no-output/empty | 图像状态不挤掉结构化结果；无图像不是失败。 |
| 像素探针 | PixelProbeProjection | `workspace-image-probe-locked` | hover 不高频播报；locked/ROI 状态有坐标/像素/清除入口。 |
| ROI | RoiInteractionOwner + Inspector typed patch | 1920 与 1350 comfortable ROI editing | 编辑/撤销/重做/放弃/应用可达；确认只产生一次 typed patch。 |
| 最终判定未配置/有效/无效 | Project decisionConfiguration + backend validation/admission | 合同/Run unit；可见 UI `当前缺失` | 必须复用现有校验与 Project save；三态不能只凭颜色；无效状态阻止正式运行。 |
| 全局变量入口 | Project GlobalVariables/Application Service | `当前缺失` | 入口可预留但不能假实现；未来继续进入唯一 Project save coordinator。 |
| Operator Flyout 展开 | 稳定版 Rail/Flyout；Next 当前无 Flyout | `当前缺失` | Stitch 后覆盖展开、搜索、分类、长中文、键盘/焦点、视口边界和关闭恢复。 |

## 5. 视觉验收基线与运行方式

新增场景目录：

- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f04-design-handoff-baseline.mjs`
- 自动场景：32 个；不可伪造/待实现状态：5 个。
- 自动场景来自真实 Vue 页面、Owner、API contract fixture 和 canonical Canvas 交互，不是独立静态 showcase。
- 截图 metadata 新增 Operator/Canvas/Inspector/Preview/Image/ROI/Probe 状态，以及 Flyout/最终判定/全局变量是否真实存在的记录。

从仓库根目录运行：

```powershell
& "./scripts/studio-ui-next/Invoke-F04DesignHandoffBaseline.ps1" `
  -SourceSha (git rev-parse HEAD)
```

脚本会：

1. 仅运行场景目录声明的 F03/F04 Playwright 测试；
2. 在 `.tmp/studio-ui-next/f04/design-handoff-baseline-<sha>/` 生成 PNG/JSON；
3. 校验 SHA、视口、density、全局横向溢出、runtime error、截图字节数与 SHA-256；
4. 覆盖 1920×1080、1350×704、compact/comfortable、长中文、Inspector 248/296/420、Preview 160/420/折叠恢复，以及真实交互状态。

该脚本提供 Browser fixture 基线，`nativeWebView2Dpi` 会诚实记录为未由此证据执行。Stitch 实施完成后仍必须补：

- 真实 WebView2 1920×1080；
- Windows 125% 系统缩放；
- 真实 WebView2 1350×704 或等效短屏 client；
- 设计稿涉及的新增 Flyout/最终判定/全局变量状态；
- 节点/端口/连线/Minimap 定稿后的视觉截图；
- 产品负责人最终视觉确认。

## 6. Stitch 定稿后实施入口

Codex 收到 Stitch 稿后按以下检查开始，不重新猜需求：

1. 将每个设计区域对回第 3 节现有组件、Owner 和合同。
2. 对第 2 节所有 `当前缺失` 项确认：设计仅预留，还是已有独立功能任务批准；未批准不得实现假能力。
3. 用第 4 节逐状态检查设计稿，缺少状态先退回补稿，不以默认态推断。
4. 先运行本基线，记录当前截图；每个实现包后重跑对应场景。
5. FlowCanvas 只改 canonical draw/presentation 层；任何需要改变序列化、命中、连接、pointer、运行或保存合同的需求立即停止并报告。
6. 最终同时提交 Browser、真实 WebView2、125% DPI 与产品视觉确认状态；未运行项标记 `NOT RUN/NOT PERFORMED`。

## 7. 必须等待 Stitch 定稿的工作

- Product Shell 与 Workspace chrome 的最终比例、对齐和视觉层级；
- Rail/Flyout 的最终形态、展开方向、宽度与分类交互；
- Inspector/Preview 在 Stitch 中的最终宽度、高度和信息优先级；
- FlowCanvas 节点标题策略、节点尺寸、执行状态是否常驻；
- 端口标签密度、兼容/不兼容反馈、连线默认/选中/错误对比；
- Minimap 可见强度、位置与折叠语法；
- 最终判定、全局变量等缺失能力是否只预留入口，还是另行批准实现；
- 弹窗/Flyout 的最终尺寸与锚点。

在这些事项定稿前，不修改视觉样式、tokens、布局或 Canvas 节点表现。
