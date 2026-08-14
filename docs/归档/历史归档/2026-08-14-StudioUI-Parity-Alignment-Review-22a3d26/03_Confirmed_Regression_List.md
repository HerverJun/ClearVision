# Confirmed Regression List / Repair Backlog

本清单只列确认缺口；`PARTIAL` 与环境验收债见 02、07。严重度按真实用户影响分配：`P0=0`、`P1=4`、`P2=8`、`P3=1`。

## P1

### CV-PARITY-001 Canvas 运行到节点/调试预览丢失

- 类型：`MISSING` / `INTERACTION_REGRESSION`
- Legacy：`wwwroot/src/core/canvas/flowCanvas.js:2715` 右键项“运行到此节点/调试预览”；`:2851-2871` 设置 active node 并调用 `requestActivePreview({ trigger: 'manual' })`。
- Next：`StudioUI/src/platform/canvas/canonicalFlowCanvas.ts:711` 明确设置 `canvas.nodeRunEnabled = false`；flow owner 无对应窄命令。
- 影响：工程师不能从当前节点直接调试到该节点，必须改走更长的全局预览路径，且当前选择上下文丢失。
- 建议：在唯一 FlowCanvas/Preview owner 边界恢复同语义命令，不新增第二执行通道；验证右键、取消、stale 和权限状态。

### CV-PARITY-002 Canvas 双击与子图导航语义丢失

- 类型：`MISSING` / `INTERACTION_REGRESSION`
- Legacy：`flowCanvas.js:2385` 的 `handleDoubleClick` 调用 `onNodeDoubleClicked`；`app.js:539-556,2150-2157` 打开/关闭子图 breadcrumb 并同步 active node。
- Next：Flow capability 未发现 dblclick/subgraph command，`canonicalFlowCanvas.ts` 仅保留基础 Canvas 包装。
- 影响：Foreach 等子图 host 无法通过原有双击进入，二级流程编辑任务断链。
- 建议：先冻结哪些节点类型仍是合法 subgraph host，再由 Canvas owner 投影进入/退出状态；补 breadcrumb、键盘返回和 leave guard。

### CV-PARITY-003 Inspector 智能参数推荐闭环断开

- 类型：`BACKEND_ORPHANED`
- Legacy：`propertyPanel.js:442` 推荐按钮；`:2358` 请求 `/operators/{type}/recommend-parameters`；`:2449` 接受；`:2456` 撤销并恢复 previous values。
- Backend：`Endpoints/ApiEndpoints.cs:1672` 的推荐 endpoint 与服务仍存在。
- Next：`InspectorPanel.vue` 无 recommend/accept/revert 控件或 API 调用。
- 影响：重要算子参数调试从“推荐 -> 预览 -> 接受/撤销”退化为全手工编辑。
- 建议：复用唯一 Inspector draft owner，推荐结果先作为可撤销候选，不直接正式保存；验证合同错误、stale node 和权限。

### CV-PARITY-004 Station 高风险命令确认语义退化

- 类型：`MISSING` / `SEMANTIC_REGRESSION`
- Legacy：`stationMonitorView.js:942-1025` 在 package/stop/reload/ping 等操作前确认，并记录 operator、时间和 command ID。
- Next：`StationAdminPanel.vue:187-193,263-281` 直接调用 `owner.issueCommand/deployPackage`；command owner 的幂等与 reconcile 已存在。
- 影响：后端权威没有丢失，但现场高风险动作更容易误触，用户在提交前看不到目标与影响复核。
- 建议：在现有 Station owner 前增加按命令风险分级的确认，不复制命令 owner；确认中显示 Station、包身份、命令、未知结果策略。

## P2

### CV-PARITY-005 示例工程创建没有 Next 入口

- 类型：`ENTRY_MISSING`，同时具有 backend-orphan 特征。
- Legacy：`projectView.js:475,525-527` 选择 full/simple；`projectManager.js:161-186` 调用 `/api/demo/create*` 与 `/api/demo/guide`。
- Next：Projects 页面无对应入口；`DemoEndpoints.cs:12,26,40` 仍存在。
- 决策：ADR-G2 将其 `RELOCATE`，F09 标为 Legacy fallback；这不是迁移完成或退休。
- 建议：由 Project lifecycle owner 批准受控入口与保存/reconcile 合同后恢复。

### CV-PARITY-006 Global Variables 搜索/筛选/定位算子丢失

- 类型：`MISSING` / `INTERACTION_REGRESSION`
- Legacy：`globalVariablePanel.js:226` 搜索；`:463` 定位算子按钮；`:1142-1155` 选择 Canvas 节点。
- Next：`GlobalVariablesWorkbench.vue` 保留 CRUD、绑定和运行值，但列表直接 `v-for`，无 search/filter/locate command。
- 影响：大工程中排查变量和跳回生产节点的成本显著增加。
- 建议：添加纯 UI 过滤投影；定位动作调用现有 Flow selection command，不复制 Flow catalog。

### CV-PARITY-007 独立本地图像文件加载缺失

- 类型：`MISSING`
- Legacy：`features/image-viewer/imageViewer.js:273` 的 `loadFromFile` 可独立载入本地图像。
- Next：`ImageViewport.vue` 只呈现 Preview/artifact/blob，未提供独立文件选择入口。
- 影响：脱离当前 preview 进行离线图像检查的工作流消失。
- 建议：先确认产品 owner 是否仍要求独立 viewer；若保留，复用 FilePickerPort 与唯一 ImageCanvas owner，不把文件路径当持久化 authority。

### CV-PARITY-008 图像 annotation 显示/清除控制缺失

- 类型：`MISSING`
- Legacy：`features/image-viewer/imageViewer.js:670,676` 的 `clearAnnotations` / `toggleAnnotations`。
- Next：ImageViewport 只有 zoom/fit/actual-size/expand 与 ROI 编辑；ROI clear 不等于 annotation clear。
- 影响：工程师无法临时隐藏诊断覆盖层以检查原图，也无法只清理 annotation 投影。
- 建议：在 Image owner 窄接口区分 artifact annotation、ROI draft 和 pixel lock，再增加独立 toggle/clear。

### CV-PARITY-009 Storage 浏览路径与立即清理缺失

- 类型：`MISSING`
- Legacy：`settingsView.js:498-499`、`systemTabs.js:120,187` 提供 browse/cleanup。
- Next：`SettingsStoragePanel.vue:113-205` 只提供路径文本、策略、retention、min free space 和 disk usage inspect。
- 决策：当前计划明确不虚构 Host dialog 或未经批准的 destructive endpoint。
- 建议：浏览能力先接共享 Host picker；cleanup 必须先冻结权限、operation id、审计和 unknown-outcome 合同。

### CV-PARITY-010 Database 高级维护仍由 Legacy fallback 承载

- 类型：`BACKEND_ORPHANED`
- Legacy：`settingsApi.js:11-15` 与 `systemTabs.js` 调用 repair/restore/cleanup/reset。
- Backend：`SettingsEndpoints.cs:185,227,254` 等 endpoint 仍在。
- Next：只实现 status/backup；`settings/contracts.ts:520-527` 明确排除高级维护。
- 决策：ADR-G2 `DEFER`，要求 Admin、operation identity、互斥、审计与 reconcile；不得标为 retired。
- 建议：保持当前不可用，先完成合同与产品批准，再在唯一 Settings owner 下迁移。

### CV-PARITY-011 Runtime Preview Pilot 管理台未迁移

- 类型：`BACKEND_ORPHANED`
- Legacy：`runtimePreviewPilotConsole.js` 及 settings conditional tab 提供 config、readiness、session、report、replay、deploy、retention、governance 等管理操作。
- Backend：`SettingsEndpoints.cs:590-1429` 保留相关路由。
- Next：只有 `contracts.ts:527` 排除范围，无 Vue panel/caller。
- 影响：管理员/开发者治理能力必须回到 Legacy，Next 无法独立覆盖完整设置工作台。
- 建议：先由 Runtime/Settings owner 明确哪些能力属于产品 UI、哪些为 internal-only；未批准前保持 Legacy fallback。

### CV-PARITY-012 Station token reveal/copy 未等价迁移

- 类型：`MISSING`
- Legacy：`stationTab.js:149-160,309-359,523-524` 可 reveal/copy。
- Next：`SettingsStationPanel.vue:239-255,392-424` 只提供 masked replace/regenerate，并声明完整 token 不回显、不入浏览器或日志。
- 影响：安全边界更强，但首次部署/轮换后的受控分发路径没有被等价替代。
- 建议：优先设计一次性生成结果或 OS 级受控复制，不把长期明文 token 投影到 Vue state。

## P3

### CV-PARITY-013 持久状态上下文减少

- 类型：`MISSING` / `UI_UX_REGRESSION`
- Legacy：`index.html:804,837,857,860` 持续显示用户、FPS、工程、版本；`app.js:3399-3410` 更新 FPS。
- Next：ProductLayout 有服务状态和用户会话，但没有持续 FPS/工程名/版本上下文。
- 影响：不阻断任务，但长时间调试时资源与当前工程辨识更弱。
- 建议：在不挤压 1080p Workspace 的前提下恢复紧凑、可折叠状态投影；数据来源必须真实。

## False Positives Removed

- Camera “采集单帧”已在 `SettingsCameraPreviewSection.vue` 实现，不列缺口。
- Project JSON import/export、Results bulk export、Line Sequence、Flow Templates、Planar Scale/Offset 与 Runtime Package 已实现。
- Station package/command owner 已实现；仅确认语义与现场证据仍不完整。
- Legacy advanced analytics/PDF 自身是 placeholder，不构成迁移回归。
- `/operators/{type}/preview` 与 `/images/upload` 未建立 Legacy caller，保持 `NOT_VERIFIED`。
