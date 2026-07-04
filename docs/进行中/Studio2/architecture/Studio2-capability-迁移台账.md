# Studio 2.0 capability 迁移白名单与 Feature Flag 台账

> 状态：G01 台账
> 日期：2026-07-01
> Initial SHA：`789e9ec643390f5a79c68cfa6c4b401c1a679be3`
> 维护规则：本文件是 Studio 2.0 capability 迁移的唯一白名单。未登记的 capability 不得迁移、挂载或建立运行时 Feature Flag。

## 总规则

- 同一 capability 任一时刻只能有一个 mounted owner、一个订阅集合、一个 timer 集合和一个写入口。
- Feature Flag 只控制挂载 owner，不改变业务权威。flag on 后旧实现必须不挂载、不订阅、不运行 timer、不持有资源。
- 不允许通过 CSS 隐藏旧实现冒充切换。
- G01 只登记 flag；G15.1/G15.2/G15X 已按各自 Goal 实现默认关闭 runtime flag，并写入 Host 配置与 `appsettings`。
- V2 typed API 必须包裹现有 `httpClient`，不得重做 auth、端口发现和网络错误策略。
- Project、Flow、GlobalVariables 的正式保存仍经 `ProjectService + ProjectSaveCoordinator`。
- Pinia、DOM、localStorage 只能作为投影或编辑草稿，不得作为正式业务权威。
- G02A 的 V2 源码目录为 `ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/`；发布资产目录为 `wwwroot/v2/`；不得把 Vue/TypeScript/package/node_modules 放入旧 `wwwroot/src`。
- FrontendV2 production build 的正式入口是 Desktop `.csproj`；CI 只提前执行 `npm ci`、lint、typecheck 和 unit test，后续 `dotnet build/publish` 通过 MSBuild 执行唯一 production build。
- G02B 的 `Studio:WorkspaceV2Enabled` 是宿主启动页面切换，不是业务 capability Feature Flag。默认 `false`；关闭时旧 `app.js` 是唯一 root，打开时 WebView2 只加载 `/v2/index.html`。
- G03 将 `/v2/index.html` 承载内容从无业务测试岛升级为 Workspace Shell MVP；它只挂载 Shell、dock、模式切换和 hosted FlowCanvas，不迁移 Flow Editor、Project 保存、Property、Preview、AI、Results、Inspection 或 GlobalVariables capability。

## capability 台账

| capability | legacy owner | planned V2 owner | read source | write entry | migration Goal | cutover Goal | deletion Goal | Feature Flag | flag off 行为 | flag on 行为 | rollback | 当前状态 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Workspace Shell | `app.js` + `viewManager` | V2 Workspace Shell root | 现有 DOM view state、Project 投影、Host 注入 API base URL | 只允许切换 view、注册 Shell owned adapter 和维护 UI 布局状态；不写业务 authority | G03 | G03 | G16 | `Studio2.WorkspaceShell` | 旧 `app.js` 是唯一 mounted root | V2 Shell 是唯一 mounted root，旧 Shell 不订阅、不运行 timer；Flow 模式只经 hosted `FlowCanvasAdapter` 创建一个现有 `FlowCanvas` | flag off 恢复旧 Shell | DONE_G03 |
| Flow Editor | `FlowCanvas` + `FlowEditorInteraction` + `FlowCanvasAdapter` | V2 Flow Editor | `StudioFlowEditorPort` snapshot、Project flow 投影 | V2 只经 `studio2.flowEditorPort` 修改画布；正式保存经 `studio2.projectPersistencePort` -> `PUT /api/projects/{id}` -> ProjectSaveCoordinator | G04A | G04B | G16 | `Studio2.FlowEditor` | 旧 Flow Editor 挂载并持有 FlowCanvas | G04B 已建立 V2 单请求保存 facade；`flowRevision` 仍仅作本地 stale 防护，后端并发身份为 `PersistenceRevision` | flag off 恢复旧 Flow Editor 与 adapter | DONE_G04B_SAVE_PORT |
| Property Panel | legacy property panel/sidebar | V2 Property Panel capability owner | selected node projection、operator metadata、Project schema | `PropertyPanelCapabilityOwner -> PropertyPanelCapabilityAdapter -> FlowCanvasAdapter.patchNodeParameters()` | G15.1 | G15.1 | G16 | `Studio2.PropertyPanel` | 旧 Property Panel 是唯一 owner | V2 Property Panel owner 是唯一 mounted owner，旧 panel 不 mounted、不订阅 selection、不运行 timer、不写参数 | flag off 恢复旧 property panel | RUNTIME_IMPLEMENTED_G15_1 |
| Preview Panel | `NodePreviewCoordinator`、preview overlay、image viewer | V2 Preview Panel capability owner | preview endpoint、artifact URL、current flow/node projection | `PreviewPanelCapabilityOwner -> PreviewPanelCapabilityAdapter -> NodePreviewCoordinator`；不写正式结果；draft request 只走既有 preview API | G15.2 | G15.2 | G16 | `Studio2.PreviewPanel` | 旧 preview panel/overlay/ROI preview 资源保持现有 legacy 行为；V2 owner 不挂载 | V2 Preview Panel owner 是唯一 mounted preview owner；legacy preview panel/overlay/ROI preview 不构造、不挂载、不订阅、不运行 debounce/timer、不发起 preview request、不读 artifact | flag off 恢复旧 preview panel | RUNTIME_IMPLEMENTED_G15_2 |
| Global Variables | legacy `GlobalVariablePanel` | `GlobalVariablesCapabilityOwner` | Project `globalVariables` schema、Project variable session projection | `GlobalVariablesCapabilityOwner -> GlobalVariablesCapabilityAdapter -> projectManager.updateGlobalVariables()/saveGlobalVariables()`，正式保存仍经 ProjectService + ProjectSaveCoordinator | G15.3 | G15.3 | G16 | `Studio2.GlobalVariables` | 旧变量面板是唯一 mounted owner | V2 变量面板是唯一 owner，旧面板不 mounted、不订阅 runtime state、不运行 polling timer、不写入 | flag off 恢复旧变量面板 | RUNTIME_IMPLEMENTED_G15_3 |
| Settings | legacy `SettingsView` | `SettingsCapabilityOwner` | 当前 settings projection、Host/desktop settings endpoint | `SettingsCapabilityOwner -> SettingsCapabilityAdapter -> settingsApi.saveSettings()` | G15.5 | G15.5 | G16 | `Studio2.Settings` | 旧 SettingsView 有效 | V2 Settings owner 有效，旧 SettingsView 不 mounted、不运行 lifecycle timeout/modals、不写 settings | flag off 恢复旧设置入口 | RUNTIME_IMPLEMENTED_G15_5 |
| Project | `projectManager` + legacy `ProjectView` | `ProjectPageCapabilityOwner` | `/api/projects/*`、Project DTO、`PersistenceRevision` | `ProjectPageCapabilityOwner -> ProjectPageCapabilityAdapter -> projectManager`；正式写入仍经 ProjectService + ProjectSaveCoordinator | G15.8 | G15.8 | G16 | `Studio2.ProjectPage` | 旧 Project 页面是唯一 owner | V2 Project Page owner 是唯一 owner，旧 ProjectView 不 mounted、不绑定按钮、不写 project | flag off 恢复旧 Project 页面 | RUNTIME_IMPLEMENTED_G15_8 |
| Inspection | `inspectionController` + legacy `InspectionPanel` | `InspectionCapabilityOwner` | existing inspection state、run endpoints、current project projection | `InspectionCapabilityOwner -> InspectionCapabilityAdapter -> inspectionController`；不写 AgentRun authority | G15.6 | G15.6 | G16 | `Studio2.Inspection` | 旧 Inspection panel 运行 | V2 Inspection owner 唯一 mounted，旧 panel 不订阅 completion/error、不运行 watchdog/timer、不发 request | flag off 恢复旧 Inspection panel | RUNTIME_IMPLEMENTED_G15_6 |
| Results/Review | legacy `ResultPanel` + analytics refresh | `ResultsReviewCapabilityOwner` | formal inspection history endpoint、compare、previous-success、evidence export | `ResultsReviewCapabilityOwner -> ResultsReviewCapabilityAdapter -> app.js` existing history/detail/compare/previous-success/evidence loaders | G15.7 | G15.7 | G16 | `Studio2.ResultsReview` | 旧 Results panel 挂载并管理 refresh timer | V2 ResultsReview owner 唯一 mounted，旧 stream/refresh timer 不运行，不读取 PreviewArtifact/preview cache | flag off 恢复旧 Results panel | RUNTIME_IMPLEMENTED_G15_7 |
| AI Panel | legacy `AiPanel` + generation controller | `AiPanelCapabilityOwner` | AgentRun replay/events、session projection、existing AgentRun endpoints | `AiPanelCapabilityOwner -> AiPanelCapabilityAdapter -> existing AgentRun endpoints`；不建立第二 Agent authority | G15.4 | G15.4 | G16 | `Studio2.AiPanel` | 旧 AI Panel 是唯一 owner | V2 AI Panel owner 是唯一 mounted AgentRun projection owner，旧 panel 不订阅 WebMessageBridge/canvas、不运行 timer/SSE、不发 command | flag off 恢复旧 AI Panel | RUNTIME_IMPLEMENTED_G15_4 |

## Feature Flag 生命周期

| Feature Flag | owner | 创建 Goal | runtime 实现 Goal | cutover Goal | 删除 Goal | 默认值 | flag off 必须保证 | flag on 必须保证 | 当前状态 |
|---|---|---|---|---|---|---|---|---|---|
| `Studio2.WorkspaceShell` | Workspace Shell | G01 登记 | G03 | G03 | G16 | off | 旧 Shell 唯一 mounted | V2 Shell 唯一 mounted，ServiceRegistry 只注册 Shell owned adapter，不注册 raw canvas | RUNTIME_IMPLEMENTED_G03 |
| `Studio2.FlowEditor` | Flow Editor | G01 登记 | G04A | G04B | G16 | off | 旧 Flow Editor 持有 FlowCanvas | V2 经 `StudioFlowEditorPort` 写入，Port 内部包装现有 `FlowCanvasAdapter`；正式保存经 `studio2.projectPersistencePort` 单次调用既有 Project PUT | SAVE_PORT_DONE_G04B |
| `Studio2.PropertyPanel` | Property Panel | G01 登记 | G15.1 | G15.1 | G16 | off | 旧 Property Panel 订阅 selection，V2 owner 不挂载 | V2 Property Panel owner 唯一订阅 selection，legacy `PropertyPanel` 不构造、不挂载、不订阅、不运行 timer、不写参数 | RUNTIME_IMPLEMENTED_G15_1 |
| `Studio2.PreviewPanel` | Preview Panel | G01 登记 | G15.2 | G15.2 | G16 | off | 旧 preview panel/overlay/ROI preview 生效，V2 owner 不挂载 | V2 Preview owner 唯一持有 preview 资源，legacy preview panel/overlay/ROI preview 不挂载、不订阅、不运行 timer/debounce、不请求、不读 artifact | RUNTIME_IMPLEMENTED_G15_2 |
| `Studio2.GlobalVariables` | Global Variables | G01 登记 | G15.3 | G15.3 | G16 | off | 旧变量面板写入后端，V2 owner 不挂载 | V2 变量面板唯一 owner，旧面板不 mounted/subscribed/timer/write | RUNTIME_IMPLEMENTED_G15_3 |
| `Studio2.Settings` | Settings | G01 登记 | G15.5 | G15.5 | G16 | off | 旧 SettingsView 生效，V2 owner 不挂载 | V2 Settings owner 唯一 mounted，旧 SettingsView 不 mounted/timer/write | RUNTIME_IMPLEMENTED_G15_5 |
| `Studio2.ProjectPage` | Project Page | G01 登记 | G15.8 | G15.8 | G16 | off | 旧 Project 页面唯一 owner，V2 owner 不挂载 | V2 Project Page owner 唯一 mounted，旧 ProjectView 不 mounted/write | RUNTIME_IMPLEMENTED_G15_8 |
| `Studio2.Inspection` | Inspection | G01 登记 | G15.6 | G15.6 | G16 | off | 旧 Inspection panel 和 timer 生效，V2 owner 不挂载 | V2 Inspection 唯一 owner，旧 panel 不 mounted/subscribed/timer/request | RUNTIME_IMPLEMENTED_G15_6 |
| `Studio2.ResultsReview` | Results/Review | G01 登记 | G15.7 | G15.7 | G16 | off | 旧 Results panel 和 refresh timer 生效，V2 owner 不挂载 | V2 ResultsReview 唯一 owner，旧 stream/refresh timer 停止 | RUNTIME_IMPLEMENTED_G15_7 |
| `Studio2.AiPanel` | AI Panel | G01 登记 | G15.4 | G15.4 | G16 | off | 旧 AI Panel 订阅 AgentRun，V2 owner 不挂载 | V2 AI Panel projection 唯一 owner，旧 panel 不 mounted/subscribed/timer/SSE/command | RUNTIME_IMPLEMENTED_G15_4 |

## 非阻断技术债

- G01 只登记 Feature Flag，不实现 runtime flag、配置文件或发布包切换。
- G02A 只建立 FrontendV2 构建底座和发布资产链路，不挂载 Vue root，不实现 runtime flag。
- G02A 收口修复固定 Vite base 为 `/v2/`，增加构建后 HTML/manifest 路径校验，消除 CI/MSBuild 重复 production build，并把 HostBridge guard 收敛为唯一 adapter 白名单规则。
- G02B 只实现宿主级 root 切换、`/v2` 静态映射、启动配置注入、legacy module facade 和无业务测试岛；不迁移 Workspace Shell、Flow、Project、Variables、Inspection、AI 或 Results capability。
- G03 只实现 Workspace Shell MVP 和 hosted FlowCanvas 载入链；真实 WebView2 人工启动未执行。
- G04A 已将 V2 Flow 写入口收敛为 `StudioFlowEditorPort`，并加入本地 `projectId`、`requestSequence`、`flowRevision`、`selectionRevision` stale 防护。G04A 不保存工程，不创建 Project API client，不改变 `ProjectSaveCoordinator`、Agent apply、Runtime Package 或 Station。
- G04B 已建立 `StudioProjectPersistencePort`，注册键为 `studio2.projectPersistencePort`；它只复用 legacy `httpClient`，通过既有 `PUT /api/projects/{id}` 单请求提交 metadata、Flow 与 GlobalVariables。`ExpectedPersistenceRevision`/`PersistenceRevision` 是后端持久化身份；`flowRevision` 不参与后端并发。旧 Project 页面和 `projectManager.saveProject()` 仍保留，Project capability 正式迁移仍等待 G15.8。
- 后续 Goal 若选择不同 V2 目录，必须先更新 ADR、台账和 `Studio2ArchitectureGuardTests` 的受控 scope。
