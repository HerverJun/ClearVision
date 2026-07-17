# Studio UI Next F03 完整开发计划

> 文档状态：<strong>READY_FOR_APPROVAL</strong>。本文件是在代码级审计初稿、独立复核与第二轮评审后形成的最终实施计划；只有获得明确批准后才可开始 G1。本文不批准入口切换，也不代表 F03 已经开始实现。

~~~text
F03_DESIGN_BASELINE_SHA=1658216c79b79c5d9371959c83c14a46bbddeccf
STUDIO_UI_NEXT_AUDIT_SHA=1658216c79b79c5d9371959c83c14a46bbddeccf
STABLE_LINE_AUDIT_SHA=dfa5ea1ef3d100e700a19cffea5ae64006648881
PLAN_REVIEW_BASE_SHA=8579fa52f856af484c2ac4b0d3c5565f7fb5dd86
PLAN_STATUS=IMPLEMENTED_PENDING_EXTERNAL_GATES
DESIGN_SYSTEM_VERSION=V1.1
Studio:StudioUiEnabled=false
F03_IMPLEMENTED=YES
F03_STATUS=PARTIAL
F03_G6_STATUS=DONE
OPEN_BLOCKERS=6
F04_STARTED=NO
AUTH_ENTRY_DECISION=PRESEEDED_SESSION_PREVIEW_ONLY
STATION_SSE=DEFERRED
~~~

## 1. Executive Summary

F03 的目标不是重画一个 Vue 工作台，而是在不复制 Project、Flow、Preview、Runtime、Result 等业务权威的前提下，把稳定线当前真实生效的视觉工程任务链迁入 Studio UI Next：

~~~text
打开工程
→ 进入工程工作区
→ 查看流程
→ 搜索并添加算子
→ 节点选择、拖动、端口连线与删除
→ 参数查看、编辑、校验
→ 图像预览
→ ROI、缩放、像素探针及正式图像交互
→ 自动预览与手动预览
→ 脏状态、保存与并发版本处理
→ 运行准入
→ 进入运行或结果复核
~~~

代码审计给出的核心结论如下。

1. 稳定线正式工作区仍由 legacy <code>index.html</code>、<code>app.js</code> composition root、单例 <code>FlowCanvas</code>、<code>PropertyPanelCapabilityOwner</code>、<code>PreviewPanelCapabilityOwner</code>、<code>ProjectManager</code> 与 <code>InspectionController</code> 协同承载。路由切换仅隐藏 DOM，并不会卸载这些 owner。
2. Studio UI Next 已具备唯一 Product Shell、Router、Design System V1.1、单一 <code>apiTransport</code>、<code>readQuery</code>、session/status owner、唯一 Host adapter、canonical FlowCanvas Lab 接入与 WebView2/publish/no-Node 证据地基；但正式 Workspace、完整 Flow decoder、写入能力、ImageCanvas/ROI/Preview owner、特殊参数编辑器与运行 command owner 均不存在。
3. 工程读取、算子 metadata、Preview、artifact、正式保存、决策校验、单次执行与结果查询均有现有入口。当前最主要的新增执行合同是“只做准入、不执行”的薄端点；此外仍需冻结 run permission 策略、snapshot/trace identity、CameraBinding read 与 Host close/reload coordination。候选 <code>POST /api/inspection/admission</code> 只复用既有 <code>IExecutionAdmissionService</code>，不得形成第二执行 authority、reservation 或 Runtime 状态机。
4. 正式保存只使用现有 <code>PUT /api/projects/{id}</code>，一次携带 Project metadata、完整 Flow、<code>GlobalVariables=null</code> 与 <code>expectedPersistenceRevision</code>，继续进入 <code>ProjectService.UpdateAsync()</code> 和 <code>ProjectSaveCoordinator.SaveExistingProjectAsync()</code>。F03 不迁移 GlobalVariables 管理 UI，因此绝不提交变量差量或完整 schema；后端按 <code>request.GlobalVariables ?? previousGlobalVariables</code> 保留当前权威 schema。
5. Preview 必须复用现有 <code>POST /api/flows/preview-node</code>、artifact 引用和后端 admission；本地 <code>flowRevision</code> 仅作 latest-request-wins/stale 防护，不能替代 <code>PersistenceRevision</code>。
6. 最终拆成 6 个串行 Goal：Workspace Read、Flow、Inspector、Preview/Image/ROI、Persistence、Run/Final Closure。Persistence authority、Execution authority 与最终证据收口不再混入同一个 Goal；六个 Goal 仍共享一个 Workspace composition owner，不形成并行业务 owner。

本最终计划的最高优先阻断项为：

- 未冻结完整 Project/Flow decoder，不得把 Project detail 的 operator/connection 数量摘要反向重建为正式 Flow；
- 未建立唯一 Workspace/FlowCanvas/ImageCanvas/Preview owner 与可自动验证的 dispose 计数，不得挂载正式路由；
- 未建立按 Goal 渐进开放的 capability transport port 与精确 route/method allowlist，不得在 G1 提前开放任意 PUT/POST/DELETE/binary，也不得“先调通 UI”；
- 未冻结单次正式保存、409/PSV011 reconcile、in-flight save 与 route-leave 语义，不得开放写入；
- 未建立 write-capable decoder 对应 encoder、persistence/transient 字段清单与 no-op round-trip golden test，不得开放保存；
- 未复现 Preview 的高成本、真实取帧、副作用、latest-request-wins 与 artifact 释放策略，不得开放自动预览；
- canonical Preview coordinator 未移除对 legacy <code>httpClient</code> 的静态 import 前，不得进入 StudioUI production bundle；
- 未扩展 F03 Browser/WebView2/no-Node/GET-WRITE method evidence runner，不得把 Browser fixture 结论写成真实 WebView2/DPI/Release 结论。

第二轮评审提出的四项计划阻断已在本文关闭：

~~~text
F03-PLAN-R1-GLOBAL-VARIABLE-CONTRACT      → GlobalVariables 固定为 null
F03-PLAN-R2-SPLIT-PERSISTENCE-AND-RUN     → G5 Persistence、G6 Run/Final Closure
F03-PLAN-R3-WRITE-ROUNDTRIP-CONTRACT      → decoder + encoder + 字段清单 + golden test
F03-PLAN-R4-PREVIEW-LEGACY-HTTP-DEPENDENCY→ 删除静态 legacy HTTP 依赖并加 bundle guard
~~~

## 2. 审计 SHA 与代码事实

### 2.1 Git 基线核验

2026-07-16（Asia/Shanghai）先在共享 Git repository 执行了 <code>git fetch origin --prune</code>，随后核验两棵工作树。两棵工作树共享 <code>C:/Users/HerverJun/Desktop/ClearVision/.git</code>。

| 工作树 | 分支 | Local SHA | Tracking SHA | Remote SHA | 工作区状态 | 审计权限 |
| --- | --- | --- | --- | --- | --- | --- |
| <code>C:\Users\HerverJun\Desktop\ClearVision-UI-Next</code> | <code>studio-ui-next</code> | <code>1658216c79b79c5d9371959c83c14a46bbddeccf</code> | 同 Local | 同 Local | <code>M CLAUDE.md</code>；<code>?? .codex/config.toml</code> | 只允许新增本文；两项保护文件不读取、不修改、不暂存 |
| <code>C:\Users\HerverJun\Desktop\ClearVision</code> | <code>codex初稿</code> | <code>dfa5ea1ef3d100e700a19cffea5ae64006648881</code> | 同 Local | 同 Local | clean | 全程只读；未修改、暂存、切分支、reset、stash、rebase 或提交 |

稳定工作树在本轮入口核验时曾位于 <code>afcbfd686e92c2bc424bad67e936da09da4c5bdc</code>；审计期间被外部流程干净 fast-forward 到 <code>dfa5ea1ef3d100e700a19cffea5ae64006648881</code>。两者之间只有 <code>scripts/verify-operator-quality-evidence.ps1</code>（提交 <code>dfa5ea1e fix(governance): normalize kernel evidence numerics</code>）发生变化，不触及本轮 Workspace、endpoint、authority 或测试代码。Legacy 与 Contract 审计均在最终 SHA <code>dfa5ea1e...</code> 重新钉住并收口；本文同时保留入口 SHA 与最终审计 SHA，避免把外部前进隐藏为同一快照。

预期入口 SHA <code>1658216c...</code> 只与 Next 工作树一致；稳定业务线的实际审计 SHA 是 <code>dfa5ea1e...</code>，本文以实际 SHA 为准。两个审计 SHA 的 merge-base 为 <code>e1bad492fecb6dff2c0a8f848db9ebfa18acf093</code>；稳定线独有 36 个提交，Next 独有 52 个提交。因此每个未来 Goal 入口都必须重新 fetch 并做稳定线漂移审计，不能把本次文件路径和合同永久视为不变。

### 2.2 三路代码审计分工

| 审计 owner | 不重叠范围 | 交付事实 |
| --- | --- | --- |
| Legacy Workspace Audit Owner | <code>codex初稿</code> 当前正式 legacy UI、直接相连 Desktop/WebView2 host | 正式 mounted owner、交互、Preview、ImageCanvas、保存、运行、Host/WebMessage 与重复/退役路径 |
| Studio UI Next Foundation Audit Owner | <code>studio-ui-next</code> 的 Product Shell、Router、Design System、transport/query、session/status、Host、capability、evidence 基础 | 可直接继承、必须新增、禁止复制的边界；Workspace 与 Product Shell 组合建议 |
| Contract / Test / Risk Audit Owner | endpoint、permission、Application Service、revision、Preview/artifact/run、测试与 evidence runner | 合同状态、错误/取消/重试语义、测试层级、阻断码与 Goal 入口门禁 |
| 主协调 | 关键路径交叉核验与唯一计划 | 选择唯一保存链、消除 owner/合同冲突、形成矩阵、Authority Map、DAG 与文件 owner 规则 |

### 2.3 稳定线当前启动和 owner 事实

稳定线 <code>ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json:29-37</code> 的实际值为：

| 配置 | 当前值 | 代码含义 |
| --- | ---: | --- |
| <code>WorkspaceV2Enabled</code> | false | legacy 正式 Workspace 不切到废弃原型 |
| <code>PropertyPanelCapabilityEnabled</code> | true | 当前属性 owner 是 <code>PropertyPanelCapabilityOwner</code> |
| <code>PreviewPanelCapabilityEnabled</code> | true | 当前预览 owner 是 <code>PreviewPanelCapabilityOwner</code> |
| <code>GlobalVariablesCapabilityEnabled</code> | true | 当前变量 owner 是 <code>GlobalVariablesCapabilityOwner</code> |
| <code>ProjectPageCapabilityEnabled</code> | true | 当前工程页 owner 是 <code>ProjectPageCapabilityOwner</code> |
| <code>NodePreviewInspectorEnabled</code> | false | <code>nodePreviewInspector.js</code> 非当前 owner |
| <code>CircleSearchV2ToolEnabled</code> | true | Circle Search V2 特殊 ROI 编辑路径生效 |
| <code>NPointCalibrationWorkbenchEnabled</code> | true | 当前 active 行为仍是 capability 属性面板中的点序列编辑，不等于 legacy 正式资产 workbench |

<code>WebView2Host.BuildStartupInjectionScript()</code> 把这些值注入并冻结到 <code>window.__CLEARVISION_STARTUP__.featureFlags</code>。当前正式 owner 链如下：

| Capability | 当前正式 owner / symbol | 创建或写入口 | Authority |
| --- | --- | --- | --- |
| Project open | <code>ProjectPageCapabilityOwner → ProjectManager.openProject()</code> | <code>GET /api/projects/{id}</code>；<code>app.js:2588 subscribeProject</code> | Project backend |
| Flow | <code>FlowCanvas</code> + <code>FlowCanvasAdapter</code> + <code>FlowEditorInteraction</code> | <code>app.js:1957 initializeFlowEditor()</code> | Canvas 是草稿投影；正式 Flow 经 Project save |
| Operators | hidden <code>OperatorLibraryPanel</code> + visible <code>OperatorPaletteShell</code> | library/types/metadata GET；click-add/drag payload | Operator backend metadata |
| Inspector | <code>PropertyPanelCapabilityOwner</code> + <code>PropertyPanelCapabilityAdapter</code> | <code>patchNodeParameters()</code> 写 Flow draft | Flow draft；正式写入仍经 Project save |
| Preview | <code>PreviewPanelCapabilityOwner → NodePreviewCoordinator</code> | <code>POST /api/flows/preview-node</code> | 后端 admission/execution；前端仅协调投影 |
| Image / ROI | <code>ImageCanvas</code>；<code>RoiEditorPanel</code> | ROI 参数/结构写 Flow draft | Flow/Project authority 不在 Canvas |
| Save / dirty | <code>ProjectManager</code> | 当前依次 PUT Project、PUT Flow | <code>ProjectService → ProjectSaveCoordinator</code> |
| GlobalVariables | <code>GlobalVariablesCapabilityOwner</code> | <code>PUT /api/projects/{id}/global-variables</code> | Project variable backend |
| Run | toolbar <code>commandHandlers.js:176</code> → <code>InspectionController.executeSingle()</code> | <code>POST /api/inspection/execute</code> | Inspection/Runtime backend |
| HTTP | <code>core/messaging/httpClient.js</code> | authenticated HTTP；401 global event | backend endpoint |
| Host | <code>webMessageBridge</code> / Desktop <code>WebMessageHandler</code> | active F03-relevant capability 是 PickFile；正式执行命令被拒绝 | Host capability only |

### 2.4 Studio UI Next 当前基础事实

| 基础 | 当前代码事实 | F03 结论 |
| --- | --- | --- |
| 根入口 | <code>StudioStartupPageResolver.Resolve()</code> 在 <code>/index.html</code> 与 <code>/studio/index.html</code> 间启动时二选一；StudioUI 资产坏时进入诊断页且不回退 legacy | 保持单 root；禁止 CSS 双挂载 |
| Root flag | <code>appsettings.json:30 StudioUiEnabled=false</code>；<code>StudioOptions.cs:7</code> 默认 false | F03 不批准默认入口切换 |
| Product Shell | <code>StudioUI/src/app/layouts/ProductLayout.vue</code> 是唯一正式 <code>&lt;main&gt;</code> owner | Workspace 是 capability-local shell，不建第二 Product Shell |
| Router | <code>StudioUI/src/app/router.ts</code> 使用 hash history；当前无 Workspace/Inspection 路由 | 新增候选 <code>/projects/:id/workspace</code> |
| Design System | <code>StudioUI/src/design-system/README.md</code> 明确 V1.1；已有 light/dark、compact/comfortable、reduced motion | 复用 tokens/primitives；新增 Workspace 语义 token 需共享 owner 审核 |
| HTTP | <code>platform/api/apiTransport.ts:224</code> 目前只有 GET；唯一 direct fetch 在该文件 | 扩展同一 transport；禁止第二 client |
| Query | <code>platform/query/readQuery.ts:165</code> 提供 abort、latest、session generation、stale previous data | 只用于 GET；保存/Preview/Run 由 command owner 管理 |
| Session/status | <code>sessionProjectionOwner</code> 60 秒；<code>systemStatusOwner</code> 30 秒 | 复用唯一 owner，不复制 timer/cache |
| Host | <code>platform/host/webView2HostAdapter.ts:42</code> 共享单 listener 并可 dispose | 文件选择做窄 wrapper，不直接访问 <code>chrome.webview</code> |
| Projects decoder | <code>projects-read/projectContracts.ts</code> 读取 identity、summary、<code>persistenceRevision</code>，详情只保留 operator/connection 数量 | 不能初始化 FlowCanvas；必须新增完整 Workspace decoder |
| Operators decoder | <code>operators-read/operatorContracts.ts</code> 已有 catalog、category、port、parameter metadata decoder | Rail 复用；编辑校验和特殊控件新增 |
| Results | <code>results-read</code> 已有九类 outcome、Execution/Decision 双轴与 deep-link | Run 成功可导航；图像/ROI result detail 不在现有 decoder |
| FlowCanvas | 正式产品无 Canvas；Lab 通过 Vite alias 调 <code>createHostedFlowCanvasAdapter()</code>，<code>canvasLabOwner.ts</code> 有单例冲突守卫 | 提炼正式窄 facade；不得复制 Lab fixture/页面或 raw/private diagnostic 访问 |
| Image/ROI/Preview | Next 当前均无正式 import、adapter、owner 或测试 | 属于 F03 新增，但必须包裹 canonical legacy 内核 |
| Evidence | runner、WebView2、DPR、publish/no-Node 已存在，但 phase/selector/method audit 仍固定 F01/F02/GET-only | Goal 1 先扩展 F03 evidence namespace |

### 2.5 重复、旁路和退役路径

| 路径 | 当前判定 | F03 处理 |
| --- | --- | --- |
| hidden <code>OperatorLibraryPanel</code> host | CSS 隐藏但仍加载、订阅，visible Rail 依赖它；不是合法 unmount | 移除 hidden owner，catalog 由 Workspace 唯一 owner 直接消费 |
| legacy <code>PropertyPanel</code> 与 embedded <code>PreviewPanel</code> | 在当前 capability flags 下 inactive | 不复制，不并列挂载 |
| <code>CalibrationDraftWorkbench</code> | rich solve/formal asset save 只在 inactive legacy PropertyPanel 下 | F03 defer；当前 active NPoint 仅迁移点序列语义 |
| <code>nodePreviewOverlay.js</code> / <code>nodePreviewInspector.js</code> | Preview capability 开启时 inactive | 不迁移为第二 Preview owner |
| legacy <code>ProjectView</code> | ProjectPage capability 开启时 inactive | 不迁移 |
| legacy <code>GlobalVariablePanel</code> | GlobalVariables capability 开启时 inactive | 不迁移；只冻结 authority |
| new Inspection capability owner | 还受一个未定义 experimental global 限制，当前未 mounted | 不把“存在代码”误报为正式 owner |
| <code>legacyGlobals.js</code> | 以 registry accessor 暴露 <code>window.flowCanvas</code> 等，context menu 仍有兼容旁路 | F03 禁止复制全局旁路 |
| <code>createHostedFlowCanvasAdapter()</code> | 在 stable legacy 正式路径不是 owner；在 Next Canvas Lab 被使用 | 只允许经一个正式 facade 复用，不创建同名第二 adapter |
| <code>FrontendV2/</code> | 废弃迁移原型 | 禁止复制代码、目录、Goal 或视觉实现 |
| <code>viewManager.switchView()</code> | 仅增删 <code>.hidden</code>，不 dispose | F03 route leave 必须真实 unmount/dispose |

### 2.6 证据真实性

本轮是计划与只读审计，不是实现验收：

- <strong>NOT RUN</strong>：unit、dotnet test、Playwright、Browser fixture、真实 WebView2、DPI、Release publish、no-Node、性能与 CI；
- 下面引用的测试文件仅表示当前代码树中存在相应测试，不表示本轮或 Final SHA 已执行通过；
- 未使用 computer-use，也未操作用户屏幕或生成截图；
- 未修改 stable 工作树、生产代码、README 权威链接、F02/F02.1 文档或两项用户保护文件。

## 3. F03 范围与非目标

### 3.1 In scope

- Projects list/detail 到 <code>/projects/:id/workspace</code> 的入口、读取、loading/error/unauthorized/readonly 生命周期；
- Product Shell 内的 full-bleed Workspace mode、工具栏、Operator Rail、Flow Canvas、Inspector、Preview、状态栏及可恢复 pane projection；
- canonical FlowCanvas、FlowEditorInteraction、ImageCanvas、ROI 与 Preview coordinator 的窄 adapter 和唯一 mounted owner；
- operator library/search/category/compatibility、click-add、drag/drop、节点/端口/连线/选择/删除与真实快捷键；
- metadata/default/validation 驱动的参数控件，含 file picker、CameraBinding、ROI/geometry、Caliper structural model、当前 NPoint point sequence；
- 自动/手动 Preview、高成本/副作用 admission、latest-request-wins、artifact、pixel probe、ROI statistics；
- write-capable Workspace decoder/encoder、persistence/transient 字段清单、no-op round-trip；脏状态、单写入口、<code>GlobalVariables=null</code>、<code>PersistenceRevision</code>、409/PSV011 reconcile、readonly/running gate；
- final decision validate、薄 execution-admission 合同、单次 inspection execute、成功后 Results deep-link 或失败留在 Workspace；
- Feature Flag on/off、legacy/new 启动切换、回滚、architecture guard、Browser/WebView2/publish/no-Node/performance evidence 计划。

### 3.2 Explicitly out of scope

- 默认把 <code>Studio:StudioUiEnabled</code> 改为 true；
- 完整登录、退出、setup-admin 或认证入口闭环；继续使用 <code>PRESEEDED_SESSION_PREVIEW_ONLY</code>；
- Station SSE、Station command/deploy、Station 现场链路；
- Runtime、Runtime Package、算法、Inspection coordinator、结果持久化或 AgentRun authority 重构；
- Agent 页面、Settings 全量迁移；
- 现场相机、PLC、Station、工业显示器验收；
- legacy 正式退役或删除 legacy bundle；
- 完整算子 Runtime 合同同步；
- inactive CalibrationDraftWorkbench 的 solve/正式 calibration asset 保存；
- local result 图像、ROI、evidence 全量复核合同；当前 Goal 6 只保证现有 Results scalar detail/deep-link；
- 用 Browser fixture、静态 Chromium、模拟 DPR 或本地 WebView2 smoke 代替真实 DPI、Release、no-Node、现场硬件结论。

### 3.3 不变量

~~~text
Vue / Pinia / DOM / localStorage != Project authority
Vue / Pinia / DOM / localStorage != Flow authority
Vue / Pinia / DOM / localStorage != GlobalVariables authority
Vue / Pinia / DOM / localStorage != execution/result authority

UI local revision != PersistenceRevision
Preview artifact != Project asset
WebMessage host capability != authenticated execution authority
Hidden DOM != unmounted owner
~~~

## 4. 旧版功能与交互对照矩阵

结论枚举严格限定为：

~~~text
MUST_PRESERVE
SEMANTICALLY_ADAPT
REDESIGN_ALLOWED
DEFER
REMOVE_WITH_REASON
~~~

状态缩写：L=Loading，E=Empty，Err=network/decoder/backend Error，401=Unauthorized，403=Forbidden，409=Conflict，R=Running/locked，RO=Readonly。测试证据均是“代码中存在，本文未运行”。

| ID | 当前用户入口与用户目标 | 当前正式实现、owner | 数据/业务 authority | 正常与异常状态 | 写入、副作用、快捷键、生命周期 | 现有测试证据 | F03 对应页/组件/adapter/store/owner | 迁移结论 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F03-M001 | Projects 列表/最近/搜索并打开工程 | <code>features/project/projectManager.js:openProject()</code>；active <code>ProjectPageCapabilityOwner</code>；GET projects/recent/search/{id} | Project backend；列表/detail 是只读投影 | L/E/Err/401/404；成功更新 current project | open request id 防旧响应；无 AbortController | <code>project.spec.ts</code>；<code>project-page-capability-owner.spec.ts</code>；Next <code>projectContracts.spec.ts</code> | <code>ProjectsPage</code>/<code>ProjectDetailPage</code> 增 Workspace CTA；<code>workspaceQueries</code> | MUST_PRESERVE |
| F03-M002 | 在未保存切换或快速连续打开时保护用户工作 | <code>ProjectManager.prepareForProjectSwitch()</code>、<code>openProjectRequestId</code> | 当前 Project/dirty projection；正式保存仍在后端 | confirm save/discard；stale result silent ignore；save Err/409 | 可触发正式 save；项目切换失效旧 request id | <code>project.spec.ts</code> | Router leave guard + <code>workspaceOwner.open()</code>；readQuery abort/latest | MUST_PRESERVE |
| F03-M003 | 工程打开后载入完整 Flow 并进入编辑区 | <code>app.js:subscribeProject</code>、<code>handleProjectChange()</code>、Flow deserialize、inspection context、draft restore、<code>switchView('flow')</code> | GET Project detail + stored Flow；local draft 仅草稿 | L/empty-flow/Err/401/404/draft-found | hydrate Canvas；可能提示 restore；当前 route 仅隐藏 | <code>editor.spec.ts</code>；<code>project.spec.ts</code> | <code>WorkspacePage</code> route owner + full <code>workspaceContracts</code> decoder + <code>workspaceOwner</code> | MUST_PRESERVE |
| F03-M004 | 在工程页、流程页、结果页间切换 | <code>core/ui/viewManager.js:switchView()</code> 只增删 <code>.hidden</code> | 无业务 authority | hidden view 仍活动；无 loading gate | 不 dispose listener/timer/SSE/request/Canvas | <code>app-infrastructure.test.mjs</code> 仅基础线索 | Vue route mount/unmount；route leave/flag-off 强制 owner dispose | SEMANTICALLY_ADAPT |
| F03-M005 | 加载算子库、metadata 与 compatibility fallback | <code>operator-library/operatorLibrary.js:OperatorLibraryPanel</code>；GET library → types → per-type metadata fallback | Operator backend/factory metadata | L/ready/unavailable/Err/401；partial metadata | 只读请求；当前 owner 无 destroy | <code>OperatorCatalogEndpointTests.cs</code>；Next <code>operatorContracts.spec.ts</code> | Workspace 唯一 <code>operatorCatalogOwner</code> 复用 Next decoder/readQuery | MUST_PRESERVE |
| F03-M006 | 分类浏览、全局搜索、compatibility 过滤、click-add、拖放算子 | <code>flow-editor/operatorPaletteShell.js:OperatorPaletteShell</code> | Catalog metadata；FlowCanvas draft 接受 add-node command | E/no-match/metadata unavailable；drag cancel | WebView2 drag 同时用 DataTransfer 与 <code>window.__draggingOperatorData</code>；owner 可 dispose | <code>operator-palette-shell.test.mjs</code>；<code>editor.spec.ts</code> | <code>OperatorRail.vue</code> + typed drag payload + <code>flowCanvasOwner.addNode()</code> | SEMANTICALLY_ADAPT |
| F03-M007 | visible Rail 背后的 hidden legacy tree | <code>OperatorLibraryPanel</code> mounted 在 <code>.operator-library-hidden-host</code> | 仍实际拥有请求/列表 | CSS hidden 但 ready/error 仍变化 | listener/request 不会因隐藏停止；无 dispose | 无针对“真正卸载”的证据 | 删除 hidden host 依赖；只保留一个 catalog owner | REMOVE_WITH_REASON |
| F03-M008 | 查看大流程、DPR 渲染、网格、minimap | <code>core/canvas/flowCanvas.js:FlowCanvas</code>；<code>app.js:initializeFlowEditor()</code> | Canvas 仅 Flow draft/view projection | empty canvas/render error/resize；selected state | ResizeObserver、RAF、DPR backing store；<code>destroy()</code> 存在 | <code>canvas-core.test.mjs</code>；<code>flow-canvas-theme-regression.spec.ts</code>；Next <code>canvas-foundation.spec.ts</code> | canonical <code>flowCanvasAdapter</code> + 唯一 <code>flowCanvasOwner</code> + <code>FlowCanvasSurface.vue</code> | MUST_PRESERVE |
| F03-M009 | Vue 接入命令式 FlowCanvas | stable <code>flowCanvasAdapter.js:FlowCanvasAdapter</code>；Next Lab <code>canonicalFlowCanvas.ts</code> 调 <code>createHostedFlowCanvasAdapter()</code> | adapter 不成为 Flow authority | owner conflict/disposed command/stale event | 当前 property Caliper 仍绕到 raw Canvas；Lab 有 dispose 顺序 | Next <code>canvasLabOwner.spec.ts</code>；<code>StudioUiArchitectureGuardTests.cs</code> | 一个正式 narrow facade，补齐 add/remove/connect 命令；Vue 永不持 raw Canvas | SEMANTICALLY_ADAPT |
| F03-M010 | 节点单选、多选、框选、拖动、Canvas pan/zoom | <code>FlowCanvas</code> + <code>FlowEditorInteraction</code> | selection/view 是 UI projection；position 是 Flow draft | no-selection/disabled/drag-cancel/out-of-view | pointer listeners、RAF；Escape cancel | <code>editor.spec.ts</code>；Next <code>canvas-foundation.spec.ts</code> | <code>flowCanvasOwner</code> 映射 selection/view/structure event | MUST_PRESERVE |
| F03-M011 | copy/paste、undo/redo、delete、duplicate、disable、select-all | <code>FlowCanvas</code>/<code>FlowEditorInteraction</code> keyboard/context commands | Flow draft；正式写入 save owner | empty selection/clipboard invalid/undo empty/RO/R | Ctrl/Cmd+C/V/Z/Y/A；Delete/Backspace；Escape | <code>editor.spec.ts</code>；<code>high-frequency-regression.spec.ts</code> | Workspace command registry 只转发到 <code>flowCanvasOwner</code>；RO/R 禁写 | MUST_PRESERVE |
| F03-M012 | 端口连线、断线并阻止非法图 | <code>FlowCanvas.addConnection()</code>、occupied-port remove、<code>FlowEditorInteraction</code> | Flow draft；port metadata 来自 backend | missing node/port、自连、type mismatch、duplicate、input occupied、cycle | 拖线；点击 occupied port/context delete；结构 revision 增长 | <code>flow-editor-port-contract.spec.ts</code>；<code>region-port-semantics.spec.ts</code>；<code>portTypeCompatibility.test.mjs</code> | typed connection command/result；错误原因进入 status/inline feedback | MUST_PRESERVE |
| F03-M013 | 选择连线并在 Inspector 查看/删除 | 稳定线未发现可靠左键持久选线；programmatic snapshot 可供 Property/Preview；context menu 可删 | Flow draft | connection selected/unselected/deleted/stale | 删除有副作用；现有语义不完整 | <code>flow-editor-port-contract.spec.ts</code> 仅连线合同 | <code>flowCanvasOwner</code> 冻结 connection selection event 与 <code>InspectorPanel</code> connection mode | SEMANTICALLY_ADAPT |
| F03-M014 | 编辑器与 ROI 的真实快捷键 | FlowCanvas/Interaction；<code>imageCanvas.js</code> ROI mode | UI/Flow draft | focus in input 时不得误触；RO/R 禁写 | Ctrl/Cmd C/V/Z/Y/A、Delete、Escape；ROI undo/redo、箭头微调、Shift=10、polygon vertex add/delete | <code>editor.spec.ts</code>；<code>roi-editor.spec.ts</code> | Workspace scoped shortcut owner；按 focus/RO/R/IME gate | MUST_PRESERVE |
| F03-M015 | 工具栏文案标注 Ctrl+S、F5 | <code>index.html</code> 仅有 <code>data-shortcut</code> 标签；未发现 JS binding；WinForms Ctrl+F5 是强制 reload | 无 | 标签与行为漂移 | F5 不能伪装已实现；Ctrl+F5 属 Host reload | 无行为证据 | 删除虚假标签；如未来实现保存快捷键，必须单独验收 | REMOVE_WITH_REASON |
| F03-M016 | 根据 metadata/default 查看和编辑普通参数 | <code>PropertyPanelCapabilityOwner</code> + <code>propertySidebarController.mjs</code> | metadata backend；值写 Flow draft | no-selection/no-metadata/readonly/invalid/default/optional | boolean、enum、number、slider、text、color、readonly file | <code>property-panel-capability-owner.test.mjs</code>；<code>property-sidebar-controller.test.mjs</code> | <code>InspectorPanel.vue</code> + typed editor registry + parameter draft projection | MUST_PRESERVE |
| F03-M017 | required、dependency、mutual exclusion、numeric 边界校验 | <code>parameterDependencyRules.js</code>；<code>validateFlowForAction()</code> | Operator metadata + Flow draft；后端最终 admission | valid/invalid/hidden-disabled/missing required/output unavailable | 参数写入；save/run 前全 Flow validate | <code>parameter-dependency-rules-parity.test.mjs</code>；property tests | <code>parameterValidationOwner</code>；decoder 保留 conditional rules；save/run 共用结果 | MUST_PRESERVE |
| F03-M018 | 参数文件选择 | Property owner → <code>webMessageBridge</code> 的 <code>PickFileCommand</code> / <code>FilePickedEvent</code> | Host 文件选择能力；路径值仍是 Flow parameter draft | user cancel/timeout/invalid message/disposed/unsupported browser | user-initiated Host side effect；需 correlation/timeout/unsubscribe | property tests；<code>WebView2HostTests.cs</code> | capability-local <code>filePickerHostPort</code> 包装唯一 <code>StudioHostAdapter</code> | MUST_PRESERVE |
| F03-M019 | 选择 CameraBinding | Property owner GET <code>/api/cameras/bindings</code>，request token 防旧响应 | CameraManager/backend；UI 仅选择 binding id | L/E/Err/401/403/stale/no binding | GET 会枚举设备；permission CanOperateHardware | <code>CameraBindingsEndpointTests.cs</code>；property tests | <code>cameraBindingQuery</code> + typed decoder + forbidden/empty states | MUST_PRESERVE |
| F03-M020 | Rectangle/Circle/Polygon/annulus/arc/CircleSearch/NPoint 等 ROI/geometry | <code>roiEditorPanel.js</code> + shared <code>ImageCanvas</code>；Property capability integration | ROI 是 Flow parameter draft；正式 Flow save authority | no-image/invalid geometry/out-of-bounds/RO/R | draw/move/resize/pan/undo/redo；destroy ImageCanvas | <code>roi-editor.spec.ts</code>；<code>roi-geometry.test.mjs</code> | <code>roiInteractionOwner</code> + <code>ImageViewport</code>；patch parameter command | MUST_PRESERVE |
| F03-M021 | Caliper 编辑时创建 RectangleRegion 并连到 CaliperTool.SearchRegion | <code>propertySidebarController.mjs:removeNode()</code>；仍 raw <code>canvas.addConnection()</code> | Flow draft structural model | target missing/port invalid/add fail/rollback fail | 一次编辑可增 node/connection；需原子 rollback | <code>region-port-semantics.spec.ts</code>；property sidebar tests | 正式 adapter 提供 atomic structural command；禁止 raw Canvas escape | MUST_PRESERVE |
| F03-M022 | NPoint Calibration 当前正式点序列编辑 | active Property capability 的 point enable/reorder/geometry；<code>NPointCalibrationWorkbenchEnabled=true</code> | Flow parameter draft | no image/insufficient point/invalid order/RO | 点增删、enable、reorder；不产生正式 calibration asset | <code>roi-editor.spec.ts</code>；部分 <code>npoint-calibration-workbench.spec.ts</code> 需区分 inactive path | <code>NPointSequenceEditor</code>，仅迁移 active point semantics | MUST_PRESERVE |
| F03-M023 | rich NPoint solve、draft session、正式 calibration asset | <code>calibrationDraftWorkbench.js</code> 在 inactive legacy PropertyPanel；POST solve/from-draft | Calibration Application Service / ProjectSaveCoordinator | draft/solve error/checksum/revision conflict | 正式 asset 写入；与 active owner 不同 | <code>CalibrationDraftEndpointsTests.cs</code>；<code>npoint-calibration-draft-workbench.test.mjs</code> | 不进入 F03；未来独立 Goal 重新冻结 endpoint/asset/revision | DEFER |
| F03-M024 | legacy PropertyPanel 与其 embedded Preview | <code>propertyPanel.js</code>，当前 flag 下 inactive | 若挂载会与 capability owner 争夺 Flow/Preview | duplicate owner/stale UI | 自带 preview/ROI/workbench，形成第二订阅集合 | legacy property/preview tests 只能作取证 | 不 import、不复制、不隐藏挂载 | REMOVE_WITH_REASON |
| F03-M025 | 自动预览、手动预览、高成本/副作用准入 | <code>previewCoordinator.js:NodePreviewCoordinator</code>；<code>PreviewPanelCapabilityOwner</code> | 后端 <code>IExecutionAdmissionService</code>/<code>IFlowExecutionService</code>；前端仅策略投影 | auto/light、manual-required、blocked、missing input/project、timeout/auth | 500ms debounce；light auto；camera/AI/OCR/template/feature-match manual；15s/30s client timeout | <code>node-preview.spec.ts</code>；<code>preview-regression.smoke.mjs</code>；<code>OperatorPreviewServiceAdmissionTests.cs</code> | canonical preview adapter + 唯一 <code>previewOwner</code>；同一 admission/status taxonomy | MUST_PRESERVE |
| F03-M026 | Preview 取消、latest-request-wins、stale、artifact 生命周期 | Coordinator request version + node/Observation identity + AbortController；GET/DELETE preview-artifacts | Preview execution + ephemeral artifact store | cancelled/stale/deleted node/oversize/404 artifact/504/500 | abort superseded；cache eviction/node switch/destroy 释放 object URL 和 artifact | <code>preview-coordinator-memory.test.mjs</code>；<code>PreviewNodeEndpointsTests.cs</code>；<code>PreviewArtifactStoreTests.cs</code> | <code>previewOwner</code> + binary transport；flowRevision/requestSequence guard；dispose ledger | MUST_PRESERVE |
| F03-M027 | 查看 structured result、diagnostic、pixel probe、locked pixel、ROI stats/world coordinate | <code>PreviewPanelCapabilityOwner</code>；pixel probe 用 img + offscreen canvas | Preview response projection；不是 result authority | no image/decode error/stale result/probe OOB/ROI empty | 读取像素/统计，无正式写；artifact read 可取消 | <code>image-pixel-probe.test.mjs</code>；preview panel tests | <code>PreviewPanel.vue</code> + <code>previewProjection</code> + <code>pixelProbeOwner</code> | MUST_PRESERVE |
| F03-M028 | Image fit、1:1、wheel zoom、pan、overlay | <code>core/canvas/imageCanvas.js:ImageCanvas</code>；global viewer/ROI 使用 | Image/overlay 只读投影；ROI draft 单独处理 | no image/load error/stale load/empty overlay | DPR、ResizeObserver、RAF、ImageBitmap/blob URL | <code>image-viewer-memory.test.mjs</code>；<code>roi-editor.spec.ts</code> | 一个 canonical <code>imageCanvasAdapter</code> + 唯一 <code>imageCanvasOwner</code> | MUST_PRESERVE |
| F03-M029 | ImageCanvas/ROI interaction 与资源释放 | <code>ImageCanvas.destroy()</code> disconnect observer/listeners/RAF/release URL/bitmap；load generation guard | UI resource owner | stale image/cancelled ROI/unmount | right-pan、wheel、keyboard；destroy 必须幂等 | <code>image-viewer-memory.test.mjs</code>；<code>roi-geometry.test.mjs</code> | lifecycle ledger：listener/timer/RAF/observer/controller/blob/bitmap 全归零 | MUST_PRESERVE |
| F03-M030 | node preview overlay / inspector 旧路径 | <code>nodePreviewOverlay.js</code>、<code>nodePreviewInspector.js</code>；当前 Preview capability 下 inactive | 会形成第二 Preview/Image owner | duplicate/stale | 各自有 listener/canvas/destroy | 对应旧 unit 仅取证 | 不迁移、不隐藏挂载 | REMOVE_WITH_REASON |
| F03-M031 | 脏状态、标题星号、状态栏保存反馈 | <code>ProjectManager.unsavedChanges</code>、<code>updateTitle()</code>/<code>updateStatusBar()</code> | UI dirty projection；正式状态由 save response | clean/dirty/saving/error/RO/R | Flow structure 250ms debounce 同步 ProjectManager；保存清 dirty | <code>project.spec.ts</code> | <code>workspacePersistenceOwner</code> 投影 dirty/saving/reconcile；Toolbar/Statusbar 消费 | SEMANTICALLY_ADAPT |
| F03-M032 | 本机 5 分钟草稿备份/恢复 | <code>app.js</code> 的 <code>cv_autosave_backup</code>、<code>AUTO_SAVE_DELAY</code> | 仅可丢弃草稿；当前只按 projectId 匹配 | draft found/invalid/stale/cross-user risk/storage error | localStorage 写完整 flow；formal save 删除匹配 backup | 部分 project/e2e 取证；无用户隔离强证据 | schema-versioned、session/user/project scoped draft；默认不得自动覆盖 server | SEMANTICALLY_ADAPT |
| F03-M033 | 正式 revision 并发身份与持久化 | DTO <code>PersistenceRevision</code>；<code>ProjectService</code>；<code>ProjectSaveCoordinator</code> | 唯一正式 Project/Flow/Variables/assets authority | success/PSV011 stale/PSV003/006/014/023 recovery conflicts/GV031 running | coordinator 原子保存/recovery；revision 每次正式 save 变化 | <code>ProjectSaveCoordinatorTests.cs</code>；<code>ProjectGlobalVariableEndpointsTests.cs</code> | full decoder 保存 revision；只有 persistence owner 能提交 expected revision | MUST_PRESERVE |
| F03-M034 | legacy 保存依次 PUT Project 再 PUT Flow | <code>ProjectManager.saveProject():233-305</code> | 两次都进 ProjectService/Coordinator，但 client 编排不是原子 | 第一次成功第二次失败时 current project revision 仍旧；并发 save 可重叠 | 无 in-flight guard；重试可能 PSV011 | endpoint/coordinator tests 未覆盖该前端两请求事务 | F03 只用一次 <code>PUT /api/projects/{id}</code>，携完整 Flow、<code>GlobalVariables=null</code>；只允许一个 in-flight mutex，不建客户端 save queue | SEMANTICALLY_ADAPT |
| F03-M035 | 409/PSV011 并发冲突用户处理 | endpoint <code>ToProjectUpdateFailure()</code> 返回 409；legacy 仅 generic toast | server revision authority | conflict/reload/keep draft/discard/readonly/running | 不得盲重试或改 expected revision；需 reconcile GET | <code>ProjectSaveCoordinatorTests.cs</code>；endpoint tests | Conflict mode：保留本地 draft、冻结写入、重新 GET、显式 compare/reapply/discard | REDESIGN_ALLOWED |
| F03-M036 | GlobalVariables 当前正式 owner 与保存链 | <code>GlobalVariablesCapabilityOwner → ProjectManager.saveGlobalVariables()</code>；PUT global-variables | Project global-variable authority / ProjectSaveCoordinator | L/E/Err/401/403/409/R | 独立正式写；当前会清 ProjectManager dirty，存在跨域同步风险 | <code>global-variable-panel.test.mjs</code>；<code>ProjectGlobalVariableEndpointsTests.cs</code> | F03 不迁移 UI；Workspace 可读 projection 仅供校验，Project PUT 固定 <code>GlobalVariables=null</code>，不得 round-trip 或覆盖 schema | MUST_PRESERVE |
| F03-M037 | richer legacy GlobalVariablePanel | capability flag 开启时 inactive | 若复制会形成第二变量 owner/save入口 | duplicate/stale/conflict | 独立 listener/save path | 旧 unit 仅取证 | 不迁移；未来独立 capability 复用同一 authority | REMOVE_WITH_REASON |
| F03-M038 | Final Decision 配置、eligible outputs 与 validation | <code>finalDecisionPanel.js</code>；POST decision-configuration/validate；decision 写 Flow draft | Flow draft + backend validation/admission | valid/invalid/no candidates/stale response/Err/401 | request id 防旧结果但不 abort；save/run 前生效 | <code>final-decision.spec.ts</code>；<code>final-decision-panel.test.mjs</code> | <code>FinalDecisionEditor</code> + cancellable latest validation command | MUST_PRESERVE |
| F03-M039 | 点击运行、前端校验、后端 admission、错误反馈 | <code>commandHandlers.js:176</code> → property validate → <code>InspectionController.executeSingle()</code> → POST execute | <code>InspectionService</code>/<code>IInspectionRuntimeCoordinator</code>/Runtime authority | no project/no nodes/invalid params/admission/decision/GV031/401/Err/R | 可执行未保存 inline Flow；当前先切 Inspection view 再发请求；无显式 frontend canRun policy | <code>InspectionServiceSingleRunTests.cs</code>；<code>ExecutionAdmissionServiceTests.cs</code>；<code>final-decision.spec.ts</code> | G6 <code>runCommandOwner</code> 单 flight；以同一 <code>clientSnapshotId</code> 和冻结 Flow 做 admission/execute；execute 重新计算 canonical hash并权威校验，accept 后才导航 | SEMANTICALLY_ADAPT |
| F03-M040 | 运行成功后进入结果复核 | execute response；legacy InspectionPanel；Next <code>ResultsPage</code> deep-link | result repository/Inspection backend | running/success/failure/cancel/timeout/not-found | 正式 result 持久化由 backend；导航无写 | <code>inspection-panel-state.test.mjs</code>；Next <code>resultsContracts.spec.ts</code> | <code>/results?source=local&amp;projectId=...&amp;resultId=...</code>；图像 detail defer | SEMANTICALLY_ADAPT |
| F03-M041 | Inspection realtime SSE 与 WebMessage result projection | fetch SSE GET realtime/{projectId}/events；WebMessage 是 fallback/projection | Inspection runtime/event backend | connecting/reconnect/backoff/overflow/auth/error | stream/timer/reconnect 必须 dispose；Station SSE 是另一合同 | <code>inspection-sse.spec.ts</code>；<code>inspection-sse-client.test.mjs</code>；<code>InspectionEventEndpointsTests.cs</code> | F03 单次 run 不启用新 stream；若未来 live run，独立 ADR/Goal | DEFER |
| F03-M042 | 正式执行只走 HTTP，阻断 legacy WebMessage 执行 | Desktop <code>WebMessageHandler</code> 拒绝 ExecuteOperator/UpdateFlow/Start/Stop，code <code>ADMISSION_LEGACY_WEBMESSAGE_DISABLED</code> | authenticated HTTP endpoint / Runtime | blocked legacy command/401/backend admission | WebMessage 仅 Host capability；不得绕过 HTTP | <code>WebView2HostTests.cs</code>；相关 handler tests | Host port 不暴露 run/preview；architecture guard 扫描 forbidden messages | MUST_PRESERVE |
| F03-M043 | 唯一 HTTP、token、401 session 失效 | legacy <code>httpClient.js</code>；Next <code>apiTransport.ts</code> + <code>sessionProjectionOwner</code> | Auth/backend；token 不进 UI authority | 400/401/403/404/409/5xx/decode/network/abort | Next direct fetch 唯一；session generation 清 protected cache | legacy <code>http-client.test.mjs</code>；Next <code>apiTransport.spec.ts</code>/<code>readQuery.spec.ts</code> | 扩展同一 transport；command owner 接入 session generation | MUST_PRESERVE |
| F03-M044 | 单一注册/通信机制边界 | legacy <code>serviceRegistry</code>/<code>eventBus</code> 已存在；Next 用 ProductRuntime/narrow injected ports | 仅 composition infrastructure | duplicate service/bus/owner | 禁止再建 EventBus/ServiceRegistry/全局状态树 | <code>app-infrastructure.test.mjs</code>；Next architecture tests | <code>workspaceRuntime</code> 注入只读/command ports，不复制 legacy registry/bus | MUST_PRESERVE |
| F03-M045 | 全局兼容访问 FlowCanvas/adapter | <code>legacyGlobals.js</code> 暴露 <code>window.flowCanvas</code> 等；context menu 仍使用 | 旁路，不是合法 authority | stale global/disposed object/duplicate owner | 绕过 adapter、难以 dispose | architecture 取证 | 正式 Vue/commands 禁止 <code>window.*Canvas</code>；guard 扫描 | REMOVE_WITH_REASON |
| F03-M046 | 顶部工程工具栏、状态栏、错误/只读/运行呈现 | legacy <code>index.html</code> + app/command handlers/status DOM | UI projection | normal/saving/conflict/error/RO/R | 保存、预览、运行 command；视觉布局可重组 | <code>quiet-precision-evidence.spec.ts</code>；Next Design tests | <code>WorkspaceToolbar.vue</code>、<code>WorkspaceStatusBar.vue</code>，复用 DS V1.1 | REDESIGN_ALLOWED |
| F03-M047 | 窗口关闭、reload、new-window 行为与未保存协调 | MainForm/WebView2Host；Ctrl+F5 clear cache/reload；new window 同 WebView2；未发现 Project dirty close 协调 | Host lifecycle；Project authority 不变 | close with dirty/save pending/reload/host unavailable | 当前 close 只明确 flush AI Plan state；Project save 未协调 | <code>WebView2HostTests.cs</code> | Host close narrow event + persistence owner prompt/wait/reconcile；不在 Vue 直接操作窗口 | SEMANTICALLY_ADAPT |
| F03-M048 | “新 Inspection capability owner”代码存在性 | <code>InspectionCapabilityEnabled=true</code> 仍需未定义 experimental global，当前未 mounted；正式仍是 legacy InspectionPanel | 无新 authority | inactive | 不能按文件存在推断 owner | inspection unit/e2e 需按 flag 路径区分 | 不复制 inactive owner；F03 只做 run command + Results deep-link | REMOVE_WITH_REASON |
| F03-M049 | legacy ProjectView | <code>ProjectPageCapabilityEnabled=true</code> 时 inactive | Project backend | inactive | 若并列会重复 GET/open listener | project capability tests | 继续使用 Next projects-read；不迁移旧 ProjectView | REMOVE_WITH_REASON |
| F03-M050 | Workspace pane、折叠、尺寸恢复与短屏布局 | legacy split layout/DOM；Next <code>CvSplitter</code> 已有 pointer/keyboard/unmount cleanup | 纯 UI preference，不属于 Project/Flow | collapsed/restored/clamped/1366×600/comfortable overflow | pane size 可持久化为 schema-versioned UI preference；不写 Project DTO | <code>split-panel.test.mjs</code>；Next Design/1366×600 evidence | <code>WorkspaceShell</code> + <code>workspaceUiProjectionOwner</code> + <code>CvSplitter</code> | REDESIGN_ALLOWED |

矩阵共 50 项，分布如下：

| 结论 | 数量 | 占比 |
| --- | ---: | ---: |
| MUST_PRESERVE | 27 | 54% |
| SEMANTICALLY_ADAPT | 10 | 20% |
| REDESIGN_ALLOWED | 3 | 6% |
| DEFER | 2 | 4% |
| REMOVE_WITH_REASON | 8 | 16% |

任何后续代码审计若改变某行的当前 owner、endpoint 或正式 flag，必须先更新本矩阵并重新计算分布，再允许相应 Goal 开工。

## 5. Authority Map

### 5.1 唯一权威链

F03 的 composition 与 authority 固定为：

~~~text
Workspace Vue Component
  → capability-local readonly projection / emits
  → workspaceOwner（唯一跨面板协调 owner）
  → narrow capability adapter / command owner
  → apiTransport 或唯一 StudioHostAdapter
  → existing endpoint / Application Service
  → ProjectSaveCoordinator 或 Runtime/Inspection authority
~~~

正式保存的推荐唯一链：

~~~text
WorkspaceToolbar / Ctrl+S
  → workspacePersistenceOwner.save()
  → encodeWorkspaceProjectUpdateV1（完整 Flow；GlobalVariables=null）
  → ProjectPersistencePort.put("projects/{id}", UpdateProjectRequest)
  → PUT /api/projects/{id}
  → ProjectService.UpdateAsync()
  → ProjectSaveCoordinator.SaveExistingProjectAsync()
  → Project repository + Flow storage + variable/asset metadata recovery
  → response.PersistenceRevision
  → workspaceOwner 更新正式 revision 并清除对应 dirty generation
~~~

Preview 链：

~~~text
Inspector / Flow selection / PreviewPanel
  → previewOwner
  → canonical NodePreviewCoordinator adapter
  → ApiTransport.post("flows/preview-node")
  → POST /api/flows/preview-node
  → IExecutionAdmissionService
  → IFlowExecutionService / preview snapshot
  → PreviewArtifactStore（可丢弃 artifact）
  → typed response / artifact GET
  → Preview projection + canonical ImageCanvas
~~~

Run 链：

~~~text
WorkspaceToolbar.Run
  → parameterValidationOwner + FinalDecision validation
  → runCommandOwner（single flight）
  → POST /api/inspection/admission（薄合同；同一 Flow snapshot；无执行 authority）
  → IExecutionAdmissionService
  → ApiTransport.post("inspection/execute")
  → POST /api/inspection/execute
  → IExecutionAdmissionService 再校验
  → InspectionService / IInspectionRuntimeCoordinator / Runtime
  → result persistence authority
  → Results deep-link
~~~

Host 链：

~~~text
File parameter editor
  → filePickerHostPort
  → existing StudioHostAdapter
  → chrome.webview single listener
  → Desktop WebMessageHandler file picker capability
  → decoded FilePickedEvent
~~~

Preview、保存和运行不得走 Host/WebMessage。

### 5.2 Authority 冻结表

| Domain | 正式 authority | F03 可持有内容 | 禁止内容 | 正式写入口 |
| --- | --- | --- | --- | --- |
| Project identity/metadata | Project repository + <code>ProjectService</code> | readonly identity、name/description edit draft、loading/error state | 以 Pinia/localStorage 作为 current Project authority | 选定的单次 <code>PUT /api/projects/{id}</code> |
| Flow | stored Flow + <code>ProjectSaveCoordinator</code> | decoded baseline、Canvas edit draft、selection/view、local flowRevision | Canvas/Pinia/DOM 作为正式 Flow；第二 save endpoint/client/queue | 同一 Project PUT 携完整 Flow |
| GlobalVariables | Project variable schema/value services + coordinator | 只读校验所需 projection；F03 不迁移 UI | Workspace 私有 variables authority；把读取 schema round-trip 回 Project PUT；构造变量差量 | F03 Project PUT 固定 <code>GlobalVariables=null</code>；既有 capability 继续现有 endpoint |
| Project assets | <code>IProjectAssetStorage</code> + coordinator | 只读 identity/引用；Preview artifact 不得混入 | local blob、ROI draft、Preview artifact 冒充 Project asset | F03 不新增 asset 写入 |
| Preview | <code>IExecutionAdmissionService</code> + <code>IFlowExecutionService</code> | request state、cost policy projection、stale guard、artifact URL | 前端 Preview engine；把成功 Preview 当作 Run admission | 现有 Preview POST；artifact GET/DELETE |
| Image / ROI draft | canonical ImageCanvas + Flow parameter draft | viewport、pixel、ROI interaction draft | 第二 ImageCanvas；把 Canvas state 私存为正式 asset | 随 Flow 正式保存 |
| Execution | <code>InspectionService</code>、Runtime coordinator/host | admission projection、running projection、cancelled/stale UI、result link | 前端 Runtime 状态机、重复执行 authority；把 admission 结果当 reservation | 薄 admission POST + 现有 inspection execute POST；execute 最终权威校验 |
| Result | result repository / inspection history endpoints | typed scalar result projection、deep-link | 从 Preview output 推导正式 result | existing Results GET |
| Revision | backend <code>PersistenceRevision</code> | baseline revision；local dirty generation、flowRevision | 混用 flowRevision 与 PersistenceRevision；自行递增正式 revision | server response only |
| Session/auth | <code>AuthMiddleware</code> / auth service | session projection、generation、401/403 UI | 第二 token provider、完整 frontend permission policy | existing session |
| Host | Desktop/WebView2 | file picker/window capability projection | Run/Preview/Flow save WebMessage；第二 HostBridge | existing Host adapter |

### 5.3 Revision 与 dirty generation

必须同时存在但严格分离三个概念：

| 概念 | 来源 | 用途 | 何时变化 | 能否提交为并发身份 |
| --- | --- | --- | --- | --- |
| <code>PersistenceRevision</code> | Project GET/save response | 正式保存 CAS、409 reconcile、结果 traceability | 后端正式持久化成功 | 是，作为 <code>expectedPersistenceRevision</code> |
| local flowRevision | canonical FlowCanvas | Preview request identity、stale 防护、节点/结构更新 | 本地 Flow draft 每次变化 | 否 |
| dirty generation | <code>workspacePersistenceOwner</code> | 判定某次 save response 能否清除当前 dirty；防 save 中继续编辑 | 任意可保存 draft 变化 | 否 |

Save 开始时记录 <code>{projectId, expectedPersistenceRevision, submittedDirtyGeneration}</code>。响应只在 projectId 仍相同且当前 dirty generation 等于 submitted generation 时清 dirty；若 save 期间继续编辑，只更新正式 revision，不清除新 generation 的 dirty。

### 5.4 禁止建立第二套

Architecture guard 必须拒绝：

- 第二 FlowCanvas kernel 或源码副本；
- 第二 ImageCanvas、第二 ROI engine、第二 Preview engine；
- 第二 save queue、Project save client 或 workspace-local persistence endpoint；
- 第二 direct fetch/HTTP client/token provider；
- 第二 HostBridge 或组件直连 <code>chrome.webview</code>；
- 第二 EventBus、ServiceRegistry 或全局 Runtime 状态树；
- 在 Vue/Pinia/localStorage 长期持有 Canvas、ImageCanvas、EventSource、AbortController、WebView2 bridge；
- 正式 capability import <code>StudioUI/src/labs/**</code>、Lab fixture DTO 或废弃 <code>FrontendV2</code>。

## 6. F03 Workspace 信息架构

### 6.1 路由与生命周期

推荐正式路由：

~~~text
/projects
/projects/:id
/projects/:id/workspace
/results?source=local&projectId={projectId}&resultId={resultId}
~~~

路由规则：

1. Projects 列表与详情都可提供“打开工作区”；Workspace 不进入一级产品导航。
2. <code>/projects/:id/workspace</code> 仍是 <code>ProductLayout</code> 子路由，保持唯一 Product Shell、唯一 <code>&lt;main&gt;</code>、共享 session/status/preferences。
3. route meta 增加候选 <code>workspaceMode: true</code>。ProductLayout 只切换 full-bleed、固定高度、无普通 page padding/max-width 的布局，不创建第二 shell。
4. session 未认证、project GET 未完成、decoder 失败、feature flag off 时，不创建 FlowCanvas、ImageCanvas、Preview 或 Host capability owner。
5. project id 改变时先 dispose 前一 <code>workspaceOwner</code>，再创建下一 owner；禁止复用旧 owner 改 projectId。
6. 高频 selection、pane、filter、preview 状态不写 URL query。当前 <code>ProductLayout.vue</code> 监听 <code>route.fullPath</code> 并移动焦点，Goal 1 必须把焦点恢复条件收窄为真实页面导航，避免 Canvas/Inspector 焦点被 query 更新夺走。
7. breadcrumbs 必须参数感知，不直接使用 <code>route.matched</code> 的原始 <code>projects/:id</code> 字符串。

### 6.2 Shell 层次

~~~text
ProductLayout（唯一正式 Shell / 唯一 main）
└── WorkspacePage（route owner）
    └── WorkspaceShell（capability-local section，不是第二 main）
        ├── WorkspaceToolbar
        ├── Work area
        │   ├── OperatorRail
        │   ├── FlowCanvasSurface
        │   ├── InspectorPanel
        │   └── PreviewPanel / ImageViewport / ROI mode
        └── WorkspaceStatusBar
~~~

一级导航规则：

- 常规页面保持 208px Product navigation；
- 进入 Workspace 后默认折叠为 48–52px icon rail；1366×600 可完全隐藏，但顶部工具栏必须保留“返回工程详情/Projects”入口；
- 用户可临时展开一级导航；展开时不得把 Canvas 最小工作区压到预算以下，应覆盖或自动折叠一个侧 pane；
- 离开 Workspace 恢复用户之前的 Product navigation 状态，不把 Workspace 的自动折叠写成全局偏好。

### 6.3 中保真尺寸规格

尺寸是 Goal 1 的实现预算，不是高保真视觉稿：

| 区域 | compact 默认 | comfortable 默认 | Min / Max | 规则 |
| --- | ---: | ---: | --- | --- |
| Workspace toolbar | 44px | 52px | fixed | 始终可见；包含返回、工程名/revision、save、preview、run、状态 |
| Product nav in Workspace | 52px collapsed | 60px collapsed | 0/52/208 | 1366×600 可隐藏；恢复入口在 toolbar |
| Operator Rail | 232px | 256px | 196–320px；可折成 44px category rail | 搜索与列表内部滚动 |
| Inspector | 320px | 352px | 280–440px；可折叠 | 参数表单内部滚动；错误摘要固定在 pane 内 |
| Preview bottom pane | 240px | 280px | 180px–45vh；可折成 36px tab | Image viewport 不得低于 160px；恢复入口始终可见 |
| Status bar | 24px | 28px | fixed | 显示 dirty/save/conflict/preview/run/zoom/pointer |
| Flow Canvas | consume remaining | consume remaining | 目标最小 520×300 CSS px | 不允许全页滚动把 toolbar/status 推出 viewport |

Pane size 只能保存在 schema-versioned UI preference，例如 <code>studio-ui.workspace-layout.v1</code>，内容限 width/height/collapsed；恢复时按 viewport clamp。不得写入 Project/Flow DTO，也不得在 localStorage 保存正式 Flow。

### 6.4 1366×768、1366×600 与高 DPI

| 环境 | 布局原则 | 阻断条件 |
| --- | --- | --- |
| 1366×768 | collapsed Product rail + 232 Operator + 320 Inspector + bottom Preview；Canvas 使用剩余区域 | toolbar/status 不可见、Canvas 宽度低于预算、pane 无恢复入口 |
| 1366×600 | Product nav 隐藏或窄 rail；Operator/Inspector 至少自动折叠一个；Preview 默认折为 tab 或 180px；所有 pane 内部滚动 | 出现全页纵向滚动、save/run 被挤出、Canvas 高度不足 300px、modal 超出 viewport |
| Browser DPR 1/1.25/1.5/2 | CSS layout 用 CSS px；Canvas backing store 跟 DPR；pixel probe world mapping 必须一致 | 用 browser deviceScaleFactor 结论冒充 Windows DPI |
| 真实 Windows DPI | WebView2 runner 单独记录 native DPI、PerMonitorV2、JS DPR、截图像素 | 未记录 native DPI 或仅看截图尺寸 |

### 6.5 Workspace 状态模型

| 状态 | Shell 行为 | Canvas/Inspector/Preview | Save / Preview / Run |
| --- | --- | --- | --- |
| Loading | 保留结构 skeleton；project identity 可渐进显示 | 命令式 owner 尚未创建 | 全禁用 |
| Empty Flow | 显示可添加算子的空画布说明 | Operator Rail 可用；Inspector/Preview empty | Save 仅在 metadata/draft 有变化时；Run 禁用 |
| Error / Decode Error | 保留 Product Shell；显示 retry 与诊断 code | 不创建 Canvas owner，不能猜 DTO | 全禁用 |
| Unauthorized | 使用现有 session projection；说明需预置会话 | 不创建 owner | 不提供未批准登录闭环 |
| Forbidden / Readonly | 可读取则挂载 readonly Workspace；403 写操作转 readonly/reconcile | Canvas navigation 可用，任何 mutation 禁止 | Save/Run 依 permission/backend response |
| Dirty | toolbar/status 显示未保存；离开时 prompt | Preview 可针对 draft；显示“草稿预览” | Save 可用；Run 必须明确当前草稿语义 |
| Saving | 保留编辑，但记录 submitted generation | 新编辑形成新的 dirty generation | Save single flight；route leave 等待或进入 reconcile |
| Conflict | 保留本地 draft 与 server revision 摘要 | mutation 默认冻结，允许只读 compare | 禁止盲 retry；提供 reload/reapply/discard |
| Previewing | selected node 与 request identity 固定 | 旧结果可标 stale；可 cancel | Save 可用；Run 不与高成本 Preview 并发 |
| Running | Workspace 显示 backend run state | 结构/参数写入按 mutation lease 规则禁用 | Save 禁用；重复 Run 禁用；成功后 Results link |

### 6.6 Theme 与 density

- light/dark 继续由现有 preferences owner 和 Design System V1.1 tokens 驱动；Canvas/ImageCanvas adapter 接受 theme projection，不读取第二主题状态；
- compact 是正式默认；comfortable 增大 toolbar/control/pane 默认尺寸，但不得破坏 Canvas 最小工作面积；
- reduced motion 下取消非必要 pane/selection animation，不影响 Canvas pan/zoom 的即时反馈；
- 新增 ROI、ImageCanvas、Running、Conflict、Readonly token 必须进入共享 tokens 文件，由共享文件协调人修改；capability 内不得散落第二套颜色常量或 SVG icon system。

## 7. API / Permission / Decoder 合同

### 7.1 合同状态与总表

本节是最终计划的合同冻结清单。只有整份计划获得明确批准、相应 Goal 入口门禁满足后，某项合同才可进入实现。状态值只使用：

~~~text
EXISTING_AND_REUSABLE
EXISTING_BUT_REQUIRES_ADAPTATION
NEW_CONTRACT_REQUIRED
DEFERRED
~~~

| ID | Method / route | 状态 | Request / response / decoder | Permission | 代码与测试依据 |
| --- | --- | --- | --- | --- | --- |
| F03-C01 | <code>GET /api/auth/me</code> | EXISTING_AND_REUSABLE | 复用现有 session decoder/projection；Workspace 不创建第二 session query | authenticated session | Next <code>sessionProjectionOwner.ts</code>、<code>sessionProjectionOwner.spec.ts</code> |
| F03-C02 | <code>GET /api/projects/{id}</code> | EXISTING_BUT_REQUIRES_ADAPTATION | response 是完整 <code>ProjectDto</code>；新增 <code>decodeWorkspaceProjectV1()</code> 与 <code>encodeWorkspaceFlowUpdateV1()</code>，读取/写回完整 persistence envelope；GlobalVariables 只读投影、不进入 encoder | authenticated | <code>ApiEndpoints.cs:139</code>；<code>ProjectService.GetByIdAsync()</code>；Next 现有 <code>projectContracts.ts</code> 只读 summary，不足以初始化 Canvas或安全写回 |
| F03-C03 | <code>GET /api/operators/library?includeCompatibility=true</code> | EXISTING_BUT_REQUIRES_ADAPTATION | 扩展现有 catalog decoder，冻结 stable metadata、lifecycle、compatibility、parameter/output conditional rule、image contract 字段 | authenticated | <code>OperatorLibraryReadOnlyAuditTests.cs</code>；Next <code>operatorContracts.spec.ts</code>；stable <code>operatorLibrary.js</code>/<code>parameterDependencyRules.js</code> 漂移 |
| F03-C04 | <code>GET /api/operators/{type}/metadata</code> | EXISTING_BUT_REQUIRES_ADAPTATION | 与 C03 共用 editor-contract decoder；不得让 <code>unknown</code> 直接进入写命令 | authenticated | operator metadata endpoint tests；Next <code>operatorContracts.ts</code> |
| F03-C05 | <code>GET /api/cameras/bindings</code> | EXISTING_BUT_REQUIRES_ADAPTATION | typed binding list；当前 GET 会调用 <code>EnumerateCamerasAsync()</code>，不得在 Workspace mount 时自动轮询 | <code>CanOperateHardware</code> | <code>SettingsEndpoints.cs:1453</code>；<code>CameraBindingsEndpointTests.cs</code> |
| F03-C06 | <code>POST /api/inspection/decision-configuration/validate</code> | EXISTING_AND_REUSABLE | request <code>OperatorFlowDto</code>；response <code>{isValid, issues, eligibleOutputs}</code>；新增 strict decoder | authenticated | <code>ApiEndpoints.cs:574</code>；<code>final-decision-panel.test.mjs</code>；<code>final-decision.spec.ts</code> |
| F03-C07 | <code>PUT /api/projects/{id}</code> | EXISTING_AND_REUSABLE | F03 唯一正式保存：<code>UpdateProjectRequest{name,description,flow,globalVariables:null,expectedPersistenceRevision}</code>；<code>Flow</code> 必须由 reviewed encoder 生成；response 完整 <code>ProjectDto</code> | <code>CanEditProject</code> | <code>ApiEndpoints.cs:168</code>；<code>ProjectService.UpdateAsync():213</code>；<code>ProjectSaveCoordinatorTests.cs</code> |
| F03-C08 | <code>PUT /api/projects/{id}/flow</code> | DEFERRED | endpoint 存在，但 F03 不调用；与 C07 并行使用会恢复 legacy 两步保存与 partial-success 风险 | <code>CanEditProject</code> | <code>ApiEndpoints.cs:220</code>；<code>ProjectService.UpdateFlowAsync()</code>；legacy <code>ProjectManager.saveProject()</code> |
| F03-C09 | <code>GET/PUT /api/projects/{id}/global-variables</code> | DEFERRED | F03 不迁移 GlobalVariables 管理 UI；Project decoder 可提供只读 schema projection 供校验，但 F03 不调用独立 PUT，也不把 schema写回C07 | GET authenticated；PUT <code>CanEditProject</code> | <code>ApiEndpoints.cs:256,273</code>；<code>ProjectGlobalVariableEndpointsTests.cs</code> |
| F03-C10 | <code>POST /api/flows/preview-node</code> | EXISTING_BUT_REQUIRES_ADAPTATION | 复用 request identity、inline Flow、artifact references；新增 strict Preview decoder；canonical coordinator core 必须删除静态 legacy <code>httpClient</code> import，legacy/Next composition分别显式注入 client port | authenticated | <code>PreviewNodeEndpoints.cs:92</code>；<code>PreviewNodeEndpointsTests.cs</code>；<code>previewCoordinator.js:5,875</code> |
| F03-C11 | <code>GET /api/preview-artifacts/{artifactId}</code> | EXISTING_AND_REUSABLE | binary/blob response；校验 content type、长度、可选 ETag/<code>X-Artifact-Sha256</code>；创建 object URL 归 Image/Preview owner | authenticated | <code>PreviewArtifactEndpoints.cs:12</code>；<code>PreviewArtifactStoreTests.cs</code> |
| F03-C12 | <code>DELETE /api/preview-artifacts/{artifactId}</code> | EXISTING_AND_REUSABLE | 无 body；204 成功；404 视为已释放；只由 Preview resource owner 调用 | authenticated | <code>PreviewArtifactEndpoints.cs:34</code>；Preview memory tests |
| F03-C13 | <code>GET /api/images/{id}</code> | EXISTING_AND_REUSABLE | PNG binary；扩展同一 transport 的 blob decoder、AbortSignal 与 object URL 清理 | authenticated | <code>ApiEndpoints.cs:1604</code>；Image viewer tests |
| F03-C14 | <code>POST /api/images/upload</code> | DEFERRED | 当前任务链可把用户选择图像作为 Preview/execute inline input；未证明必须开放上传。若未来开放，需 413、内容类型与大小合同 | authenticated | <code>ApiEndpoints.cs:1581</code>；Image payload endpoint tests |
| F03-C15 | <code>POST /api/inspection/admission</code>（候选 route） | NEW_CONTRACT_REQUIRED | 薄合同：request携 <code>clientSnapshotId</code> 与冻结Flow；服务端canonicalize并返回同id、<code>canonicalFlowHash</code>、allowed/code/violations/revision trace；不创建 reservation | F03默认采用parity：authenticated，与当前execute一致；新增专用permission需独立安全hardening批准 | 现有服务：<code>ExecutionAdmissionService.cs</code>、<code>ExecutionAdmissionServiceTests.cs</code>；当前无 admission-only endpoint |
| F03-C16 | <code>POST /api/inspection/execute</code> | EXISTING_BUT_REQUIRES_ADAPTATION | 当前request只有project/image/camera/flow；F03 additive request增加 <code>clientSnapshotId</code> 与 expected canonical hash，服务端重新计算并校验；当前response只有result/outcome等，是否增加runId/flowVersionHash/projectRevision由G6合同测试决定 | F03 parity为authenticated；<code>CanRunInspection</code>不默认纳入 | <code>ApiEndpoints.cs:588,1487</code>；<code>InspectionResultDto.cs:210</code>；<code>InspectionServiceSingleRunTests.cs</code> |
| F03-C17 | <code>GET /api/inspection/history/{projectId}</code> 与 <code>GET /api/inspection/history/{projectId}/{resultId}</code> | EXISTING_AND_REUSABLE | 复用 Next Results scalar decoder/deep-link；不扩展为图像、ROI、evidence authority | authenticated | <code>ApiEndpoints.cs:653,685</code>；Next <code>resultsContracts.spec.ts</code> |
| F03-C18 | <code>POST /api/inspection/realtime/start|stop</code>、<code>GET /api/inspection/realtime/{projectId}/events</code> | DEFERRED | 不进入 F03 单次运行链；原生 <code>EventSource</code> 不能携 bearer，未来必须独立冻结 authenticated stream | 当前仅 authenticated；新 run permission/stream auth 未定 | <code>InspectionEventEndpoints.cs</code>；<code>InspectionEventEndpointsTests.cs</code>；<code>inspection-sse-client.test.mjs</code> |
| F03-C19 | N-point draft solve 与 calibration asset save | DEFERRED | active F03 只迁移 point-sequence 参数语义；rich solve/formal asset endpoint 不进入本阶段 | solve authenticated；formal save <code>CanEditProject</code> | <code>CalibrationDraftEndpoints.cs</code>；<code>CalibrationDraftEndpointsTests.cs</code> |

### 7.2 精确 HTTP method allowlist

F02 的 GET-only guard 不得一次性删除或替换成“任意 method transport”。底层可在 <code>apiTransport.ts</code> 内部复用一个 reviewed request core，但 ProductRuntime 只按 Goal 暴露窄 capability port；Vue 组件永远拿不到通用任意 PUT/POST/DELETE。

| Goal | 新开放 method/route | 唯一允许 port/owner | 该 Goal 前保持关闭 |
| --- | --- | --- | --- |
| G1 Workspace Read | 既有 GET：<code>auth/me</code>、<code>projects/{id}</code> | session/readQuery、<code>workspaceProjectQuery</code> | 全部 write、binary、Host file picker、admission |
| G2 Flow / Rail | GET：<code>operators/library?includeCompatibility=true</code>、<code>operators/{type}/metadata</code> | 唯一 <code>operatorCatalogOwner</code> | POST/PUT/DELETE/binary |
| G3 Inspector | 用户打开 CameraBinding editor 时 GET <code>cameras/bindings</code>；POST <code>inspection/decision-configuration/validate</code> | <code>cameraBindingQuery</code>、<code>finalDecisionValidationPort</code> | Preview/Save/Run methods |
| G4 Preview/Image | POST <code>flows/preview-node</code>；GET blob <code>preview-artifacts/{id}</code>/<code>images/{id}</code>；DELETE <code>preview-artifacts/{id}</code> | <code>previewTransportPort</code>、<code>imageResourcePort</code> | Project PUT、admission、execute |
| G5 Persistence | PUT <code>projects/{id}</code> | 唯一 <code>projectPersistencePort</code> | Flow-only PUT、GlobalVariables PUT、Run POST |
| G6 Run / Final | POST <code>inspection/admission</code>、<code>inspection/execute</code>；既有 Results GET | 唯一 <code>runCommandPort</code>、Results capability | realtime/SSE/Station/Runtime command |

Architecture tests 每个 Goal 分别断言“当前累计 allowlist”与“尚未开放表面”。以下 route 即使后端存在，也始终不进入 F03：Project create/delete、Flow-only PUT、GlobalVariables mutation、image upload、continuous preview、inspection realtime start/stop/SSE、Station、Runtime package、Agent、Settings、PLC/TCP/camera command。任一额外 method/route 触发 <code>F03-B03-WRITE-GUARD-NOT-FROZEN</code>。

### 7.3 Decoder、Encoder 与持久化字段冻结

| Decoder | 必须读取 | 必须拒绝或保留 | 不得推断 |
| --- | --- | --- | --- |
| <code>decodeWorkspaceProjectV1</code> | Project id/name/description/version/timestamps；整数 <code>persistenceRevision</code>；完整 persistence-relevant Flow；GlobalVariables只读projection；asset identity | required 字段缺失、非法 GUID/enum/finite number进入Decode Error；write-capable层出现未知字段时必须opaque passthrough或标记<code>saveCompatibility=blocked</code>，不得静默忽略 | 不得从operatorCount/connectionCount重建Flow；不得从local cache补正式revision |
| <code>encodeWorkspaceFlowUpdateV1</code> | 从baseline persistence envelope与typed draft生成完整 <code>OperatorFlowDto</code>；保留Flow/Operator/Port/Parameter/Connection identity、metadata与decision | 只允许剥离明确列入transient strip allowlist的字段；未知未审计字段阻断save | 不得直接把Vue view model或当前 <code>FlowCanvas.serialize()</code> 当正式encoder；后者当前不含Flow id/name并剥离execution字段 |
| <code>decodeOperatorEditorContractV1</code> | lifecycle/defaultHidden/category/keywords/tags；port identity/type/required；parameter dataType/default/min/max/options/required；Visible/Hidden/Ignored rules；output availability；image contract | enum 需兼容当前真实 numeric/string JSON 形状并有 exhaustive test；未知 editor kind 进入 unsupported state | 不得按算子名称硬编码 TemplateMatching 等规则；不得猜 side-effect/readiness |
| <code>decodeProjectSaveResponseV1</code> | Project identity、保存后的完整 Flow/GlobalVariables、<code>PersistenceRevision</code> | id 不匹配、revision 非整数或回退、缺 Flow 时进入 reconcile，不得清 dirty | 不得自行 <code>revision + 1</code>；不得用提交前 draft 覆盖 server canonical response |
| <code>decodePreviewNodeResponseV1</code> | success/project/node/debug session、Observation identity、output data/image、artifacts、execution time、failed operator、metrics/diagnostics | identity 不一致一律 stale 丢弃并释放 artifact；过大 payload 进入 bounded warning | Preview success 不得推断 Run allowed；local flowRevision 不得转为 PersistenceRevision |
| <code>decodeAdmissionResponseV1</code> | allowed/code/message/violations/surface/projectId/project revision/flow hash | response identity 与冻结 snapshot 不一致视为 stale | 不得把 allowed 当 reservation 或跳过 execute 内部 admission |
| <code>decodeInspectionExecutionResponseV1</code> | result id/project id/status、Execution/Decision 双轴、reason code、defect summary、processing time、image id、trace 字段 | project/result identity 不匹配、未知 required outcome 进入 Decode Error | 不得把 diagnostic 文案折叠成 NG；不得从 Preview response 合成正式 result |
| <code>decodeBinaryArtifactV1</code> | status/content type/length/blob；artifact 可选 SHA/ETag | 404、abort、content-type mismatch、oversize 明确分类 | 不得把 blob URL 持久化为 Project asset |
| <code>decodeApiProblemV1</code> | HTTP status、server <code>Code</code>/<code>Error</code>/<code>Detail</code>/<code>Violations</code> | 未知 code 保留原值，UI 使用安全 fallback | 不得为通过 UI 测试吞掉 401/403/409 或改成 generic success |

写回字段清单冻结为：

| DTO 层级 | Persistence-relevant allowlist | Transient strip allowlist | 代码依据 |
| --- | --- | --- | --- |
| Flow | <code>Id</code>、<code>Name</code>、<code>DecisionConfiguration</code>、<code>Operators</code>、<code>Connections</code> | 无 | <code>OperatorFlowDto.cs:15-37</code>；当前 <code>FlowCanvas.serialize()</code> 未保留 Id/Name，不能直接用于PUT |
| Operator | <code>Id</code>、<code>Name</code>、<code>Type</code>、<code>Metadata</code>、<code>X</code>、<code>Y</code>、<code>InputPorts</code>、<code>OutputPorts</code>、<code>Parameters</code>、<code>IsEnabled</code> | <code>ExecutionStatus</code>、<code>ExecutionTimeMs</code>、<code>ErrorMessage</code>，但G1必须用backend/legacy regression确认strip不会改变Project authority | <code>OperatorDto.cs:14-78</code>；<code>ProjectService.MapOperatorToDto()</code> |
| Port | <code>Id</code>、<code>Name</code>、<code>Direction</code>、<code>DataType</code>、<code>IsRequired</code> | 无 | <code>PortDto</code>；连线依赖port ID |
| Parameter | <code>Id</code>、<code>Name</code>、<code>DisplayName</code>、<code>Description</code>、<code>DataType</code>、<code>Value</code>、<code>DefaultValue</code>、<code>MinValue</code>、<code>MaxValue</code>、<code>IsRequired</code>、<code>Options</code> | 无 | <code>ParameterDto</code>；metadata enrichment/migration会补齐部分字段 |
| Connection | <code>Id</code>、<code>SourceOperatorId</code>、<code>SourcePortId</code>、<code>TargetOperatorId</code>、<code>TargetPortId</code> | 无 | <code>OperatorConnectionDto</code> |

未知字段策略：

1. 只读页面可以忽略未知展示字段；write-capable Workspace 不可以。
2. 已批准的扩展点可在 owner 内保存 opaque passthrough JSON，并由 encoder 原样合并；不批准的未知 persistence key 使 Save 禁用并显示 contract drift。
3. Vue/Canvas 只编辑明确字段；opaque bag 不进入UI authority，也不得被局部重构删除。
4. 每次 stable contract 前进后重新生成 persistence allowlist 与 golden fixture fingerprint。

G1 必须新增 no-op round-trip golden test：

~~~text
GET ProjectDto
→ decodeWorkspaceProjectV1
→ 不做业务编辑
→ encodeWorkspaceFlowUpdateV1
→ PUT /api/projects/{id}（GlobalVariables=null）
→ GET /api/projects/{id}
→ 对 persistence-relevant 字段做结构等价比较
~~~

该测试必须覆盖 Flow Id/Name、Operator Metadata/IsEnabled、全部 port/parameter/connection ID、parameter value/default/range/options、DecisionConfiguration；只允许 transient strip allowlist 差异。若后端migration发生，必须断言其canonical变化与revision语义，而不是把差异静默视为成功。

### 7.4 Project 读取、保存、revision 与冲突协议

| 项 | 冻结规则 |
| --- | --- |
| Read | <code>readQuery</code> 以 <code>workspace-project:{sessionGeneration}:{projectId}</code> 管理 GET；route/project 变化 abort predecessor；latest request 才能创建 owner |
| Save request | 一次 <code>PUT /api/projects/{id}</code>，携 reviewed encoder 生成的完整 Flow、用户可编辑 Project metadata、<code>GlobalVariables=null</code> 与当前正式 <code>expectedPersistenceRevision</code> |
| No-op guard | 在发PUT前对baseline与encoded persistence envelope做canonical structural compare；无业务变化且无server migration待落盘时不发请求。Golden test仍强制走一次PUT/GET验证无损往返 |
| Permission | 后端 <code>CanEditProject</code> 是唯一安全边界；前端 role 只优化可见性，403 必须进入 Readonly/Forbidden 投影 |
| Single flight | 同一 Workspace 只允许一个 in-flight PUT；保存期间按钮禁用。期间继续编辑会增加 dirty generation，但不会创建客户端 save queue，也不会自动发第二次请求 |
| Success | 只在projectId与submitted generation匹配时清dirty；以server response重新decode/rebase Canvas、Inspector、Preview baseline。若保存期间有新编辑，更新base revision并把新draft重放到server canonical baseline，保留dirty |
| 409 <code>PSV011</code> | 进入 Conflict；保留本地 draft；重新 GET server project；显式提供 compare/reapply/discard。不得替换 expected revision 后自动重试 |
| 409 <code>GV031</code> | 进入 Running/locked；不发第二写；读取 authoritative runtime state 的合同若未冻结，则只呈现 server code 并允许稍后人工刷新 |
| Network/abort after dispatch | 结果未知。不得盲重试；下次写前必须 GET 并比较 revision/content fingerprint。route leave 可忽略 disposed owner 的回调，但要记录 <code>saveOutcome=unknown</code> 供重新进入时 reconcile |
| Cancellation | 请求尚未 dispatch 可取消；已发出 PUT 后 AbortSignal 只能终止客户端等待，不能宣称服务端未提交 |
| Idempotency/retry | 条件 PUT 不是可盲重试操作；transport 不做自动 retry。用户显式 retry 也必须先完成 reconcile |
| Tests | <code>ProjectSaveCoordinatorTests.cs</code>、<code>ProjectPersistenceConcurrencyTests.cs</code>、<code>ProjectServiceTests.cs</code>、Desktop endpoint tests；Next新增decoder+encoder、persistence allowlist、no-op golden、<code>GlobalVariables=null</code>、409/unknown-outcome/rebase tests |

### 7.5 Preview、artifact、image 与 ROI 协议

Preview request 至少包含：

~~~text
projectId
targetNodeId
debugSessionId
clientRequestSequence
flowRevision              # local stale identity only
flowData                  # frozen inline draft
inputImageBase64?         # bounded, optional
parameters?
imageFormat?
timeoutMs
artifactMode="references"
~~~

规则：

1. <code>previewOwner</code> 保留 canonical 500ms debounce；新请求先 abort predecessor，再增加 request sequence。
2. response 必须同时匹配 projectId、targetNodeId、debugSessionId、clientRequestSequence、flowRevision；不匹配即释放 response artifact 且不提交 UI。
3. camera、AI/OCR、template/feature-match 等高成本类型不得自动 Preview；真实外部 I/O 与持久化副作用必须由后端 admission 阻断或 safe dry-run。
4. Preview POST 不自动 retry。timeout/abort/401/blocked/404 artifact 分开呈现；手动 Preview 是一次新的用户命令，不是 transport retry。
5. artifact GET 只由 active identity 触发；blob URL、ImageBitmap、artifact id 在 eviction、node/project switch、route leave、flag-off、destroy 时释放。
6. artifact DELETE 204 或 404 均视为资源终态；其他错误记录诊断但不把 ephemeral cleanup 失败升级为 Project save failure。
7. ROI 没有独立保存 endpoint。ROI/geometry 只修改 Flow parameter draft，最终随 C07 保存；pixel probe 与 ROI stats 是只读 projection。
8. ImageCanvas 读取 <code>GET /api/images/{id}</code> 时使用 latest image-load generation；旧 blob/image decode 完成后不得覆盖新图。

### 7.6 Run admission、execute、result 与 stream 协议

推荐的 admission request/response 候选如下，仍需评审：

~~~text
POST /api/inspection/admission
request = {
  projectId,
  surface: "StudioInspectionRun",
  clientSnapshotId,            # client生成的UUID；同一次admission/execute保持一致
  flowData,                    # 与随后 execute 完全同一冻结 snapshot
  basePersistenceRevision,     # 未保存draft仅作trace，不是snapshot authority
  inputMode: "stored-project" | "current-unsaved-draft"
}

response = {
  allowed,
  code,
  message,
  violations,
  projectId,
  surface,
  clientSnapshotId,
  projectPersistenceRevision,
  canonicalFlowHash
}
~~~

约束：

- admission 只做 preflight projection，不返回可绕过执行校验的 token，不建立 reservation，不持有 Runtime 状态；
- <code>runCommandOwner</code> freeze Flow后生成 <code>clientSnapshotId</code>；admission response必须回显该id并返回server canonical hash。任何编辑都会废弃该snapshot并要求重新准入；
- execute request携同一Flow、同一 <code>clientSnapshotId</code> 和admission返回的expected canonical hash；服务端重新canonicalize并在hash不一致时返回稳定 <code>ADMISSION_SNAPSHOT_MISMATCH</code>，随后仍调用同一 <code>IExecutionAdmissionService</code>；
- <code>basePersistenceRevision</code> 对未保存draft只用于trace。它不能代替Flow snapshot identity，也不能被当作运行reservation；
- admission/execute结果不一致时，以execute的server code为authority，并触发 <code>F03-B25-PREVIEW-RUN-ADMISSION-DIVERGENCE</code> 诊断；
- 未保存但无 unresolved conflict 的 draft 可以作为候选正式运行模式，UI 必须明确显示“运行当前未保存草稿”；是否最终批准见 D08。Conflict、invalid parameter、missing final decision、active runtime 一律阻断；
- <code>POST /api/inspection/execute</code> 非幂等，永不自动 retry；请求中断后的结果未知时，从 Results/history 按 project/clientSnapshot/result trace reconcile；
- execute 成功后使用 response <code>id/projectId</code> 导航现有 Results deep-link。完整图像/ROI/evidence 复核继续 DEFER；
- F03 不创建 EventSource、不启动 inspection realtime，也不接 Station SSE。未来 stream 必须有 authenticated fetch-SSE 或独立 stream token ADR，并复用唯一 stream owner。

### 7.7 Permission 冻结与待决缺口

| 能力 | 当前后端事实 | F03 候选决定 | Gate |
| --- | --- | --- | --- |
| Project read | authenticated | 复用 | 无额外 frontend policy |
| Project save | <code>CanEditProject</code> | 复用 | 403 进入 readonly；不得隐藏为网络错误 |
| Camera binding read | <code>CanOperateHardware</code> 且会枚举设备 | 只在用户打开 CameraBinding editor 时请求，或新增纯配置 read contract；二选一需评审 | <code>F03-B02-OPERATOR-CONTRACT-UNSYNCED</code> / D10 |
| Preview | authenticated + backend side-effect admission | 复用；不新增 frontend side-effect allowlist authority | blocked code 必须保留 |
| Admission / execute | execute 当前只有authenticated，无显式run permission | F03选择A/Parity：admission与execute沿用authenticated边界并补endpoint regression；B/Security hardening（新增<code>CanRunInspection</code>、角色迁移、legacy/API regression）不自动纳入F03，需独立批准 | G6入口记录最终选择；若改选B则先完成独立backend contract commit |
| Results read | authenticated | 复用 | scalar contract only |
| Host file picker | Desktop host capability | user gesture、correlation、timeout、decoded response；无业务 permission 替代 | Host port 不暴露 Preview/Save/Run |

<code>AUTH_ENTRY_DECISION=PRESEEDED_SESSION_PREVIEW_ONLY</code> 保持不变；F03 不增加 login/logout/setup-admin 流程。任何 role-based button hiding 都只是提示，不能替代 endpoint 的 401/403。

权限方案明确为：

~~~text
A. F03 Parity（本计划默认）
   admission + execute = existing authenticated boundary
   不修改角色映射，不影响 legacy/API 客户端

B. Security Hardening（独立批准后替代A）
   新增 CanRunInspection
   → 角色/默认权限迁移
   → admission + execute 同时应用
   → legacy Studio、现有API客户端、管理员/工程师/操作员矩阵回归
~~~

未明确选择A或B时只阻断G6，不阻断G1–G5。

## 8. Adapter 与生命周期

### 8.1 四个命令式域的唯一 owner

| Domain | 现有命令式内核 | F03 narrow adapter | 唯一 mounted owner | 输入 | 输出 / 事件映射 | Stale / revision 防护 | Dispose 责任 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| FlowCanvas | <code>wwwroot/src/core/canvas/flowCanvas.js</code>、<code>flowCanvasAdapter.js</code>、<code>features/flow-editor/flowEditorInteraction.js</code> | 一个 production facade，暴露 deserialize/serialize/add/remove/connect/patch/select/view/subscribe/diagnostics；Lab 与 Workspace 共用 | <code>flowCanvasOwner</code>，每个 active Workspace 恰好 1 | decoded Flow baseline、operator contracts、theme/DPR、readonly/running gate | structure、selection、view、connection result、command error；映射为 readonly projection | workspace generation + projectId + local flowRevision；disposed owner 的 event 丢弃 | unsubscribe → interaction.destroy → adapter.dispose；清 listener、RAF、ResizeObserver、global mouse handlers、context-menu timer |
| ImageCanvas | <code>wwwroot/src/core/canvas/imageCanvas.js</code> | <code>imageCanvasAdapter</code>，暴露 load blob/image、fit/actual/zoom/pan、overlay、pixel transform、resource diagnostics | <code>imageCanvasOwner</code>，Preview/ROI 共用同一 active viewport owner，不各建一套 ImageCanvas | active Preview image identity/blob、theme/DPR、viewport mode | image-ready/error、view transform、pointer/pixel coordinate、overlay projection | imageLoadGeneration + preview identity；旧 decode/blob 不提交 | destroy observer/listener/RAF；revoke blob URL；close ImageBitmap；清 overlays/pointer capture |
| ROI | canonical <code>ImageCanvas</code> ROI mode + <code>roiGeometry.mjs</code>；legacy <code>RoiEditorPanel</code> 仅作行为取证 | <code>roiInteractionAdapter</code> 只暴露 geometry set/edit/undo/redo/validate；不 new 第二 ImageCanvas | <code>roiInteractionOwner</code>，挂在现有 <code>imageCanvasOwner</code> 之上 | selected node/parameter contract、image bounds、current geometry、readonly gate | geometryDraftChanged/committed/cancelled/invalid；转成 typed parameter patch 或 atomic Caliper structural command | selection generation + node id + local flowRevision；node/project 切换取消 draft | detach overlay callback/shortcut/pointer state；清 history；不保留 Project asset 或正式 Flow 副本 |
| Preview | <code>previewCoordinator.js:NodePreviewCoordinator</code> + Preview endpoint/artifact store；当前文件静态import legacy <code>httpClient</code>并作为默认artifact client | 提炼无HTTP静态依赖的canonical coordinator core；legacy/Next composition分别显式注入窄client port | <code>previewOwner</code>，每个 active Workspace 恰好 1 | project/flow/node identity、operator metadata、input image、auto/manual trigger | idle/loading/success/error/auth-error/blocked/cancelled、structured output、artifact refs | requestVersion + client sequence + flowRevision + Observation identity + project/node scope | clear debounce/timeout；abort request/artifact read；unsubscribe structure/listeners；revoke URL/delete artifact/clear cache；destroy |

Vue 组件只接收 readonly projection 与窄 command；不得把 raw <code>FlowCanvas</code>、<code>ImageCanvas</code>、<code>NodePreviewCoordinator</code>、<code>AbortController</code> 或 Host adapter 存入 ref/Pinia/localStorage。

G4 的 Preview composition 硬门禁：

1. canonical coordinator core 不得静态 import <code>core/messaging/httpClient.js</code>；默认 client fallback删除，缺依赖时构造失败。
2. legacy <code>app.js</code> composition显式传入 legacy preview/artifact client adapter。
3. StudioUI composition显式传入基于唯一 <code>apiTransport</code> 的 <code>previewTransportPort</code>。
4. StudioUI production bundle/import graph architecture test禁止出现 <code>core/messaging/httpClient.js</code>。
5. 上述拆分只能形成两套composition adapter，不能复制两套 <code>NodePreviewCoordinator</code> 状态机。

### 8.2 创建、切换与销毁顺序

创建顺序：

~~~text
session ready + Workspace flag on
→ project GET success + strict decode
→ workspaceOwner reserve owner slot
→ flowCanvasOwner mount + hydrate baseline
→ inspector/parameter owners subscribe readonly flow projection
→ imageCanvasOwner mount
→ roiInteractionOwner attach when editor mode requires
→ previewOwner mount and subscribe selection/structure
→ G5 persistence command owner enable after round-trip/save gates
→ G6 run command owner enable after snapshot/admission gates
~~~

销毁顺序：

~~~text
workspaceOwner phase = disposing; reject new commands
→ mark dispatched save/execute as settle-or-reconcile, never blind retry
→ cancel read/validation/preview requests and debounce/timeout
→ unsubscribe cross-panel projections
→ previewOwner.dispose()
→ roiInteractionOwner.dispose()
→ imageCanvasOwner.dispose()
→ inspector/parameter owners.dispose()
→ flow interaction.destroy()
→ flow adapter.dispose()
→ Host capability subscriptions.dispose()
→ UI projection owner.dispose()
→ owner diagnostics count = 0
~~~

Project id 变化必须完成上一个序列再创建新 owner；不得在同一 raw Canvas 上就地替换 projectId。保存或执行已经 dispatch 时，dispose 只能阻止 stale callback 写回旧 Vue，不得宣称后端操作已取消。

### 8.3 为什么 hidden DOM 不等于卸载

稳定线 <code>viewManager.switchView()</code> 只切换 <code>.hidden</code>。隐藏后 JavaScript object 仍可持有 DOM、EventBus、timer、RAF、observer、HTTP、SSE、WebMessage subscription 和写入口；因此：

- hidden Preview 仍可能在节点变化时发 POST；
- hidden Canvas 仍可能接收 keyboard/window listener 或继续 RAF；
- hidden property/global-variable owner 仍可能写 Flow/Project；
- hidden stream 仍可能 reconnect；
- 同一 capability 的新 owner 挂载后会形成两个订阅集合和两个命令入口。

F03 的“折叠 pane”只改变可见布局，不释放 capability；“route leave、project switch、root flag/Workspace flag off”才触发真正 dispose。自动化必须区分 <code>collapsed=true, ownerCount=1</code> 与 <code>unmounted, ownerCount=0</code>。

### 8.4 Lifecycle diagnostics 与自动化守卫

<code>workspaceLifecycleDiagnostics</code> 至少投影：

~~~text
workspaceOwnerCount
flowCanvasOwnerCount
imageCanvasOwnerCount
roiOwnerCount
previewOwnerCount
activeSubscriptions
activeTimers
activeAnimationFrames
activeObservers
activeAbortControllers
activeBlobUrls
activePreviewArtifactIds
activeHostSubscriptions
inFlightReads
inFlightWrites
inFlightPreview
inFlightExecute
~~~

Gate：

1. 任一 owner count 只能是 0 或 1；大于 1 立即抛 conflict error，而不是仅记录 warning。
2. 连续 20 次 <code>/projects/:id/workspace ↔ /projects/:id</code>、20 次 project id 切换、flag-on/off 独立启动后，unmounted 状态所有 owner/resource 计数必须回到 0。
3. DOM element、listener instrumentation、heap 只作趋势证据；是否泄漏以 owner/resource ledger、可解释的 GC 后稳定值和重复循环共同判断。
4. Architecture guard 扫描 production import：禁止 <code>src/labs/**</code>、<code>FrontendV2</code>、第二 direct fetch、第二 HostBridge、<code>window.flowCanvas</code>、第二 EventBus/ServiceRegistry。
5. Browser test 的 20 次循环不能替代真实 WebView2 循环；二者都要有各自 evidence classification。

### 8.5 Canonical 跨分支漂移处理

本次逐文件比较确认不能整文件覆盖：

| 路径 | stable <code>dfa5ea1e</code> 的前进 | Next <code>1658216c</code> 必须保留 | 合并规则 |
| --- | --- | --- | --- |
| <code>flowCanvas.js</code> | stable 业务语义是当前参考，但缺少 Next 的 destroyed/global mouseup/context-menu timer 等生命周期硬化 | destroyed guard、global cleanup、timer/RAF/observer dispose | 逐函数语义合并；生命周期测试先于 UI 挂载 |
| <code>flowEditorInteraction.js</code> | stable 恢复 direct <code>TemplateSelector</code> 构造等路径 | Next 的 dispose/transient-state hardening | 不接受整文件 ours/theirs；Template 能力若不在 F03 则不得恢复为隐藏 owner |
| <code>operatorLibrary.js</code> | image input contract 与 compatibility 呈现 | Next 单 owner/新 shell边界 | metadata decoder 先冻结，再适配 Rail |
| <code>propertyPanel.js</code> / <code>propertyPanelCapabilityOwner.mjs</code> | metadata-authoritative visible/hidden/ignored、output availability、lifecycle 呈现 | Next 不复制 legacy DOM owner | 只迁移语义与测试，Vue 重新组合 presentation |
| <code>parameterDependencyRules.js</code> | 移除 TemplateMatching 硬编码，消费 backend metadata constraints | Next editor registry/lifecycle | stable authority 规则优先；不得重新硬编码 |
| <code>ProjectService.cs</code> | PixelStatistics output/decision migration | Next 当前后端 lifecycle/save hardening | 通过 Git 语义同步；不得手抄工作区文件 |
| <code>flowCanvasAdapter.js</code>、<code>imageCanvas.js</code>、<code>previewCoordinator.js</code>、<code>ProjectSaveCoordinator.cs</code>、Preview/admission endpoint | 本次比较无相关语义差异 | 当前 Next hardening | 每 Goal 入口仍重新 diff，不能假定永久一致 |

未完成上述 ledger 与对应 tests 时触发 <code>F03-CANONICAL-DRIFT-001</code>，Goal 2/3/4 不得开始。

## 9. Feature Flag、共存与回滚

### 9.1 Flag 模型

根入口保持：

~~~text
Studio:StudioUiEnabled=false
~~~

建议新增但默认关闭的 capability flag：

~~~text
Studio:WorkspaceCapabilityEnabled=false
StartupConfig featureFlags["Studio2.Workspace"]=false
~~~

名称仍需 D02 评审；不得借用 <code>Studio2.ProjectPage</code>、legacy Property/Preview flag 或未定义 experimental global。<code>StudioStartupConfigV1</code> 的 featureFlags 是启动时冻结值，因此 F03 验收以独立 Desktop 启动做 on/off，不实现未评审的运行时热切换。

| StudioUiEnabled | Workspace flag | 启动/路由行为 | Owner 预期 |
| ---: | ---: | --- | --- |
| false | 任意 | 启动 legacy <code>/index.html</code>；Next root 不 mounted | legacy owner；Next Workspace owner=0 |
| true | false | 启动 Next Product Shell；Workspace CTA 隐藏或 route 显示 Disabled，不加载 project/Canvas | Next root=1；Workspace/Canvas/Image/Preview=0 |
| true | true | Next Product Shell 可进入 <code>/projects/:id/workspace</code>；通过 session/project decode 后创建唯一 owner | 每个 F03 owner=1 |
| true | true 但 StudioUI assets 缺失 | 进入现有诊断页；不自动回退 legacy | 所有 product/workspace owner=0 |

### 9.2 双轨共存原则

- “共存”是两个可选择的启动 bundle 与同一后端 authority 共存，不是同一 WebView2 页面同时挂 legacy 和 Next DOM。
- Legacy 继续是默认入口；F03 不批准入口切换、旧版退役或数据迁移。
- 两端都必须消费相同 Project/Flow/GlobalVariables/Preview/Inspection authority；不得为 Next 创建平行 endpoint、存储或 Runtime projection tree。
- stable 线后续提交只按 Git 单向、语义审计后进入 <code>studio-ui-next</code>；不从 Next 把未完成实验反向混入 <code>codex初稿</code>。

### 9.3 回滚步骤

1. 停止新发布并保留失败 Final SHA/evidence；不清理用户 Project 数据。
2. 若仅 Workspace capability 有问题，设置 Workspace flag false 并重启 Desktop，验证 Workspace owner/resources 为 0，Next 其余只读页面仍可用。
3. 若 Next root 有问题，保持/恢复 <code>Studio:StudioUiEnabled=false</code> 并重启，确认启动 legacy；不得在同一会话通过 CSS 显示 legacy。
4. 对已 dispatch 的保存/执行结果按 server revision/history reconcile；回滚 UI 不回滚 Project/Runtime authority。
5. 不使用 <code>git reset --hard</code>、强推或删除稳定线提交；代码回滚使用可审计 revert/forward fix，并重新跑 flag-off、legacy smoke、method audit。

回滚完成门禁：legacy root only、Next Workspace owner=0、无残留 request/timer/observer/blob、Project 可由 legacy 正常打开、remote/local Final SHA 有记录。任一失败触发 <code>F03-B18-FEATURE-FLAG-COEXISTENCE</code>。

## 10. 分阶段 DAG

### 10.1 推荐数量与依赖

基于当前真实代码量，推荐 6 个串行 Goal：

- G1–G4 分别建立 Workspace 读取地基、Flow 命令式域、参数合同域、Preview/Image/ROI 命令式域；这些边界与现有 owner、生命周期和合同边界一致；
- Persistence authority、Execution authority、Final evidence closure 是三个独立高风险验收面。Persistence 必须先以 no-op round-trip、revision/conflict 和 unknown-outcome reconcile 独立稳定，Run 才能消费已冻结的 snapshot 与 revision；
- 因此把原合并阶段拆为 G5 Persistence 与 G6 Run/Final Closure。6 个 Goal 仍严格串行，不增加并行 authority；Goal 内只允许无共享状态权威、无文件重叠的叶子组件和测试并行。

~~~mermaid
flowchart LR
    R["Review gate：范围、读取合同、flag"] --> G1["G1 Workspace Read Foundation"]
    G1 --> G2["G2 FlowCanvas/Operator Rail"]
    G2 --> G3["G3 Inspector/参数合同"]
    G3 --> G4["G4 Preview/ImageCanvas/ROI"]
    G4 --> G5["G5 Persistence/Revision/Conflict"]
    G5 --> G6["G6 Admission/Execute/Results/Final Closure"]
    D["每个 Goal 入口重新审计 origin/codex初稿 漂移"] --> G1
    D --> G2
    D --> G3
    D --> G4
    D --> G5
    D --> G6
~~~

### 10.2 Goal 交付依赖

| Goal | 必须消费的前置产物 | 产出给下一 Goal 的稳定接口 | 禁止越过 |
| --- | --- | --- | --- |
| G1 | 最终计划评审决定、两分支 drift ledger | Workspace route/shell、full decoder/encoder contract、GET query lifecycle、owner diagnostics、F03 evidence phase | 不开放 write/binary/Host picker/admission；未冻结读取与往返字段合同不得开始 Canvas UI |
| G2 | G1 owner/transport/route；canonical drift 合并策略 | typed Flow draft facade、selection/structure/view projection、Operator Rail commands | 不实现 Inspector 私有 Flow copy |
| G3 | G2 Flow facade/selection；stable operator metadata decoder | parameter draft/validation/editor registry、FinalDecision projection、特殊编辑覆盖清单 | 不创建 Preview/ImageCanvas owner |
| G4 | G2 Flow revision；G3 parameter/geometry contracts | Preview/Image/ROI projection、artifact lifecycle、image-backed editor commands、无 legacy HTTP 静态依赖的 canonical coordinator | 不建立正式 save/run authority |
| G5 | G1–G4 全部 owners、typed snapshot 与正式 encoder | atomic Project PUT、canonical rebase、revision/conflict/unknown-outcome/close reconcile | 不实现 admission、execute 或 Results handoff |
| G6 | G5 已稳定的 persistence baseline、冻结 Flow snapshot 与 revision | admission/execute/results handoff、Final SHA evidence 与 rollback decision package | 未通过 final gates 不改入口、不退役 legacy |

## 11. Goal 拆分

### 11.1 Goal 1 — Workspace Read Foundation

| 项 | 计划 |
| --- | --- |
| 目标 | 建立 <code>/projects/:id/workspace</code>、ProductLayout workspaceMode、完整 Project/Flow decoder、与 decoder 配对的正式 encoder/持久化字段清单、唯一 Workspace owner、GET query lifecycle、owner/resource diagnostics、F03 runner namespace 与读取 route allowlist |
| 非目标 | 不渲染正式 Flow 节点；不开放 PUT/POST/DELETE/binary；不接 Host file picker；不实现参数编辑、Preview、保存、admission、execute；不把 root flag 设为 true |
| 入口门禁 | D01、D02、D05、D14 完成范围/6 Goal/flag/渐进 allowlist/canonical 策略决定；重新 <code>git fetch origin --prune</code>；记录 Next/stable SHA；完成 <code>F03-CANONICAL-DRIFT-001</code> ledger；保护文件未进入 index，stable worktree 保持只读隔离 |
| Capability | WorkspacePage/Shell、route lifecycle、loading/error/unauthorized/readonly states、project full query、layout projection、decoder/encoder contract、lifecycle diagnostics |
| Endpoint 前置 | 冻结并仅调用 C01/C02；C03–C19 只登记依赖状态，不要求在 G1 开放或实现。G1 architecture guard 必须证明只有 Project/session GET 可达 |
| 文件 owner | Workspace Foundation Owner：<code>StudioUI/src/capabilities/project-workspace/{WorkspacePage.vue,WorkspaceShell.vue,workspaceOwner.ts,workspaceContracts.ts,workspaceQueries.ts,workspaceUiProjectionOwner.ts,workspaceLifecycleDiagnostics.ts}</code> |
| 共享文件协调人 | 主协调唯一修改 <code>router.ts</code>、<code>ProductLayout.vue</code>/CSS、<code>productRuntime.ts</code>、<code>apiTransport.ts</code>、StartupConfig、Vite、Design tokens、evidence scripts、package/lockfile、CI、feature flags；G1 不修改业务写 endpoint |
| 可并行子任务 | 在接口冻结后，Workspace layout leaf、decoder unit tests、lifecycle diagnostics tests 可分开；不得并行修改 router/transport/ProductLayout；不得各自建立 owner/store/client |
| 测试层级 | TS unit：decoder/encoder、persistence/transient field classification、opaque passthrough、transport/route projection；golden fixture：GET payload decode→encode 结构等价（不在 G1 实际 PUT）；architecture：single fetch、no labs、GET-only allowlist、owner 0/1；Desktop：StartupConfig/flag/resolver；Playwright：Loading/401/404/decode/flag-off shell |
| WebView2 / 视觉证据 | 独立启动验证 root flag on + Workspace flag off/on；1366×768、1366×600；light/dark、compact/comfortable；只验证 shell/owner lifecycle，不宣称 Flow 功能 |
| 性能/生命周期预算 | 20 次 route mount/unmount 后 owner/resource 全 0；普通 Project GET 使用 readQuery abort/latest；Workspace shell 首次 ready 不得相对同机 F02 Product route 连续三组回归 >20%；无全页 overflow |
| 提交策略 | 先提交合同/architecture guard，再提交 transport/route，再提交 Workspace shell/tests；shared file commit 由主协调完成；evidence 临时产物不提交 |
| 完成门禁 | C02 full decoder 可拒绝 malformed payload；encoder 保留全部 persistence allowlist 字段并只剥离批准的 transient allowlist；未知持久化字段可 opaque passthrough，否则 write capability 必须保持 disabled；flag-off owner=0；flag-on project success owner=1；route leave=0；GET-only allowlist 生效；F03 runner 接受 phase/selector；<code>F03-B08/B09/B16/B18/B28/B33</code> 均关闭 |
| 下一 Goal 输入 | 稳定的 <code>WorkspaceRuntime</code> narrow read ports、decoded Flow baseline、正式 encoder contract、owner slot、layout surfaces、F03 evidence namespace |

### 11.2 Goal 2 — FlowCanvas、Operator Rail、节点/端口/连线

| 项 | 计划 |
| --- | --- |
| 目标 | 迁入 canonical FlowCanvas/interaction；建立唯一 production facade 与 owner；复用 Operator catalog decoder；完成搜索/分类/click-add/drag-drop、节点选择/多选/拖动、pan/zoom、端口连线/断线、删除、copy/paste/undo/redo/duplicate/disable、typed error feedback |
| 非目标 | 不实现参数表单、Preview/Image/ROI、正式保存、运行；不复制 Lab 页面/fixture；不恢复 hidden OperatorLibrary owner |
| 入口门禁 | G1 完成；stable operator/canvas drift 重新 diff；Flow DTO/port/metadata enum decoder frozen；canonical lifecycle hardening tests 先通过 |
| Capability | OperatorRail、FlowCanvasSurface、flowCanvasOwner、production facade、Workspace scoped shortcuts、status feedback、readonly/running mutation gate |
| Endpoint 前置 | C02、C03、C04；Operator Rail 只有一个 catalog query owner。未同步 stable conditional/image contract 时可显示 unsupported，但不得猜字段 |
| 文件 owner | Flow Workspace Owner：<code>project-workspace/flow/**</code>、<code>OperatorRail.vue</code>、<code>FlowCanvasSurface.vue</code>；canonical shared JS 仍由主协调逐函数合并 |
| 共享文件协调人 | 主协调唯一修改 Vite aliases、canonical <code>wwwroot/src/core/canvas/**</code>、<code>flowEditorInteraction.js</code>、shared operator contracts、router/shell/tokens |
| 可并行子任务 | Operator Rail presentation/filter tests可与 facade diagnostics tests并行；connection error presentation可作为叶子组件；唯一 flowCanvasOwner、interaction 与 canonical files 串行 |
| 测试层级 | unit：facade disposed command、selection/structure/view mapping、drag payload、connection reason；legacy parity：port compatibility；Playwright：add/drag/select/connect/delete/shortcuts/RO；architecture：owner conflict/raw Canvas/global bypass |
| WebView2 / 视觉证据 | 真实 WebView2 验证 pointer capture、drag/drop、keyboard focus、context menu、DPR；1366×600 保证 Canvas minimum；Browser fixture 明确标注模拟数据 |
| 性能/生命周期预算 | canonical 100 nodes/150 connections、300/450 fixtures；2 warmups + 5 samples；同 fingerprint 相对 stable 连续三组 >20% 才 block；interaction long task、heap、listener/RAF 记录；20 次 mount/project switch 资源归零 |
| 提交策略 | canonical drift 同步独立 commit；facade/owner独立 commit；Operator Rail/interaction/tests独立 commit；禁止把 stable 全文件复制成一个“大同步”提交 |
| 完成门禁 | 所有正式 Flow command 只经 facade；owner count 0/1；hidden catalog owner不存在；合法/非法连线 reason稳定；shortcuts受 focus/IME/RO/R gate；<code>F03-B02/B08/B09/B17/B26/B27</code> 关闭 |
| 下一 Goal 输入 | 唯一 Flow draft projection、selection/structure/view events、typed node/port/parameter identities、local flowRevision、atomic structural command boundary |

### 11.3 Goal 3 — Inspector、metadata validation 与非图像参数编辑

| 项 | 计划 |
| --- | --- |
| 目标 | 建立 InspectorPanel、parameter editor registry、metadata-authoritative visibility/ignored/output rules、default/required/min/max/integer/mutual exclusion validation、普通控件、file picker、CameraBinding、GlobalVariable reference projection、FinalDecision editor；冻结所有特殊 editor coverage |
| 非目标 | 不创建 ImageCanvas/Preview；不实现 rich calibration asset workbench；不迁移完整 GlobalVariables UI；不正式保存 Project |
| 入口门禁 | G2 selection/patch/atomic structural command稳定；C03/C04 editor decoder frozen；stable <code>parameterDependencyRules.js</code> 语义已同步；D10 CameraBinding 策略决定 |
| Capability | Inspector normal/empty/connection/readonly modes；boolean/enum/number/nullable/range/slider/text/textarea/color/file/CameraBinding；validation summary；FinalDecision；ROI/NPoint/Caliper editor contract slots |
| Endpoint 前置 | C03/C04、按决定的 C05、C06；本 Goal 才开放 CameraBinding GET、FinalDecision validation POST 与 Host file-picker port；GlobalVariables 只来自 C02 projection；不调用 C09 PUT |
| 文件 owner | Inspector Owner：<code>project-workspace/inspector/**</code>、<code>parameterEditors/**</code>、<code>parameterValidationOwner.ts</code>、<code>FinalDecisionEditor.vue</code> |
| 共享文件协调人 | 主协调唯一修改 shared Design primitives/tokens/icons、Host adapter、operator API contracts、canonical dependency rules、C# metadata contracts |
| 可并行子任务 | 冻结 editor registry interface 后，不同 leaf editor 与其 unit tests可并行；file picker、CameraBinding、FinalDecision可分包；Inspector owner/validation tree/共享 registry 只有一个 owner |
| 测试层级 | decoder/enum/condition parity；editor round-trip/default/nullability；validation before save/run；Host cancel/timeout/stale；Camera 401/403/empty；FinalDecision latest-wins；Playwright keyboard/ARIA/RO/R/short screen |
| WebView2 / 视觉证据 | 真 WebView2 文件选择 user gesture/cancel；长参数列表、错误摘要、compact/comfortable、light/dark、1366×600；不使用 computer-use |
| 性能/生命周期预算 | 普通 keystroke 不产生 >50ms long task；300-node Flow 全量 validation p95 预算 100ms，超出需增量化但不得改变后端规则；选节点 100 次不增加 listener/Host subscription；文件对话框结束后 subscription=0 |
| 提交策略 | metadata decoder/parity先行；普通 editors；Host/Camera/FinalDecision；coverage matrix与 tests；任何 canonical/shared 变更单独主协调 commit |
| 完成门禁 | stable active parameter kinds 全部映射为 implemented/Goal4-image-backed/DEFER-with-reason 三态，不得遗漏；unknown kind 显示 unsupported且阻断写；save/run使用同一 validation projection；<code>F03-B02/B23/B26/B27</code> 关闭 |
| 下一 Goal 输入 | typed parameter draft、validation result、special-editor descriptors、selected node/image input requirements、FinalDecision state、atomic patch/structural commands |

图像耦合的 Rectangle/Circle/Polygon/annulus/CircleSearch/NPoint/Caliper 在 G3 完成 metadata、校验和 command contract，在 G4 接入唯一 ImageCanvas/ROI owner。这样既不遗漏特殊编辑器，也不在 G3 提前创建第二命令式图像域。

### 11.4 Goal 4 — Preview、ImageCanvas、ROI、pixel probe 与 artifact

| 项 | 计划 |
| --- | --- |
| 目标 | 复用 canonical NodePreviewCoordinator 与 ImageCanvas；完成自动/手动 Preview、高成本/副作用准入、artifact refs/blob、structured result、pixel probe/locked pixel/world coordinate、ROI stats、图像缩放/pan/fit/1:1、image-backed ROI/NPoint/Caliper editor |
| 非目标 | 不做 inspection realtime/Station SSE；不做 full result image/evidence review；不做 rich NPoint solve/formal asset；不写 Project |
| 入口门禁 | G2 local flowRevision/event稳定；G3 editor descriptors/validation稳定；C10–C13 frozen；canonical preview/image diff ledger clean；canonical coordinator 已可在不静态 import legacy <code>core/messaging/httpClient.js</code> 的条件下组合 |
| Capability | previewOwner、PreviewPanel、imageCanvasOwner、ImageViewport、roiInteractionOwner、pixelProbeOwner、artifact resource owner；auto/manual controls与 blocked/timeout/auth/error states |
| Endpoint 前置 | C10/C11/C12/C13；C14/C18/C19 不开放；Preview side-effect code taxonomy冻结 |
| 文件 owner | Preview/Image Owner：<code>project-workspace/preview/**</code>、<code>image/**</code>、<code>roi/**</code>；canonical <code>previewCoordinator.js</code>/<code>imageCanvas.js</code>/<code>roiGeometry.mjs</code> 由主协调 |
| 共享文件协调人 | 主协调唯一修改 apiTransport Preview POST/blob GET/artifact DELETE 支持、Vite aliases、canonical JS、legacy/Next composition roots、evidence runner、shared image/ROI tokens/icons |
| 可并行子任务 | Preview presentation、pixel probe pure functions、ROI geometry unit tests可并行；previewOwner/imageCanvasOwner/transport/artifact lifecycle串行；不得各建 ImageCanvas实例 owner |
| 测试层级 | unit：cost policy、five-part identity、abort/latest、artifact cleanup、image load generation、ROI geometry/undo/redo/Caliper rollback；endpoint：Preview/artifact；Playwright：auto/manual/blocked/timeout/image/ROI/pixel；architecture：one Preview/Image owner，StudioUI production bundle/source graph 禁止 import legacy <code>core/messaging/httpClient.js</code> |
| WebView2 / 视觉证据 | 真 WebView2 DPR/native DPI separate；wheel/pan/pointer capture、ROI keyboard、blob image、route switch cleanup；1366×600 Preview tab恢复；light/dark image overlay可读性 |
| 性能/生命周期预算 | 500ms debounce保持；auto最多1个pending+1个active且前者被 supersede；高成本 auto 请求数=0；20次 node/project/route switch 后 artifact/url/bitmap/controller/timer=0；Canvas性能连续三组不得回归 >20% |
| 提交策略 | transport-injection/canonical cleanup；Preview owner；Image owner；ROI/pixel；integrated tests/evidence分 commit；不提交截图/日志到根目录 |
| 完成门禁 | canonical coordinator 无 legacy client 默认静态依赖；legacy composition 显式注入 legacy adapter，Next composition 显式注入 Studio transport adapter；forbidden-import gate通过；stale response提交数=0；副作用/高成本策略与 stable一致；artifact/object URL无残留；ROI只写 Flow draft；Preview success不设置 runAllowed；<code>F03-B08/B09/B10/B22/B25/B27/B34</code> 关闭 |
| 下一 Goal 输入 | 可冻结的完整 Flow snapshot、统一 validation/Preview states、resource-clean owners、image/ROI parameter draft、result-ready projection |

### 11.5 Goal 5 — Persistence、revision 与 conflict

| 项 | 计划 |
| --- | --- |
| 目标 | 完成唯一 Project PUT、dirty generation、server canonical response rebase、PSV011 conflict、GV031 running lock、network/abort unknown-outcome reconcile、route/close settle-or-reconcile 与 readonly 写入阻断 |
| 非目标 | 不实现 admission、execute、Results handoff 或 Final closure；不迁移 GlobalVariables UI；不创建客户端 save queue；不切默认入口 |
| 入口门禁 | G1–G4 complete；D03、D09、D11、D16 的 persistence 部分解决；C07 frozen；正式 encoder、persistence/transient allowlist 与 no-op round-trip golden fixture通过；stable drift re-audit无未处理 Project/Flow authority修复 |
| Capability | workspacePersistenceOwner、dirtyGeneration、Conflict/Readonly/Running UX、unknownOutcomeReconciler、route/close coordination、save toolbar/status projection |
| Endpoint 前置 | 只开放 C07 <code>PUT /api/projects/{id}</code>；request 携完整 Project metadata/Flow、<code>GlobalVariables=null</code>、server-issued <code>ExpectedPersistenceRevision</code>。C08/C09/C15/C16/C18/C19 保持不调用 |
| 文件 owner | Persistence Owner：<code>project-workspace/persistence/**</code> 与 Workspace save toolbar/status integration；Project DTO/encoder、Host close boundary、ProjectService/Coordinator contract shared files由主协调 |
| 共享文件协调人 | 主协调唯一修改 apiTransport Project PUT route support、Project DTO/encoder contracts、Host close boundary、feature flags、C# Project endpoint/Application Service/Coordinator regression、package/lockfile/csproj |
| 可并行子任务 | Conflict presentation、readonly/running status、golden fixture扩展可在接口冻结后并行；唯一 persistence owner、encoder、Project PUT 与 close/reconcile integration串行；同一 <code>.csproj</code> tests串行 |
| 测试层级 | decoder/encoder golden：<code>GET → decode → no edit → encode → PUT(GlobalVariables=null) → GET</code>；metadata、port/parameter IDs、DecisionConfiguration 与 opaque persistence字段结构等价；dirty generation；save中继续编辑；PSV011/GV031；network/abort unknown outcome；无自动retry；server canonical response一次性rebase Canvas/Inspector/Preview；legacy Project save regression |
| WebView2 / 视觉证据 | 真 WebView2 覆盖 edit→save、save中继续编辑、Conflict、Running lock、unknown outcome reopen/reconcile、route/close guard；1366×600 下 save/conflict/readonly状态可见；本 Goal 不宣称运行链通过 |
| 性能/生命周期预算 | save最多1 in-flight；无客户端 save queue与transport retry；route leave后 stale callback=0；20次 save/conflict/reconcile cycle 后 command owner、controller、listener归零；预算与第13.3节记录的同机 fingerprint绑定 |
| 提交策略 | encoder/round-trip guard；atomic persistence；conflict/unknown outcome；Host close coordination；integration tests按逻辑分 commit。每次 push前 fetch，不混入 run/backend security改动 |
| 完成门禁 | no-op round-trip除 transient strip allowlist 外结构等价；未知 persistence字段不会静默丢失；Project PUT始终 <code>GlobalVariables=null</code>；PersistenceRevision只由server response更新；PSV011/GV031/unknown outcome/close-reconcile通过；save后各 projection同 baseline；<code>F03-B11/B20/B21/B24/B30/B33</code> 关闭 |
| 下一 Goal 输入 | 已稳定并可复核的 server canonical Project/Flow baseline、PersistenceRevision、冻结 snapshot encoder、无 unresolved conflict 的运行候选输入 |

### 11.6 Goal 6 — Admission、execute、Results 与 Final Closure

| 项 | 计划 |
| --- | --- |
| 目标 | 完成经批准的 admission-only contract、snapshot identity、single execute、Results scalar deep-link；收集同一 Final SHA 的 unit/contract/architecture/Playwright/真实 WebView2/Release/no-Node/DPI/performance/flag/rollback证据并形成 completion report |
| 非目标 | 不切默认入口；不实现 realtime/SSE/Station/现场硬件；不退役 legacy；不扩 full result image/ROI/evidence；不把 admission 变成 reservation 或 Runtime authority |
| 入口门禁 | G5 complete；D04、D06、D07、D08、D13 与 D17 解决；C15/C16/C17 frozen；permission 选择、<code>clientSnapshotId</code>/<code>canonicalFlowHash</code>、未保存 draft 语义与 stable drift审计均有评审记录 |
| Capability | runCommandOwner、admission/execute decoders、frozenSnapshotIdentity、Results navigation、run toolbar/status、final evidence/rollback coordination |
| Endpoint 前置 | 开放经批准的 C15 admission POST、C16 execute POST 与 C17 Results GET。两次 POST 使用同一冻结 Flow 与 <code>clientSnapshotId</code>；admission 返回 canonical hash，execute重算并以 <code>ADMISSION_SNAPSHOT_MISMATCH</code> 拒绝不一致。<code>basePersistenceRevision</code> 对未保存 draft 仅作 trace |
| 文件 owner | Run/Final Closure Owner：<code>project-workspace/run/**</code>、run toolbar/status integration、Results handoff tests/evidence；backend permission/endpoint/DTO、Results shared decoder/router、CI/evidence scripts由主协调 |
| 共享文件协调人 | 主协调唯一修改 <code>ApiEndpoints.cs</code>、permission guards/role mappings、admission/execute DTO 与 service wiring、Results decoder/router、CI/evidence scripts、package/lockfile/csproj |
| 可并行子任务 | Results deep-link tests、run status leaf、evidence manifest校验可在合同冻结后并行；run owner、backend endpoint/permission与共享 integration串行；同一 <code>.csproj</code> tests串行 |
| 测试层级 | permission matrix；admission/execute same-snapshot、edited-between、hash mismatch、service revalidation；execute success/failure/timeout/unknown；Results identity；完整 Playwright task chain；GET/WRITE audit；legacy execute regression；flag/rollback；Final SHA CI |
| WebView2 / 视觉证据 | 真 WebView2 完整链：Projects→Workspace→edit→Preview→save或明确运行未保存draft→admission→execute→Results；1366×768/600、theme/density、Browser DPR/真实DPI分开；独立 Release published executable与no-Node process tree |
| 性能/生命周期预算 | admission/execute各最多1 in-flight且latest-request-wins仅适用于可取消的preflight，不自动重试execute；disposed owner回写=0；完整链无未解释long-task burst；100/150、300/450与20-cycle结果绑定同一环境 fingerprint |
| 提交策略 | permission/admission backend独立 contract commit；snapshot identity/execute；Results handoff；final integration；evidence/docs分别提交。Final candidate后任何代码修复产生新 SHA并重跑受影响证据 |
| 完成门禁 | permission决定与角色/legacy回归一致；admission不建立reservation且execute总是重验；snapshot mismatch稳定拒绝；local/tracking/remote Final SHA一致；适用 unit/contract/architecture/Playwright、真实 WebView2、Release、no-Node、DPI、performance、method/flag/rollback evidence均指向该 SHA；所有适用 blocker关闭或经评审明确 DEFER |
| 下一阶段输入 | 评审通过的 F03 completion report、完整 evidence index、已知 deferred contracts；仍保持 <code>StudioUiEnabled=false</code>，入口切换另立 Goal |

## 12. 文件 owner 与并行规则

### 12.1 Owner 白名单

| 文件/目录族 | 唯一 owner | 可并行范围 | 禁止 |
| --- | --- | --- | --- |
| <code>StudioUI/src/capabilities/project-workspace/**</code> 根 composition、<code>workspaceOwner.ts</code> | Workspace Foundation Owner | 只读 projection leaf、测试在接口冻结后 | 第二 workspaceOwner、第二 state tree、跨域直接 fetch |
| <code>project-workspace/flow/**</code>、Operator Rail | Flow Workspace Owner | Rail presentation、pure tests | 直接持 raw Canvas、修改 Inspector/Preview/persistence owner |
| <code>project-workspace/inspector/**</code> | Inspector Owner | 独立 leaf editor/test | 修改 Flow owner、建第二 validation tree/Preview engine |
| <code>project-workspace/preview/**</code>、<code>image/**</code>、<code>roi/**</code> | Preview/Image Owner | pure formatter/geometry/pixel tests | 第二 ImageCanvas/Preview owner、Project asset 写入 |
| <code>project-workspace/persistence/**</code> | Persistence Owner | Conflict/readonly leaf tests、golden fixtures | 第二 save client/queue、GlobalVariables patch、run command |
| <code>project-workspace/run/**</code> | Run/Final Closure Owner | Results/status leaf tests、evidence manifest校验 | Runtime state machine、WebMessage execute、修改 persistence baseline |
| canonical <code>wwwroot/src/core/canvas/**</code>、<code>features/flow-editor/**</code> 跨分支同步 | 主协调 | 只读审计可并行 | 多人整文件合并、手抄 stable 工作区文件、ours/theirs 粗暴选择 |
| Router、ProductLayout、navigation、tokens/icons、apiTransport/readQuery、Host/StartupConfig、Vite | 主协调 | 无并行写 | capability owner自行修改共享 root |
| API DTO/endpoints/Application Service/permission、<code>.csproj</code>、CI、scripts、package/lockfile | 主协调 | 不同测试项目且资源完全隔离时只读/执行可并行 | 多 owner 修改同一 contract；同一 csproj并行 test |
| <code>C:\Users\HerverJun\Desktop\ClearVision</code> stable worktree | 永久只读审计 | 只读 diff/log/rg | 修改、暂存、切分支、stash/reset/rebase/commit/clean |
| <code>CLAUDE.md</code>、<code>.codex/config.toml</code> | 用户保护 | 无 | 读取内容、修改、暂存、提交或覆盖 |

### 12.2 并行实施规则

1. 每个 capability 同一时刻只有一个实现 owner，主协调维护文件白名单；越界需求只报告。
2. Design primitives、纯 formatter/decoder tests、独立 leaf editor 可以并行；FlowCanvas+Inspector+Preview、save+Project encoder、run+admission、bootstrap/router/providers 不拆成多个 owner。G5 与 G6 串行，不能并行修改同一 snapshot/revision boundary。
3. 共享 contract 变更先由主协调提交，子任务只消费固定 SHA/interface；不得在各自分支创建临时同名 client/store 后事后合并。
4. 同一 <code>.csproj</code> 的 <code>dotnet test</code> 串行；推荐用仓库 <code>scripts/run-dotnet-test-serial.ps1</code> 或固定 regression script。已 build 后定向测试用 <code>-NoBuild -NoRestore</code>。
5. Browser/WebView2 并行运行必须隔离 HTTP/CDP/PLC port、WebView2 user-data、SQLite/test DB、result/evidence/publish目录。
6. 每个 Goal 入口与完成时重新记录 <code>origin/codex初稿</code> SHA；authority/security/contracts 修复优先语义吸收，Next lifecycle/UI 专属实现保留。
7. 冲突不能用整文件 <code>ours/theirs</code>；先写冲突分析，按 stable authority + Next lifecycle hardening 逐符号解决。

### 12.3 建议 capability-local 文件结构

~~~text
StudioUI/src/capabilities/project-workspace/
├── WorkspacePage.vue
├── WorkspaceShell.vue
├── workspaceOwner.ts
├── workspaceRuntime.ts
├── workspaceContracts.ts
├── workspaceQueries.ts
├── workspaceUiProjectionOwner.ts
├── workspaceLifecycleDiagnostics.ts
├── flow/
├── inspector/
│   └── parameterEditors/
├── preview/
├── image/
├── roi/
├── persistence/
└── run/
~~~

这是文件归属建议，不批准现在创建这些文件。正式实现前应把最终白名单写入 Goal execution card；若现有代码结构表明更合适的命名，可调整目录，但不得改变唯一 owner 与 authority 边界。

## 13. 测试、性能与证据

### 13.1 证据分层

| 层级 | 当前可复用证据 | F03 必须新增/扩展 | 判定与分类 |
| --- | --- | --- | --- |
| StudioUI lint/typecheck/unit | <code>StudioUI/package.json</code> 的 <code>lint</code>、<code>typecheck</code>、<code>test:unit</code>、<code>build</code>；现有 transport/query/host/canvas tests | Workspace decoder/owner、flow facade、Inspector、Preview/Image/ROI、persistence/run tests | Final SHA 重新执行；历史 PASS 只作线索 |
| Legacy UI unit/parity | <code>ClearVision.Product.UI.Tests/tests/unit</code> 的 canvas、operator、property、preview、ROI、pixel、final-decision、http-client tests | 对 active stable 语义做 parity fixtures；不能 import legacy DOM owner到生产 | 证明“语义保留”，不证明 Vue lifecycle/WebView2 |
| Decoder/contract | Next project/operator/results contracts；Desktop endpoint tests | C01–C19 strict decoders、numeric/string enum、malformed/unknown、method allowlist、permission/error/revision tests | decoder failure不得降级成伪数据 |
| Backend unit/integration | <code>ProjectSaveCoordinatorTests.cs</code>、<code>ProjectServiceTests.cs</code>、<code>ExecutionAdmissionServiceTests.cs</code>、Preview/Inspection tests | atomic combined save、admission-only endpoint、run permission、execute trace、unknown-outcome reconcile coverage | 同一 csproj 串行；未运行写 NOT RUN |
| Architecture guard | <code>f02Architecture.spec.ts</code>、<code>StudioUiArchitectureGuardTests.cs</code>、retirement/Startup tests | single owner、no labs/FrontendV2、single fetch/Host、GET/WRITE exact routes、no EventBus/ServiceRegistry/Runtime tree、no raw Canvas/global bypass | 任一 forbidden import/method/owner count >1 直接 block |
| Playwright / Browser fixture | existing F02 product/canvas scenarios；legacy editor/ROI/Preview scenarios | 完整 Workspace task chain、states、shortcuts、1366×600、theme/density、20-cycle lifecycle、flag on/off harness | 标记 <code>DATA_SOURCE=BROWSER_FIXTURE</code>、<code>AUTH_SOURCE=HARNESS_SEEDED_SESSION</code>、<code>DPR=BROWSER_EMULATED_DPR</code> |
| Browser fixture server | <code>tests/support/studio-ui-next-server.cjs</code> 当前只接受 F01/F02 且 GET-only | 增 <code>f03</code> phase、strict expected GET/PUT/POST/DELETE、Workspace fixtures与错误场景 | 额外 method/route 失败；fixture 不冒充真实 authority |
| 真实 WebView2 | <code>Invoke-StudioUiWebView2Evidence.ps1</code>、<code>Invoke-StudioUiWebView2Matrix.ps1</code>、Desktop runner | Workspace selector、full chain、method capture、owner/resource probe、1366×600、flag off/on、real endpoint states | 必须标 <code>REAL_WEBVIEW2_*</code>；Browser fixture不能替代 |
| Lifecycle/leak | Canvas Lab owner tests、现有 memory tests、runner instrumentation | Workspace/Flow/Image/ROI/Preview/save/run resource ledger，20次 route/project/full-cycle | owner/resource计数是主要 gate；heap/listener只作趋势并需解释 |
| DPR | existing Canvas Browser DPR 1/1.25/1.5/2 | Workspace Flow/Image/ROI/pixel matrix与1366×600组合 | 只结论为 browser-emulated DPR |
| 真实 DPI | WebView2 matrix可记录 native DPI、PerMonitorV2、JS DPR、截图像素 | 至少一组真实 Windows DPI matrix，Flow/Image/ROI坐标一致 | 未记录 native DPI则 <code>NOT PERFORMED</code> |
| 性能 | canonical 100/150、300/450 fixtures；existing performance runner | 完整 Workspace而非仅Lab；route ready、interaction、Preview、heap/long task、resource cleanup | 2 warmups+5 formal samples；绝对预算与回归判断都绑定相同环境 fingerprint |
| Release publish | Desktop csproj 已把 Vite dist复制到 published <code>wwwroot/studio</code> | Release publish到 <code>.tmp/publish-check/studio-ui-next-f03/**</code>；验证 assets/hash/startup | Debug/WebView2 smoke不能替代 Release |
| no-Node | <code>Test-StudioUiNoNodeEvidence.ps1</code> | 接受 f03 phase；扫描 source/node_modules/runtime/source map/Vite signature；运行 published process tree | “clean machine without Node”未做则明确 NOT PERFORMED |
| Feature Flag / rollback | Startup resolver/Host tests | 四种 flag truth table、独立启动、owner counts、legacy回退、assets-missing diagnostic | CSS hidden或同页双root直接失败 |
| Final SHA CI | <code>.github/workflows/ci.yml</code> 的 StudioUI/UI Browser/solution jobs | F03 phase进入适用 job；必要时 workflow_dispatch；单独记录 Release/WebView2/no-Node | 普通 <code>studio-ui-next</code> push不等于完整CI；skipped不写 PASS |

### 13.2 必测用户任务与状态矩阵

Playwright Browser fixture 与真实 WebView2 至少覆盖：

1. Projects list/detail → Workspace；loading、empty flow、401、403 readonly、404、decode error、stale refresh。
2. 搜索/分类/click-add/drag-add；节点单/多选、拖动、pan/zoom、合法/非法连线、删除、copy/paste、undo/redo、focus/IME gate。
3. 普通参数、dependency/ignored/output rule、file cancel/success、CameraBinding empty/403、FinalDecision invalid/valid、unknown editor blocked。
4. Preview auto/manual、high-cost manual-only、side-effect blocked、abort/latest、timeout、artifact 404/cleanup、image load stale、pixel probe、ROI geometry/keyboard。
5. dirty → save success；save中继续编辑；PSV011；GV031；network/abort unknown → reopen reconcile；readonly write 403。
6. Preview success但 Run admission失败；dirty draft run标签；admission stale；execute success/failure/timeout/unknown；Results deep-link。
7. route leave、project switch、Workspace flag off、root flag off、assets missing、20次循环与rollback。
8. 1366×768、1366×600；light/dark；compact/comfortable；Browser DPR 与真实 DPI 分开。

### 13.3 性能与生命周期预算

所有绝对时间与相对回归门禁都必须绑定 evidence manifest 中的环境 fingerprint：CPU 型号/逻辑核、内存、Windows build、电源模式、GPU/driver、WebView2/Chromium runtime、.NET runtime、build configuration、窗口/viewport、DPR/native DPI、fixture ID 与 source SHA。fingerprint 不同的结果只能作为趋势线索，不能直接据此 PASS/BLOCK；100ms 等绝对目标也必须同时报告该 fingerprint 和样本分布。

| 指标 | Budget / gate |
| --- | --- |
| Owner | Workspace/Flow/Image/ROI/Preview 各 0/1；任何时刻 >1 block |
| Route/project cycles | 20次后 unmounted owner、subscription、timer、RAF、observer、controller、blob、artifact、Host subscription均为0 |
| Canvas fixtures | 100/150与300/450；2 warmups+5 samples；相对stable同fingerprint >20% 为warning，连续3组 >20% block |
| Long task | 在记录的同机 fingerprint 下，普通参数输入不产生 >50ms long task；完整Flow validation p95目标≤100ms；超预算先profile，不得删校验 |
| Preview | 500ms debounce；同一owner最多1 active request；高成本auto request=0；stale UI commit=0 |
| Save/Run | save与execute各最多1 in-flight；无自动retry、无客户端save queue；disposed owner回写=0 |
| Layout | 1366×600无全页overflow；toolbar/status/Canvas minimum/恢复入口可见 |
| Resource trend | GC后heap/listener/node不要求机械归零，但20-cycle不得单调无界增长，且ledger必须全0 |

### 13.4 Evidence namespace 与真实性

建议固定：

~~~text
EvidencePhase=f03
EvidenceRoot=.tmp/studio-ui-next/f03/**
RuntimeRoot=.tmp/studio-ui-next/f03/runtime/**
PublishRoot=.tmp/publish-check/studio-ui-next-f03/**
~~~

- 截图只使用仓库脚本、Playwright 和 WebView2 runner；禁止 computer-use 操作用户屏幕。
- 每份 evidence manifest 记录 source SHA、stable audit SHA、configuration、runtime kind、data/auth source、route、window/viewport、DPR/native DPI、expected/observed methods、owner/resource counters。
- Final candidate 发生任何代码或配置变化后，旧 evidence 立即降级为历史线索；不得“补写”成新 SHA 的 PASS。
- 本次计划修订阶段所有 build/test/Playwright/WebView2/DPI/Release/no-Node/performance/CI 均为 <strong>NOT RUN / NOT PERFORMED</strong>；代码级审计与文档机械检查不冒充上述运行证据。

## 14. 风险、阻断码与门禁

第二轮计划评审的四个修订项已在本计划文本中关闭，关闭的是“计划表达与边界”，不是 F03 实现门禁：

| 修订项 | 计划关闭证据 |
| --- | --- |
| F03-PLAN-R1-GLOBAL-VARIABLE-CONTRACT | C07/G5 固定 <code>GlobalVariables=null</code>；删除变量差量/round-trip 假设 |
| F03-PLAN-R2-SPLIT-PERSISTENCE-AND-RUN | 第10–11节改为6个串行Goal，G5/G6分别验收 persistence 与 run/final closure |
| F03-PLAN-R3-WRITE-ROUNDTRIP-CONTRACT | 第7.3节、G1/G5加入 encoder、字段清单、opaque passthrough 与 no-op golden chain |
| F03-PLAN-R4-PREVIEW-LEGACY-HTTP-DEPENDENCY | 第8节与G4要求删除 canonical coordinator 静态 legacy client依赖，并设置 forbidden-import gate |

| 阻断码 | 风险/触发条件 | 可判定检测 | 解除条件 / 行动 |
| --- | --- | --- | --- |
| F03-B01-STABLE-BASELINE-DRIFT | Goal入口 stable SHA前进或owner/contract变化未审计 | fetch后 diff/log、canonical ledger | 逐符号分类 authority/security/UI drift；更新矩阵/合同后再开工 |
| F03-CANONICAL-DRIFT-001 | whole-file同步会丢 stable业务语义或Next lifecycle hardening | 指定 canonical 文件 cross-SHA diff与parity/lifecycle tests | stable authority + Next hardening语义合并；禁止ours/theirs |
| F03-B02-OPERATOR-CONTRACT-UNSYNCED | metadata decoder缺 visible/hidden/ignored/output/image/lifecycle 字段 | contract fixture与stable payload对比 | decoder freeze、parity tests、unknown字段策略通过 |
| F03-B03-WRITE-GUARD-NOT-FROZEN | Goal所需方法/route超渐进allowlist或组件直调 | architecture/method audit | 按G1–G6只开放7.2批准route；组件仅持窄capability port与唯一transport |
| F03-B04-RUN-PERMISSION-UNDECIDED | G6开始前 admission/execute权限政策未决或两者不一致 | endpoint metadata/permission/role tests | D06选择parity或独立security hardening并完成对应回归；只阻断G6 |
| F03-B05-ADMISSION-CONTRACT-MISSING | G6 UI准备显示runAllowed但无authority-backed preflight | C15 endpoint/decoder tests缺失 | 薄endpoint通过且execute仍重验；否则不显示runAllowed；只阻断G6 |
| F03-B06-RUN-REVISION-UNBOUND | execute无法关联冻结Flow snapshot或把PersistenceRevision误当snapshot identity | request/response trace/hash tests | <code>clientSnapshotId</code>与server <code>canonicalFlowHash</code>冻结；execute重算校验；revision仅作trace；只阻断G6 |
| F03-B07-SSE-AUTH-UNUSABLE | 原生EventSource无法携bearer或新增第二stream client | stream auth integration test | F03保持DEFER；未来独立ADR后才解除 |
| F03-B08-DUPLICATE-OWNER | 任一capability owner count>1或两个写入口 | runtime diagnostics/architecture guard | 冲突即抛错；dispose旧owner后计数≤1 |
| F03-B09-HIDDEN-NOT-DISPOSED | route/flag/project切换后资源仍活跃 | 20-cycle ledger | 全owner/resource归0；CSS hidden不计通过 |
| F03-B10-PREVIEW-STALE-COMMIT | 旧project/node/request/flow response进入UI | identity injection tests | stale commit=0且artifact被释放 |
| F03-B11-SAVE-AMBIGUOUS-RETRY | PUT unknown outcome后自动retry或改revision重发 | fault injection/network abort test | 先GET reconcile；无transport auto retry；显式用户决策 |
| F03-B12-DPR-DPI-CONFLATION | 把deviceScaleFactor/截图像素称为native DPI | evidence manifest audit | Browser DPR与Windows native DPI分开记录 |
| F03-B13-WEBVIEW2-NOT-RUN | 只跑Browser fixture却声称Desktop通过 | evidence classification | Final SHA真实WebView2适用matrix完成，否则NOT RUN |
| F03-B14-NO-NODE-NOT-RUN | 只build未验证published process/assets | no-Node report缺失 | Final SHA Release publish + no-Node runner完成 |
| F03-B15-FINAL-SHA-MISMATCH | code/evidence/local/tracking/remote SHA不一致 | manifest与git rev-parse | 同一Final SHA重跑或降级旧证据 |
| F03-B16-1366X600-OVERFLOW | toolbar/status/run/save/Canvas被挤出或全页滚动 | Playwright+WebView2 viewport assertions | pane auto-collapse/clamp/内部滚动通过 |
| F03-B17-PERFORMANCE-REGRESSION | 同fingerprint连续三组>20%或资源无界增长 | performance runner/20-cycle | profile/fix并重跑；不得删功能/校验换PASS |
| F03-B18-FEATURE-FLAG-COEXISTENCE | 同页双root、flag-off仍mount、assets坏时偷回legacy | startup/owner/method evidence | truth table四态通过；rollback重启验证 |
| F03-B19-CONTRACT-WEAKENED-FOR-TEST | 为测试改旧合同、吞错误、放宽decoder/permission | diff review+legacy/backend regression | 恢复真实合同；fixture适配真实payload，不反向弱化生产 |
| F03-B20-AUTHORITY-SPLIT | Canvas/Pinia/localStorage成为Project/Flow/Execution authority | architecture scan+save snapshot audit | 仅draft/projection；正式链进入Coordinator/Inspection/Runtime |
| F03-B21-REVISION-IDENTITY-MIXED | flowRevision/dirty generation写入expected revision | type/API tests | PersistenceRevision独立类型与only-server-update guard |
| F03-B22-PREVIEW-COST-BYPASS | camera/high-cost/side-effect在auto path执行 | request capture+admission tests | high-cost auto=0；blocked/safe-dry-run语义保留 |
| F03-B23-EDITOR-COVERAGE-GAP | stable active parameter/editor kind无映射 | generated coverage matrix against catalog | implemented/G4/defer-reason三态完整；unknown阻断写 |
| F03-B24-POST-SAVE-PROJECTION-SPLIT | 保存后Canvas/Inspector/Preview使用不同baseline/revision | integration test with server canonical response | workspaceOwner一次性rebase三投影；stale Preview清除/重跑 |
| F03-B25-PREVIEW-RUN-ADMISSION-DIVERGENCE | Preview成功被当作Run允许，或preflight/execute快照不同 | freeze-snapshot/edited-between tests | same snapshot；execute重验；错误码留在Workspace |
| F03-B26-SHARED-FILE-MULTI-OWNER | 多代理改router/transport/contracts/tokens/CI | file ownership audit | 主协调单owner；越界改动撤出并重新集成 |
| F03-B27-SECOND-INFRASTRUCTURE | 第二HTTP/Host/Canvas/Image/Preview/EventBus/Registry/Runtime tree | source/import guard | 删除平行实现，复用既有单一基础；新增须独立ADR批准 |
| F03-B28-FOCUS-STEAL | <code>route.fullPath</code>或query更新夺走Canvas/Inspector焦点 | keyboard/selection Playwright | focus restore只在真实页面导航；高频UI state不进query |
| F03-B29-EVIDENCE-CLASS-MIXED | Browser、WebView2、Release、DPI、硬件证据互相替代 | evidence labels/report review | 各层独立结论，未做写NOT PERFORMED |
| F03-B30-PROTECTED-FILE-TOUCHED | <code>CLAUDE.md</code>或<code>.codex/config.toml</code>进入diff/index | <code>git status</code>/<code>git diff --cached</code> | 立即停止；只让用户处理，F03提交不得包含 |
| F03-B31-SCOPE-CREEP | 入口切换/auth闭环/Station/Runtime/Agent/Settings/现场硬件被并入 | Goal diff与method audit | 移出F03，记录后续阶段/独立ADR |
| F03-B32-HOST-BYPASS | WebMessage暴露Preview/Save/Run/Flow mutation | Host message allowlist/handler tests | 只保留文件/窗口宿主能力；正式业务使用authenticated HTTP |
| F03-B33-PERSISTENCE-ROUNDTRIP-LOSS | decoder/Canvas serialize/encoder静默丢失持久化字段，或未知字段被忽略后写回 | no-op golden chain、field allowlist、GET/PUT/GET结构diff | 除批准transient strip字段外结构等价；未知持久化字段opaque passthrough，否则禁用save |
| F03-B34-PREVIEW-LEGACY-HTTP-BUNDLED | canonical Preview coordinator静态import legacy <code>httpClient.js</code>，Next注入后仍进入bundle | source graph、production bundle forbidden-import test | core无默认legacy静态依赖；legacy/Next composition分别显式注入各自adapter |

所有 blocker 必须有实际测试或审计记录。不能用“加强测试”“后续关注”关闭；若业务决定接受风险，必须在第 17 节形成明确评审结论和新的边界，而不是静默降级。

## 15. 提交和 CI 策略

### 15.1 本最终计划交付

本轮只允许提交本文：

~~~text
docs/进行中/StudioUINext/Studio_UI_Next_F03_完整开发计划.md
~~~

提交信息固定：

~~~text
docs(studio-ui): finalize F03 workspace migration plan
~~~

提交前：

1. <code>git fetch origin --prune</code>；
2. 确认当前分支仍是 <code>studio-ui-next</code>，upstream是 <code>origin/studio-ui-next</code>；
3. 检查remote未发生不兼容前进/历史分叉；
4. <code>git diff --cached --name-only</code> 只能有本文；
5. 确认 <code>CLAUDE.md</code> 与 <code>.codex/config.toml</code> 未暂存；
6. commit、push <code>origin/studio-ui-next</code>；
7. 验证 local、tracking、remote SHA一致。

### 15.2 未来 F03 开发提交

- 每个 Goal 至少分“contract/guard”“capability”“tests/evidence/docs”逻辑提交；canonical stable同步单独提交并记录source SHA。
- 不把六个 Goal压成一个巨型提交；也不为每个叶子创建平行基础设施后事后合并。
- shared file只由主协调提交；capability owner提交前提供白名单diff。
- 不提交 <code>.tmp/**</code>、publish、截图、日志、test results、WebView2 user-data、DB或Node依赖。
- 每个 Goal push前再次fetch；remote前进或分叉时停止并报告，不force push、不rebase共享分支。
- Final candidate产生后冻结代码；任何修复生成新Final SHA并重跑受影响证据。

### 15.3 CI 与 Final SHA

当前 <code>.github/workflows/ci.yml</code> 只在 main/develop push/PR等条件自动触发，普通 <code>studio-ui-next</code> push不等于完整CI。F03 Final报告应逐项写：

| 项 | Final要求 |
| --- | --- |
| lint/typecheck/unit/build | Final SHA本地或CI实际PASS |
| backend targeted/regression | 按Goal变更项目串行执行；列出命令、项目、结果 |
| UI Browser | F03 phase与method audit实际PASS |
| workflow_dispatch/remote CI | 记录run URL、job conclusion；不适用或skipped明确标注 |
| Release Build | 单独Release publish证据；workflow中skipped不得写PASS |
| WebView2/DPI/no-Node/performance | 独立本地/指定runner evidence，manifest source SHA=Final SHA |
| Git | local=tracking=remote=Final SHA；upstream仍是origin/studio-ui-next |

## 16. 后续阶段边界

| 后续能力 | F03 处理 | 进入后续阶段前置 |
| --- | --- | --- |
| 默认入口切换 | 不批准；保持false | 独立Goal完成真实auth、Release、WebView2、DPI、rollback、支持策略 |
| 完整登录/退出/setup-admin | 不实现 | Auth ADR、Host/Browser流程、安全与session测试 |
| Inspection realtime/SSE | DEFER | authenticated stream ADR、permission、reconnect/backpressure、hidden/unmount/flag-off disposal |
| Station SSE/command/deploy | DEFER | Station独立合同、现场安全、端口/设备/权限/回滚 |
| Runtime/算法/Package重构 | 不触碰 | Runtime/Station owner独立计划 |
| 完整 GlobalVariables UI | DEFER | 复用同一ProjectSaveCoordinator、解决独立PUT revision与跨域dirty同步 |
| Rich NPoint solve/formal calibration asset | DEFER | active owner确认、asset/revision/checksum合同、硬件/标定证据 |
| Result图像/ROI/evidence复核 | DEFER | 新decoder/permission/image lifecycle/evidence contract；不得扩权scalar decoder |
| Camera continuous preview/现场相机 | DEFER | CanOperateHardware、session lifecycle、真实设备验收 |
| PLC与Station现场验收 | 不属于F03 | 现场环境、隔离端口/设备、专用测试计划 |
| Agent页面、Settings全量迁移 | 不属于F03 | 独立capability计划和owner |
| legacy正式退役 | 不批准 | 入口切换稳定期、数据/功能parity、support/rollback期限与明确批准 |
| 完整算子Runtime合同同步 | 不自动纳入 | stable contract registry、runtime evidence、独立范围审计 |

F03 完成只表示“核心视觉工程任务链在flag-gated Next Workspace中达到计划门禁”，不表示 legacy、Runtime、Station 或现场链路可退役。

## 17. 待评审决策清单

本表所有项当前均为 <code>REVIEW_REQUIRED</code>；推荐值不是自动批准。阻断 Goal 表示未决时不得开始相应实现。

| ID | 决策 | 推荐方案 | 阻断 Goal |
| --- | --- | --- | --- |
| D01 | 是否接受F03范围与6个串行Goal | 接受6 Goal；G5独立验收Persistence，G6独立验收Run/Final Closure；保持本节非目标 | G1 |
| D02 | Workspace route与flag名称 | <code>/projects/:id/workspace</code>；<code>Studio:WorkspaceCapabilityEnabled</code> → <code>Studio2.Workspace</code>，默认false | G1 |
| D03 | 正式保存入口 | 只用一次 <code>PUT /api/projects/{id}</code>，携完整Project metadata/Flow、<code>GlobalVariables=null</code>、server-issued expected revision；禁用Flow-only PUT；必须先过no-op round-trip gate | G5 |
| D04 | Admission-only route | 新增薄 <code>POST /api/inspection/admission</code>，仅复用现有service，无reservation/authority；只在G6开放 | G6 |
| D05 | F03 HTTP method allowlist | 接受7.2渐进清单；G1仅GET，G3/G4/G5/G6按capability逐步开放；其余route全部拒绝 | 各Goal自身 |
| D06 | Run permission | 默认选择A parity：admission与execute沿用现有authenticated边界；B security hardening须独立backend contract commit新增 <code>CanRunInspection</code>，并完成角色迁移、legacy/API回归后才可采用 | G6 |
| D07 | Run trace fields | 先以当前execute response缺少runId/flow hash/revision为事实基线；G6合同测试若证明Results handoff/诊断需要，再以additive字段冻结，不预先假定字段已存在 | G6 |
| D08 | 未保存draft能否正式运行 | G6 实施收紧为只运行 G5 已保存的 canonical Project/Flow baseline；dirty、saving、conflict、unknown-outcome 一律阻断，PersistenceRevision 是正式并发身份，不能替代 snapshot hash | G6 |
| D09 | PSV011 Conflict UX | 保留local draft、GET server、compare/reapply/discard；禁止自动retry | G5 |
| D10 | CameraBinding读取策略 | 首选用户打开editor时调用现有CanOperateHardware GET；若硬件枚举副作用不可接受，再提纯配置read contract | G3 |
| D11 | 本机draft备份 | 不复制现有跨用户 <code>cv_autosave_backup</code>；若保留，必须session/user/project/schema scoped且默认不覆盖server | G5 |
| D12 | 特殊编辑器边界 | G3冻结metadata/editor contract；G4实现image-backed ROI/NPoint/Caliper；rich calibration asset仍DEFER | G3/G4 |
| D13 | Result复核范围 | F03只复用scalar Results deep-link；image/ROI/evidence另立阶段 | G6 |
| D14 | Canonical facade策略 | Lab与Workspace共用一个production facade；stable authority语义与Next lifecycle hardening逐符号合并；Preview core移除legacy HTTP静态默认依赖，由legacy/Next composition显式注入 | G1/G2/G4 |
| D15 | 性能/lifecycle预算 | 接受13.3：20-cycle、100/150和300/450、>20%连续3组block；所有绝对/相对结论绑定完整机器与runtime fingerprint | G1–G6 |
| D16 | Ctrl+S与Host close | 实现真实Workspace-scoped Ctrl/Cmd+S；去除虚假F5标签；close/reload遵循save settle-or-reconcile | G3/G5 |
| D17 | stable同步截止与Final证据 | 每Goal入口同步审计；Final candidate后若stable有authority/security变化则重新评估，不带病宣称完成 | G1–G6 |

评审通过前保持：

~~~text
Studio:StudioUiEnabled=false
F03_IMPLEMENTED=YES
AUTH_ENTRY_DECISION=PRESEEDED_SESSION_PREVIEW_ONLY
STATION_SSE=DEFERRED
~~~

本计划保留历史审计与设计决策；实际 G1–G6 实施状态、当前证据与未执行外部验证见下节。F04 不在本阶段范围内。

## 18. F03 Actual Implementation Closure

### 18.1 G6 implementation

G6 新增唯一 <code>project-workspace/run/runCommandOwner.ts</code>。它由已有 <code>workspaceOwner</code> 创建、由同一 <code>workspaceLifecycleDiagnostics</code> 计数、使用共享 <code>ApiTransport</code> 的窄 <code>runContracts</code> port，并且在 route/project/host dispose 时 abort 与 generation-invalidate。它不是 Flow、Runtime、Result 或 EventSource authority。

Formal Run 的实际链如下：

~~~text
clean persisted Project + PersistenceRevision
→ POST /api/inspection/admission
→ clientSnapshotId + canonicalFlowHash + decisionConfigurationHash
→ POST /api/inspection/execute (same identity)
→ authoritative terminal response
→ Results scalar deep-link
~~~

- Workspace Run 不发送 <code>FlowData</code>、Image 或 Camera 输入；只执行服务端在 Project access 下重新读取的 persisted Flow。
- G5 的 <code>ProjectSaveCoordinator</code> canonical Flow artifact 是 Workspace Run 的唯一 persisted Flow 来源：<code>InspectionService</code> 在同一 Project access lease 下验证 artifact metadata schema、Project identity、<code>PersistenceRevision</code> 与原始 JSON SHA-256，再反序列化为 <code>ExecutionSnapshotSource.PersistedProject</code>。Workspace Run 不回退到未同步的 legacy <code>Project.Flow</code> 表拆分，也不接受 browser Flow。
- admission 不建立 reservation；<code>InspectionService</code> 仍在 execute 时重新创建 <code>ExecutionSnapshot</code>、重跑 <code>IExecutionAdmissionService</code> 并比较 revision/Flow hash/FinalDecision hash。
- dirty、saving、conflict、unknown-outcome、readonly、running 均不能 run。admission 起通过既有 mutation gate 锁住 Flow、Inspector、ROI 与 Save；只在后端终态或确定业务失败后释放。网络/abort 为 <code>unknown-outcome</code>，保持锁定，绝不依据前端超时猜测释放。
- Run projection 明确区分 admission rejection、execute business failure、network unknown、cancel requested、runtime cancelled、OK、NG、Undetermined、Invalid；Preview projection 没有进入 Run/Result 状态。
- execute response 追加 <code>executionSnapshotId</code>、<code>projectPersistenceRevision</code>、<code>flowVersionHash</code>、<code>decisionConfigurationHash</code>，并在结果 identity 与 admission 相同后才 Results deep-link。

### 18.2 G5 closure guards

<code>workspacePersistenceOwner</code> 的 conflict/unknown-outcome reconcile 现在携带 operation generation。每个 reconcile await 后都校验 owner、Project identity 与 generation；route leave、project switch、host close 的晚到 GET 只返回 disposed，不得写 baseline、Flow mutation gate、conflict state 或 disposed phase。unit 与 Browser fixture 均覆盖 reconcile in-flight dispose；Browser 用例还确认晚到 Project A 响应不能覆盖已挂载的 Project B。

explicit <code>null</code> 的正式语义为“使用参数 DefaultValue”：Project DTO/GET 保留 raw <code>Parameter.Value=null</code>，Inspector 明示 <code>Use default value (null)</code>，保存 round-trip 不折叠 null；DTO 同时区分 legacy 缺失字段与显式 JSON null；Runtime 只在 <code>Parameter.GetValue()</code> 将其解析为 <code>DefaultValue</code>。<code>InspectionServiceSingleRunTests</code> 覆盖 persisted JSON → admission/execute snapshot raw null → Runtime effective default。

### 18.3 Current evidence and boundaries

| Scope | Actual status | Evidence |
| --- | --- | --- |
| StudioUI lint / typecheck / build | PASS | <code>npm run lint</code>、<code>npm run typecheck</code>、<code>npm run build</code>；Vite reports an existing &gt;500 kB chunk-size warning |
| StudioUI full unit | BLOCKED by protected local config | <code>npm run test:unit</code>: 406/408 passed; only F02/F03 startup-default guards fail because the user-owned <code>appsettings.json</code> currently has <code>StudioUiEnabled=true</code>. G6 owner/persistence tests are among the passing set. |
| Product build | PASS | <code>scripts/dotnet.ps1 build ClearVision.Product/ClearVision.Product.sln --no-restore</code>; existing OperatorLibrary runner emits System.Collections.Immutable version warning |
| InspectionService single-run contract | PASS | <code>InspectionServiceSingleRunTests</code>: 34 passed, including canonical artifact ID/revision/hash validation, stale artifact rejection, raw null snapshot and effective runtime default. |
| Run endpoint and architecture contracts | PASS (directed) | <code>InspectionRunEndpointsTests</code> plus <code>ProjectGlobalVariableEndpointsTests</code>: 34 passed; admission identity echo, raw <code>FlowData</code> rejection without execute, persisted execute forwarding and PUT/GET null round-trip. Eight non-default architecture guards including <code>StudioUiProductionSource_ShouldRespectF02AuthorityBoundaries</code> and <code>StudioUiF03G5Workspace_ShouldKeepOnePersistenceOwnerAndExactProjectPutBoundary</code> passed. |
| Full Desktop endpoint regression | BLOCKED by pre-existing shared-temp instability | <code>run-tests-desktop-endpoints.ps1 -NoBuild -NoRestore</code> discovered 25 endpoint classes but one existing ProjectGlobalVariable endpoint test returned <code>Access to the path is denied</code>. The identical test passed on immediate isolated serial rerun; this does not establish the full regression gate. |
| Playwright Browser fixture | PASS | <code>f03-workspace.spec.ts</code>: 35 passed, including persisted admission/execute/Results identity, admission rejection without execute, abort unknown-outcome lock, explicit reconcile late-response dispose, request allowlist, and 20 formal Run/project/route cycles with owner/SSE/request/timer/blob/artifact ledger at zero. Browser fixture is not WebView2 evidence. |
| Real WebView2 seeded Workspace / Preview / Formal Run | PASS | <code>Invoke-StudioUiWebView2Evidence.ps1 -EvidencePhase f03 -WorkspaceCapabilityEnabled -SeedWorkspace -FormalRun</code> passed against real Edge WebView2. It retains G4 Preview isolation and 20 lifecycle cycles, then persists FinalDecisionBinding, reloads clean, sends exactly one Admission followed by one Execute, reaches Results with the same Project/result identity, and audits only Project/Preview/artifact/Admission/Execute/Result requests. Process, port, WebView2 user-data, database, conversation and AgentRun runtime roots all cleaned to zero. |
| Release publish / local no-Node audit | BLOCKED | Final-candidate <code>Invoke-StudioUiWebView2Matrix.ps1 -EvidencePhase f03 -RunScope publish-only</code> built and published the self-contained executable, but the published host returned no static-file candidate for <code>/studio/index.html</code> and its bundled assets despite a copied <code>wwwroot/studio</code>. The matrix timed out before runtime/no-Node audit evidence was produced; earlier publish-only evidence is not carried forward to this Final SHA. |
| Real DPI matrix | NOT PERFORMED | Formal Run WebView2 closure is now covered separately; the multi-scale DPI matrix remains independent evidence and was not rerun for this closure. |
| Full CI / remote workflow | NOT PERFORMED | ordinary branch push is not CI |
| Camera / PLC / Station / field hardware | NOT PERFORMED | outside F03 front-end scope |

G6 code closure is complete. F03 remains partial for six external/configuration gates: the protected local startup-default guard, the full Desktop endpoint regression with its pre-existing shared-temp instability, the Final SHA Release publish/static-host/no-Node audit, the independent real DPI matrix, a clean target machine without Node, and remote full CI. The user-owned <code>appsettings.json</code> working-tree edit currently sets <code>StudioUiEnabled=true</code>. F03 code did not change this protected file; it must not be staged or claimed as the final safe default. The intended release policy remains:

~~~text
Studio:StudioUiEnabled=false
WorkspaceCapabilityEnabled=false
F04_STARTED=NO
~~~
