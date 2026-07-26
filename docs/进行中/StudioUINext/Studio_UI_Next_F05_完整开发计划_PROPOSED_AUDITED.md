# Studio UI Next F05 完整开发计划（基于实际代码的只读审计）

> 状态：`G1_DONE_WITH_PRE_EXISTING_BASELINE_DEBT`｜F05 定义：**以前端产品化为主，附带最小 Host 真值链与安全加固**；不重造后端业务权威。G0.1 已获批，G1 已在基线债务例外下完成并等待代码级复审；G2 仍未授权。

## 0. 审计基线与证据限制

| 项目 | 事实 |
|---|---|
| 审计日期 | 2026-07-25 |
| Next 工作树 | `C:\Users\HerverJun\Desktop\ClearVision-UI-Next`，分支 `studio-ui-next` |
| F05 G0 源码审计基线 | `ac5815f16b40ce2d7ed7834f48e07f5f9a698d0e` |
| G0.1 修订输入 SHA | `59e2801f85ba03dab51650ba18588f8390214595` |
| G0.1 获批计划 SHA | `ff9273b3c0e9b66e5b3aa3ec582826eb5298b280` |
| G1 实施起点 / Next `origin/studio-ui-next` | `ff9273b3c0e9b66e5b3aa3ec582826eb5298b280`（ahead 0 / behind 0） |
| G1 开始前 Next 未提交修改 | 仅 `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`（用户本地配置，本轮保持原状、未暂存、未提交） |
| Legacy 工作树 | `C:\Users\HerverJun\Desktop\ClearVision`，分支 `codex初稿` |
| Legacy local HEAD | `bea404394ac8cf403cca719c1990c426414a06c2` |
| Legacy `origin/codex初稿` | `bea404394ac8cf403cca719c1990c426414a06c2`（ahead 0 / behind 0） |
| Legacy 未提交修改 | 存在 12 个已修改文件与 3 个未跟踪文件（preview/observation 方向的在研工作）。本轮**只读**，未修改、未清理、未提交 |
| 远端 | 两个工作树同为 `https://github.com/HerverJun/ClearVision.git` |

审计范围：Next `StudioUI/` 全量源码与测试、Legacy `wwwroot/src/` 全量源码、Desktop 后端 `Endpoints/` / `Middleware/` / `Configuration/` / `WebView2Host` / `StudioStartupPageResolver`、`.github/workflows/ci.yml`、StudioUI 构建配置、UI.Tests e2e 目录结构、`docs/进行中/StudioUINext/` 现有报告。G0.1 只收敛计划；`59e2801f` 相对 G0 源码基线仅引入本 F05 计划，不把 Legacy 工作树写入或作为实现来源。

证据限制（本轮**不**取证，均以 F04-R 现有报告为权威）：

```text
F05_G0_BROWSER_EVIDENCE=NOT_COLLECTED_THIS_ROUND
F05_G0_WEBVIEW2_EVIDENCE=NOT_COLLECTED_THIS_ROUND
F05_G0_DPI_EVIDENCE=NOT_COLLECTED_THIS_ROUND
F05_G0_RELEASE_PUBLISH=NOT_COLLECTED_THIS_ROUND
F05_G0_BUILD_TEST_RUN=NOT_RUN
F05_G0_REAL_HARDWARE=NOT_PERFORMED
```

F04-R 的视觉、真实 WebView2 Debug/Release、Windows 125% DPI、Release publish、Remote CI 与 Final Gate 结论以 [F04-R 完成报告](./F04R_完成报告.md) 和 [G4B 证据索引](./F04R_G4B_WebView2_DPI_Release证据索引.md) 为准，本计划不重新取证、不改写其结论。

---

## 1. 当前代码事实（以代码为准，含文档偏差）

### 1.1 Next 已产品化的范围（`src/app/router.ts`）

已注册 route：`/setup`、`/login`、`/change-password`、`/forbidden`、`/not-found`、`/`（redirect → `/projects`）、`/overview`、`/projects`、`/projects/:id`、`/projects/:id/workspace`、`/operators`、`/operators/:operatorType`、`/stations`、`/stations/:stationId`、`/results`、`/diagnostics`、`/about`、`/labs/design`、`/labs/canvas`。

主导航（`src/app/navigation.ts`）只有两项：`/projects`、`/results`。即 Operators、Overview、Diagnostics、About、Stations 均已实现 route 但**不在主导航**；`/stations` 另受 `Studio2.StationsRead` flag 门控，`/labs/*` 要求 `hostKind === 'browser-test'`。

Route guard 事实：`allowedRoles` 只出现在 `project-workspace` 与 `diagnostics`（均为 `Admin|Engineer`）。`resolveSafeReturnRoute` 白名单**不含** `/stations`，即从 `/stations` 触发 401 后无法带 returnTo 回跳。

**未注册 route（代码中完全不存在）**：`/inspection`、`/projects/:id/inspection`、`/ai`、`/settings`、`/account`。这一点与 F04-R G1 矩阵一致——它们是 F05 的真实新增域，不是"页面存在但空"。F05 已冻结：`/inspection` 只作检测入口或工程选择，实际运行页唯一为 `/projects/:id/inspection`；不得依赖隐藏的“当前工程”内存状态。

### 1.2 Workspace 已有能力（`capabilities/project-workspace/`）

已实现 owner：`workspaceOwner`、`persistence/workspacePersistenceOwner`（唯一写入口，走 `PUT /api/projects/{id}` + 后端 `PersistenceRevision`）、`flow/flowCanvasOwner`（canonical `FlowCanvas` adapter）、`inspector/inspectorOwner`、`preview/previewOwner` + `previewWorkbenchOwner`、`roi/roiInteractionOwner`、`image/imageCanvasOwner`、`global-variables/workspaceGlobalVariablesOwner`、`final-decision/finalDecisionOwner`、`camera/cameraBindingEditorOwner`、`run/runCommandOwner`、`runtime-package/runtimePackageExportOwner`。

Formal Run 事实（`run/runCommandOwner.ts` 544 行）：状态机为 `idle | blocked | admitting | executing | succeeded | failed | cancelled | cancel-requested | unknown-outcome | disposed`，走 `inspection/admission` → `inspection/execute`，`stop`/`reconcile` 对应 `inspection/stop`、`inspection/reconcile`，并对 `projectId / clientSnapshotId / persistenceRevision / canonicalFlowHash / decisionConfigurationHash` 五元身份做双向校验，身份不符即进入 `unknown-outcome` 并锁定 Workspace。**这是单次持久化快照运行，不是连续检测**。

参数编辑器覆盖（`inspector/parameterEditorRegistry.ts`）：支持 `text / number / boolean / enum / slider`；`file` → `extension:file-picker`，消息仍是"文件选择器尚未接入当前工作区"；`camerabinding` → `extension:camera-binding` 且 `InspectorPanel.vue` 已真实接入 `CameraBindingEditor`；ROI 家族（rectangle/circle/polygon/annulus/arc/circlesearch/npoint/caliper）→ `extension:image-backed`，指向预览区"编辑 ROI"；其余类型 → `unsupported`。

**代码事实 vs 注册表消息偏差**：`camerabinding` 的 registry `message` 仍写"尚未接入当前工作区"，但 UI 已接入。这是一处需要在 F05 顺手校正的文案漂移，不影响功能。

`workspaceContracts.ts`（1403 行）实现了 save 兼容性闸门：`WorkspaceSaveCompatibility` 的 `status` 为 `blocked` 时 `canEncode=false`，`workspacePersistenceOwner` 直接进入只读并阻止保存。大小写重复字段、`blocked` 模式的未知字段都会进入 `blockedPaths`。这意味着**Legacy 写入过、而 Next 契约未建模的字段会让工程在 Next 中变为只读**。F05 必须保持这项只读保护，既不削弱、也不以 F05 完成取代验证；真实存量工程语料的建设与验证归入 F07，结论作为 F08 默认入口切换门禁。

### 1.3 Legacy 尚未迁移的真实能力（`wwwroot/src/`，共 119 文件 / 81,755 行）

Next `StudioUI/src` 为 34,304 行。规模差主要集中在以下未迁移域：

| Legacy 能力 | 主要文件（行数） | Next 状态 |
|---|---|---|
| AI 工程助手 | `features/ai/aiPanelAgentWorkspace.js` (6663)、`aiPanel.js` (2095)、`aiPanelPendingParameters.js` (1638)、`aiPanelLiveEvents.js` (1289)、`aiPanelAgentRun.js` (1287)、`aiPanelBuildPresentation.js` (1057)、`agentWorkspaceState.js` (868)、`aiPanelResourceBinding.js` (844)、`aiPanelGenerateRequest.js` (783)、`aiPanelSessionHistory.js` (779)、`aiPanelChat.js` (748)、`aiPanelApplyPreview.js` (711) 等 30 个文件 | **无任何 Next owner / route / contract** |
| 结果面板（批量 / 分析 / 缺陷） | `features/results/resultPanel.js` (2943)、`features/inspection/analysisCardsPanel.js` (797)、`shared/portDataTypeRenderer.mjs` | Next `/results` 只有列表 + 详情 + Evidence，无对比 / 统计 / 批量 / 实时 |
| 工作站监控 | `features/stations/stationMonitorView.js` (2155) | Next 只有只读列表 / 详情 / 保守轮询，且 flag 在真实 host 未注入 |
| 设置（7 个 tab） | `features/settings/tabs/cameraTab.js` (1692)、`aiTab.js` (1271)、`systemTabs.js` (996)、`plcTab.js` (643)、`tcpTab.js` (637)、`stationTab.js`、`runtimePreviewPilotConsole.js` (561) + `settingsApi/Validators/Normalizers` | **无 Next `/settings`** |
| 连续检测 / 实时检测 | `features/inspection/inspectionController.js` (1445)、`inspectionPanel.js` (1161)、`inspectionSseClient.mjs`、`inspectionCapabilityOwner.mjs` | **无 Next owner**；Next 只有单次 Formal Run |
| 标定工作台 | `core/calibration/planarScaleOffsetCalibWizard.js`、`features/flow-editor/calibrationDraftWorkbench.js` (849) | 无 Next owner |
| 模板 / 子图 / Lint / 线序辅助 | `templateSelector.js` (749)、`core/canvas/lintPanel.js`、`wireSequenceAssist.js` | 无 Next owner |
| 工程 import/export | `features/project/projectManager.js:484-543` | 无 Next owner |
| 算子库工作台 | `features/operator-library/operatorLibrary.js` (789) | Next 有 `/operators` 只读页，但不是 Workspace 内资源入口 |

### 1.4 后端合同已存在但 Next 未消费（无需新增后端）

审计确认以下 endpoint 已在 Desktop 后端注册，Next 未调用：

- **连续检测**：`POST /api/inspection/realtime/start`、`POST /api/inspection/realtime/stop`、`GET /api/inspection/realtime/{projectId}/state`、`GET /api/inspection/realtime/{projectId}/events`（SSE）、`GET /api/inspection/realtime/diagnostics`。
- **结果扩展**：`GET /api/inspection/history/{projectId}/compare`、`/statistics/{projectId}`、`/{resultId}/previous-success`、`/{resultId}/evidence/manifest`、`/evidence/export`（后两者 Next 已消费）。
- **分析域**：`GET /api/analysis/statistics|defect-distribution|trend|report/{projectId}`。
- **Station 操作**：`GET /api/stations/{id}/logs|commands`、`POST /api/stations/{id}/commands`、`PATCH /api/stations/{id}/identity`、`POST /api/stations/{id}/deploy-package`、`GET /api/station-packages`、`POST /api/station-packages/test`、`GET /api/station-packages/{id}/download`、`GET /api/stations/audit`、`GET /api/stations/events`。
- **AI**：`/api/ai/agent-plan`、`/agent-plan/readiness-preview`、`/agent-intent-router-runs`、`/agent-plan-runs`、`/agent-runs`、`/agent-runs/latest`、`/agent-runs/{runId}`、`/agent-runs/{runId}/events`（SSE）、`/stream-token`、`/cancel`、`/sessions/{id}/workspace-snapshot`。
- **设置**：`/api/settings`（GET/PUT）、`/theme`、`/reset`、`/disk-usage`、`/database/status|repair|backup|restore|cleanup`、`/api/ai/models` 全套、`/api/settings/runtime-preview-pilot/**`（约 25 个）、`/api/cameras/**`、`/api/trigger-input/**`、`/api/plc/**`、`/api/tcp/**`、`/api/station-communication/**`、`/api/users/**`。
- **算子 / 模板**：`/api/operators/library|types|{type}/metadata|{type}/preview|{type}/recommend-parameters`、`/api/templates`（GET/POST/PUT）、`/api/autotune/**`、`/api/calibration/npoint-draft/solve`、`/api/projects/{id}/calibration-assets/from-draft`。

**结论：F05 以前端产品化为主，附带最小 Host flag 真值链和 realtime endpoint 安全加固；它不是“纯前端”工作，也不是后端业务权威、执行状态机、保存协议或 Runtime/Station 链路的扩权工作。**

### 1.5 权限事实与真实缺口（`Endpoints/EndpointPermissionGuards.cs`）

策略集合：`RequireAuthenticated`、`RequireAdmin`、`RequireEngineerOrAdmin`、`RequireStationAdmin`（= Admin）、`CanEditProject`（= Engineer|Admin）、`CanOperateHardware`（= Engineer|Admin）、`CanReadSensitiveConfig`（= Admin）。

已加固：Project 写、Flow 写、GlobalVariables 写、`runtime-package/export`（`CanEditProject`）、`inspection/admission|stop|reconcile|execute` 与 `history/{projectId}`（`CanOperateHardware`）、Station 增强域（`RequireStationAdmin`）、Settings/PLC/TCP/Camera/Trigger（`RequireAdmin` 或 `CanOperateHardware`）。

**仍只有全局 `AuthMiddleware` 认证、无显式策略的关键 endpoint**（代码事实）：

| Endpoint | 现状 | F05 影响 |
|---|---|---|
| `POST /api/inspection/realtime/start` / `stop` | 仅 Authenticated | 连续检测会驱动真实相机与产线，权限低于单次 `execute`（`CanOperateHardware`）。**F05 检测域的前置阻断** |
| `GET /api/inspection/realtime/{id}/state` / `events` / `diagnostics` | 仅 Authenticated | 运行状态流无角色约束 |
| `GET /api/inspection/history/{id}/compare`、`/{resultId}`、`/statistics`、`/evidence/**` | 仅 Authenticated | 与列表端点（`CanOperateHardware`）不一致，是既有不对称 |
| `/api/ai/**` 全部 11 个 endpoint | 仅 Authenticated（`/agent-runs/{id}/events` 另走 streamToken 白名单） | AI 可写 Flow、可 Apply，权限应不低于 `CanEditProject`；属于 F06，不进入 F05 G1 |
| `/api/analysis/**` 4 个 | 仅 Authenticated | 只读，风险较低 |
| `/api/operators/{type}/preview`、`/api/templates` 写、`/api/autotune/**` | 仅 Authenticated | preview 会执行算子；template 写会改共享资产 |
| `/api/demo/**` | 仅 Authenticated | 会创建工程 |
| `/api/images/upload`、`/api/images/{id}` | 仅 Authenticated | — |

`/api/ai/agent-runs/{runId}/events` 在 `AuthMiddleware.TryAuthorizeAgentRunEventStream` 中通过 `streamToken` 查询参数绕过 token 认证（`consume: false`）。这是既有设计，留给 F06 AI 域复用而非新建通道；F05 不修改该路径。

**G1 权限范围冻结**：只对 `inspection/realtime/start`、`stop`、`state`、`events`、`diagnostics` 五个 surface 施加显式 `CanOperateHardware` 策略并做双向授权测试。G1 **不得**顺手修改 `/api/analysis/**`、operator preview、templates、autotune、demo、AI 或其他 endpoint 的权限。

### 1.6 启动、flag 与默认入口事实

`Configuration/StudioOptions.cs` 默认值：`StudioUiEnabled=false`、`WorkspaceCapabilityEnabled=false`，其余 capability flag 默认 `false`，`CircleSearchV2ToolEnabled` / `NPointCalibrationWorkbenchEnabled` 默认 `true`。

`StudioStartupPageResolver`：`studioUiEnabled=false` → `/index.html`（Legacy）；`true` → `/studio/index.html`，且资产不完整时进入**诊断页而非回退 Legacy**。

`StudioStartupProfileCatalog.Resolve`：`LEGACY_DEFAULT`（两 flag 均 false）、`NEXT_FULL_CANDIDATE`（两 flag 均 true）、`IsolatedTruthTable`（不一致）；`NEXT_PILOT` 与 `NEXT_FULL_CANDIDATE` 共享同一 flag 组合，仅靠 `CV_STUDIO_UI_PROFILE` 环境变量区分。

**关键 contract 漂移（代码事实）**：`WebView2Host.BuildStudioUiFeatureFlags` 注入的 flag 为 `Studio2.Workspace`、`Studio:NodePreviewInspectorEnabled`、`Studio2.PropertyPanel`、`Studio2.PreviewPanel`、`Studio2.GlobalVariables`、`Studio2.Settings`、`Studio2.ProjectPage`、`Studio2.Inspection`、`Studio2.ResultsReview`、`Studio2.AiPanel`、`Studio:CircleSearchV2ToolEnabled`、`Studio:NPointCalibrationWorkbenchEnabled`。**其中不含 `Studio2.StationsRead`**，而 Next router 用 `Studio2.StationsRead` 门控 `/stations`。因此 `/stations` 在真实 WebView2 host 中恒为 403，只在 Browser fixture（`f02-stations.spec.ts` 显式注入）中可达。F04-R G1 已记录此漂移，**代码在 `ac5815f1` 仍未修复**。

同时，Legacy 语义的 `Studio2.Inspection`、`Studio2.Settings`、`Studio2.AiPanel`、`Studio2.ResultsReview` 被注入给 StudioUI，但 Next 代码库中**无任何消费点**（grep 确认仅 `Studio2.StationsRead` 与 `Studio2.Workspace` 被 Next 引用）。G1 必须新增独立的 `StudioOptions.InspectionRunCapabilityEnabled`（默认 `false`）并将其一对一注入为 **`Studio2.InspectionRun`**。不得读取、复用、重命名、双写或重定义 Legacy `Studio2.Inspection` / `InspectionCapabilityEnabled` 的语义。

用户本地 `appsettings.json` 当前为 `StudioUiEnabled=true`、`WorkspaceCapabilityEnabled=false`（即 `IsolatedTruthTable`），仓库 HEAD 版本为 `StudioUiEnabled=false`。F04-R 完成报告记录的 4 个 Vitest 失败与 1 个 architecture 失败即来自该本地差异，这是**已知的本地配置断言**，不是产品缺陷。

### 1.7 构建、包体与 CI 事实

`vite.config.ts`：无 `manualChunks`、无 `build.rollupOptions`；`router.ts` 全部页面为静态 `import`，**没有任何 `defineAsyncComponent` 或动态 `import()` 路由分包**。虽然 Vite 已启用 `manifest: true`，但当前没有可重复的入口依赖汇总或预算门禁。F04-R 记录的主 chunk `963.63 kB` 与此一致；F05 新增检测与 Station 控制域前必须先建立按入口及同步依赖总和统计的 build manifest。

CI（`.github/workflows/ci.yml`）：`studio-ui` job 只跑 `npm run lint`、`npm run typecheck`、`npm run test:unit`，**不跑 `npm run build`**（build 由 `product` / `desktop` job 通过 dotnet build 间接触发）。`ui-browser` job 用 `CV_UI_SCENARIO: studio-ui-next` 跑 Playwright。`final-gate` 依赖 `guard-and-catalog`、`studio-ui`、`product`、`desktop` 等 job。CI 中**没有** bundle size 门禁、没有真实 WebView2、没有 DPI、没有 Release publish、没有真实硬件。

---

## 2. 主要能力差距（按用户价值与阻断性排序）

| # | 差距 | 阻断默认入口 | 阻断 Legacy 退役 | 后端就绪 |
|---|---|---|---|---|
| D1 | 连续 / 实时检测工作台（含 SSE 运行状态、停止、缺料超时与连续 NG 保护的呈现） | 是 | 是 | 是（权限需先加固） |
| D2 | AI 工程助手全域（澄清 → 计划 → Build → Apply/Undo → 快照恢复 → 会话历史 → 资源 readiness） | 是 | 是 | 是（权限需先加固） |
| D3 | 系统设置全域（常规 / 相机 / PLC / TCP / Station 通信 / 存储数据库 / AI 模型 / 用户安全） | 是 | 是 | 是 |
| D4 | Station 控制域（日志、命令、身份、包下发、审计、包管理） | 是（仅"完整替代"维度） | 是 | 是 |
| D5 | `Studio2.StationsRead` host 注入缺失，`/stations` 生产不可达 | 是 | 是 | N/A（Host 修复） |
| D6 | editable Project import / export（Legacy 有 JSON 导出与导入建工程） | 是 | 是 | 部分（导出可用 `GET /api/projects/{id}`；导入需明确合同） |
| D7 | Results 对比 / 统计 / 分析 / 批量 / 实时订阅 | 否（可接受只读子集） | 是 | 是 |
| D8 | Workspace 缺口：file-picker 参数槽、模板、子图、Lint 完整可达、线序辅助、标定工作台 | 是（file-picker 与 Lint） | 是（全部） | 是 |
| D9 | `WorkspaceSaveCompatibility` 对真实存量工程的覆盖率未验证（未建模字段 → 工程只读） | 是（F08 门禁；语料验证归 F07） | 是 | N/A（前端契约） |
| D10 | 单 chunk 963 kB，无 lazy loading；F05 新域会显著恶化 | 软阻断（性能门禁） | 否 | N/A |
| D11 | 真实相机 / PLC / Station / 独立无 Node 目标机证据全部 `NOT_PERFORMED` | 是 | 是 | N/A |
| D12 | 术语与 flag 漂移：registry 中 camera-binding 文案、Legacy flag 无消费点、`resolveSafeReturnRoute` 不含 `/stations` | 否 | 否 | N/A |

---

## 3. F05 范围裁决

不把全部剩余能力塞进一个 F05。F05 只做**检测运行与工作站**这一条依赖链最短、用户价值最高、且能独立形成产线闭环的波次；以前端产品化为主，附带为该链路所需的最小 Host 真值链和 endpoint 安全加固。AI、设置、交付与退役各自独立成后续 F 级波次。

### 3.1 In Scope（F05）

1. **S1 最小真值链与安全加固**：`Studio2.StationsRead` 进入 `WebView2Host` 启动 flag 真值链；新增 `Studio2.InspectionRun` ← `InspectionRunCapabilityEnabled` 的独立真值链；只为 `inspection/realtime/start|stop|state|events|diagnostics` 加显式权限策略；`resolveSafeReturnRoute` 覆盖 `/stations`；registry 文案校正。
2. **S2 检测运行域**：`/inspection` 只作检测入口或工程选择，实际运行页固定为 `/projects/:id/inspection`；单一 `inspectionRunOwner` 消费既有 `realtime/start|stop|state|events|diagnostics`，含 SSE 生命周期、运行中断/重连、离开守卫和后端互斥冲突恢复，绝不使用隐藏的“当前工程”内存状态。
3. **S3 运行状态与结果衔接**：运行进度、最近结果、决策计数投影；从检测域跳转 `/results` 的既有详情与 Evidence 链路复用。
4. **S4 工作站只读产品化**：把已实现的 `/stations` 只读域真正在生产 profile 可达并进入主导航，含 `stations/summary`、`statistics`、`results`、`health`。
5. **S5 Station 控制域（Admin）**：日志、命令下发、身份修订、审计、运行包下发与包管理，全部走 `RequireStationAdmin` 既有 endpoint。
6. **S6 路由级 lazy loading 与包体门禁**：为新增大域引入动态 import 分包，并把 bundle 预算写成可执行门禁。

**视觉边界**：F05 只遵循现有 Quiet Precision 设计系统完成必要页面与状态；不得借此进行全局视觉重设计。全局视觉定稿在 F07 完成后、F08 默认入口切换前单独进行。

### 3.2 Out of Scope（F05 明确不做）

- AI 工程助手任何 route、owner、contract 或 UI（含 AgentRun 事件消费）。
- `/settings` 任何 tab（相机、PLC、TCP、Station 通信、存储、数据库、AI 模型、用户安全）。
- editable Project import / export 与版本迁移。
- 模板、子图、Lint 完整可达性、线序辅助、标定工作台。
- Results 的对比 / 统计 / 分析 / 批量导出。
- `/api/analysis/**`、operator preview、templates、autotune、demo、AI 的权限改动。
- 默认入口切换、Legacy 退役、`StudioUiEnabled` 正式默认值变更。
- 任何后端业务权威、执行状态机、保存协议、运行包格式变更。

### 3.3 Deferred（已识别、排入后续波次）

| 项 | 目标波次 |
|---|---|
| AI 工程助手全域 + `/api/ai/**` 权限加固 | F06 |
| `/settings` 全域 + 用户安全 | F07 |
| editable Project import/export、版本兼容与迁移 | F07 |
| Workspace 剩余缺口（file-picker、模板、子图、Lint、线序、标定） | F07 |
| `WorkspaceSaveCompatibility` 真实存量工程语料建设与验证 | F07；结果作为 F08 默认入口切换门禁 |
| Results 分析 / 对比 / 批量 | F07（或 F06 附带） |
| 默认入口切换门禁 | F08 独立门禁 |
| Legacy 退役门禁 | F09 独立门禁（**不得与 F08 合并**） |

---

## 4. 推荐阶段划分、Goal 与执行顺序

F05 分 6 个 Goal，严格串行；每个 Goal 未过门禁不进入下一个。

| Goal | 名称 | 依赖 | 停止边界 |
|---|---|---|---|
| **G0** | 只读审计与计划（本文档） | — | 不实现 |
| **G1** | 合同与前置加固冻结 | G0.1 复审批准 | 只改最小 Host flag 真值链、`inspection/realtime/start|stop|state|events|diagnostics` 权限、returnTo 白名单、文案；不新建 route |
| **G2** | 检测域合同冻结（canonical route / 输入、Role、Owner、SSE 生命周期、后端互斥权威） | G1 | 只出合同文档 + ADR，不写产品代码 |
| **G3** | 检测运行域实现（`/inspection` 入口 + `/projects/:id/inspection` 运行页 + `inspectionRunOwner`） | G2 | 不含 Station 控制；不改后端业务状态机 |
| **G4** | 工作站只读产品化 + Station 控制域（Admin） | G3 | 不含 Legacy 退役 |
| **G5** | Lazy loading、包体门禁与性能收口 | G3、G4 | 不改业务语义 |
| **G6** | 隔离 E2E、真实 WebView2 / DPI / Release、Remote CI、Final Evidence | G1–G5 | 不切默认入口 |

执行顺序理由：G1 必须最先，因为 `Studio2.StationsRead` 不修则 G4 无法在真实 host 验证，`realtime` 五个 surface 的权限不加固则 G3 会把无角色约束的产线控制或运行态暴露给任何登录用户。G2 先冻结“已保存 canonical Project snapshot”输入与后端互斥权威，G3 才能只做投影与调用。G5 放在 G3/G4 之后，因为只有新增域落地后包体预算才有真实基线。

---

## 5. 关键 Route / Role / Profile / Owner / Authority 边界

| Route | Role guard | Profile / Flag | 唯一 Owner | 写入边界 |
|---|---|---|---|---|
| `/projects/:id/workspace`（现有，不动） | `Admin\|Engineer` | `Studio2.Workspace` | `workspaceOwner` 树 | `workspacePersistenceOwner` → `PUT /api/projects/{id}` → `ProjectSaveCoordinator` |
| `/inspection`（G3 新增，入口 / 工程选择） | `Admin\|Engineer` | `Studio2.InspectionRun` ← 独立 `InspectionRunCapabilityEnabled`（G1） | 入口/选择 owner；不得挂载运行 owner | 只导航至带显式 `:id` 的运行页；不得保存或推断“当前工程” |
| `/projects/:id/inspection`（G3 新增，唯一运行页） | `Admin\|Engineer`；硬件动作对齐 `CanOperateHardware` | `Studio2.InspectionRun`；**不得复用或重定义 Legacy `Studio2.Inspection`** | 新增 `inspectionRunOwner`（唯一 SSE + 唯一 start/stop 写口） | 只调 `inspection/realtime/start\|stop\|state\|events\|diagnostics`；仅接受已保存、revision/hash 校验通过的 canonical Project snapshot；**不写 Project、不发送 Draft `FlowData`、不复制 Runtime 状态机** |
| `/stations`、`/stations/:id`（G4 产品化） | 读：Authenticated | `Studio2.StationsRead`（G1 修复 host 注入） | 现有 `stationLifecycleOwner`，不新建 | 只读 |
| `/stations/:id` 控制区（G4 新增） | `Admin`（`RequireStationAdmin`） | 与只读同 flag | 仅 Admin mounted 的同一 `stationLifecycleOwner` command 子 owner | 只调既有 Station endpoint；Engineer 只保留只读监控，不渲染或挂载“可见但禁用”的控制区；包下发不新建 package 格式 |
| `/results`（现有，不动） | Authenticated | 无 | `resultsReadRuntime` + `resultEvidenceOwner` | 只读 |

**Authority 红线（不可越过）**：Project/Flow/GlobalVariables/正式 assets → 既有 Application Service + `ProjectSaveCoordinator`；连续检测执行、结果持久化、Runtime Package、Station 现场链路 → 既有后端；Pinia/Vue/DOM/localStorage 只保存 UI 投影、草稿与可丢弃缓存。

**连续检测执行输入（G2/G3 必须冻结并测试）**：Studio UI Next 只运行已保存、经后端 `PersistenceRevision` 与 canonical flow / decision hash 校验通过的 canonical Project snapshot。运行页有未保存修改时必须禁止启动并提示先保存；不得向 realtime start 发送前端 Draft `FlowData`，不得以 Pinia、DOM 或缓存的 Flow 代替后端快照。本轮不改既有后端业务状态机；G2 写入合同，G3 以调用、拒绝与回归测试落实该合同。

**Formal Run 与连续检测互斥（G2 必须冻结）**：`IInspectionRuntimeCoordinator` 是 Project 级唯一运行互斥权威，负责运行会话与 mutation lease；它的冲突结果是唯一最终判定。`inspectionRunOwner` 与 `runCommandOwner` 只投影状态、预禁用和处理后端冲突恢复；`workspacePersistenceOwner`、任何 Pinia 状态或前端缓存都不是运行锁。不得新增第二运行锁，也不得把 `setRunning/clearRunning` 扩展为连续检测锁。G2 需明确：连续检测运行中 Formal Run 应由后端拒绝，反之亦然；离开路由时必须先 stop + reconcile 或明确拒绝离开。

---

## 6. 各阶段用户旅程、代码范围、测试与退出门禁

### G1 合同与前置加固冻结

- **旅程**：Admin 在真实 WebView2 中打开工作站监控并成功看到站点列表；Engineer 触发连续检测被正确按角色放行/拒绝。
- **代码范围**：`WebView2Host.cs` 的 `BuildStudioUiFeatureFlags`（增加 `Studio2.StationsRead` 与 `Studio2.InspectionRun`）、`Configuration/StudioOptions.cs`（为两者新增独立 option，其中检测固定为 `InspectionRunCapabilityEnabled`）、`Endpoints/ApiEndpoints.cs`（仅 realtime `start|stop` 加 `CanOperateHardware`）、`Endpoints/InspectionEventEndpoints.cs`（仅 `state|events|diagnostics` 加同一策略）、`StudioUI/src/app/router.ts`（`resolveSafeReturnRoute` 含 `/stations`）、`inspector/parameterEditorRegistry.ts`（文案）。**不得**修改 analysis、operator preview、templates、autotune、demo、AI 或其他 endpoint 的权限。
- **测试**：上述五个 realtime surface 的 Desktop endpoint 权限用例（403/200 双向且无旁路扩大）、`WebView2Host` 启动 flag/option 一对一真值表单测（含 `Studio2.InspectionRun` 与 Legacy `Studio2.Inspection` 独立）、`routeGuard.spec.ts` 与 `router.spec.ts` 扩充。
- **退出门禁**：`dotnet format --verify-no-changes`、`build -warnaserror`、Desktop endpoints 全绿、StudioUI lint/typecheck/test:unit 全绿；真实 WebView2 中 `/stations` 可达（G1 只需 Debug 一次冒烟，Release 留 G6）。

### G2 检测域合同冻结

- **交付**：`F05_G2_检测域合同.md` + ADR：冻结 `Studio2.InspectionRun` / `InspectionRunCapabilityEnabled`、`/inspection` 入口与 `/projects/:id/inspection` 唯一运行页、显式 `projectId` 路由参数、SSE 重连与退避、离开守卫、缺料超时/连续 NG 的呈现语义、错误码到用户文案的映射。
- **连续执行合同**：只接受已保存且由后端 `PersistenceRevision`、canonical flow hash、decision configuration hash 校验通过的 canonical Project snapshot；页面有未保存修改时禁止启动并提示先保存；Studio UI Next 请求体禁止含 Draft `FlowData`。`IInspectionRuntimeCoordinator` 是 Project 级唯一运行互斥权威，前端只做投影、预禁用和冲突恢复；不新增第二运行锁，也不把 `workspacePersistenceOwner` / Pinia 写成运行锁。
- **门禁**：合同必须列出“未保存阻止、旧 revision/hash 拒绝、后端运行冲突恢复、请求无 `FlowData`”的 G3 测试条目；本轮不改既有后端业务状态机，且不含任何产品代码变更。

### G3 检测运行域实现

- **旅程**：从 `/inspection` 选择工程 → 进入 `/projects/:id/inspection` → 仅在工程已保存且 identity 校验通过时选择相机并启动连续检测 → 观察 SSE 运行状态与最近结果 → 停止 → 跳转检测结果查看历史与 Evidence。任何未保存修改均阻止启动并提示“请先保存工程”。
- **代码范围**：新增 `capabilities/inspection-run/`（contracts、入口/运行页 owner、SSE adapter）；`app/router.ts` 注册两条冻结 route；`app/navigation.ts` 增加检测入口；`app/leave/productLeaveGuardOwner.ts` 接入新 owner 的 `prepareForLeave`。`inspectionRunOwner` 只持有命令窄接口与可丢弃投影，不能持有 `EventSource` / `AbortController` 的外泄引用。**禁止**以 `workspacePersistenceOwner.setRunning/clearRunning` 或 Pinia 实现运行互斥、禁止发送 Draft `FlowData`、禁止依赖隐藏当前工程、禁止改既有后端业务状态机。
- **测试**：owner 单测（启动/停止/SSE 断线重连/终态/dispose 零资源 ledger）、离开守卫单测、未保存工程启动被阻止、请求不含 `FlowData`、revision/hash 不符被拒绝、后端运行冲突的投影和恢复、Playwright 检测旅程（fixture SSE）。如现有 endpoint 合同无法表达 revision/hash 身份，G3 必须停止并上报合同缺口，不得退回 Draft 输入。
- **门禁**：single-owner 与 lifecycle ledger 归零；20-cycle 挂载/卸载无泄漏；Browser E2E 通过；现有 Quiet Precision 组件/令牌复用且无全局视觉重设计。

### G4 工作站只读产品化 + Station 控制

- **旅程**：Admin 打开工作站监控 → 查看站点健康/结果/统计 → 进入详情查看日志与审计 → 下发命令 → 下发运行包 → 校验包列表。
- **代码范围**：`capabilities/stations-read/`（扩展，不新建第二 owner）、`app/navigation.ts`、`app/router.ts`。
- **测试**：Station 契约解码单测、role 收敛单测（Engineer 看不到且不挂载控制区）、Playwright Station 旅程。
- **门禁**：命令与包下发全部走既有 endpoint；无第二 package 模型；Admin/Engineer 双角色 E2E 通过；Engineer 只读监控不是“可见但禁用”的控制区。

### G5 Lazy loading 与包体门禁

- **代码范围**：`app/router.ts`（动态 import）、`vite.config.ts`（必要时 `manualChunks`）、新增 bundle manifest / 预算脚本 + CI 接入。
- **门禁**：P6 先从可重复的 production build 生成 manifest，按每个入口及其**同步依赖闭包**汇总字节数；在该口径下的硬门禁至少不劣于 F04-R `963.63 kB` 基线。实测 manifest 产出后，把入口级更严格阈值冻结并接入 CI；typecheck/lint/test:unit/build 全绿；真实 WebView2 首屏无退化。

### G6 隔离 E2E 与 Final Evidence

- **门禁**：Browser/Playwright、真实 WebView2 Debug、真实 WebView2 Release、Windows 125% DPI、Release publish + 发布目录旅程、Remote CI workflow_dispatch、Final Gate 全部实际执行并分别记录。

---

## 7. 证据要求（分类不可互相替代）

| 证据类型 | F05 要求 | 说明 |
|---|---|---|
| Browser / Playwright fixture | 必需（G3、G4） | 不等同真实端点 |
| 真实 WebView2 Debug | 必需（G1 冒烟、G6 完整） | 1920×1080 + 1366×768 |
| 真实 WebView2 Release | 必需（G6） | — |
| Windows 125% 真实 DPI | 必需（G6） | native DPI 120 / DPR 1.25，结束后恢复 100% |
| Release publish + 发布目录旅程 | 必需（G6） | self-contained win-x64 |
| Remote CI（workflow_dispatch + Final Gate） | 必需（G6） | 普通 push 不等于完整 CI |
| Bundle 预算门禁 | 必需（G5 建立） | CI 当前无此门禁 |
| **真实相机** | **F05 不可能完成 → 必须写 `NOT_PERFORMED`** | 连续检测的真实闭环需要现场硬件 |
| **真实 PLC** | 同上 | — |
| **真实 Station** | 同上 | 命令下发与包下发的现场验证 |
| **独立无 Node 目标机** | 同上（F03 遗留，产品负责人已接受延期） | — |

**必须写明**：F05 G3/G4 完成后，连续检测与 Station 控制只有 fixture 与本机替身证据。这不构成产线签收，且是默认入口切换的独立阻断项。

---

## 8. 默认入口切换条件（`DEFAULT_ENTRY_CHANGE` 门禁）

`F04-R COMPLETE` 与 F05 完成**都不自动批准**默认入口切换。切换 `Studio:StudioUiEnabled` 正式默认值需同时满足：

1. 检测（连续运行）、AI 工程助手、系统设置三个域在 Next 中真实可用，或产品负责人对每个缺失域出具书面例外。
2. Station 只读与控制在真实 host 可达（`Studio2.StationsRead` 已修复且验证）。
3. editable Project import/export 与版本兼容有明确结论（迁移或显式放弃）。
4. F07 已完成 `WorkspaceSaveCompatibility` 对真实存量工程语料的覆盖率验证，且 F08 确认无“打开即只读”回归；F05 不削弱现有只读保护，也不是该语料的完成门禁。
5. 真实相机 + 真实 PLC + 真实 Station 至少完成一轮现场验证，或产品负责人书面接受风险。
6. 真实 WebView2 Debug/Release + 125% DPI + Release publish + Remote CI + Final Gate 全绿。
7. 包体与首屏性能达成 G5 建立的预算。
8. 回滚方案可执行：`StudioUiEnabled=false` 即刻回到 `/index.html`，且该路径经真实验证。
9. 产品负责人对切换本身单独批准（与 F05 完成批准分离）。

```text
DEFAULT_ENTRY_CHANGE=BLOCKED
```

## 9. Legacy 退役条件（`LEGACY_RETIREMENT` 独立门禁）

**必须晚于默认入口切换，且是完全独立的第二道门禁。** 条件：

1. 默认入口已切换并在生产环境稳定运行满一个产品负责人指定的观察期。
2. Legacy `wwwroot/src/` 的全部 119 个文件、81,755 行能力已逐项确认"已迁移"或"经批准废弃"，包含标定向导、模板、子图、Lint、线序辅助、runtime preview pilot console、分析卡片、缺陷渲染等长尾。
3. 观察期内无回退到 Legacy 的记录。
4. 真实相机 / PLC / Station 现场验证在 Next 上完成，不再依赖 Legacy 兜底。
5. `StudioStartupPageResolver` 的 Legacy 分支、`UseDesktopStaticAssets` 的 legacy provider、`BuildStartupInjectionScript` 的 Legacy flag 组、`UI.md` 契约、Legacy Playwright 套件的退役方案有独立 ADR。
6. 回滚方案：退役前必须保留可从 Git 完整恢复 Legacy 的 tag，并验证恢复流程。
7. 产品负责人对退役单独批准。

```text
LEGACY_RETIREMENT=BLOCKED
```

## 10. 回滚方案

| 层级 | 回滚动作 | 验证 |
|---|---|---|
| 入口 | `Studio:StudioUiEnabled=false` → 重启 → `/index.html` | 真实 WebView2 冒烟 |
| 新域 | 关闭 G2 定义的检测域 flag → owner 必须真正 unmount 并停止 SSE/timer/请求 | lifecycle ledger 归零 |
| Station | 关闭 `Studio2.StationsRead` → route 403 且轮询停止 | 单测 + 宿主验证 |
| 代码 | 每个 Goal 独立提交，可按 Goal 粒度 revert | — |
| 后端权限 | G1 权限加固为收紧方向，回滚等于放宽，需产品负责人确认后单独处理 | — |

---

## 11. 风险、共享文件冲突与不应重复建设

### 高风险项

| 风险 | 影响 | 缓解 |
|---|---|---|
| 连续检测与 Formal Run 双运行入口 | 同一 Project 两条执行路径，可能产生冲突运行与不一致结果 | `IInspectionRuntimeCoordinator` 保持 Project 级唯一运行互斥权威；G2 冻结前端预禁用/冲突恢复，绝不把 `workspacePersistenceOwner` 作为运行锁或新建第二锁 |
| SSE 生命周期泄漏 | `EventSource` 未 dispose 导致后台持续订阅 | adapter 持有、owner dispose、ledger 断言、20-cycle 测试 |
| `realtime` 权限当前无角色约束 | 任何登录用户可启动产线 | G1 前置加固，先于 G3 |
| `WorkspaceSaveCompatibility` 覆盖不足 | 存量工程在 Next 中变只读 | 保持现有只读保护；F07 建设并验证真实语料，F08 把结论作为默认入口切换门禁 |
| 包体持续膨胀 | 首屏与 WebView2 性能退化 | G5 lazy loading + CI 预算门禁 |
| 把 fixture 证据当现场签收 | 违反项目真实性要求 | 证据分类表强制分别记录，硬件项写 `NOT_PERFORMED` |

### 共享文件（只能由主协调代理修改，禁止并行 owner）

`StudioUI/package.json`、`package-lock.json`、`vite.config.ts`、`src/app/router.ts`、`src/app/navigation.ts`、`src/app/layouts/ProductLayout.vue`、`src/app/createStudioApp.ts`、`src/app/studioPlatform.ts`、`src/app/leave/**`、`src/platform/api/**`、`src/platform/startup/**`、`src/design-system/**` tokens、`ClearVision.Product.Desktop.csproj`、`WebView2Host.cs`、`Configuration/StudioOptions.cs`、`Program.cs`、`.github/workflows/ci.yml`、根 `AGENTS.md`、本目录 ADR 与计划文档。

### 明确不应重复建设

- 第二 EventBus / ServiceRegistry / HTTP 栈 / Canvas 内核 / HostBridge。
- 第二 Project save endpoint、save client 或前端私有持久化链。
- 第二 Station owner、第二 package 模型、第二 Runtime Package 导出路径。
- 第二运行锁或第二 AgentRun 事件存储。
- 复制 Legacy `inspectionController.js` / `stationMonitorView.js` 的实现结构到 Vue —— 只复用其**语义**，不复用其架构。
- Vue 组件直接持有 `EventSource` / `AbortController` / `FlowCanvas` / `ImageCanvas` / WebView2 bridge。

---

## 12. 推荐后续 Prompt 数量与划分

建议 **7 个 Prompt**（G0 已由本轮完成）：

| Prompt | 覆盖 | 交付 |
|---|---|---|
| P1 | G1 前置加固 | Host flag 真值链、后端权限策略、returnTo、文案；本地回归 + Debug WebView2 冒烟 |
| P2 | G2 检测域合同冻结 | 合同文档 + ADR，无产品代码 |
| P3 | G3 检测运行域实现（前半：owner + canonical 输入合同 + 单测） | `inspection-run` owner；无 Draft `FlowData`、未保存阻止、revision/hash 与后端冲突测试 |
| P4 | G3 检测运行域实现（后半：入口、运行页、导航、离开守卫 + E2E） | `/inspection` 工程选择可用，实际运行页为 `/projects/:id/inspection` |
| P5 | G4 工作站只读产品化 + Station 控制 | `/stations` 生产可达 + Admin 控制区 |
| P6 | G5 Lazy loading 与包体门禁 | 先生成按入口及同步依赖总和统计的可重复 build manifest，再冻结严格 CI 预算（底线不劣于 `963.63 kB`） |
| P7 | G6 隔离 E2E、真实 WebView2/DPI/Release、Remote CI、Final Evidence、F05 完成报告 | F05 收口 |

P3/P4 拆分是因为检测 owner 的 SSE 生命周期与运行互斥是本波次最高风险点，需要独立门禁；不建议合并。

---

## 13. 产品负责人复审与剩余裁决

**本次已冻结，不得在 G1 重新打开**：F05 为“前端产品化为主 + 最小 Host 真值链与安全加固”；G1 权限只覆盖 `inspection/realtime/start|stop|state|events|diagnostics`；检测 flag 固定为 `Studio2.InspectionRun` 与独立 `InspectionRunCapabilityEnabled`；`/inspection` 只入口/工程选择、`/projects/:id/inspection` 才运行；连续检测只用已保存 canonical Project snapshot；`IInspectionRuntimeCoordinator` 是唯一运行互斥权威；Station 控制区仅 Admin mounted；SaveCompatibility 语料归 F07/F08；F05 沿用 Quiet Precision；P6 先测量 manifest 再冻结更严预算。

当前授权边界：

1. **G0.1 已获批；G1 已在“candidate 无新增 format/build 失败”的基线债务例外下完成，等待代码级复审**；复审前不得进入 G2。
2. **G2 未授权**：检测域合同冻结与后续实现仍为 `FORBIDDEN`。
3. **真实硬件证据处置**：F05 完成时是否接受 `REAL_CAMERA/PLC/STATION=NOT_PERFORMED`，或要求现场验证作为 F05 完成门禁；无论何种选择，F08 默认入口切换仍需满足第 8 节的独立条件。

---

## 14. 状态

```text
F05_G0_AUDIT_STATE=DONE
F05_G0_1_REVIEW=APPROVED
F05_PLAN_STATE=G5_DONE
F05_G1_AUTHORIZATION=AUTHORIZED
F05_G1_STATE=DONE_WITH_PRE_EXISTING_BASELINE_DEBT
F05_G2_STATE=DONE
F05_G3_STATE=DONE_BROWSER_FIXTURE_ONLY
F05_G4_STATE=DONE_BROWSER_FIXTURE_ONLY
F05_G5_AUTHORIZATION=AUTHORIZED
F05_G5_STATE=DONE
F05_G6_ENTRY=READY
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=BLOCKED
```
