# Studio UI Next F04 — G3C Project 生命周期前端闭环

## 1. Closure

```text
G3C_STATUS=DONE
G3C_FRONTEND_SHA=2e766e41b21e4be0f4bb653373522072200147c7
G3C_TEST_SHA=b6156c2c5e9dc7c1e27fe0d605dfa0f17350c662

PROJECT_COMMAND_OWNER_COUNT=1
PROJECT_READ_QUERY_OWNER_COUNT=1
SECOND_PROJECT_CACHE=NO
CREATE_RESPONSE_LOSS_RECONCILE=PASS
UPDATE_REVISION_CONFLICT=PASS
EXPLICIT_OPEN_UI=PASS
DELETE_UNKNOWN_OUTCOME_RECONCILE=PASS
PROJECT_A_B_LATE_RESPONSE_ISOLATION=PASS
PROJECT_LEAVE_PROTECTION=PASS

G4_ENTRY=APPROVED
```

G3C 只消费 [G3B 后端闭环](./F04_G3B_Project生命周期后端闭环.md) 已冻结的 HTTP/operation authority，没有新增第二 Project service、read cache、HTTP client、EventBus、save chain 或 route leave guard。

## 2. Owner topology

```text
ProductRuntime
├─ shared ReadQueryClient                         唯一 read-query/cache authority
├─ projectLifecycleCommandOwner                  唯一 Project command writer
│  ├─ POST projects                              blank create
│  ├─ PUT projects/{id}                          name/description update
│  ├─ POST projects/{id}/open                    explicit open
│  ├─ POST projects/{id}/delete                  revision-aware delete
│  └─ GET project-operations/{id}?kind=...       unknown-outcome reconcile
└─ WorkspaceRuntime
   └─ existing Workspace owner / persistence / run leave protection
```

- `projectLifecycleCommandOwner` 在 authenticated `ProductRuntime` 中只创建一次，ProductRuntime dispose 时释放。
- diagnostics 发布 `ownerCount`、active AbortController、in-flight command、pending operation identity 与总 reconcile 数；重复 mounted owner 直接拒绝。
- Project read 仍使用既有 `projects-read` capability 与同一个 `ReadQueryClient`。列表、recent、detail query handle 是页面生命周期投影，不是第二 cache/authority。
- 最近工程 query 不缓存跨页面旧 authority；每次挂载重新读取服务端 `LastOpenedAt`。

## 3. Command lifecycle

状态机：

```text
idle
creating
updating
deleting
reconciling
conflict
unknown-outcome
succeeded
failed
disposed
```

所有 command 使用：

- operation generation；
- 单一 active `AbortController`；
- ProjectId scope；
- duplicate-flight dedupe；
- disposed guard；
- late-response guard；
- structured HTTP `Code` 映射。

Project A command 尚未返回时切换到 Project B，会先 abort/invalidate A generation；即使 transport fake 仍交付 A 的晚到响应，也不能覆盖 B projection。

## 4. Product journeys

### Create

- Projects 页面只提供名称与描述，不暴露 Flow、template、assets 或 arbitrary payload。
- 每次逻辑创建生成一个稳定 `clientOperationId`；duplicate click 复用同一 Promise 与 identity。
- POST 响应丢失后进入 `unknown-outcome/reconciling`，只查询 operation endpoint，不发第二次 POST。
- 成功路由只使用服务端 `projectId`。

### Update

- Project Detail 使用当前服务端 `PersistenceRevision` 更新名称与描述。
- 不提交 Flow/GlobalVariables，不复制 Workspace save 逻辑。
- `PROJECT_REVISION_CONFLICT` 与 `PROJECT_MUTATION_CONFLICT` 进入明确 conflict 状态；不会自动覆盖。
- 用户选择“重新读取服务端版本”后才替换表单投影。

### Open

- Projects 列表、recent、Project Detail 与 Workspace deep-link 均经唯一 command owner 调用 `POST /api/projects/{id}/open`。
- Workspace 只在 open 成功后执行 Project GET 与 owner mount；open 的 401/403/404/失败不会伪装进入。
- `LastOpenedAt` 只读取后端 recent authority，前端不自行写入。

### Delete

- list/detail destructive confirmation 使用当前服务端 revision 与新 `clientOperationId`。
- command owner 在 POST 前调用既有 Workspace `prepareForLeave`；未 settle 时不发删除请求。
- UI 不做 optimistic removal；operation completed/tombstone authority 成立后才刷新或返回 Projects。
- response loss 只走 operation query；cleanup pending/failed-retryable 不恢复 Project 可见性。
- Detail 删除成功或 reconcile 成功后清理可丢弃 read projection并安全返回 `/projects`。

## 5. Auth and reauthentication

- 401 仍由 shared `ApiTransport` callback 交给唯一 Auth owner，Project owner 不存 token。
- 若 create/delete operation identity 尚未 reconcile，session expiration 会隔离该 identity并让 ProductRuntime 保留；重新认证后先 reconcile，再恢复 Runtime。
- 无 pending operation 的 401 不伪造业务成功，也不建立第二 reauth 流程。

## 6. Verification

### StudioUI static gates

```text
LINT=PASS
TYPECHECK=PASS
STUDIO_UI_UNIT=PASS (462/462, 72 files)
STUDIO_UI_BUILD=PASS
```

定向 G3C unit/architecture 页面集合：`45/45 PASS`，覆盖 owner count/lifecycle、duplicate create、response loss、pending reconcile、A/B late response、update conflict、explicit open、delete leave block、delete reconcile、unauthorized/reauth、disposed owner、create route、delete confirmation 与 tombstone removal。

### Browser fixture

```text
F04_G3C_PROJECT_LIFECYCLE=PASS (2/2)
F03_WORKSPACE_REGRESSION=PASS (41/41)
F02_PROJECTS_READ_REGRESSION=PASS (3/3; 2 visual captures SKIPPED because no evidence target was requested)
```

F04 stateful Browser fixture 执行：

```text
empty list
→ create POST response loss
→ operation reconcile
→ detail
→ rename with expected revision
→ explicit open
→ Workspace
→ return detail
→ delete POST response loss
→ operation reconcile
→ list removal
→ detail/open 404
```

另有独立 Browser journey 验证 revision conflict 后保留本地输入、显式 reload 服务端 authority，不自动覆盖。

Browser fixture 不等同于真实后端、真实 WebView2 或 DPI；这些证据不得由本节替代。

## 7. F04 final-SHA 复验

唯一 `projectLifecycleCommandOwner` 与完整 Project 旅程已在 F04 final code SHA 上复验：

```text
F04_FINAL_CODE_SHA=0c78962d2a005ebea165eaee8a98558aca88c99c
STUDIO_UI_UNIT=PASS (480/480, 75 files)
BROWSER_FULL=PASS (78 passed, 17 optional visual captures skipped)
REAL_WEBVIEW2_FINAL_JOURNEY=PASS
CREATE_RESPONSE_LOSS_RECONCILE=PASS
EXPLICIT_OPEN_AFTER_RESTART=PASS
DELETE_RESPONSE_LOSS_RECONCILE=PASS
PROJECT_COMMAND_OWNER_COUNT=1
```

final-SHA journey 使用真实 UI 和后端合同，没有通过 fixture 直接篡改前端状态；完整证据见 [G6 隔离 E2E 与 Final Evidence](./F04_G6_隔离E2E与FinalEvidence闭环.md)。

## 8. Preserved boundaries

```text
Studio:StudioUiEnabled=false
Studio:WorkspaceCapabilityEnabled=false
FORMAL_DEFAULTS_CHANGED=NO
LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO

F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```

Vite build 仍报告既有单 chunk 超过 500 kB 的 warning；build 成功，本轮未通过放宽阈值隐藏该 warning。
