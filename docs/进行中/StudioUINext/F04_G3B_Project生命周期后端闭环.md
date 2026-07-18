# Studio UI Next F04 — G3B Project 生命周期后端闭环

## 1. Closure

```text
G3B_STATUS=DONE
G3B_BACKEND_SHA=3a0354a258a3696486ddc2b37bfd4ee6dc8dc7bb
G3B_TEST_SHA=3a0354a258a3696486ddc2b37bfd4ee6dc8dc7bb

CREATE_OPERATION_IDEMPOTENCY=PASS
BLANK_CREATE_PARTIAL_SUCCESS_CLOSED=YES
DELETE_EXPECTED_REVISION=PASS
DELETE_TOMBSTONE_AUTHORITY=PASS
DELETE_CLEANUP_RETRY=PASS
STRUCTURED_ERROR_MAPPING=PASS
EXPLICIT_OPEN_AUTHORITY=PASS
LEGACY_API_COMPATIBILITY=PASS

F04-B20-PROJECT-CREATE-NON-IDEMPOTENT=CLOSED
F04-B21-PROJECT-CREATE-PARTIAL-SUCCESS=CLOSED
F04-B22-PROJECT-DELETE-NO-REVISION=CLOSED
F04-B23-PROJECT-DELETE-UNKNOWN-CLEANUP=CLOSED
F04-B24-PROJECT-NOT-FOUND-SEMANTICS=CLOSED
F04-B25-RECENT-PROJECT-NO-OPEN-AUTHORITY=CLOSED

G3C_ENTRY=APPROVED
```

G3B 仅实现 [G3A 合同决策](./F04_G3A_Project生命周期合同决策.md) 与 [ADR-F04-G3A](./ADR-F04-G3A-Project生命周期合同.md) 已批准的后端权威；没有新增第二 Project service、save chain、repository、HTTP authority 或前端持久化模型。

## 2. Operation authority

唯一 durable journal 是 `ProjectLifecycleOperations`：

```text
identity=(UserId, Kind, clientOperationId)
payloadFingerprint=SHA-256(normalized payload), version 1
status=pending | completed | failed-retryable | failed-terminal
```

- 同用户、同 kind、同 operation id、同 payload 返回同一 ProjectId，不重复创建或删除。
- 同 identity 不同 payload 返回 `409 OPERATION_PAYLOAD_MISMATCH`。
- operation query 按 authenticated user 隔离；不存在或跨用户均不披露权威结果。
- create operation terminal record 保留 7 天。
- delete operation 与 cleanup audit terminal record 保留 30 天。
- Project tombstone 长期保留；未来物理 purge 需要独立 retention ADR。
- `ProjectLifecycleRecoveryHostedService` 是唯一 recovery/cleanup owner，每轮复用同一 coordinator 与 journal；前端不承担 cleanup timer。

## 3. HTTP contracts

### Blank create

```text
POST /api/projects
GET  /api/project-operations/{clientOperationId}?kind=create
```

- 带 `clientOperationId` 的新合同只接受名称、描述与 operation identity，不接受 initial Flow、template、assets 或任意 Project payload。
- 首次成功返回 201；相同 operation replay 返回 200，并带稳定 `operationReplayed`。
- ProjectId 只使用服务端绑定结果。
- 未存在 Flow 文件时返回 canonical empty Flow；首次正式 Flow 持久化仍由用户 Save 进入 `ProjectSaveCoordinator`。

### Revision-aware delete

```text
POST /api/projects/{projectId}/delete
GET  /api/project-operations/{clientOperationId}?kind=delete
```

请求包含 `clientOperationId` 与 `expectedPersistenceRevision`。revision mismatch 返回 `PROJECT_REVISION_CONFLICT`；活动 save/run/mutation lease 返回 `PROJECT_MUTATION_CONFLICT`。Project tombstone 与 operation completion 原子保存，tombstone 后 list 不显示，detail/open/update 返回 `PROJECT_NOT_FOUND`。

cleanup 状态是：

```text
cleanup-pending
cleanup-completed
cleanup-failed-retryable
```

cleanup 失败不会恢复 Project 可见性；状态持久化并按退避计划重试。重复 delete 复用原 cleanup authority，不产生第二 cleanup 决策。

### Explicit open

```text
POST /api/projects/{projectId}/open
```

该 command 只更新服务端 `LastOpenedAt`，不修改 `PersistenceRevision`、`ModifiedAt`、Flow 或 assets，不触发 Save，也不绕过 permission。`/recent` 只读取该后端 open authority。

## 4. Error contract

新 lifecycle endpoint 返回稳定 HTTP + `Code`：

| HTTP | Code | 语义 |
| --- | --- | --- |
| 400 | `PROJECT_VALIDATION_*` | 请求或 operation kind 无效 |
| 401 | Auth middleware contract | 未认证 |
| 403 | Permission middleware contract | 无 Project 编辑权限 |
| 404 | `PROJECT_NOT_FOUND` | Project 不存在或已 tombstone |
| 404 | `PROJECT_OPERATION_NOT_FOUND` | 当前用户无该 operation authority |
| 409 | `PROJECT_REVISION_CONFLICT` | server revision 与 expected revision 不一致 |
| 409 | `PROJECT_MUTATION_CONFLICT` | 活动 run/save/mutation 阻止写操作 |
| 409 | `OPERATION_PAYLOAD_MISMATCH` | operation identity 被不同 payload 复用 |
| 503 | `PROJECT_OPERATION_RETRYABLE` | command outcome 必须 reconcile |
| 503 | `PROJECT_CLEANUP_RETRYABLE` | tombstone 已成立，cleanup 等待重试 |

Project PUT 的 save revision conflict 使用 typed `ProjectSaveRevisionConflictException`，对外返回 `PROJECT_REVISION_CONFLICT` 并保留 `compatibilityCode=PSV011`；新生命周期 UI 不解析 exception、SQL 或本地化文本。

## 5. Persistence and compatibility

- EF migration：`20260719000000_AddProjectLifecycleOperations`。
- SQLite maintenance schema version：5 → 6，并包含旧库 schema repair。
- migration `Down` 抛出明确的 `NotSupportedException`；operation/tombstone authority 是 forward-only，禁止通过 rollback 删除。
- Legacy 不带 `clientOperationId` 的 initial-Flow create 保留 compatibility，并记录 deprecated telemetry；它不对 Studio UI Next F04 开放。
- Legacy `DELETE /api/projects/{id}` 委托同一 lifecycle coordinator，服务端生成内部 identity，保持 204 compatibility 与稳定重复删除。

## 6. Verification

代码与测试位于同一冻结提交 `3a0354a258a3696486ddc2b37bfd4ee6dc8dc7bb`。本轮实际执行结果：

| Evidence | Result |
| --- | --- |
| Project lifecycle focused | PASS, 11/11 |
| Project/Save/Concurrency focused | PASS, 69/69 |
| Desktop Project/Auth/DB/DI focused | PASS, 58/58 |
| Architecture guards | PASS, 27/27 |
| Services regression | PASS, 505/505 |
| Desktop endpoints regression | PASS, 316/316 |
| Desktop build | PASS |
| Infrastructure build | PASS |
| `git diff --check` | PASS |

覆盖 concurrent same-operation create、payload mismatch、cross-user isolation、pending/completed/retryable/terminal、reserved create/delete restart recovery、retention、canonical empty Flow、Legacy create、revision/mutation conflict、tombstone read semantics、cleanup retry、repeat delete、open timestamp/recent order/revision stability，以及 401/403/404/409/validation。

测试构建仍报告仓库既有 `System.Collections.Immutable` 8/9 版本冲突 warning；本轮相关测试均通过，该 warning 未被改写为新 PASS 证据或隐藏。

## 7. Boundaries preserved

```text
Studio:StudioUiEnabled=false
Studio:WorkspaceCapabilityEnabled=false
FORMAL_DEFAULTS_CHANGED=NO
LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO

CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```

G3C 只允许建立唯一 `projectLifecycleCommandOwner` 并消费以上合同；不得重新设计后端 authority。
