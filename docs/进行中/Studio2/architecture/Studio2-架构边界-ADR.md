# ADR：Studio 2.0 架构边界、状态权威与迁移规则

> 状态：Accepted
> 日期：2026-07-01
> Goal：G01
> Initial SHA：`789e9ec643390f5a79c68cfa6c4b401c1a679be3`
> 适用范围：Studio 2.0 Foundation 及后续 capability 迁移。

## 背景

G00 已冻结 Vision Agent 恢复治理阶段，并在 `docs/进行中/Studio2/状态权威与恢复边界.md` 中确认 AgentRun、EventStore、Projection Journal、Workspace Snapshot、Project/Flow/GlobalVariables 与 `ProjectSaveCoordinator` 的既有权威。本 ADR 将这些边界转成 Studio 2.0 后续开发必须遵守的单一架构决策，防止后续 Goal 在前端、保存链路、执行链路或图像渲染侧形成第二套业务权威。

本 ADR 不新增运行时行为，不挂载 `FrontendV2`，不修改生产代码。G02A 及后续 Goal 在引入 V2 文件时，必须先满足本 ADR 与配套迁移台账。

## 决策

1. `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/app.js` 当前仍是前端 composition root。Studio 2.0 在 G02A/G02B 前不得建立第二个实际挂载入口。
2. `EventBus` 与 `ServiceRegistry` 只能复用 `core/app/eventBus.js` 和 `core/app/serviceRegistry.js` 的现有单例模块，不得在 V2 中复制第二套总线或注册表。
3. `FlowCanvasAdapter` 是 V2 接入 `FlowCanvas` 的唯一业务 facade。V2 业务代码不得直接持有 `FlowCanvas` raw instance 作为写入口。
4. `ProjectService + ProjectSaveCoordinator` 是 Project、Flow、GlobalVariables 的正式保存权威。前端 `projectManager`、未来 V2 API 或 store 只能调用既有后端入口，不得建立第二套保存 endpoint、保存 client 或持久化 authority。
5. `flowRevision` 是 UI 本地 revision 线索，不能替代后端 `PersistenceRevision`。
6. Pinia、DOM、localStorage、未来 V2 store 和执行包文件只能是投影、编辑草稿或迁移台账，不得成为 Project、Flow、GlobalVariables 或 Agent 的业务权威。
7. AgentRun、EventStore、Projection Journal、Workspace Snapshot 不在 Studio 2.0 中重构；Studio 2.0 只消费既有投影和恢复边界。
8. Observation、Scene、Geometry 只能是可丢弃投影、编辑草稿或数学模型，不得成为 Project 或执行结果权威。
9. 旧实现和 V2 任一 capability 在同一时刻只能有一个 mounted owner、一个订阅集合和一个写入口。Feature Flag 打开后，旧实现必须不挂载、不订阅、不运行 timer、不持有资源；不得用 CSS 隐藏冒充切换。

## 状态域权威表

| 状态域 | 当前 authority | 当前 projection/UI | 唯一 write entry | lifecycle owner | 当前 legacy 实现路径 | 未来 V2 owner | 允许迁移的 Goal | capability cutover 条件 | 旧实现删除 Goal | rollback 边界 | 禁止形成的第二套权威 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Desktop Host | WinForms Desktop host + ASP.NET Core Desktop 进程 | WebView2 文档与本地 HTTP 页面 | `WebView2Host.InitializeAsync()`、`Program.GetWebPort()` 和现有 Desktop endpoints | Desktop host 进程 | `ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs` | G02B HostBridge 适配层 | G02B | flag off 时仍加载旧页面；flag on 时只挂载一个 V2 root，API base URL 仍由 Host 注入 | G16 | 关闭 V2 flag 回到旧页面与现有 Host 注入 | 第二 Desktop host、Electron、Node runtime、Station 依赖 Studio 前端 |
| 前端 composition root | 旧前端 `app.js` | 旧 DOM 视图、`viewManager`、按需加载的 legacy panel | `app.js` 内 bootstrap 与现有 `serviceRegistry` 注册 | Desktop WebView 页面生命周期 | `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/app.js` | G02B 的 V2 root adapter，后续 Shell 由 G03 管理 | G02B、G03 | V2 root 由单一 flag 控制；旧 root 不再 mounted、不订阅、不运行 timer | G16 | flag off 保持 `app.js` 为唯一 composition root | 第二 `EventBus`、第二 `ServiceRegistry`、CSS 隐藏式双挂载 |
| Flow | 后端 Project flow 持久化与现有 `FlowCanvas` 运行态 | FlowCanvas UI、本地 `flowRevision`、未来 V2 Flow Editor 投影 | V2 只能经 `FlowCanvasAdapter` 写入画布；正式保存仍经 `ProjectService` | Flow Editor capability owner | `core/canvas/flowCanvas.js`、`core/canvas/flowCanvasAdapter.js`、`features/project/projectManager.js` | G04A/G04B Flow Editor owner | G04A、G04B | V2 编辑经 adapter 产生结构变更；保存经 ProjectSaveCoordinator；`flowRevision` 不参与持久化冲突判定 | G15.1 到 G16 收口 | 关闭 Flow Editor flag 回到旧 Flow UI 和现有 adapter | V2 直接持有 raw `FlowCanvas` 作为业务写入口、第二 Flow command bus、前端持久化 authority |
| Project | `ProjectService` 与 Project repository | 旧 Project 页面、`projectManager` 当前工程投影 | `/api/projects/*` -> `ProjectService` -> `ProjectSaveCoordinator` | Application Service + Desktop endpoints | `ProjectService.cs`、`ApiEndpoints.cs`、`projectManager.js` | G13A Project 正式资产 owner；G15.8 UI 迁移 owner | G04B、G13A、G15.8 | V2 保存请求仍走既有 typed API 包装 `httpClient`；后端返回 `PersistenceRevision` 作为保存身份 | G16 | flag off 恢复旧 Project 页面和旧 `projectManager` 投影 | 第二 Project save endpoint、第二 Project save client、Pinia/localStorage Project authority |
| GlobalVariables | Project GlobalVariables schema 与 ProjectVariable session/state | 旧全局变量面板、未来 V2 Property/Variables 投影 | `ProjectService.UpdateGlobalVariablesAsync()` -> `ProjectSaveCoordinator` | Application Service + Project variable session | `features/global-variables/*`、`ProjectService.cs`、`ProjectSaveCoordinator.cs` | G15.3 Global Variables capability owner | G07B、G13A、G15.3 | V2 只提交 schema draft；正式保存统一进入 ProjectSaveCoordinator；变量状态随 Project revision 迁移 | G16 | flag off 恢复旧变量面板和既有保存入口 | 前端变量 store 作为正式 schema 或 runtime value authority |
| AgentRun / Vision Agent | `AgentRunEventStore` 与 `AgentRunEventStreamService` | terminal/session 投影、Workspace Snapshot | 既有 AgentRun endpoints 与 Build run service | AgentRun 服务和恢复协调器 | `AgentRunEventStore.cs`、`AgentRunEventStreamService.cs`、`AgentRunEndpoints.cs` | 无 V2 owner；Studio 2.0 仅消费投影 | 不迁移 | 不适用。后续 UI 只能读取现有投影和事件流 | 不删除 | 关闭相关 UI flag 不影响 AgentRun 事件权威 | 第二 AgentRun event log、第二 run 状态机、独立终态判断 |
| Observation | 运行结果与现有 inspection/result 投影 | Preview、Result、Observation UI 只读投影 | 运行服务和既有结果读取 endpoint；Observation envelope 不持久化 | Inspection/result service owner | `inspectionController.js`、results 相关面板、后续 preview artifact endpoint | G05A/G05B/G06 Observation owner | G05A、G05B、G06、G14A | V2 只读、metadata-only 或受控 artifact 读取；不把 envelope 写成正式结果 | G15.2、G15.7、G16 | flag off 恢复旧 Preview/Results UI | Observation envelope 作为执行结果权威、前端缓存作为结果库 |
| Scene | 现有图像结果与 ImageCanvas 渲染内核 | Visual Scene 只读投影 | Scene 只能消费后端结果、artifact 与几何草稿，不写 Project authority | Image/result projection owner | `ImageCanvas`、image viewer、result/preview 图像路径 | G08 Visual Scene owner | G08、G10C、G14B | Scene 能从 canonical ResultPath 和 artifact 安全恢复；只读投影可丢弃 | G15.2、G15.7、G16 | flag off 回到旧图像预览和结果面板 | 第二图像渲染内核、Scene 作为 Project/Result authority |
| Geometry | 几何数学内核与 Project 中正式保存的参数/资产 | Geometry editor draft、Scene overlay | Geometry draft 经对应 capability commit 到正式 Project/Tool 参数；正式保存仍经 ProjectSaveCoordinator | Geometry/Spatial capability owner | 旧 ROI/参数编辑路径、现有 ImageCanvas overlay | G09A/G09B/G09C Geometry owner | G09A、G09B、G09C、G10A | draft/commit 边界明确；数学内核有 round-trip/parity 测试；保存仍进入 Project authority | G16 | flag off 恢复旧参数编辑和旧 ROI 交互 | Geometry draft/localStorage 作为正式 Project 或执行结果 authority |

## V2 范围和架构守卫

G02A 后的自动守卫只检查明确的 Studio 2.0/V2 范围：

- `ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src`
- `ClearVision.Product/src/ClearVision.Product.Station`

`FrontendV2` 源码必须位于 `wwwroot` 外，构建产物由 Desktop `.csproj` 复制到输出和发布目录的 `wwwroot/v2/`。Vite public base 固定为 `/v2/`，构建后的 `index.html` 不得引用根路径 `/assets/`。G02A 完成后，守卫必须扫描真实 V2 源文件，不得继续以空 scope 通过。后续若选择其他 V2 源目录，必须先更新本 ADR 和 `Studio2ArchitectureGuardTests` 的受控 scope，再添加 V2 代码。

G02B 实现的宿主级启动开关为 `Studio:WorkspaceV2Enabled`，默认 `false`。该开关只决定 WebView2 初始页面：关闭时导航 `/index.html`，打开时导航 `/v2/index.html`。它不是 Project、Flow、Variables、Agent 或任一业务 capability 的 authority，也不得由 localStorage、查询参数或浏览器控制台覆盖。`/v2` 静态资产从输出目录 `AppContext.BaseDirectory/wwwroot/v2` 独立服务，legacy `/` 仍按 `DesktopWebRootResolver` 的 Debug 源码优先规则服务。

G03 将 `/v2/index.html` 下的测试岛替换为 Workspace Shell。Shell 只提供顶部 toolbar、左右 dock、中央 workspace、底部 status bar 和 Flow/Tool/Review 模式切换；Tool/Review 仍为占位，不加载 Property、Preview、AI、Results、Project、Inspection 或 GlobalVariables 业务模块。Flow 模式承载现有 `FlowCanvas`，创建链固定为 `FrontendV2` 动态加载 legacy `flowCanvasAdapter.js`，再由 `createHostedFlowCanvasAdapter(canvasId, options)` 在 adapter 内部创建唯一 `FlowCanvas`。V2 不直接 import `flowCanvas.js`、不调用 `new FlowCanvas(...)`、不使用 `adapter.raw`，也不注册 raw canvas 到 `ServiceRegistry`。

G04A 将 V2 Flow 本地编辑写入口收敛为 `StudioFlowEditorPort`。Workspace runtime 私有持有 hosted `FlowCanvasAdapter`，`ServiceRegistry` 只公开 `studio2.flowEditorPort`；V2 组件不得取得 raw `FlowCanvas`、`nodes` Map、可变 node 引用、`adapter.raw` 或 `window.flowCanvas`。Port 的 `flowRevision`、`selectionRevision`、`requestSequence` 仅用于前端本地 stale 防护，不等同后端 `PersistenceRevision`，也不参与正式 Project 保存冲突判定。G04A 不新增 Project 保存 endpoint/client，不修改 `ProjectSaveCoordinator`、`projectManager.saveProject`、Agent apply、Runtime Package 或 Station；G04B 才负责 canonical Project DTO 与持久化身份。

G04B 将 V2 工程保存收敛为 `StudioProjectPersistencePort`，由 Workspace runtime 注册为 `studio2.projectPersistencePort`。该 port 只复用 legacy `httpClient` 调用既有 `PUT /api/projects/{id}`，一次 payload 提交 metadata、Flow 与 GlobalVariables，并把后端返回的 canonical `ProjectDto.PersistenceRevision` 作为保存后的持久化身份。V2 请求新增 `expectedPersistenceRevision` 字段，对应后端 `UpdateProjectRequest.ExpectedPersistenceRevision`；并发冲突由 `ProjectSaveCoordinator` 的 `PSV011` 判定并映射为 HTTP 409。`flowRevision` 仍只表示前端本地草稿版本，不得作为后端持久化并发条件。旧 `projectManager.saveProject()` 及 `/flow`、`/global-variables` 兼容入口暂不迁移，Project 页面正式 owner 仍等待 G15.8；G04B 不新增保存 endpoint、不建立第二套 Project/Flow/Variables authority。

守卫必须防止：

- V2 定义第二套 `EventBus` 或 `ServiceRegistry`；
- V2 直接导入或实例化 raw `FlowCanvas` 作为业务写入口；
- V2 在 legacy `flowCanvasAdapter.js` 之外创建第二个 FlowCanvas facade、使用 `adapter.raw`，或为同一 Workspace 生命周期注册多个 FlowCanvas adapter；
- V2 通过 direct `fetch` 或第二 client 绕过既有 `httpClient` 建立 Project 保存入口；
- V2 用 localStorage authority 形态 key 保存 Project、Flow、Agent 或 GlobalVariables；
- Station 依赖 Vue、Vite、Pinia、FrontendV2、Node 或 Studio 前端目录。
- V2 在 `ClearVision.Product/src/ClearVision.Product.Desktop/FrontendV2/src/host/hostBridge.ts` 之外直接访问 `window.chrome.webview` 或 `chrome.webview`。

G02B 才允许实现唯一 HostBridge adapter。`HostBridge`、`AgentRun` 或 `agent-run` 名称本身不是违规；违规边界是第二套 AgentRun event store、第二套 run 状态机、第二终态判断，或把 AgentRun 写入 localStorage/indexedDB 作为前端持久化权威。

G02B 的 HostBridge adapter 只包装既有 `webMessageBridge`，typed API 只包装既有 `httpClient`。V2 源码不得 direct `fetch`，不得复制旧 `httpClient`、`EventBus` 或 `ServiceRegistry` 实现；只有 `FrontendV2/src/host/hostBridge.ts` 可作为未来 direct WebView2 access 的唯一白名单位置。

## 回滚边界

- G01 本身只新增文档和测试，不改变运行时，回滚只需还原本轮提交。
- 后续每个 capability 的 rollback 以迁移台账对应 Feature Flag 为边界。flag off 必须恢复旧实现为唯一 mounted owner。
- 一旦某 capability 完成 deletion Goal，rollback 不得依赖已删除旧实现；需要按该 Goal 的 release notes 和回归包执行恢复。

## 影响

- API：无变更。
- Project format：无变更。
- Runtime Package：无变更。
- Station：无运行时变更，并新增守卫禁止 Station 依赖 Studio 前端。
- AgentRun：无重构，无新增事件权威。
