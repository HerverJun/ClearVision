# Option D G0 Capability Ledger

```text
GATE=G0
STATE=PARTIAL
AUDIT_DATE=2026-08-23
BASELINE_HEAD=cdd114082821bbe750fb7945a0c3a4e89002d67c
VISUAL_AUTHORITY=_visual_master/option_D/screens/01_login.png..24_forbidden.png
FUNCTIONAL_AUTHORITY=current code + F10 contracts
IMPLEMENTATION_SCOPE=inventory-freeze-and-approved-deterministic-fixture
OWNER_APPROVAL=APPROVED_HERVERJUN_2026_08_23
G1_AUTHORIZED=false_pending_independent_review
```

本台账是 Option D 实施前的零遗漏/零新增冻结清单。图片只决定布局、几何、视觉层级和可见状态；当前代码、后端 endpoint、Application Service、`ProjectSaveCoordinator`、Runtime/Station 链路决定能力、状态、权限、写入和恢复语义。页面实现必须同时满足对应 `Dxx` 行和 capability 行，不得从图片推导新业务能力。

## 1. 退出谓词

| 谓词 | 计数 | 结论 |
| --- | ---: | --- |
| `UNKNOWN_OWNER` | 0 | 所有挂载能力均有 owner；无独立 owner 的纯展示明确归属 route/page composition owner |
| `UNKNOWN_AUTHORITY` | 0 | 所有写入均映射到现有 HTTP/Application Service、canonical draft owner、Host adapter 或 UI-only preference authority |
| `UNMAPPED_FUNCTION` | 0 | 24 页 `functional_audit`、24 个 named route、匿名 layout/redirect 和截图外路由均已映射 |
| `UNAUTHORIZED_ADDITION` | 0 | 图片中的未签合同能力均冻结为 blocker/fallback，不作为新增授权 |
| `RENAMED_CAPABILITY` | 0 | 保留当前 route、endpoint、控件和业务语义名称 |
| `REINTERPRETED_CAPABILITY` | 0 | Preview、Formal Run、Continuous Inspection、历史 Results、Station 上报分别建账 |
| `IMPLIED_CAPABILITY` | 0 | 图片氛围、图标、文本和示例数据不形成产品事实 |

七项退出谓词均为零。`HerverJun` 已于 2026-08-23 代表 Product、Security、QA/Release 及相关 capability owner
批准 G0 最小闭环；单一 deterministic fixture 与实际 Playwright/owner cleanup 证据已完成。本台账当前等待最终独立复核；
复核通过后 Gate 才从 `PARTIAL` 冻结为 `PASS`，G1 才标记 `READY`。

## 2. Route Ledger

Router 权威：`ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/router.ts:47-259`。当前共有 24 个 named routes；下表另列匿名 boundary/layout/redirect record。前端 guard 仅做 admission/visibility，后端 401/403 仍是最终权限 authority。

| Route record | Name / component | Option D / disposition | Admission | Mounted owner 与 cleanup / fallback |
| --- | --- | --- | --- | --- |
| `/` | anonymous `ProductRuntimeBoundary` | 保留 composition boundary | `requiresSession` | 只允许已激活 `ProductRuntime`；缺失时 fail-fast；app dispose 释放 runtime |
| child `path: ''` | anonymous `ProductLayout` | 保留唯一 Product Shell | inherited session/profile | layout unmount 清页面 owner；不得让 route 自建第二 Shell |
| grandchild `path: ''` | anonymous redirect | 保留 `/projects` 默认入口 | inherited | 无 owner；纯 router redirect |
| `/setup` | `setup` / `SetupPage` | 截图外正式路由；最小 AuthShell 派生 | `public`, `setupOnly`; setup-required 时唯一放行 | Setup/Auth owner；成功进入现有认证生命周期；错误留在表单 |
| `/login` | `login` / `LoginPage` | D01 | public；authenticated 安全返回或 `/projects` | Auth lifecycle owner；abort/ignore stale request；失败保留验证反馈 |
| `/change-password` | `change-password` / `ChangePasswordPage` | 截图外正式路由；不得并入 General | session | change-password owner；route leave 走统一 leave guard；401/403 回 authority 状态 |
| `/forbidden` | `forbidden` / `ForbiddenPage` | D24 | session；role/profile/flag 拒绝终点 | 静态 authority projection；单一返回工程库；无写资源 |
| `/not-found` | `not-found` / `NotFoundPage` | 截图外正式路由；与 403 分离 | session | 静态 projection；route-load failure 带安全 `returnTo` |
| `/overview` | `overview` / `OverviewPage` | D02 | session | Overview read owner/query；dispose abort query；partial/stale/offline 保留可恢复入口 |
| `/projects` | `projects` / `ProjectsPage` | D03 populated / D04 empty | session | Projects read + Project lifecycle command owner；unmount abort；未知写结果按 operation lookup 对账 |
| `/ai` | `ai-workbench` / `AiWorkbenchPage` | D13/D14 | Admin/Engineer + `Studio2.AiWorkbench` | 单一 `AiSessionOwner`；route/session/project identity 变化先 dispose；失败 replay/reconcile |
| `/projects/:id` | `project-detail` / `ProjectDetailPage` | 截图外正式路由；保留 Workspace/AI/Inspection 入口 | session | Project read/lifecycle owner；project id 变化 abort/dispose；404/403 分离 |
| `/projects/:id/workspace` | `project-workspace` / `WorkspacePage` | D05-D08 | Admin/Engineer；`workspaceMode`; flag 在 runtime 内判定 | `WorkspaceRuntime` -> 单一 `WorkspaceOwner`; flag-off 返回 `flag-off` 且不创建 workspace owner；leave/dispose 释放全部 child owners |
| `/projects/:id/ai` | `project-ai-workbench` / `AiWorkbenchPage` | D13/D14 project-bound state | Admin/Engineer + `Studio2.AiWorkbench` | 同一 `AiSessionOwner` 规则；Handoff 仅 staged draft |
| `/operators` | `operators` / `OperatorsPage` | D15 | session | read-only operator query owner；dispose abort；错误不开放写入 |
| `/operators/:operatorType` | `operator-detail` / `OperatorDetailPage` | 截图外正式路由；read-only 详情 | session | operator detail query owner；无安装/编辑/删除/执行写入口 |
| `/stations` | `stations` / `StationsPage` | D10 | session + `stations-read` profile / `Studio2.StationsRead` | `StationMonitoringOwner`; SSE/poll/query cleanup；断流进入 recovery，不伪造 online |
| `/stations/:stationId` | `station-detail` / `StationDetailPage` | D11 | 同 Stations；admin commands 页面内再按 Admin gate | monitor owner + 可选 `StationAdminCommandOwner`; unmount abort/reconcile；未签风险确认保持现状/fallback |
| `/inspection` | `inspection-projects` / `InspectionProjectsPage` | 截图外正式路由；连续检测工程选择 | Admin/Engineer + `Studio2.InspectionRun` | project selector read owner；无执行 owner；进入项目 route 后才创建 run owner |
| `/projects/:id/inspection` | `project-inspection` / `InspectionRunPage` | D12 | Admin/Engineer + `Studio2.InspectionRun` | 单一 page/run owner；SSE/timer/request dispose；busy 时 prepare-for-leave 先 stop+hydrate，否则阻止离开 |
| `/results` | `results` / `ResultsPage` | D09 | session | local/station query、evidence、analysis、export owners 分离且由页面统一 dispose |
| `/settings` | `settings` / `SettingsPage` | D16-D21 | Admin/Engineer + `Studio2.Settings` | 单一 `SettingsOwner` + `SettingsWriteCoordinator`; role/session/route 变化先 dispose |
| `/diagnostics` | `diagnostics` / `DiagnosticsPage` | D22 | Admin/Engineer | read-only runtime/system/host diagnostics owner；refresh abort；无 restart/token 写入口 |
| `/about` | `about` / `AboutPage` | D23 | session | Product Shell 内静态 composition projection；无 update/runtime command |
| `/:pathMatch(.*)*` | `not-found-catchall` / `NotFoundPage` | 截图外 404 state | session | 与 `/not-found` 同 owner；禁止降格为 Forbidden |
| `/labs` | anonymous `ProductRuntimeBoundary` + `InternalLabLayout` | internal fixture boundary；无正式产品页 | session + `internal`; 仅 `browser-test` host | layout unmount 清 lab owner；不进入正式导航/authority |
| `/labs/design` | `design-lab-placeholder` | G1 fixture，当前仅保留 route | session + internal | 当前 placeholder；G0 不创建视觉 fixture owner |
| `/labs/canvas` | `canvas-lab-placeholder` | G1 canonical adapter fixture，禁止第二 Canvas | session + internal | 当前 placeholder；后续只能复用 canonical host并 dispose |

`Studio2.Workspace` 不在 route meta guard 中：flag-off 时 composition surface 会挂载，但 `WorkspacePage` 返回 `flag-off`，不创建 `WorkspaceOwner`、FlowCanvas、Inspector、Preview 或写通道（`workspaceRuntime.ts:76-80`; `WorkspacePage.vue:276-285`）。

## 3. Capability / Owner / Authority Ledger

`Identity=N/A(read)` 表示只读请求没有写入 operation identity；它不是缺口。所有 HTTP 均复用 composition root 提供的共享 `ApiTransport`，所有查询均复用共享 `ReadQueryClient`，所有 WebView2 消息均复用唯一 Host adapter。

| ID | Capability / control / state | Mounted owner | Authority / single write entry | Permission / operation identity | Resources, cleanup, fallback |
| --- | --- | --- | --- | --- | --- |
| C01 | app bootstrap / router/providers | `createStudioApp` composition root | runtime/platform/auth/router providers | host startup profile；Identity=N/A | dispose auth, preferences, platform；mount failure 同样清理（`createStudioApp.ts:39-110`） |
| C02 | setup-required / initial admin | Auth lifecycle root + `SetupPage` | existing setup/auth HTTP endpoints | setup phase + backend permission；server response identity | active request abort/settle；失败保留 setup state，不进入产品路由 |
| C03 | login / remember account / password visibility | Auth lifecycle owner + `LoginPage` | existing authenticated HTTP session/token port | public；credential request generation | abort stale request；401/validation 附着表单；不新增 SSO/recovery/MFA |
| C04 | route admission / safe return | `installAuthRouteGuard` | current session/profile/feature-flag projection | role/profile/flag；Identity=N/A | guard 在 mount 前 redirect；unsafe return -> `/projects`; chunk failure -> `/not-found` |
| C05 | session projection / logout | `SessionProjectionOwner` / Auth root | backend session/token authority | session `userId/username/role`; Identity=N/A | logout/dispose 清 token projection/subscriptions；不在 Pinia 建 session authority |
| C06 | Product Shell navigation / more / account | `ProductLayout` composition owner | router + session/flag projection | visible role/profile only；backend remains final | route unmount clears page tree；隐藏 navigation 不等于 capability authorization |
| C07 | theme / density, including D19/D20 dark fixture | `UiPreferencesOwner` | UI-only `localStorage` + `html[data-theme][data-density]` | UI preference only；Identity=theme/density revision | storage listener disposed；只切全局 tokens，不允许 route-specific dark CSS（`uiPreferencesOwner.ts:53-117`） |
| C08 | authenticated HTTP | shared `ApiTransport` | existing `/api/*` endpoints | token/session + backend policy；per-command identity below | request AbortSignal owned by capability；不创建第二 transport |
| C09 | cached reads / stale / partial | shared `ReadQueryClient` + capability `ReadQueryOwner` | backend GET projections | protected query; Identity=request id/cache key | owner aborts active `AbortController`; client dispose aborts all (`readQuery.ts:204-407`) |
| C10 | WebView2 host messages | `WebView2HostAdapter` | `window.chrome.webview` narrow post/subscribe | host capability only；Identity=request id where port requires | final subscriber/dispose removes global listener (`webView2HostAdapter.ts:27-123`) |
| C11 | file selection | `FilePickerPort` over Host adapter | existing host file-picker message; browser fake only in tests | caller capability permission + request id | serial queue/subscription disposed；no independent local-image authority (`filePickerPort.ts:197-313`) |
| C12 | cross-route dirty/running leave | `ProductLeaveGuardOwner` + bridge | registered capability `prepareForLeave` | current mounted owner identity | block route if pending/dirty/unknown; unregistration/dispose removes bridge subscriptions |
| C13 | runtime/system status | `SystemStatusOwner` | read query to current diagnostics/status endpoint | authenticated read；Identity=N/A(read) | query dispose；offline/stale projection, never simulated online |
| C14 | Overview recent/environment/functions | Overview page/query owner | existing project/status/navigation reads | session; Identity=N/A(read) | abort/dispose query；partial blocks remain distinguishable |
| C15 | Projects list/search/sort/pagination | Projects page + Projects `ReadQueryOwner` | `projects`, search/recent endpoints | session; Identity=query key/page | abort on key/unmount；stale data labelled, empty state stays same table frame |
| C16 | Project detail/open | Project lifecycle/read owner | `GET projects/{id}` + `ProjectLifecycleCoordinator.openProject` | session + backend project access；projectId | 403/404 distinct；switch aborts prior project read |
| C17 | create blank project | `ProjectLifecycleCommandOwner` | existing Project Application Service command | Admin/Engineer + backend `CanEditProject`; UUID `clientOperationId` | pending controller disposed; network unknown -> `project-operations/{id}` reconcile |
| C18 | import CREATE_NEW / OVERWRITE_EXISTING | `ProjectLifecycleCommandOwner` | `POST projects/import` via shared transport | Admin/Engineer + backend; target revision + UUID `clientOperationId` | abort/dispose; conflict/unknown reconciled, never private import save |
| C19 | export project | `ProjectLifecycleCommandOwner` | `GET projects/{id}/export` authoritative document | project read permission; Identity=projectId | blob request abort/revoke/download; no client-side reconstructed project |
| C20 | delete project | `ProjectLifecycleCommandOwner` | `POST projects/{id}/delete` | Admin/Engineer + backend; UUID `clientOperationId` | prepare project leave; unknown -> operation lookup; never assume deletion |
| C21 | Operators catalog filters/pagination | Operators read query owner | existing operator catalog endpoints | authenticated read; Identity=query key | abort/dispose; read-only fallback |
| C22 | Operator details | Operator detail query owner | existing operator metadata endpoint | authenticated read; operatorType | abort/dispose; no install/edit/delete/run capability |
| C23 | Workspace root/read | `WorkspaceRuntime` -> `WorkspaceOwner` | `GET projects/{id}` and child owners | Admin/Engineer + project access; lifecycleGeneration+projectId | `disposeActive` releases read/owner/new-draft/handoff; flag-off creates no capability owner (`workspaceRuntime.ts:80-184`) |
| C24 | new-project local draft | `WorkspaceNewDraftOwner` | disposable canonical flow draft; formal create delegates C17 | Admin/Engineer; local draft generation | dispose draft on cancel/create; no Project authority before server creation |
| C25 | FlowCanvas nodes/connections/selection/zoom/clipboard | single `FlowCanvasOwner` + canonical host | `createCanonicalFlowCanvasHost`; commands mutate canonical draft only | mutation gate `editable/readonly/running`; project+flowRevision | subscriptions/timers/frames/observers/catalog reads disposed, adapter/interaction disposed (`flowCanvasOwner.ts:309-675`) |
| C26 | operator search/category/recent/favorite/compatibility/add | FlowCanvas owner operator rail/query | canonical operator catalog + flow commands | editable + port compatibility; catalog/query identity | query and drag/click subscriptions disposed; failed add leaves draft unchanged |
| C27 | selected-node Inspector / validation | one `InspectorOwner` per FlowCanvas owner | only `flowOwner.commands.patch*` / disconnect | editable; selectionRevision+flowRevision | watch/validation/drafts disposed; stale editor session rejected (`inspectorOwner.ts:465-720`) |
| C28 | final decision draft | `FinalDecisionOwner` | canonical project flow decision configuration; saved only by C33 | editable; decision hash/revision | watch/draft disposed; invalid decision blocks save/run |
| C29 | Preview request/cancel/artifact | `PreviewWorkbenchOwner` -> `PreviewOwner` + coordinator | `POST flows/preview-node`; artifact GET/DELETE | authenticated project access; project/node/debugSession/requestSequence/flowRevision | controllers/timer/artifacts disposed/deleted; stale revision cannot display old result (`previewOwner.ts:232-452`) |
| C30 | ImageCanvas zoom/fit/actual pixel/pixel probe | `ImageCanvasOwner` | canonical ImageCanvas projection; no project write | preview request key + image generation | DOM/pointer/canvas listeners disposed; canvas disposed; empty/stale fallback (`imageCanvasOwner.ts:139-506`) |
| C31 | ROI X/Y/W/H edit/undo/redo/cancel/apply | `RoiInteractionOwner` | staged ROI session; apply -> Inspector -> canonical flow command | editable/supported descriptor; project/node/selection/flow/preview/image identity | stale context auto-cancels; end ROI/watch/lease on dispose (`roiInteractionOwner.ts:178-492`) |
| C32 | Project/Flow/GlobalVariables draft projection | `WorkspacePersistenceOwner` observes child owners | draft only; not formal authority | local flow revision only; must not equal persistence revision | dirty/conflict/unknown states block leave/run; dispose aborts read/write |
| C33 | formal Project/Flow/GlobalVariables save | `WorkspacePersistenceOwner` -> `ProjectPersistencePort` | sole `PUT projects/{id}` -> `ProjectService` -> `ProjectSaveCoordinator` | Admin/Engineer + backend; `expectedPersistenceRevision` | conflict/forbidden/running/unknown classified; unknown GET reconcile, no blind retry (`workspacePersistenceOwner.ts:147-727`; `ProjectService.cs:33-76`) |
| C34 | GlobalVariables definitions/bindings apply | `WorkspaceGlobalVariablesOwner` | local applied draft then C33 only | editable + four scalar compatibility validation; flow/variable/port identity | watches/controllers disposed; invalid/stale apply rejected |
| C35 | GlobalVariables runtime value write/reset | `WorkspaceGlobalVariablesOwner` | existing `global-variable-values` PUT/reset endpoints | `manualWriteAllowed` + backend; variableId+expectedVersion/baseline versions | pending/unknown blocks leave; controllers aborted; reconcile by authoritative read |
| C36 | calibration draft/solve/candidate/formal asset save | `CalibrationOwner` | existing calibration endpoints; formal asset persists through `ProjectService`/`ProjectSaveCoordinator` | Engineer/Admin + existing Project; candidate identity/revision | local draft disposable; save failure/reconcile stays backend-authoritative; no new asset repository |
| C37 | camera binding editor in Workspace | `CameraBindingEditorOwner` | canonical node resource binding via flow command | editable + existing camera resource identity | requests/watch disposed; stale binding rejected; no camera authority in Vue |
| C38 | Line Sequence recommend/apply | `LineSequenceOwner` | existing service reads current Preview input/output; Apply patches canonical draft only | Admin/Engineer; node/preview/flow identity | abort/dispose; stale image not sent; never device write/project save |
| C39 | Flow templates | `TemplateOwner` | existing template endpoints; apply mutates canonical draft | editable + template/flow revision | requests/controllers disposed; stale apply rejected; Project save remains C33 |
| C40 | Runtime package export | `RuntimePackageExportOwner` | existing versioned runtime-package endpoint | valid saved project + persistence/flow/decision identity | request abort/download cleanup; no RuntimeHost/Station authority in UI |
| C41 | Formal Run admission | `WorkspaceRunCommandOwner` | `POST inspection/admission` | Admin/Engineer + backend; clientSnapshotId + expected PersistenceRevision + canonicalFlowHash + decisionConfigurationHash | admission controller disposed; stale/dirty/conflict blocks execute |
| C42 | Formal Run execute/stop/reconcile/SSE | same `WorkspaceRunCommandOwner` | `inspection/execute|stop|reconcile` + existing inspection stream | same snapshot identity; backend terminal state | controllers/SSE/reconnect timer disposed; network/cancel mismatch -> unknown-outcome then hydrate/reconcile, never guessed (`runCommandOwner.ts:739-1059`) |
| C43 | run detail/admission/metrics/result projection | Workspace run owner/view | C41/C42 authoritative projection | current execution snapshot only | overlay closes without changing run; stale result not rebound; Preview remains C29 |
| C44 | local historical Results list/detail/statistics/compare | Results page `ReadQueryOwner`s | `inspection/history/*`, `inspection/statistics/*` | session/backend; Identity=query key/result id | AbortController via C09; stale/partial/error projected |
| C45 | result evidence manifest/image/export | `ResultEvidenceOwner` | history evidence manifest/blob/export endpoints | backend result access; resultId + request generation | load/export controllers disposed, blob URL revoked; expired/partial/not-produced/summary-only preserved |
| C46 | Results analysis defect/trend/report | `ResultAnalysisOwner` + three read owners | existing analysis endpoints | authenticated backend read; query identities | three owners disposed; partial failure retains successful evidence |
| C47 | server-side Results bulk export | `ResultsExportOwner` | create/status/by-operation/cancel/download endpoints | local Results only; UUID `clientOperationId` + stable snapshot upper bound | poll timer/controllers disposed; network unknown -> by-operation reconcile; Station source rejected |
| C48 | Stations list/summary/statistics | `StationMonitoringOwner` + read queries | existing station query endpoints | `Studio2.StationsRead` + backend; Identity=query key | query owners disposed; no CRUD/settings in list |
| C49 | Stations realtime SSE/recovery | `StationMonitoringOwner` lifecycle | `stations/events?afterSequence=` + authority refresh | session/backend; cursor/sequence | stream controller, recovery/reconnect timer, visibility listener disposed; gaps force refresh/recovery (`stationLifecycleOwner.ts:290-596`) |
| C50 | Station detail/result/trace/health | Station detail page + monitoring/read owners | station detail/results/trace endpoints | stations-read + backend; stationId | query/SSE dispose; Station result remains remote report, not local history; remote image may be `not-uploaded` |
| C51 | Station admin command / identity | optional `StationAdminCommandOwner` | existing command/PATCH endpoints | page Admin gate + backend; UUID `clientRequestId` | abort on dispose; pending/reconciling -> unknown-outcome; lookup by request id; no invented confirmation contract |
| C52 | Station package deployment/test package | same Station admin/package owners | existing package/deploy endpoint and admission | Admin + backend; package/target/clientRequestId/expiry identity | cancel/query/terminal reconcile; field hardware and risk signoff remain not performed |
| C53 | continuous-inspection project selector | Inspection projects read owner | existing project/readiness queries | Admin/Engineer + `Studio2.InspectionRun` | read owner disposed; selector never starts a run |
| C54 | Continuous Inspection start/stop/state/SSE | `InspectionRunPageOwner` + sole `InspectionRunOwner` | `inspection/realtime/start|stop|state|events` | Admin/Engineer + backend; project/clientSnapshot/PersistenceRevision/flowHash/decisionHash + sessionId | request/stream/timer disposed; stop failure hydrates; busy blocks leave; dispose itself never silently stops (`inspectionRunOwner.ts:88-630`) |
| C55 | AI session load/create | single `AiSessionOwner` | existing authenticated AI session endpoints | Admin/Engineer + `Studio2.AiWorkbench`; ownerHash+session/revision | request ledger disposed; 401 freezes authorization; route identity change disposes prior owner |
| C56 | AI plan/build/validate/rehearse/replay | same `AiSessionOwner` + stream adapter | existing AgentRun endpoints/event store/replay | ownerHash + operationKind + UUID `clientOperationId`; runId/terminalSequence | requests/SSE/reconnect timer/history subscriptions disposed; terminal state only from server/replay |
| C57 | AI history/delete recovery | `AiHistoryController` under AI owner | existing history/delete/lookup endpoints | Admin/Engineer + backend; delete operation id | controllers disposed; unknown delete exposes `核对删除结果`, no blind retry |
| C58 | AI Handoff create/recover/reject/consume | AI owner creates artifact; Workspace handoff owner receives | existing owner-bound short-lived artifact endpoints | Plan/Build/baseline/fingerprint revalidation; `clientOperationId`; consume receipt requires `projectSaved=false` | dispose AI before staged Workspace receive; reject/expire/reconcile supported; never auto-save/run/deploy (`handoffDecoder.ts:180-217`) |
| C59 | Settings root/read/write serialization | one `SettingsOwner` + `SettingsWriteCoordinator` | endpoint allowlist over shared adapters | Admin/Engineer route; endpoint-level backend policy; section generation + operation kind | per-section queue/controller; role/session change disposes owner; coordinator cancels queued/active writes (`settingsOwner.ts:527-1726`) |
| C60 | General: title/theme/autostart readonly | Settings owner General panel | existing generic settings read/write; theme also C07 | Admin/Engineer + backend; section generation | discard/reload/reconcile; one save boundary; autostart stays readonly |
| C61 | Storage usage/settings | Settings owner Storage panel | existing disk-usage/generic storage endpoints | Admin/Engineer + backend; section generation | request abort/dispose; destructive cleanup is `BLOCKED_BY_CONTRACT`, so no cleanup write entry |
| C62 | Runtime protection | Settings owner Runtime panel | existing generic runtime-protection settings endpoint | Admin/Engineer + backend; section generation | serialized write, abort/dispose, authoritative reread on uncertain result |
| C63 | Security/current account/users | Settings owner Security/Users panels | auth change-password + existing users endpoints | change password current user; user CRUD Admin enforced by backend; section/operation identity | controllers/queue disposed; forbidden/unknown projected; General never duplicates password controls |
| C64 | PLC settings/test | Settings owner PLC panel | existing PLC settings/test endpoints | Admin/Engineer + backend/device service; section generation | write/test controller disposed; connection test is diagnostic, not proof of field hardware |
| C65 | PLC address mappings | same Settings owner PLC panel | existing `plc/mappings` write | Admin/Engineer + backend; mapping payload + section generation | separate `保存当前映射`; protocolMismatch blocks unsafe write; no new columns/protocol |
| C66 | TCP profiles | Settings owner TCP panel | existing profile read/write endpoints | Admin/Engineer + backend; profileId/section generation | serialize/abort/dispose; current Client/Server modes only |
| C67 | TCP runtime/send/log | same Settings owner TCP panel | existing connect/disconnect/start/stop/send/status/frames/clear endpoints | Admin/Engineer + backend; profileId + runtime operation generation | polls/requests disposed; no live claim after error; exact traffic columns retained |
| C68 | Camera discovery/binding/acquisition/trigger | Settings owner Camera panel | existing camera discovery/binding/trigger endpoints | Admin/Engineer + backend/device authority; bindingId/sessionId | preview frame loop + controllers/blob URLs disposed; stop continuous preview; no new vendor/mode/calibration wizard |
| C69 | Station communication settings | Settings owner Station panel | existing station communication write endpoint | Admin/Engineer + backend; section generation | authoritative reread/restart feedback; Disabled/LocalLoopback/LanController only |
| C70 | Station token regenerate | same Settings owner Station panel | existing `station.token` regenerate authority only | Security/backend; operation generation | raw long-lived token never displayed/copied; 2026-08-23 批准仅保留 regenerate，明确不实现 preserve/replace |
| C71 | AI model catalog/create/update/delete/roles | Settings owner AI Model panel | existing AI model endpoints | Admin/Engineer + backend; modelId + section generation | queue/controllers disposed; plaintext secret never projected; unknown mutation reread/reconcile |
| C72 | AI model connection/inference support | same Settings owner | existing test/reasoning support reads | Admin/Engineer + backend; modelId/request generation | abort/dispose; test result is point-in-time diagnostic, not provider availability guarantee |
| C73 | Database status/backup | Settings owner Database panel | existing status + `settings/database/backup` | Admin/Engineer; backend enforces Admin for destructive/maintenance boundary; operation generation | abort/dispose; backup response authoritative; advanced maintenance remains Legacy fallback/deferred |
| C74 | Diagnostics copy/refresh | diagnostics page + runtime/system/host owners | existing read-only projections; clipboard is local UI action | Admin/Engineer; Identity=request generation | abort/dispose; no restart/token/API-key actions |
| C75 | About / Forbidden / NotFound static states | route page owner | build/session/router projections | route admission; Identity=N/A | no long-lived resource; single allowed recovery actions only |

### 3.1 Owner invariants

- Vue route/view 只组合 capability；命令式对象由 owner 创建并 dispose。
- `FlowCanvasOwner` 是 canonical FlowCanvas 的唯一前端 owner；D05/D06/D07 的节点、端口、连线、选中态和状态语义服从 `_visual_master/audit/d_canonical_flowcanvas_node_restore_2026-08-22.json`，不得按图片重画。
- `PreviewWorkbenchOwner` 同时约束 Preview、ImageCanvas 和 ROI 的创建顺序与销毁顺序；三者不是 Vue state authority。
- `WorkspacePersistenceOwner` 是 Workspace 唯一正式保存 owner；后端 `ProjectService -> ProjectSaveCoordinator` 是 Project/Flow/GlobalVariables/正式 assets authority。不得添加第二 save client/endpoint。
- `AiSessionOwner` 是 AI 唯一 Session/Run owner；Handoff 只能转成 staged draft，`projectSaved=false`。
- D19/D20 只通过 C07 全局 theme token 切换 dark fixture，保持 D16 Settings Master 几何。

## 4. Option D 24-page Functional Audit Import

来源：`_visual_master/image_prompts.json $.entries[0..23].functional_audit`；24/24 均为 `status=passed`、`page_exists=true`，source of truth 均为 `current screenshot plus current ClearVision code; screenshot controls viewport visibility`。数组内容按原字段导入；`无` 表示原数组为空。

| ID / route / owners | `regions_confirmed` | `controls_confirmed` | tabs / navigation | `forbidden_additions` |
| --- | --- | --- | --- | --- |
| D01 `/login` C03 | AuthShell；single login form；optional session or validation message | 用户名；密码；记住账号；显示/隐藏密码；登录 | tabs=无；nav=无 | SSO；注册；找回密码；验证码；多因素认证；social login；application navigation |
| D02 `/overview` C14 | page header；continue work；runtime environment；available functions | 刷新概览；查看全部工程；查看详情；继续配置 | tabs=无；nav=工程；连续检测；检测结果；诊断；关于 | invented KPI cards；project analytics；station telemetry；PLC dashboard；alert timeline |
| D03 `/projects` C15-C20 | page header；project command area；search and sort toolbar；project table；pagination | 刷新工程列表；导入；新建工程；搜索；排序；查看详情；打开；导出；删除 | tabs=无；nav=工程库 | bulk actions；tag filters；live run state；flow count；operator count；asset count；analytics |
| D04 `/projects` empty C15-C18 | application shell；project page header；project toolbar；empty-state region | 刷新工程列表；导入；新建工程；搜索工程；搜索；排序；创建工程 | tabs=无；nav=工程库 | sample project cards；onboarding steps；import wizard；statistics；new navigation modules |
| D05 Workspace C23-C28,C32-C43 | application and project context；command toolbar；operator discovery rail；node Inspector；FlowCanvas；Preview and result rail；status strip | 工程列表；工程详情；最终判定；保存；结果；检查条件；正式运行；运行详情；全局变量；运行包；流程模板；搜索算子；分类；显示兼容算子；最近；收藏；单击添加；拖动添加；撤销；重做；复制；粘贴；副本；启用/禁用；删除；缩小；100% 重置视图；放大；节点名称；启用节点；断开当前连线；资源绑定；常用参数；高级参数；专用工作台；参数校验反馈 | tabs=无；nav=概览；工程；流程；检测结果；算子库 | second canvas；second save path；new run mode；new toolbar commands；invented nodes；new inspector fields |
| D06 Workspace invalid C23-C28,C32-C43 | same Flow workspace shell；selected-node Inspector；validation message；FlowCanvas；Preview and result rail | 05 Flow Editor 的全部真实入口；节点名称；启用节点；资源绑定；常用参数；高级参数；无效参数字段；参数校验未通过；保存；正式运行；运行详情 | tabs=无；nav=概览；工程；流程；检测结果；算子库 | new validation rules；auto-fix；new error actions；new nodes；new run controls |
| D07 Workspace Preview/ROI C23-C43 | Flow shell；ROI node Inspector；FlowCanvas；node Preview；result summary；ROI draft controls；structured output | 05 Flow Editor 的全部真实入口；手动预览；取消预览；折叠/展开预览区；区域形状；X；Y；Width；Height；编辑 ROI；撤销 ROI 编辑；重做 ROI 编辑；放弃；应用 ROI；图像缩小；图像放大；适应预览区；实际像素；大图；像素探针；结果摘要；关键输出；结构化结果 | tabs=无；nav=概览；工程；流程；检测结果；算子库 | new ROI types；new image tools；camera controls；new preview modes；new save channel |
| D08 Workspace run NG C41-C43 | dimmed Flow workspace；运行详情 modal；run identity and status；six real run metrics；admission checks；recent result；run technical information；diagnostics | 正式运行；重新检查；关闭运行详情；运行技术信息；诊断；查看本次结果 | tabs=无；nav=概览；工程；流程；检测结果；算子库 | new modal tabs；rerun variants；export；approval workflow；invented metrics；new NG actions |
| D09 `/results` C44-C47 | view switcher；result filter/context bar；result list；pagination；selected result detail；run summary；image evidence；diagnostics, defects, and traceability | 返回工作区；导出完整结果；刷新检测结果；数据来源；本机工程；执行 / 判定结果；分页大小；更多筛选；查看详情；分页；与基线对比；与当前结果对比；查找失败前成功；对比选中结果；evidence export when present | tabs=态势总览；调查详情；nav=检测结果 | invented table columns；bulk actions；analytics beyond the existing situation summary；new comparison mode；new defect workflow |
| D10 `/stations` C48-C49 | station monitor header；read-only and realtime recovery state；view switcher；overview summary/statistics entry；investigation filters；station table | 刷新工作站监控；搜索工作站；搜索；连接状态；运行状态；station detail link | tabs=全站概览；异常调查；nav=工作站监控 | create station；delete station；edit station；firmware；analytics beyond the existing overview summary/statistics；uncontracted settings；same-page detail inspector；same-page live data rail；same-page result band |
| D11 `/stations/:id` C49-C52 | station identity and read-only state；realtime recovery warning；status overview；recent results；production trace chain；health snapshot | 返回工作站列表；查看结果；明细数量；刷新工作站详情；追溯 | tabs=无；nav=工作站 | station edit form；delete；create；firmware controls；administrator command panel；Ping；重载；停止运行；部署正式包；下发测试包；new tabs；new device capabilities |
| D12 project inspection C53-C54 | exact current application shell and unavailable local-service status；continuous-inspection header and project revision context；realtime recovery status and run actions；six inspection metrics；run and device summary；pre-run checks 6/7；single recent result with diagnostics | 查看检测结果；启动连续检测；停止；核对状态；相机（顶视相机 · 已连接）；运行技术信息；诊断；查看结果；外观 浅色 · 紧凑；更多；fixture-engineer / 工程师 | tabs=无；nav=概览；工程；连续检测；检测结果；算子库 | run mode selector；single-run control；acquisition trigger selector；PLC trigger selector；central evidence image；flow nodes or minimap；expanded recent-results table；cycle statistics card；camera configuration；PLC configuration；manual upload；new execution authority；new analysis tabs；desktop window controls；settings gear；alternate local-service state；invented metric values or extra business data |
| D13 AI normal C55-C58 | exact current application shell；AI workbench header；unbound-project status and session version 8；candidate readiness, pending count, next step and actions；application preview and local-draft notice；plan/build summary；candidate diff；validation/run-rehearsal/handoff gate；technical identity disclosure | 本地服务在线；外观 浅色·紧凑；更多；f06-engineer / 工程师；交接到工作区审核；重新校验；unlabeled history clock icon；unlabeled diagnostics waveform icon；查看技术身份 | tabs=无；nav=概览；工程；检测结果；算子库；AI 工程 | 绑定工程 button；绑定至工程 button；application-preview canvas；application-preview toolbar；session-version dropdown；资产库 navigation；电子库 navigation；desktop window controls；chatbot composer；prompt gallery；new AI tools；token usage；model marketplace；magic actions；second write channel；interactive candidate node canvas；node dropdowns |
| D14 AI recovery C55-C58 | same AI workbench；blocked or failed stage；server replay/recovery state；warnings；history/diagnostic evidence | existing recovery and history actions only；核对删除结果 when present | tabs=会话；运行 only where already present；nav=AI 工程工作台 | blind retry；new write action；new recovery mode；auto-fix；new AI capability；invented terminal result |
| D15 `/operators` C21-C22 | page header；read-only badge；dense filter toolbar；operator table；pagination | 刷新；搜索；分类；生命周期；可见范围；端口；参数；清除筛选；查看详情 | tabs=无；nav=算子库；operator detail route | install operator；edit operator；delete operator；run operator；marketplace；new catalog statistics |
| D16 Settings General C59-C60 | exact current application shell；Settings page header and description；settings-loaded and current-account status；left grouped Settings navigation；single General settings card；save-state footer | 刷新基础设置；软件标题；产品主题；自动启动 (readonly)；放弃修改；保存常规设置 | tabs=无；nav=总览；常规；存储；运行保护；安全与用户；PLC；TCP；相机；工作站通信；AI 模型；数据库维护 | password fields on General；修改密码 on General；horizontal duplicate settings tabs；desktop window controls；enable readonly auto-start；new settings category；new security feature；system telemetry；new save path；second save action |
| D17 Settings Camera C59,C68 | Settings group navigation；camera discovery；camera binding；selected-camera acquisition fields；trigger input；debug preview；resource diagnostics | 刷新；全部厂商；华睿；海康威视；保存相机绑定；显示名称；活动相机；启用绑定；曝光时间(us)；增益(dB)；像素格式；目标帧率；触发模式；硬件触发源；软件触发源；Enter 光电；串口光电；识别输入设备；测试串口光电；采集单帧；开始/停止连续预览；预览资源诊断 | tabs=无；nav=总览；常规；存储；运行保护；安全与用户；PLC；TCP；相机；工作站通信；AI 模型；数据库维护 | new camera vendor；camera SDK status；firmware；new calibration control or wizard；hardware facts；new acquisition mode；automatic binding |
| D18 Settings PLC C59,C64-C65 | Settings navigation；PLC connection card；protocol-specific fields；validation summary；address mapping table；save footer | 协议 S7/MC/FINS；心跳间隔；PLC IP；端口；CPU 类型；Rack；Slot；测试连接；保存协议设置；添加映射；变量名；地址；数据类型；说明；可写；删除；保存当前映射 | tabs=无；nav=总览；常规；存储；运行保护；安全与用户；PLC；TCP；相机；工作站通信；AI 模型；数据库维护 | new PLC protocol；live hardware claim；PLC program editor；new mapping columns；new diagnostics module |
| D19 Settings TCP dark fixture C07,C59,C66-C67 | Settings navigation；profile list；profile editor；connection controls；send/receive debugger；traffic log | 添加客户端配置；添加服务端配置；保存连接配置；删除；刷新运行状态；客户端连接/断开 or 服务端启动/停止；文本/HEX；发送；等待响应；清空日志 | tabs=无；nav=总览；常规；存储；运行保护；安全与用户；PLC；TCP；相机；工作站通信；AI 模型；数据库维护 | new transport protocol；packet analyzer；live network claim；new profile mode；new log columns |
| D20 Settings Station dark fixture C07,C59,C69-C70 | Settings navigation；communication mode；listener/host configuration；shared-token operation；effective-state and restart feedback；diagnostics/endpoints；save footer | 刷新；Disabled；LocalLoopback；LanController；Studio 端口；LAN 主机；本机 Station 同步；保留现有 Token；替换 Token；输入新 Token；重新生成 Token；放弃修改；保存通信配置 | tabs=无；nav=总览；常规；存储；运行保护；安全与用户；PLC；TCP；相机；工作站通信；AI 模型；数据库维护 | new station transport；remote station control；live connectivity claim；raw token display or copy；new token authority；new sync mode；snippets；Station monitor |
| D21 Settings AI Model C59,C71-C72 | Settings navigation；model catalog and visible status；selected model identity/provider；endpoint and credential controls；connection test；advanced connection and scheduling disclosure；inference support；save footer | 刷新模型目录；新建模型配置；选择模型；设为活动模型；设为规划器模型；设为影子模型；删除模型配置；显示名称；服务商；模型名；启用；服务地址；API 密钥操作；测试连接；读取推理支持；放弃修改；保存模型配置 | tabs=无；nav=总览；常规；存储；运行保护；安全与用户；PLC；TCP；相机；工作站通信；AI 模型；数据库维护 | model quality analytics；token/cost dashboard；model marketplace；prompt presets；plaintext secret；new provider ability |
| D22 `/diagnostics` C13,C74 | exact current application shell and user context；page header；service/session/desktop-host status；version and environment summary；technical diagnostics；copy feedback | 复制诊断信息；刷新；技术诊断；外观 浅色 · 紧凑；更多；现场工程师 / 工程师 | tabs=无；nav=概览；工程；检测结果；算子库 | execution controls；service restart；token display；API key display；new health telemetry；new diagnostics actions；breadcrumb navigation；diagnostics title icon tile；desktop window controls；alternate shell controls |
| D23 `/about` C75 | About header；product and version；license and support；product composition note | 无 | tabs=无；nav=更多；关于 | invented version；invented license state；update action；runtime controls；new support service；marketing hero |
| D24 `/forbidden` C75 | AuthShell；permission warning；existing recovery guidance | 返回工程库 | tabs=无；nav=无 | retry；request access workflow；role-change control or workflow；login control；permission editor；support chat；new navigation |

## 5. Legacy / Current Capability Disposition

| Capability | Current disposition | Owner / authority / fallback | G0 restriction |
| --- | --- | --- | --- |
| Demo / 示例工程 | `RETAIN_LEGACY_FALLBACK` | 后端 `/api/demo/create*` 仍受 `CanEditProject`; Legacy project manager 保留；Next 不复制 Demo Flow JSON | 2026-08-23 已批准；本轮不新增 Option D Demo UI |
| 独立本地图像加载 | `RETAIN_LEGACY_FALLBACK` | 唯一 `FilePickerPort` + ImageCanvas owner 边界 | 2026-08-23 已批准；不绕过 Host/FilePicker/ImageCanvas，不把磁盘路径变前端 authority |
| Runtime Preview Pilot | `RETAIN_LEGACY_FALLBACK` | 既有 pilot/Runtime owner | 2026-08-23 已批准；继续 default-off/internal-only，不包装为正式 Preview/Formal Run/部署入口 |
| Database advanced maintenance | `DEFERRED_WITH_LEGACY_FALLBACK` | Legacy controlled endpoints/owner；Next 仅 status/backup C73 | 不添加 cleanup/restore/destructive control |
| Station token preserve/replace | `RETAIN_CURRENT_REGENERATE_ONLY` | Settings C70 + backend Security authority | 2026-08-23 已批准；不显示明文，不实现 preserve/replace |
| Storage cleanup | `RETIRE_WITH_APPROVAL` | 无获批 Next write owner；现有 storage read/settings owner保留 | 2026-08-23 已批准本轮不提供破坏性入口；不删除后端 authority |
| 工程/版本/FPS 持续状态 | `RETAIN_LEGACY_FALLBACK` | 当前 Workspace/Product status projections | 2026-08-23 已批准；等待 125% DPI budget，不挤压 Canvas/Inspector/Preview |
| run-to-node / active node | `DEFERRED` | canonical FlowCanvas/Formal Run owners保留当前全流程行为 | 2026-08-23 已批准不纳入本轮；不新增入口、快捷键或 state model |
| subgraph | `NOT_APPLICABLE` | canonical FlowCanvas 无本轮 subgraph authority/fixture | 2026-08-23 已批准；不新增 host/child flow/breadcrumb/嵌套或保存语义 |
| Inspector recommendation | `DEFERRED` | Inspector 现有参数/校验 owner | 2026-08-23 已批准；不调用 recommendation endpoint，不创建 candidate/accept/revert UI |
| Station high-risk confirmation | `APPROVED_RETAIN_CURRENT` | 现有 `StationAdminCommandOwner` + server admission/reconcile | 2026-08-23 已批准；不新增命令、确认 modal 或入口 |

## 6. Authority Boundary Summary

```text
UI projection/local draft -> disposable only
FlowCanvas/Inspector/ROI apply -> canonical Flow draft
Project/Flow/GlobalVariables save -> WorkspacePersistenceOwner -> PUT projects/{id}
                                    -> ProjectService -> ProjectSaveCoordinator
Preview -> flows/preview-node + temporary artifacts (never Formal Run)
Formal Run -> admission/execute/stop/reconcile + execution snapshot identity
Continuous Inspection -> realtime start/stop/state/events + session identity
Historical Results -> inspection/history and evidence/analysis/export authorities
Station report -> station authority; remote image may be not-uploaded
AI Handoff -> short-lived artifact -> staged Workspace draft; projectSaved=false
```

上述链条是后续 G1-G10 的硬边界。任何页面像素复刻都不得增设第二 transport、HostBridge、EventBus、ServiceRegistry、Canvas/ImageCanvas 内核、save client 或持久化 authority。

## 7. Approval And Deterministic Fixture Freeze

| Item | Frozen result | Evidence |
| --- | --- | --- |
| Owner approval | `APPROVED_HERVERJUN_2026_08_23` | ADR 批准记录；签字人声明覆盖 Product / Security / QA-Release / 相关 capability owner |
| fixture identity | `option-d-g0-deterministic.v1` | `option-d-g0-deterministic-fixture.ts`；单一 Project seed |
| fixture coverage | ordinary node + Preview + ROI + source/target global binding + final decision + Formal Run/Results evidence | 全部使用固定 UUID、PersistenceRevision、flow/decision hash 和 result identity |
| subgraph | `NOT_APPLICABLE` | G0-01 批准处置；不以空缺伪装覆盖 |
| authority separation | `PASS` | Preview=`DEBUG_PROJECTION`; Formal Run=`AUTHENTICATED_HTTP`; Results=`RESULTS_READ`; save=`PROJECT_SAVE_COORDINATOR` |
| owner cleanup | `PASS` | G0 Playwright 路由离开后 Workspace/FlowCanvas/Inspector/Preview/ImageCanvas/ROI/Persistence/Run owner 与 subscription/timer/request 全部为 0 |

冻结 fixture 不是产品假数据，仅作为 deterministic QA/Release 验收输入。正式结果证据保持在 Formal Run/Results response seed，
未写入 Project 持久化结构；Preview artifact 不冒充 Formal Result。
