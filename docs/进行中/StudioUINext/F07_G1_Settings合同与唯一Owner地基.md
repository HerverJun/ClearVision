# F07 G1：Settings 合同与唯一 Owner 地基

## 当前状态

```text
REPORT_STATE=DONE
INITIAL_SHA=ee64875b331e43ab2b68a28de390b6e7e597406f
F07_G1_BACKEND_PERMISSION_BLOCKER=OPEN
F07_G1_SECTION_SAVES=NOT_IMPLEMENTED
F07_SETTINGS_ROUTE=NOT_IMPLEMENTED
BUILD=NOT_RUN
BROWSER=NOT_RUN
BACKEND_TESTS=NOT_RUN
CI=NOT_RUN
ROUTE_CONFIG_DEFAULTS_CI_LEGACY=UNMODIFIED
F07_G2_ENTRY=AWAITING_REVIEW
F07_G2_IMPLEMENTATION=FORBIDDEN
```

本报告关闭的是 G1 的 capability-local 合同、decoder、唯一 owner 和写协调骨架，不表示 `/settings` route、导航、正式页面、任何 section 保存、后端权限修复或 F07 完成。所有实现均以执行时 `INITIAL_SHA` 为基线，分支为 `studio-ui-next`。

## 1. G0 决策落地

- `/settings` 的 UI route 角色固定为 `Admin` / `Engineer`；`Operator` 在 owner 创建后即 fail closed，不发起 read 或 write 请求。该判断只表达产品 route 规则，不扩大后端权限。
- Next UI preferences 与 AppConfig 产品主题保持独立。本轮没有把 Next preference 写入 generic General，也没有新增 preference 持久化链。
- 不新增 conditional revision、ETag 或前端并发协议。owner generation 只用于 stale、abort 和 draft identity；正式 AppConfig revision 仍是后端观察值，保存沿用后端无条件 revision / last-write-wins 语义。
- generic `/api/settings` 的前端 scope 固定为 General、Storage、Runtime、Security。PLC、TCP、Camera、Station、AI 不通过 generic fallback 或双写。
- Settings Import/Export 不在本轮合同、endpoint、schema 或入口内。
- Database 只登记 status 与 backup；restore、cleanup、repair、全局 reset 均排除。
- Camera Settings 只对应系统管理；Workspace 继续拥有工程绑定与预览。Camera mutation 遇后端 `409` 按 fail closed 处理。
- Station token 后端 generation、storage、reveal 安全升级继续登记为 `F07-D01`，前端只消费当前 masked/reveal/regenerate 语义。
- AI model management 进入 F07 合同；RuntimePreview Pilot 继续 developer-only，不进入普通 Settings endpoint 矩阵。
- 默认入口切换和 Legacy 退役仍是独立决策，本轮没有修改 flag、route、导航或 Legacy 文件。

## 2. G1 产物与唯一 Owner

新增范围只在 Settings capability-local 目录：

| 文件 | 责任 |
|---|---|
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/contracts.ts` | section、endpoint、角色、权限、敏感字段和 saved/effective/restart/error 语义矩阵；generic payload builder |
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/decoder.ts` | strict DTO decoder；camelCase/PascalCase 兼容；safe subset、masked token、AI redaction 和安全错误投影 |
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/apiAdapter.ts` | 只接收共享 `ProductRuntime.api` / `ApiTransport`；generic、Station、AI read adapter；不提供 section save 调用 |
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/settingsOwner.ts` | 单 mounted owner、role gate、read generation、stale/abort/dispose、错误投影和 owner diagnostics |
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/settingsWriteCoordinator.ts` | 每 section 串行队列、active abort、invalidate/cancel/dispose 和 typed write result；仅为未来保存提供窄骨架 |
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/index.ts` | capability-local exports |
| `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/tests/unit/capabilities/settings/` | contract、decoder、owner/coordinator 测试 |

唯一链路固定为：

```text
mounted SettingsOwner
        -> SettingsApiAdapter
        -> ProductRuntime.api / shared ApiTransport
        -> existing backend endpoint
```

owner 通过 module-level mount token 拒绝第二实例；dispose 会 abort read、dispose coordinator、清除 active owner token，并使晚到的 projection/write result 无效。没有新增 HTTP client、EventBus、ServiceRegistry、HostBridge、Canvas kernel、Project save client、SSE 或持久化存储。

## 3. Contract Matrix

下表是 G1 的消费合同，不是新的后端权限。`authenticated-safe/full` 表示当前后端对已认证非 Admin 返回 safe projection、Admin 返回 full projection；它不授权 Operator 进入 `/settings`。

| section | authority / read endpoint | write / operation endpoint | 现有权限投影 | sensitive / redaction | saved / effective / restart / conflict |
|---|---|---|---|---|---|
| General | `IConfigurationService` / `GET /api/settings` | scoped `PUT /api/settings`；theme 使用 `PUT /api/settings/theme` | read `authenticated-safe/full`；write `Admin` | generic sensitive keys fail closed；不保留 password/token | persisted；AppConfig projection 可回读；restart `unknown`；conflict 为后端合同缺口，先 reload |
| Storage | `IConfigurationService` / `GET /api/settings`、disk usage `GET /api/settings/disk-usage` | scoped `PUT /api/settings` | read `authenticated-safe/full`；disk usage 与 write `Admin` | image path 只作投影和受控输入；不导出 backup/path bundle | persisted；consumer 是否 reload 由 endpoint/代码确认；restart `unknown`；保存结果不伪装为磁盘写入成功 |
| Runtime | `IConfigurationService` / `GET /api/settings` | scoped `PUT /api/settings` | read `authenticated-safe/full`；write `Admin` | 不把 RuntimePreview Pilot 或执行状态纳入 generic draft | persisted；部分 consumer reload/startup dependent；restart `unknown`；conflict 后端合同缺口 |
| Security policy | `IConfigurationService` / `GET /api/settings` | scoped `PUT /api/settings` | read `authenticated-safe/full`；write `Admin` | password/token/private secret 不进入 projection；用户记录和密码操作不在 generic body | persisted；auth consumer 生效时机需现有服务证据；restart `unknown`；失败按 validation/unknown outcome 分开 |
| PLC | `/api/plc/settings`、`/api/plc/mappings` | `PUT /api/plc/settings`、`PUT /api/plc/mappings`；test `POST /api/plc/test-connection` | read/test `Engineer/Admin`；write `Admin` | IP、port、protocol 进入受控投影和脱敏证据；test 不等于 save | save persisted；test runtime-only；restart `none`；当前 validation 语义需保留后端响应，不由 UI 改成成功 |
| TCP | `GET /api/tcp/profiles` 与 profile status/frames | `PUT /api/tcp/profiles`；connect/disconnect/send/server/frames 使用专用 `/api/tcp/profiles/{id}/...` | read/runtime `Engineer/Admin`；profile write `Admin` | host、port、frame payload 按敏感运行配置处理；不缓存 socket | profile persisted；runtime operation only；restart `none`；connected/listening 只能取 runtime result |
| Camera system | `/api/cameras/**`、`/api/trigger-input/**` | bindings、soft trigger、trigger diagnostics、continuous preview 使用各自专用 endpoint | hardware read/operation `Engineer/Admin` | device identity、IP、serial、session/blob 只由 adapter/owner 管理；`409` 不覆盖 | binding persisted / projection reload；preview/trigger runtime-only；restart `none`；active stream `409-fail-closed` |
| Station communication | `GET /api/station-communication/settings` | `PUT /api/station-communication/settings`；token `POST /api/station-communication/token` | `Admin` | read 只接受 masked token view；raw token 只作为 reveal/regenerate ephemeral result，owner 不存储 | persisted；restart-dependent；`studio` / `localStation` flags 是后端结果；unknown outcome stop/report；`F07-D01` 不在前端修复 |
| AI model | `GET /api/ai/models`；reasoning `POST /api/ai/reasoning-support` | `/api/ai/models` CRUD、activate/default/test 专用 endpoint | current read `authenticated-safe/full`；CRUD/activate/default/test `Admin` | raw API key、secret map、敏感 header/query/body 必须 `<redacted>` 或 absent；不读 `ai_model_secrets` | model mutation persisted / projection reload；reasoning/test runtime-only；restart `none`；并发仍是后端合同缺口 |
| Database maintenance | `GET /api/settings/database/status` | `POST /api/settings/database/backup` | `Admin` | backup path 不进入普通 draft、日志或 bundle | maintenance operation-only；不是普通 save；restart `none`；unknown outcome stop/report；restore/cleanup/repair/reset excluded |

User management、change-password 属于计划中的 G4 独立 authority，不在本 G1 的十个 Settings section DTO 或 generic payload 中；用户记录、密码和 password hash 不得进入 Settings draft。G4 开始前必须以现有 `/api/users/**` 和 `/api/auth/change-password` 重新形成独立 contract/decoder/permission tests。

## 4. Decoder 与 error 语义

- Generic settings decoder 只接受已批准顶层字段和四个 generic section；支持现有 camelCase/PascalCase 序列化差异；safe subset 含受限 section、未知字段或 raw sensitive key 时直接 decode failure。
- Station decoder 只返回必要的 masked projection，要求 `hasToken/mask/last4`，拒绝把 read response 当作 raw token；token operation 的 raw token 标记为 ephemeral，不进入 owner projection。
- AI decoder 同时接受当前后端 Admin full DTO 和非 Admin safe DTO，但要求整个数组保持 full 或 safe 单一形态；嵌套数组、敏感 map 和 reasoning support 通过递归 redaction 校验，raw API key/header/query/body fail closed。
- 错误 DTO 只保留 `unauthorized`、`forbidden`、`not-found`、`conflict`、`validation`、`network`、`decode`、`server`、`unknown-outcome` 等公开分类和 validation issues；未知字段或敏感字段不会回显。
- `SettingsOperationSemantics` 明确 `persistence`、`effective`、`restart`、`conflict`、`unknownOutcome`；G1 没有把本地 generation 当成 AppConfig `Revision`，也没有声称已有 conditional save。

## 5. Owner 与 coordinator 门禁

- `SettingsOwner` 只读 `ProductRuntime.api` 的 generic projection；Operator 在调用 adapter 前被拒绝，Engineer 只能消费服务端 safe projection，Admin 才能进入后续 mutation 白名单。
- read refresh 会取消上一代 `AbortController`；generation 不匹配、owner disposed 或 signal aborted 的晚到结果不会覆盖当前 projection。
- write coordinator 对同一 section 串行，对不同 section 保持独立队列；invalidate 使 active/queued work stale，cancel/dispose 会取消并释放资源；writer exception 保留 typed `failed` 结果和原始错误对象供上层诊断。
- G1 没有具体 section save function、PUT/POST save implementation、draft UI、route mount 或导航接入，因此没有产生第二保存入口。

## 6. 后端合同 blocker 与遗留债务

### B01：Settings/AI read endpoint 缺少显式 endpoint-level permission contract（OPEN）

当前后端事实：

- `SettingsEndpoints.cs` 的 `GET /api/settings` 没有 `.RequireClearVisionPermission(...)`；它通过 `IsAdmin` 返回 full 或 safe subset。G1 只把这一现有行为建模为 `authenticated-safe/full`，没有把它升级成新的权限。
- `GET /api/ai/models` 和 `POST /api/ai/reasoning-support` 也没有显式 endpoint filter；`/api/ai/models` 通过 `IsAdmin` 选择 full/safe DTO，reasoning support 返回公共 catalog。
- 其他 Settings mutation、PLC/TCP/Camera/Station/Database 专用入口仍按现有 `RequireAdmin`、`CanOperateHardware` 或 Station policy 消费；本轮没有改动它们。

这使“Admin/Engineer 才可进入 route”与“直接 API read 的 safe projection policy”仍不是一个完整、可审计的 endpoint contract。后端 owner 必须决定并测试 `RequireAuthenticated`、safe projection 和 Admin/Engineer/Operator 矩阵的最终关系；前端不能通过隐藏 section、重写 response 或新增权限来绕过。B01 关闭前不得把 G1 报告解释为后端权限合同完成。

### `F07-D01` 至 `F07-D05`：已登记、不在本轮修复

- `F07-D01` Station token 后端 generation/storage/reveal 安全升级延期。
- `F07-D02` Settings Import/Export 排除。
- `F07-D03` Database restore、cleanup、repair、全局 reset 延期。
- `F07-D04` 默认入口切换与 Legacy 退役独立决策。
- `F07-D05` RuntimePreview Pilot 保持 developer-only。

另外，后端 `MergeSettingsUpdate` 当前仍识别 `communication`、`tcpCommunication`、`features` 等潜在 generic overlap。G1 前端合同明确不使用这些字段，不通过前端猜测修复；若后续要消除 overlap，必须另立 backend contract/ADR。

## 7. 共享文件 Owner 与回滚边界

| owner | 本轮边界 |
|---|---|
| 主协调代理 | Router、navigation、ProductLayout、composition root、`ProductRuntime`、package/lockfile、Vite、CI、flags、默认配置、共享 API transport、HostBridge、F07 主计划与 G0 文档；本轮未修改 |
| Settings capability owner | 仅本报告列出的 `capabilities/settings` 与 capability-local tests；不得修改 Router、后端 authority、Workspace owner、Legacy 或共享 transport |
| Workspace owner | 继续拥有工程 Camera binding、preview、stream/session 生命周期；Settings 不复制或抢占 |
| 后端 authority owner | AppConfig、PLC/TCP、Camera、Station、AI、Database、User/Auth；前端只消费既有 endpoint，B01 由后端 owner 决定 |

回滚顺序固定为：

1. 在 G2 之前直接不挂载 capability；如需撤回实现，只撤回本次 Settings capability-local 提交，不改 Legacy、默认入口或其他 worktree。
2. 单 section 发生错误时停止该 section queue，保留后端 authority；不扩展 generic endpoint、不自动重试未知结果。
3. Camera `409` 保留现有 binding/stream，要求既有 Workspace owner 释放；不抢占、不静默覆盖。
4. Station token、AI secret、Database maintenance 交由各自后端 authority 处理；前端不复制 token/secret、不执行 restore/cleanup/reset。

## 8. 测试与证据

| 门禁 | 结果 |
|---|---|
| `npm run typecheck` | PASS；app/vitest/node 三套 TypeScript 配置 |
| `npm run test:unit -- tests/unit/capabilities/settings` | PASS；3 files，22/22 tests |
| `npx eslint src/capabilities/settings tests/unit/capabilities/settings --max-warnings=0` | PASS |
| build / production build | NOT RUN，按本轮范围未执行 |
| Browser / Playwright / WebView2 / DPI / publish | NOT RUN / NOT PERFORMED |
| backend `.csproj` tests / hardware / model provider / CI | NOT RUN / NOT PERFORMED |

定向测试覆盖：route role、Operator no-request、ProductRuntime.api、唯一 owner、stale refresh、abort/dispose、section serial queue、invalidate/cancel/dispose、writer failure、generic scope、PascalCase、unknown field、raw sensitive field、Station masked token、AI full/safe redaction、403/error projection 语义。测试没有写入产品配置、数据库、硬件或端口。

## 9. G2 准入条件

G2 只能在新的明确授权后开始，且至少满足：

- 提交并推送后的 HEAD、远端 `origin/studio-ui-next`、clean worktree 重新核对；不得沿用历史 SHA 代替新授权。
- B01 由后端 owner 形成可审计的 endpoint permission decision 和对应 backend tests；在此之前前端只保留 safe/full projection 消费合同。
- Review 确认 G0 D01-D10 与 `F07-D01` 至 `F07-D05` 不被重新解释；不新增 conditional revision、Settings bundle、后端权限或第二 authority。
- G2 route 仍必须只有 Admin/Engineer，Operator forbidden；只能挂载一个 Settings owner，route leave/logout/flag off 必须真实 dispose 并停止请求/写入。
- G2 只能继续复用 `ProductRuntime.api` 和本 G1 coordinator；任何具体 section 保存前必须先补齐对应 endpoint response、saved/effective/restart/unknown-outcome、validation、403/404/409 和 redaction tests。
- Camera system 与 Workspace owner、Station token debt、Database status/backup、AI safe/full projection 的边界在 review 中签字确认；User/password 进入 G4 前另立独立 contract。
- 本轮未运行的 build、Browser、WebView2、DPI、publish、backend、CI 证据不得写成 G2 或 F07 通过。

```text
F07_G1_STATE=DONE
F07_G2_ENTRY=AWAITING_REVIEW
F07_G2_IMPLEMENTATION=FORBIDDEN
```

本轮在上述状态处停止，不开始 G2 实现。
