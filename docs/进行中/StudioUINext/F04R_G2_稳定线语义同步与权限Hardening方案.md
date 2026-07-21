# Studio UI Next F04-R G2 稳定线语义同步与权限 Hardening 方案

> 状态：`FROZEN_PROPOSAL`
> 当前代码：`483a212783d4bc66f9f434e0a22de4be944e46c7`
> 稳定线 remote：`bea404394ac8cf403cca719c1990c426414a06c2`
> 本轮不执行代码、merge、cherry-pick 或权限修改。

## 1. 稳定线处置

`bea40439` 是一个混合提交，包含相机/图像语义、Legacy fallback lifecycle、Modbus 上限、lockfile 和 `tmp/pdfs`。不能整提交合入。受控处置：

| Disposition | 文件/语义 | 决策 |
|---|---|---|
| `MUST_SYNC_BEFORE_F04R` | `InspectionImageFormatDetector.cs` 的真实 MIME；`ApiEndpoints.cs` image response MIME；`PreviewNodeEndpoints.cs` captured frame/source node validation | Prompt 3 第一个同步包；共享后端事实先于 UI |
| `MUST_SYNC_BEFORE_F04R` | Legacy `app.js`、`viewManager.js`、`previewCoordinator.js`、`propertyPanelCapabilityOwner.mjs`、`imageViewer.js`、`inspectionController.js` | 稳定 fallback 必须先保住，再接 Next；逐 hunk 同步，不覆盖当前 composition |
| `MUST_SYNC_BEFORE_F04R` | 相应 Desktop/Application/UI tests | 与语义同步串行验证；测试 fixture 不等于真实设备 |
| `ALREADY_EQUIVALENT` | Next preview artifact lifecycle、AbortController、MIME/length/SHA 校验、ImageCanvas/ROI/Preview owner dispose | 不复制 stable Legacy owner；只补缺失的 source identity contract |
| `DEFER_WITH_REASON` | `ModbusCommunicationOperator` SlaveId `247 -> 255` 及测试 | PLC/通信域，F05；不因单提交整包引入 |
| `OUT_OF_SCOPE` | `tmp/pdfs/gr-bus-pages/**`、`tmp/pdfs/gr-manual/**` | 临时图片，不提交进产品同步 |
| `CONFLICT_REQUIRES_DECISION` | 共享 endpoint 同时被稳定线与 Next 文档/contract 引用；lockfile 是否必要 | 主协调者按 hunk 审计并批准后才可实施 |

详细的 capture/source/frame 校验位于 [相机绑定与单帧捕获合同](./F04R_G2_相机绑定与单帧捕获合同.md)。

## 2. Prompt 3 实施顺序与 owner

```text
P3-0 重新 fetch/审计远端与工作树
 -> P3-1 稳定后端 MIME + captured-frame semantic sync
 -> P3-2 串行运行稳定相关 .NET/Legacy tests
 -> P3-3 Legacy fallback lifecycle hunk + tests
 -> P3-4 backend policy hardening（原 endpoint，不新增 authority）
 -> P3-5 Next camera editor / GlobalVariables / FinalDecision / Results capability
 -> P3-6 Browser/Playwright + owner/contract guards
 -> P3-7 真实 WebView2、DPI、真实端点/硬件证据
```

共享文件 owner 仍是主协调者：Router、Shell、startup flags、API transport/contracts、Workspace root、Project save chain、README/主计划。Capability owner 只修改获批目录，不能顺手修 policy、Host 或共享 DTO。

P3-1 完成前不得实现相机 editor；P3-4 完成前不得把 Formal Run/Stop/Reconcile 或 Preview command 暴露给错误 role；任何 unknown write 均不得自动重试。

## 3. 权限 Hardening 决策

当前 `AuthMiddleware` 对非白名单 `/api/**` 做 Authenticated session；`EndpointPermissionGuards` 的角色映射为 Admin/Engineer。下表冻结目标，不在本轮修改 C#。

| 能力 | 当前代码事实 | F04-R 目标 | 待修改 endpoint / 说明 |
|---|---|---|---|
| Workspace route | Next Router Admin/Engineer；后端 Project reads Authenticated | Admin/Engineer | route guard 保留；后端不因页面隐藏放宽 |
| Preview（Flow debug） | `/api/flows/preview-node` 仅依赖 AuthMiddleware；artifact read/delete 当前无 capability policy | `CanEditProject` | 给 preview-node/artifact command 加既有 policy filter；Preview 仍是可丢弃投影 |
| Camera binding/discover/capture | `/api/cameras/*` 使用 `CanOperateHardware` | `CanOperateHardware` | 保持硬件边界；不把硬件动作误降为普通 Project edit |
| Formal Run / Stop / Reconcile | admission/execute/stop/reconcile 当前无显式 `.RequireClearVisionPermission`，仅 Authenticated | `CanOperateHardware` | 在原 endpoints 使用既有 policy；不新增 run endpoint |
| GlobalVariables definitions/bindings | Project save/`global-variables` writes `CanEditProject`；runtime values writes也沿用 `CanEditProject` | definitions/bindings `CanEditProject`；F04-R runtime values read-only | Next 不调用 runtime value write；是否保留 Legacy manual value 写入由稳定线单独审计 |
| FinalDecision validation | `/api/inspection/decision-configuration/validate` 仅 Authenticated | `CanEditProject`（建议） | 校验是编辑前置，不改变正式 Run admission；后端 owner 批准后收紧 |
| Project save | `PUT /api/projects/{id}` `CanEditProject` | `CanEditProject` | 保留单一 ProjectSaveCoordinator |
| Runtime Package export | `POST /api/projects/{id}/runtime-package/export` `RequireAdmin` | `RequireAdmin` | 保留；Next 不携带 draft Flow override |
| Results/Evidence read/export | history/detail/manifest/export 仅 Authenticated | Authenticated | 不扩大为写权限；413/409 由现有 service 返回 |
| Station read | `/api/stations*` 读 Authenticated；Next route 还受 `Studio2.StationsRead` profile | Authenticated + 明确 read profile | 先修 Desktop startup injection 漂移；不把 fixture 当 production |
| Station commands/package | Station endpoints 有 `RequireStationAdmin` | Admin / `RequireStationAdmin` | F05；不进入 Pilot 主导航 |

### 3.1 权限语义注意

- Admin/Engineer 的前端 route 可见性不等于 backend authorization；后端 403 永远优先。
- `CanEditProject` 与 `CanOperateHardware` 当前角色集合相同，但名称保留语义隔离，避免未来 Engineer 权限扩展时前端不变而后端越权。
- FinalDecision validation 收紧到 `CanEditProject` 是目标提案；若后端 owner 选择保持 Authenticated，必须记录批准理由和不允许修改 Project 的边界，不由 Vue 自行覆盖。
- Camera capture 需要硬件 policy，即使其结果只进入 Preview；否则“Preview=CanEditProject”会绕过硬件安全边界。

## 4. 现有 endpoint 合同缺口（只记录）

| 编号 | 缺口 | 处理 |
|---|---|---|
| `H01` | Run/Stop/Reconcile 缺显式 `CanOperateHardware` filter | P3-4 原 endpoint hardening；不新建命令通道 |
| `H02` | Preview node/artifact 缺 `CanEditProject` filter | P3-4；Preview 不接收正式执行或私有 bridge |
| `H03` | Runtime Package endpoint 接受临时 `Flow` override | Next 不使用；后端 owner 评估是否对 StudioUI 请求拒绝/忽略，保持既有 endpoint |
| `H04` | `/api/inspection/decision-configuration/validate` Authenticated | 目标收紧为 `CanEditProject` 或形成批准例外；本轮 pending |
| `H05` | `Studio2.StationsRead` 未由 Desktop WebView2 startup 注入 | 只能纳入现有 startup authority 或关闭 profile；不得新增第二 flags 源 |
| `H06` | Runtime Package export 无 mutation identity/reconcile | UI 对 network/unknown 锁定自动重试并提示人工核对；是否后端补合同属于后续 ADR，不在本轮擅扩 |
| `H07` | Runtime package manifest 已有 DecisionConfigurationHash，但 export success response 未投影 | 在既有 endpoint response 增量投影；不创建新 endpoint 或前端 hash |
| `H08` | history detail traceability 未投影 Project revision/decision hash，package/station 当前为 null | 在既有 result detail DTO/endpoint 投影真实可用 identity；无值时明确 null，不由 UI 猜测 |
| `H09` | Project update 将 GlobalVariables 多条 diagnostic 压成异常文本，只结构化首个 code | 在既有 Project update error response 投影 diagnostics 与 Variable/Operator/Port/Parameter identity；不新建变量 validation/save endpoint |
| `H10` | FinalDecision validation issue 无显式 field path | 在既有 validation response 增加稳定 field key，或冻结 code-to-field 映射；禁止解析 message |

## 5. 验证与证据

### 只读、已执行/已存在

- 已读取当前 Next route/policy/owner、Project DTO、GlobalVariables validator、Decision resolver/catalog、Run contracts、Evidence manifest models、RuntimePackageExporter。
- 已审计 `bea40439` 文件级 diff，确认 Modbus 与临时图片可排除。
- F04.2 既有 Legacy Playwright `3/3 PASS`、Next Playwright `22/22 PASS`、Next typecheck/lint PASS、Browser viewport evidence；本轮未重跑。

### 未执行，必须如实保留

```text
Browser/Playwright re-run       = NOT PERFORMED
Real WebView2 Debug/Release     = NOT PERFORMED
Windows 125% DPI                = NOT PERFORMED
Real API/database               = NOT PERFORMED
Real camera/PLC/Station         = NOT PERFORMED
Stable semantic sync tests      = PENDING
Backend policy hardening        = PENDING
Product visual confirmation     = FAIL / pending G4 owner decision
```

## 6. 状态

```text
STABLE_CAMERA_SEMANTIC_SYNC=PENDING
BACKEND_POLICY_HARDENING=PENDING
G3_ENTRY=AWAITING_PRODUCT_OWNER_APPROVAL
IMPLEMENTATION=FORBIDDEN
F05_ENTRY=BLOCKED
```
