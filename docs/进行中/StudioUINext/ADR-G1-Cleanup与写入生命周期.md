# ADR-G1：Cleanup 与写入生命周期

状态：Accepted（G1 Workspace lifecycle Owner）

## 决策

G1 不把 `dispose()` 后的请求默认解释为“未提交”。所有写请求先由唯一 capability owner 持有到 settle；网络失败、响应丢失或 abort 只能产生 `unknown-outcome`。后端没有 `clientOperationId` 的合同不得由前端自动重放，必须由用户触发读取协调，且只有读取足以证明目标状态时才进入 `reconciled`。

当前适用 owner：

- Workspace persistence：`workspacePersistenceOwner` 持有 Project GET/PUT，并把正式保存交给既有 `ProjectSaveCoordinator`。
- Global Variables：`workspaceGlobalVariablesOwner` 持有 runtime GET/PUT/POST。其运行值 endpoint 当前没有 operation identity；未知写入会阻止离开并保留待协调标记。
- Template：`templateOwner` 持有详情 GET 与模板 POST/PUT。模板合同当前没有 operation identity；未知保存不会自动重试。
- Camera：`cameraBindingEditorOwner` 持有绑定 GET、单帧请求和 continuous-preview session。Camera stop 只由该 owner 发起。
- AI handoff：`handoffReceivePort` 使用服务端 `clientOperationId`，acknowledge 响应丢失时先按同一 identity GET；确认 `consumed` 才保留本地 staged draft，否则调用 Workspace rollback。

## Cleanup policy

1. route、project、session 或 feature flag 变化时，读请求和 SSE 立即 abort；写请求先经过 leave guard，不能安全中止的写入必须等待 settle。
2. Camera continuous-preview stop 是幂等 cleanup；每次 session 只允许一个 owner 发起一次 stop，超时为 3 秒，失败不自动重试。离开前 cleanup 未 settle 或失败会阻止离开；owner 因 session 失效或宿主关闭被 dispose 时允许发起一次 bounded best-effort stop，结果记录为 cleanup unknown，不把它伪装成业务写入成功。
3. Artifact DELETE 当前没有被 Next owner 使用。任何新增 DELETE 必须复用 artifact owner 的 durable identity、幂等合同和 reconcile endpoint；在合同冻结前保持阻断，不在 dispose 中发送私有删除请求。
4. 所有 cleanup controller、timer 和请求都进入现有 Workspace diagnostics lease；lease dispose 后资源投影必须归零，晚到响应不得写入新 Project。

## 后果

这会在服务端合同不足时保守地阻断离开，但避免重复创建模板、重复写运行值、误删 artifact 或把旧工程响应投影到新工程。G2 需要为 Global Variables、Template 和 Camera 写入补齐可查询 operation identity 后，才能缩小 unknown-outcome 的人工协调范围。
