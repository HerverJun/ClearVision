# Studio UI Next F04-R G1 Route / Role / Profile / Owner 合同矩阵

> 状态：`READY_FOR_PRODUCT_OWNER_REVIEW`
> 证据基线：`56fbf18fcb59f91e9d63666c08e302db92ff692c`。提案 route 不表示已在 Router 注册。

## 1. 当前 Router 真值

| Route | 当前名称 | Route guard | Profile/flag | 当前 mounted owner |
|---|---|---|---|---|
| `/setup` | 首次管理员初始化 | public + setup-only | 无 | `AuthLifecycleOwner` + route page |
| `/login` | 登录 | public | 无 | `AuthLifecycleOwner` + route page |
| `/change-password` | 修改密码 | Authenticated | 无 | `AuthLifecycleOwner` + route page |
| `/forbidden` | 403 | Authenticated | 无 | route page |
| `/not-found` | 404 | Authenticated | 无 | route page |
| `/overview` | 概览 | Authenticated | 无 | `OverviewPage` query handles；`ProductRuntime.systemStatus` |
| `/projects` | 工程 | Authenticated | 无 | `ProjectsPage` query handles；共享 `projectLifecycleCommandOwner` |
| `/projects/:id` | 工程详情 | Authenticated | 无 | `ProjectDetailPage` query handle；共享 command owner |
| `/projects/:id/workspace` | 工程工作区 | Admin/Engineer | `Studio2.Workspace` 在 runtime mount 时 fail closed | `WorkspaceRuntime -> workspaceOwner` 单树 |
| `/operators` | 算子库 | Authenticated | 无 | `OperatorsPage` query handle |
| `/operators/:operatorType` | 算子详情 | Authenticated | 无 | `OperatorDetailPage` query handle |
| `/stations` | 工作站 | Authenticated + profile | `Studio2.StationsRead` | `StationsPage` queries + visible polling owner |
| `/stations/:stationId` | 工作站详情 | Authenticated + profile | `Studio2.StationsRead` | `StationDetailPage` queries + visible polling owner |
| `/results` | 检测结果 | Authenticated | 无 | `ResultsPage` query handles |
| `/diagnostics` | 诊断 | Admin/Engineer | 无 | `DiagnosticsPage` + runtime diagnostics probe |
| `/about` | 关于 | Authenticated | 无 | static route page |
| `/labs/design` | Design Lab | Authenticated + internal + browser-test | `hostKind=browser-test` | route page |
| `/labs/canvas` | Canvas Lab | Authenticated + internal + browser-test | `hostKind=browser-test` | `CanvasLabOwner` |

根 route 当前 redirect 到 `/overview`。`resolveSafeReturnRoute` 允许 overview/projects/operators/results/diagnostics/about，不允许 Labs；Stations 也不在 returnTo 白名单。

## 2. 产品域合同提案

### 2.1 Owner 术语

- `query owner`：当前 `ReadQueryClient` 生成的 route-local query handle；共享 cache/transport authority 仍只有一个。
- `command owner`：唯一写入口。不存在时写 `NONE`，不能用页面临时 handler 替代。
- `mounted/dispose owner`：创建 timer、SSE、Canvas、AbortController、blob 或订阅的生命周期 owner；route unmount 必须 dispose。
- `PROPOSED` owner 只表示 Prompt 2/后续需要批准的唯一职责，不授权本轮创建。

| 产品域 | Route | Role / backend policy | Capability flag / profile | Query owner | Command owner | Mounted / dispose owner | HTTP / SSE authority | F04-R disposition |
|---|---|---|---|---|---|---|---|---|
| 工程 | `/projects`、`/projects/:id` | GET Authenticated；create/update/delete `CanEditProject`；runtime package `RequireAdmin` | 无 Next domain flag；所有 Next profile | `ReadQueryClient` + Projects route handles | `projectLifecycleCommandOwner` | Route component dispose query；command owner 随 `ProductRuntime.dispose` | `GET projects/recent/search/{id}`；`POST projects`、`POST projects/{id}/open|delete`、`PUT projects/{id}`、`GET project-operations/{id}`；`POST projects/{id}/runtime-package/export` | 保留 owner；补导入/导出决策，不建第二 save client |
| 流程 | `/projects/:id/workspace` | Route Admin/Engineer；save `CanEditProject`；Preview/Run 当前仅 Authenticated | `Studio2.Workspace=true`；`NEXT_PILOT`；ROI 工具另有两个现有 flags | `WorkspaceRuntime` + `createWorkspaceProjectDefinition` | Canvas commands；`workspacePersistenceOwner`；`runCommandOwner`；Preview/ROI local commands | `workspaceOwner` 拥有 FlowCanvas/Inspector/Preview/Image/ROI/Persistence/Run 并统一 dispose | `GET/PUT projects/{id}`；`POST flows/preview-node`；`GET/DELETE preview-artifacts/{id}`；`POST inspection/admission|execute|stop|reconcile` | F04-R 核心；GlobalVariables/FinalDecision 必须进入同一 owner/save payload |
| 检测 | `/inspection`（提案，当前不存在） | 建议 Admin/Engineer；后端需决定 `CanOperateHardware`/正式 Inspection policy | legacy `Studio2.Inspection` 不是 Next flag；profile 待 Prompt 2 | NONE | NONE | `inspectionCapabilityOwner`（PROPOSED，须与 legacy owner 互斥） | 现有 `POST inspection/realtime/start|stop`、`GET inspection/realtime/{projectId}/state|events` SSE；不得用 WebMessage 作为 Next 正式通道 | 仅冻结；F05 实现 |
| 检测结果 | `/results` | Authenticated read | 无 Next flag；`Studio2.ResultsReview` 不被 Next 消费 | `ReadQueryClient` + Results route handles | NONE | `ResultsPage` unmount dispose queries | `GET projects`、`GET inspection/history/{projectId}`、`GET .../{resultId}`、`GET stations/results`；后端另有 compare/evidence/export/previous-success/statistics/SSE 未被 Next 消费 | F04-R 保留 run handoff/详情；扩展 F05 |
| 工作站监控 | `/stations`、`/stations/:stationId` | 普通读 Authenticated；admin detail/log/command/package `RequireStationAdmin` | `Studio2.StationsRead`；当前只在 Browser fixture 可达 | `ReadQueryClient` station queries | 当前 NONE | `VisibleStationPollingOwner` + route dispose；未来 SSE owner 只能有一个 | `GET stations|summary|statistics|results|{id}/results|health`；现有 `GET stations/events` SSE；Admin endpoints 见下节 | 保留只读；先修 profile contract；操作域 F05 |
| AI 工程助手 | `/ai`（提案，当前不存在） | 建议 Admin/Engineer；当前 `/api/ai/**` 仅全局 Authenticated，显式 policy 缺口 | legacy `Studio2.AiPanel` 不被 Next 消费；profile 待批 | NONE | NONE | 单一 Next AI capability owner（PROPOSED），消费既有 AgentRun authority | `POST ai/agent-plan*|agent-runs*|workspace-snapshot`；`GET ai/agent-runs/{id}|events`；stream token/cancel | F05；不得重造 AgentRun/EventStore/recovery |
| 系统设置 | `/settings`（提案，当前不存在） | Admin 全量；Engineer 仅 `CanOperateHardware` 允许的读/动作；Operator 无入口 | legacy `Studio2.Settings` 不被 Next 消费；profile 待批 | NONE | NONE | 单一 settings capability owner（PROPOSED），每个 tab 资源可为其子 owner | `/api/settings*`、`/api/plc*`、`/api/tcp*`、`/api/station-communication*`、`/api/cameras*`、`/api/users*` | F05；当前不得让 `/diagnostics` 代替 |
| 系统诊断 | `/diagnostics` | Route Admin/Engineer；`/health` anonymous；其余受全局 Auth | 无 | `ProductRuntime.systemStatus` / diagnostics probe | NONE | ProductRuntime/route component dispose | `GET /health` + 本地 runtime/host diagnostics；不写业务配置 | 保留；与设置分离 |
| 账户 | `/change-password` + logout command；不提案空 `/account` | Authenticated | 无 | `AuthLifecycleOwner.session` | `AuthLifecycleOwner` | `AuthLifecycleRoot` 随 app unmount dispose | `GET auth/me`；`POST auth/change-password|logout`；登录/setup 见 public routes | 保留 |
| 关于 | `/about` | Authenticated | 无 | NONE | NONE | route component | 无业务 endpoint | 保留 |
| 内部 Labs | `/labs/design`、`/labs/canvas` | Authenticated + browser-test | `hostKind=browser-test` | NONE | Canvas lab commands only | `CanvasLabOwner`；route unmount dispose | 无产品业务 endpoint | INTERNAL_ONLY |

## 3. 现有后端 endpoint 与权限补充

### 3.1 Project / GlobalVariables / FinalDecision

| Capability | Endpoint / authority | Policy | 结论 |
|---|---|---|---|
| Project reads | `GET /api/projects*` | Authenticated | 可复用 |
| Project create/update/delete/open | 当前 lifecycle endpoints | `CanEditProject`（open 为 Authenticated read） | `projectLifecycleCommandOwner` 唯一 writer |
| Project save | `PUT /api/projects/{id}` -> Application Service -> `ProjectSaveCoordinator` | `CanEditProject` | Flow、GlobalVariables、FinalDecision 与正式 assets 继续走此链 |
| GlobalVariables definitions/values | `GET/PUT /api/projects/{id}/global-variables`、values/reset endpoints | GET Authenticated；writes `CanEditProject` | 合同存在；Next 可达 owner 缺失，不是新增后端 authority 的理由 |
| FinalDecision validation | `POST /api/inspection/decision-configuration/validate` | Authenticated | 校验合同存在；正式写入仍随 Project save |
| Runtime package export | `POST /api/projects/{id}/runtime-package/export` | `RequireAdmin` | 可作为交付资产候选；当前 Next 无入口 |

### 3.2 Results / Station

| Capability | Endpoint | Policy |
|---|---|---|
| Local history/detail | `GET /api/inspection/history/{projectId}`、`.../{resultId}` | Authenticated |
| Compare/evidence/export | `GET .../compare`、`.../evidence/manifest|export`、`.../previous-success` | Authenticated |
| Result statistics/realtime | `GET /api/inspection/statistics/{projectId}`、Inspection SSE | Authenticated |
| Station read | `GET /api/stations`、summary/statistics/results、station results/health、events SSE | Authenticated |
| Station admin | station detail/logs/commands/audit/identity/deploy/packages | `RequireStationAdmin=Admin` 或等价 handler check |

### 3.3 Settings

当前设置合同不是单一权限等级：

- `GET /api/settings` 对非 Admin 返回脱敏投影；`PUT /api/settings`、theme/reset/disk/database 为 Admin。
- PLC/TCP 的部分读和运行操作使用 `CanOperateHardware=Admin|Engineer`，持久化写通常 Admin。
- Camera discover/bindings/preview/trigger 使用 `CanOperateHardware`。
- Station communication/token 与用户管理是 Admin。

未来 `/settings` 必须按 backend policy 形成可解释的 section-level 只读/禁止状态，不能在前端复制一套安全权威。

## 4. 401 / 403 / 404 / 409 / unknown outcome

| 状态 | 冻结行为 |
|---|---|
| `401` | 唯一 `AuthLifecycleOwner` 收敛并发 401，清 token、提升 session generation、quarantine ProductRuntime，保留 Project/Run reconcile identity，跳转登录；capability 不另建 reauth |
| `403` | Route guard 只做可见性优化；backend 403 是安全 authority。只读子区可降级，不得把 403 显示为网络错误或伪造数据 |
| `404` | Project/result/station detail 显示明确 not-found；operation 404 不跨用户泄漏；不得从缓存/list 猜测对象存在 |
| `409` | Project lifecycle 由 structured code 进入 conflict；Workspace save 使用 `PersistenceRevision`；AI snapshot/其他写合同必须先冻结稳定 code |
| Unknown write outcome | Project create/delete 只查 operation endpoint；Workspace save 重新 GET Project authority；Formal Run/Stop 使用 inspection reconcile；其他未来写命令没有获批 reconcile 合同时禁止接入 |
| Network/5xx | Read 可 stale/partial failure；write 不盲目重试；必须说明影响和下一步 |
| Decode failure | fail closed，不推断字段、不显示伪数据 |

## 5. Route / Role / Profile / Owner 冲突

| 冲突 | 严重度 | 当前事实 | 必须决策 |
|---|---:|---|---|
| `Studio2.StationsRead` 无 Desktop 注入 | P1 | Router/Navigation 消费该 key，WebView2 host 不生成 | 纳入现有 startup authority，或取消该 flag 并用获批 profile 表达；不得再建第二配置源 |
| “设置” route 指向诊断 | P1 | ProductLayout `/diagnostics` | `/settings` 与 `/diagnostics` 分离；未实现时隐藏设置 |
| Workspace route 与 backend policy 不一致 | P1 | Route 限 Admin/Engineer；Preview、Formal Run endpoints 仅 Authenticated | Prompt 2 决定是否收紧后端 policy；本轮不改 API |
| AI role 未冻结 | P1 | 建议 Engineer/Admin，但 AgentRun endpoints 无显式 role policy | F05 前冻结 permission，不由 Vue 自行决定 |
| 连续检测 authority 仍混有 WebMessage callback | P1 | start endpoint 可经 `WebMessageHandler` 通知；另有 HTTP/SSE | Next 只使用 authenticated HTTP/SSE，冻结 persisted snapshot/stop/reconcile 语义 |
| 工程导入/可编辑导出无版本化合同 | P1 | Legacy 使用 client JSON + create/save；正式 assets 不保证完整 | Prompt 2 选择后端合同或明确只做 runtime package；不得复制 legacy 私有格式当 authority |
| `/overview` 是根落点 | P2 | 当前 redirect 与登录恢复均指向 Overview | 产品负责人决定 `/projects` 推荐落点 |
| legacy capability flags 与 Next 消费脱节 | P2 | Host 注入 Settings/Inspection/Results/AI flags，Next 不消费 | F05 按 capability 单 owner 逐项决定复用/退役，不新增平行 flag 树 |
| 账户无 `/account` route | P3 | 能力通过账户菜单、改密 route、logout 完成 | 不造空页；仅当真实个人资料合同出现再提案 |

## 6. 单一写 Owner 检查

| 写域 | 唯一 owner | 第二 writer |
|---|---|---|
| Project lifecycle | `projectLifecycleCommandOwner` | `NONE` |
| Project/Flow/GlobalVariables/FinalDecision save | `workspacePersistenceOwner` -> existing Project save chain | `NONE` |
| Flow/Inspector draft | `flowCanvasOwner` / `inspectorOwner` 在同一 workspace tree | `NONE` |
| Preview/ROI | `previewOwner` / `roiInteractionOwner`，只写可丢弃调试投影或 Flow draft | `NONE` |
| Formal Run/Stop/Reconcile | `runCommandOwner` | `NONE` |
| Auth/token | `AuthLifecycleOwner` + token port | `NONE` |
| Stations read | no writer | `NONE` |
| Results read | no writer | `NONE` |
| AI/Settings/Continuous Inspection Next | `NONE`（未实现） | 禁止临时页面 writer |

没有发现当前 Next 同一 capability 两个写 owner。提案也没有批准新增第二 Router、Shell、HTTP client、EventBus、Canvas、HostBridge 或保存链。

## 7. 状态

```text
ROUTE_ROLE_PROFILE_MATRIX=PROPOSED_NOT_APPROVED
OWNER_CONTRACT_MATRIX=PROPOSED_NOT_APPROVED
OWNER_WRITE_CONFLICT_COUNT=0
CONTRACT_DECISION_BLOCKER_COUNT=8
G1_AUDIT_STATE=DONE
G1_PROPOSAL_STATE=READY_FOR_PRODUCT_OWNER_REVIEW
G1_STATUS=AWAITING_PRODUCT_OWNER_APPROVAL
G2_ENTRY=BLOCKED
IMPLEMENTATION=FORBIDDEN
```
