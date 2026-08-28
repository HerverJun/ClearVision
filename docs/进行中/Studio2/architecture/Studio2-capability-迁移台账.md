# Studio 2.0 capability 迁移白名单与 Feature Flag 台账

> 状态：2026-07 历史迁移台账；2026-08-28 current disposition 见 G16
> 日期：2026-07-01
> Initial SHA：`789e9ec643390f5a79c68cfa6c4b401c1a679be3`
> 历史维护规则：本文件记录 Studio 2.0 capability 迁移白名单；2026-08-28 起不再以表内“默认 on/当前状态”作为运行时事实，新的迁移或挂载必须先更新 G16/current plan 和单一 flag authority。
> 2026-08-28 当前 release 决定：本表保留为历史迁移与 owner 证据，不再授权直接切换 `/v2`。当前唯一 production root 是 `wwwroot/index.html + app.js + capability owners`，FrontendV2 为 non-production；未来 Vue/StudioUI 迁移须另立完整 parity/migration epic。当前执行权威见 [G16](../goals/G16.md) 与 [统一补齐 TODO](../../当前计划/ClearVision-未尽事项统一补齐TODO-2026-08-28.md) U05。
> 术语纠偏：G15 `*CapabilityOwner` 模块位于当前 `wwwroot/src`/`app.js` root，不等于 `Desktop/FrontendV2`。Property、Preview、GlobalVariables、Project、ResultsReview owner 继续生产挂载；Settings、Inspection、AI 的不完整实验 owner 已按本次 disposition 删除，不能再按 2026-07 历史目标启用。
> Wave 0 implementation/evidence SHA：`1e2342c3909cb1f157d902aef1338e92f1ac44a3`。

## 总规则

- 同一 capability 任一时刻只能有一个 mounted owner、一个订阅集合、一个 timer 集合和一个写入口。
- Feature Flag 只控制挂载 owner，不改变业务权威。flag on 后旧实现必须不挂载、不订阅、不运行 timer、不持有资源。
- 不允许通过 CSS 隐藏旧实现冒充切换。
- G01 只登记 flag；G15 各 Goal 的历史实现继续保留。原“G16 最终切 `/v2` production root”目标已被 2026-08-28 当前 release 决定取代；G16 只收敛当前 production root、owner/flag disposition 与 release evidence。
- V2 typed API 必须包裹现有 `httpClient`，不得重做 auth、端口发现和网络错误策略。
- Project、Flow、GlobalVariables 的正式保存仍经 `ProjectService + ProjectSaveCoordinator`。
- Pinia、DOM、localStorage 只能作为投影或编辑草稿，不得作为正式业务权威。
- G02A 的 V2 源码目录为 `ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/`；发布资产目录为 `wwwroot/v2/`；不得把 Vue/TypeScript/package/node_modules 放入旧 `wwwroot/src`。
- FrontendV2 的历史构建入口是 Desktop `.csproj`；Wave 0 已删除其 production build/copy/publish 和 release Gate 路径，源码仅作为被项目文件隔离的 non-production 开发资产。未来只有另行批准完整迁移 epic 才能重建 production 路径。
- G02B 曾以 `Studio:WorkspaceV2Enabled` 切换宿主页面；该运行时选项、Host 注入和 startup resolver 分支现已删除，Desktop 对 `/v2` 固定返回 404。
- G03 的 `/v2/index.html` Workspace Shell MVP 只保留为历史/non-production 源码事实，不是本次 release 可挂载入口。

## 2026-07 capability 迁移历史台账

| capability | legacy owner | G15 capability owner（位于当前 app.js root） | read source | write entry | migration Goal | cutover Goal | deletion Goal | Feature Flag | flag off 行为 | flag on 行为 | rollback | 2026-07 回填状态 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Workspace Shell | `app.js` + `viewManager` | V2 Workspace Shell root | 现有 DOM view state、Project 投影、Host 注入 API base URL | 只允许切换 view、注册 Shell owned adapter 和维护 UI 布局状态；不写业务 authority | G03 | G03 | G16 | `Studio2.WorkspaceShell` | 旧 `app.js` 是唯一 mounted root | V2 Shell 是唯一 mounted root，旧 Shell 不订阅、不运行 timer；Flow 模式只经 hosted `FlowCanvasAdapter` 创建一个现有 `FlowCanvas` | flag off 恢复旧 Shell | DONE_G03 |
| Flow Editor | `FlowCanvas` + `FlowEditorInteraction` + `FlowCanvasAdapter` | V2 Flow Editor | `StudioFlowEditorPort` snapshot、Project flow 投影 | V2 只经 `studio2.flowEditorPort` 修改画布；正式保存经 `studio2.projectPersistencePort` -> `PUT /api/projects/{id}` -> ProjectSaveCoordinator | G04A | G04B | G16 | `Studio2.FlowEditor` | 旧 Flow Editor 挂载并持有 FlowCanvas | G04B 已建立 V2 单请求保存 facade；`flowRevision` 仍仅作本地 stale 防护，后端并发身份为 `PersistenceRevision` | flag off 恢复旧 Flow Editor 与 adapter | DONE_G04B_SAVE_PORT |
| Property Panel | legacy property panel/sidebar | V2 Property Panel capability owner | selected node projection、operator metadata、Project schema | `PropertyPanelCapabilityOwner -> PropertyPanelCapabilityAdapter -> FlowCanvasAdapter.patchNodeParameters()` | G15.1 | G15.1 | RELEASE-FOLLOWUP | `Studio2.PropertyPanel` | 旧 Property Panel 只作为 flag-off lazy library | V2 Property Panel owner 是默认 production owner，旧 panel 不 mounted、不订阅 selection、不运行 timer、不写参数 | flag off 恢复旧 property panel | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| Preview Panel | `NodePreviewCoordinator`、preview overlay、image viewer | V2 Preview Panel capability owner | preview endpoint、artifact URL、current flow/node projection | `PreviewPanelCapabilityOwner -> PreviewPanelCapabilityAdapter -> NodePreviewCoordinator`；不写正式结果；draft request 只走既有 preview API | G15.2 | G15.2 | RELEASE-FOLLOWUP | `Studio2.PreviewPanel` | 旧 preview panel/overlay/ROI preview 资源只作为 flag-off lazy library | V2 Preview Panel owner 是默认 production preview owner；legacy preview panel/overlay/ROI preview 不构造、不挂载、不订阅、不运行 debounce/timer、不发起 preview request、不读 artifact | flag off 恢复旧 preview panel | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| Global Variables | legacy `GlobalVariablePanel` | `GlobalVariablesCapabilityOwner` | Project `globalVariables` schema、Project variable session projection | `GlobalVariablesCapabilityOwner -> GlobalVariablesCapabilityAdapter -> projectManager.updateGlobalVariables()/saveGlobalVariables()`，正式保存仍经 ProjectService + ProjectSaveCoordinator | G15.3 | G15.3 | RELEASE-FOLLOWUP | `Studio2.GlobalVariables` | 旧变量面板只作为 flag-off dynamic import library | V2 变量面板是默认 production owner，旧面板不 mounted、不订阅 runtime state、不运行 polling timer、不写入 | flag off 恢复旧变量面板 | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| Settings | legacy `SettingsView` | `SettingsCapabilityOwner` | 当前 settings projection、Host/desktop settings endpoint | `SettingsCapabilityOwner -> SettingsCapabilityAdapter -> settingsApi.saveSettings()` | G15.5 | G15.5 | RELEASE-FOLLOWUP | `Studio2.Settings` | 旧 SettingsView 只作为 flag-off dynamic import library | V2 Settings owner 是默认 production owner，旧 SettingsView 不 mounted、不运行 lifecycle timeout/modals、不写 settings | flag off 恢复旧设置入口 | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| Project | `projectManager` + legacy `ProjectView` | `ProjectPageCapabilityOwner` | `/api/projects/*`、Project DTO、`PersistenceRevision` | `ProjectPageCapabilityOwner -> ProjectPageCapabilityAdapter -> projectManager`；正式写入仍经 ProjectService + ProjectSaveCoordinator | G15.8 | G15.8 | RELEASE-FOLLOWUP | `Studio2.ProjectPage` | 旧 Project 页面只作为 flag-off dynamic import library | V2 Project Page owner 是默认 production owner，旧 ProjectView 不 mounted、不绑定按钮、不写 project | flag off 恢复旧 Project 页面 | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| Inspection | `inspectionController` + legacy `InspectionPanel` | `InspectionCapabilityOwner` | existing inspection state、run endpoints、current project projection | `InspectionCapabilityOwner -> InspectionCapabilityAdapter -> inspectionController`；不写 AgentRun authority | G15.6 | G15.6 | RELEASE-FOLLOWUP | `Studio2.Inspection` | 旧 Inspection panel 只作为 flag-off dynamic import library | V2 Inspection owner 是默认 production owner，旧 panel 不订阅 completion/error、不运行 watchdog/timer、不发 request | flag off 恢复旧 Inspection panel | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| Results/Review | legacy `ResultPanel` + analytics refresh | `ResultsReviewCapabilityOwner` | formal inspection history endpoint、compare、previous-success、evidence export | `ResultsReviewCapabilityOwner -> ResultsReviewCapabilityAdapter -> app.js` existing history/detail/compare/previous-success/evidence loaders | G15.7 | G15.7 | RELEASE-FOLLOWUP | `Studio2.ResultsReview` | 旧 Results panel 只作为 flag-off dynamic import library | V2 ResultsReview owner 是默认 production owner；`serverPaged=true` prevents legacy analytics refresh timer | flag off 恢复旧 Results panel | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| AI Panel | legacy `AiPanel` + generation controller | `AiPanelCapabilityOwner` | AgentRun replay/events、session projection、existing AgentRun endpoints | `AiPanelCapabilityOwner -> AiPanelCapabilityAdapter -> existing AgentRun endpoints`；不建立第二 Agent authority | G15.4 | G15.4 | RELEASE-FOLLOWUP | `Studio2.AiPanel` | 旧 AI Panel 只作为 flag-off dynamic import library | V2 AI Panel owner 是默认 production AgentRun projection owner，旧 panel 不订阅 WebMessageBridge/canvas、不运行 timer/SSE、不发 command | flag off 恢复旧 AI Panel | PRODUCTION_DEFAULT_ON_G16_PARTIAL |

## 2026-07 Feature Flag 历史生命周期

| Feature Flag | owner | 创建 Goal | runtime 实现 Goal | cutover Goal | 删除 Goal | 2026-07 目标默认值 | flag off 必须保证 | flag on 必须保证 | 历史回填状态 |
|---|---|---|---|---|---|---|---|---|---|
| `Studio2.WorkspaceShell` | Workspace Shell | G01 登记 | G03 | G03 | G16 | off | 旧 Shell 唯一 mounted | V2 Shell 唯一 mounted，ServiceRegistry 只注册 Shell owned adapter，不注册 raw canvas | RUNTIME_IMPLEMENTED_G03 |
| `Studio2.FlowEditor` | Flow Editor | G01 登记 | G04A | G04B | G16 | off | 旧 Flow Editor 持有 FlowCanvas | V2 经 `StudioFlowEditorPort` 写入，Port 内部包装现有 `FlowCanvasAdapter`；正式保存经 `studio2.projectPersistencePort` 单次调用既有 Project PUT | SAVE_PORT_DONE_G04B |
| `Studio2.PropertyPanel` | Property Panel | G01 登记 | G15.1 | G15.1 | RELEASE-FOLLOWUP | on | 旧 Property Panel lazy library 仍可 rollback | V2 Property Panel owner 唯一订阅 selection，legacy `PropertyPanel` 不构造、不挂载、不订阅、不运行 timer、不写参数 | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.PreviewPanel` | Preview Panel | G01 登记 | G15.2 | G15.2 | RELEASE-FOLLOWUP | on | 旧 preview panel/overlay/ROI preview lazy library 仍可 rollback | V2 Preview owner 唯一持有 preview 资源，legacy preview panel/overlay/ROI preview 不挂载、不订阅、不运行 timer/debounce、不请求、不读 artifact | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.GlobalVariables` | Global Variables | G01 登记 | G15.3 | G15.3 | RELEASE-FOLLOWUP | on | 旧变量面板 dynamic import library 仍可 rollback | V2 变量面板唯一 owner，旧面板不 mounted/subscribed/timer/write | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.Settings` | Settings | G01 登记 | G15.5 | G15.5 | RELEASE-FOLLOWUP | on | 旧 SettingsView dynamic import library 仍可 rollback | V2 Settings owner 唯一 mounted，旧 SettingsView 不 mounted/timer/write | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.ProjectPage` | Project Page | G01 登记 | G15.8 | G15.8 | RELEASE-FOLLOWUP | on | 旧 Project 页面 dynamic import library 仍可 rollback | V2 Project Page owner 唯一 mounted，旧 ProjectView 不 mounted/write | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.Inspection` | Inspection | G01 登记 | G15.6 | G15.6 | RELEASE-FOLLOWUP | on | 旧 Inspection panel dynamic import library 仍可 rollback | V2 Inspection 唯一 owner，旧 panel 不 mounted/subscribed/timer/request | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.ResultsReview` | Results/Review | G01 登记 | G15.7 | G15.7 | RELEASE-FOLLOWUP | on | 旧 Results panel dynamic import library 仍可 rollback | V2 ResultsReview 唯一 owner，旧 stream/refresh timer 停止 | PRODUCTION_DEFAULT_ON_G16_PARTIAL |
| `Studio2.AiPanel` | AI Panel | G01 登记 | G15.4 | G15.4 | RELEASE-FOLLOWUP | on | 旧 AI Panel dynamic import library 仍可 rollback | V2 AI Panel projection 唯一 owner，旧 panel 不 mounted/subscribed/timer/SSE/command | PRODUCTION_DEFAULT_ON_G16_PARTIAL |

## 2026-08-28 当前 effective owner/flag 事实

| capability | `appsettings.json` | 额外客户端门禁 | 当前 effective 状态 | G16 disposition |
| --- | --- | --- | --- | --- |
| Property、Preview、GlobalVariables、Project、ResultsReview | 各自为 `true` | 无额外实验门禁；各 flag 只从 Host startup `featureFlags` 读取一次 | 各自唯一 capability owner | `RETAINED_PRODUCTION_OWNER_WAVE0` |
| Settings | 实验 flag key 已删除 | experimental global、adapter、实验 owner 均已删除 | legacy `SettingsView` 唯一 production owner | `RETIRED_EXPERIMENTAL_OWNER_WAVE0` |
| Inspection | 实验 flag key 已删除 | experimental global、adapter、实验 owner 均已删除 | legacy `InspectionPanel` 唯一 production owner | `RETIRED_EXPERIMENTAL_OWNER_WAVE0` |
| AI Panel | 实验 flag key 已删除 | experimental global、adapter、实验 owner 均已删除 | legacy `AiPanel` 唯一 production owner | `RETIRED_EXPERIMENTAL_OWNER_WAVE0` |

- Settings `destroy()` 经 `deactivate()` 清除 tracked timeout/modal；Inspection `dispose()` 取消 completion/error subscription；AI `dispose()` 清理 message subscriptions、EventSource/transports、timeout 与 animation frame。三者不是 CSS 隐藏的第二 owner。
- `appsettings`、`StudioOptions`、WebView2 startup payload 与 `app.js` 均不再包含 Settings/Inspection/AI 实验 flag authority；每个保留 capability 只有一个服务端注入且客户端 snapshot-once 的决定源。
- 上方历史表的 `on/PRODUCTION_DEFAULT_ON_G16_PARTIAL` 不再代表当前 effective 状态，不能用于关闭 G16。
- legacy 删除门禁仍保留：clean CI、Playwright、真实 WebView2、no-Node 目标机、DPI/分辨率矩阵通过前，不删除仍承担 current production 行为的实现。
- 当前代码证据：`Studio2ArchitectureGuardTests`、`WebView2HostTests`、`StudioStartupPageResolverTests`、`ProgramStaticAssetsTests` 与 `g15x-capability-owners.test.mjs`。Wave 0 Desktop 六类合并回归 `62/62`、UI owner/lifecycle focused `10/10`、UI 完整 unit `988/988`、七个 Playwright spec 非视觉回归 `68/68` PASS；本地 Release publish 确认 legacy root 资产存在且 `/v2`、FrontendV2、Node/package 资产为 0。5 个字体抗锯齿 visual baseline、真实 WebView2、clean clone、no-Node 目标机、DPI/分辨率矩阵和同 SHA CI 仍由 G16 阻断。

## 非阻断技术债

- G01 只登记 Feature Flag，不实现 runtime flag、配置文件或发布包切换。
- FrontendV2 的 Vite/HostBridge/Workspace Shell 源码继续作为 non-production 历史开发资产；Desktop project、publish 与主 CI 均不消费它。
- G02A/G02B 的 build、发布映射和宿主 root switch 已从当前 release 路径退役；若未来恢复，必须先批准新的完整 parity/migration epic。
- G03 只实现 Workspace Shell MVP 和 hosted FlowCanvas 载入链；真实 WebView2 人工启动未执行。
- G04A 已将 V2 Flow 写入口收敛为 `StudioFlowEditorPort`，并加入本地 `projectId`、`requestSequence`、`flowRevision`、`selectionRevision` stale 防护。G04A 不保存工程，不创建 Project API client，不改变 `ProjectSaveCoordinator`、Agent apply、Runtime Package 或 Station。
- G04B 已建立 `StudioProjectPersistencePort`，注册键为 `studio2.projectPersistencePort`；它只复用 legacy `httpClient`，通过既有 `PUT /api/projects/{id}` 单请求提交 metadata、Flow 与 GlobalVariables。`ExpectedPersistenceRevision`/`PersistenceRevision` 是后端持久化身份；`flowRevision` 不参与后端并发。旧 Project 页面和 `projectManager.saveProject()` 仍保留，Project capability 正式迁移仍等待 G15.8。
- 后续 Goal 若选择不同 V2 目录，必须先更新 ADR、台账和 `Studio2ArchitectureGuardTests` 的受控 scope。
