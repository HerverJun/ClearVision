# Studio UI Next F04 — G3A Project 生命周期合同决策

## 1. 批准与范围

```text
G3A_STATUS=DONE
G3A_APPROVED_BY=PRODUCT_OWNER
G3A_APPROVED_AT=2026-07-18
G3A_APPROVAL_SOURCE=F04_PROMPT_3_OF_4
G3B_ENTRY=APPROVED

G3B_BACKEND_IMPLEMENTATION=NO
G3C_FRONTEND_IMPLEMENTATION=NO
```

本文件只冻结目标合同。没有修改 `ProjectService`、repository、数据库 migration、operation journal、endpoint、Project UI 或 `projectLifecycleCommandOwner`。

## 2. 当前代码事实

1. `POST /api/projects` 接收 `CreateProjectRequest { name, description, flow?, globalVariables? }`，受 `CanEditProject` 保护；无 `clientOperationId`、无 operation record、无 reconcile query。
2. `ProjectService.CreateAsync` 先 `AddAsync(Project)`，再在 `flow != null` 时写 Flow JSON。DB 成功、Flow 文件失败会抛异常，endpoint 返回含异常消息的 400，但 Project 可能已经存在。
3. 空白创建时数据库 Project 已有默认空 Flow；没有 Flow 文件时 `GetByIdAsync` 回退到 DB projection。当前新 UI 尚无 create command。
4. `DELETE /api/projects/{id}` 先获取 mutation lease，再 `MarkAsDeleted`/repository update，随后同步删除 variable session、Flow JSON 与 assets。cleanup 失败会抛异常并返回 400，但 tombstone 已可能生效，list/detail/open repository 已隐藏该 Project。
5. 当前 delete 没有 expected revision、client operation identity、durable cleanup status、response-loss reconcile 或稳定二次删除结果。
6. `ProjectNotFoundException` 在不同 endpoint 仍可能被 generic catch 映射为 400；UI 不能依赖异常字符串。
7. `Project.RecordOpen()` 会写 `LastOpenedAt` 并调用 `MarkAsModified()`，但生产 endpoint 没有显式 open command；`GET projects/recent` 只读取非 deleted 且 `LastOpenedAt != null` 的记录。
8. `Project.IsDeleted` 与 repository 的 `!IsDeleted` filter 已提供 tombstone 基础，但没有单独 operation/cleanup authority。
9. 正式 Project/Flow/GlobalVariables 保存权威仍是 `ProjectService` + `ProjectSaveCoordinator`；G3B 不得新增第二 Project service/save chain。

## 3. 决策总表

| Decision | 状态 | 冻结结论 |
| --- | --- | --- |
| D-PROJECT-01 | `APPROVED_TARGET_CONTRACT` | create 增加 user-scoped `clientOperationId` 与只读 operation reconcile |
| D-PROJECT-02 | `APPROVED_BLANK_ONLY` | F04 只创建空白 Project；不在 create 写 initial Flow/assets/template |
| D-PROJECT-03 | `APPROVED_TARGET_CONTRACT` | 新 delete command 使用 operation id、expected revision、tombstone 与 durable cleanup |
| D-PROJECT-04 | `APPROVED_STRUCTURED_ERRORS` | 400/401/403/404/409 使用稳定 code；unknown outcome 走 operation query |
| D-PROJECT-05 | `APPROVED_TARGET_CONTRACT` | 新显式 open command，只更新 LastOpenedAt，不改内容 revision/ModifiedAt |
| D-PROJECT-06 | `DEFERRED_WITH_REASON` | F04 不提供 template create；不阻塞 F04/default entry |

## 4. Operation authority 通用合同

G3B 允许新增一个 Project lifecycle operation journal，作为 create/delete response-loss reconcile 的唯一 authority；不得复制 Project repository 或保存链。

### Identity 与安全

- `clientOperationId`：非空 UUID，由 client 为一次用户意图生成。
- 唯一键：`(AuthenticatedUserId, OperationKind, ClientOperationId)`。
- 相同用户、kind、operation id 与相同 payload fingerprint 返回原权威结果。
- 相同 identity、不同 fingerprint：`409 OPERATION_PAYLOAD_MISMATCH`。
- 不同用户的同 operation id 互不可见、不可复用；reconcile 对“不存在、已过期、属于他人”统一返回 404。
- create/delete command 与 query 均要求 authenticated；command 继续要求 `CanEditProject`。

### Payload fingerprint

- 输入先转为版本化 canonical JSON：固定字段名/顺序、trim 后 name、description 的 null/空值规则、UUID 小写规范；禁止包含 token、时间戳或前端本地 revision。
- fingerprint：canonical UTF-8 JSON 的 SHA-256，journal 保存算法版本与 hash，不保存凭据。

### 状态

```text
pending
completed
failed-retryable
failed-terminal
```

- pending 必须在 crash recovery 后继续或转为明确失败，不能静默清理。
- terminal operation 保留 7 天；删除 cleanup/tombstone 审计见 D-PROJECT-03 的更长保留规则。
- cleanup owner：G3B 新增单一 hosted recovery/cleanup owner，使用同一 journal；不得由前端 timer 承担。

### Reconcile endpoint

```text
GET /api/project-operations/{clientOperationId}?kind=create|delete
```

响应：

```json
{
  "clientOperationId": "uuid",
  "kind": "create|delete",
  "status": "pending|completed|failed-retryable|failed-terminal",
  "projectId": "uuid|null",
  "result": {},
  "errorCode": "string|null",
  "createdAtUtc": "timestamp",
  "updatedAtUtc": "timestamp",
  "expiresAtUtc": "timestamp|null"
}
```

该 GET 是 response-loss 的唯一 reconcile；禁止按 Project 名称、异常文本或 list 猜测。

## 5. D-PROJECT-01 — Create idempotency

### Endpoint 与 request

```text
POST /api/projects
Permission=CanEditProject
```

F04 request：

```json
{
  "clientOperationId": "uuid",
  "name": "string",
  "description": "string|null"
}
```

- F04 request 不接受 `flow`、`globalVariables`、template、assets 或 imported payload。
- completed 返回现有 canonical `ProjectDto`，HTTP 201，Location 保持 `/api/projects/{id}`。
- 同 identity 重放返回同一 ProjectId/ProjectDto；可以返回 200 replay 或 201，并通过稳定 response 字段 `operationReplayed` 区分，G3B 必须选定并测试。批准默认：首次 201、重放 200。
- journal 必须在 Project create 前 durable reserve；ProjectId 在 reserve 时生成并绑定，避免 crash 后创建第二 ID。

### 404/409

- reconcile 不存在/他人/过期：404 `PROJECT_OPERATION_NOT_FOUND`，不泄漏跨用户存在性。
- payload mismatch：409 `OPERATION_PAYLOAD_MISMATCH`。
- completed Project 后续被删除时，operation 仍返回原 create result identity并标记 `projectDeleted=true`，不复活 Project。

## 6. D-PROJECT-02 — Blank create 与部分成功

```text
STATUS=APPROVED_BLANK_ONLY
CREATE_MODE=BLANK_PROJECT_CREATE
```

- 数据库 Project 创建成功即为 create 成功；PersistenceRevision 初始为 0。
- create 阶段不写 Flow JSON、assets 或 template copy，因此 F04 不存在第二次文件写入的部分成功窗口。
- 缺少 Flow 文件时，服务端返回 deterministic canonical empty Flow：Project identity 绑定、固定空 operators/connections、无伪造业务算子；首次 Save 才经现有 `ProjectSaveCoordinator` 持久化 Flow。
- canonical empty Flow 的 schema/字段必须在 G3B contract test 固定，不能由前端私有默认替代。
- Legacy 不带 `clientOperationId` 且携带 Flow/GlobalVariables 的旧请求暂时保留；它不进入 F04 UI，旧 partial-success 风险在本轮不宣称已修复。G3B 必须保留兼容回归并明确 deprecated telemetry。
- Template create：D-PROJECT-06 延期。

## 7. D-PROJECT-03 — Delete revision、幂等与 cleanup

### 新 command

```text
POST /api/projects/{projectId}/delete
Permission=CanEditProject
```

```json
{
  "clientOperationId": "uuid",
  "expectedPersistenceRevision": 12
}
```

目标流程：

```text
reserve operation
→ acquire Project mutation lease + ProjectSaveCoordinator access
→ verify not deleted and expectedPersistenceRevision
→ durable tombstone
→ operation result=deleted/cleanup-pending
→ async durable Flow/assets/session cleanup
```

状态：

```text
deleted
cleanup-pending
cleanup-completed
cleanup-failed-retryable
```

- tombstone 是删除权威时点；其后 list/detail/open 均按 not-found/deleted 处理。
- cleanup 失败不能让 Project 再可见，也不能只抛异常；journal/tombstone 保存结构化状态与最后错误 code。
- cleanup retry owner 是单一 hosted service；进程重启扫描 pending/retryable。
- tombstone 默认长期保留；delete operation 与 cleanup audit 在 cleanup terminal 后保留至少 30 天。未来物理 purge 需独立 retention ADR。
- 相同 operation id 重放返回同一结果。新 operation id 删除已 tombstone Project 返回稳定 completed projection，`alreadyDeleted=true`，不得 500/模糊 400。
- active run/save/mutation lease：409 `PROJECT_MUTATION_CONFLICT`。
- revision mismatch：409 `PROJECT_REVISION_CONFLICT`。
- response loss：只查询 operation endpoint，不做不可逆 optimistic success。
- 删除当前工程前，G3C 必须调用 Workspace leave protection；前端不猜测 cleanup。

### Legacy compatibility

现有 `DELETE /api/projects/{id}` 保留为 compatibility adapter，继续要求 `CanEditProject`。G3B 将其委托给同一 delete coordinator，但不要求旧 client 提供 operation id；服务端生成内部 identity，保持成功 204，并让重复删除稳定返回 204。该 adapter 不向 F04 新 UI 开放，且不得形成第二 delete authority。

## 8. D-PROJECT-04 — Structured error mapping

| 条件 | HTTP | Code |
| --- | --- | --- |
| Project 不存在/已删除 | 404 | `PROJECT_NOT_FOUND` |
| operation 不存在、过期或非当前用户 | 404 | `PROJECT_OPERATION_NOT_FOUND` |
| revision conflict | 409 | `PROJECT_REVISION_CONFLICT` |
| operation payload mismatch | 409 | `OPERATION_PAYLOAD_MISMATCH` |
| active run/save/mutation lease | 409 | `PROJECT_MUTATION_CONFLICT` |
| validation | 400 | 稳定 `PROJECT_VALIDATION_*` code |
| forbidden | 403 | 后端 policy response |
| unauthenticated | 401 | Auth middleware response |
| unknown command outcome | n/a | operation reconcile，不解析异常字符串 |

现有 endpoint compatibility 可以继续保留 `{ Error }` 字段，但 G3B 新合同必须同时返回稳定 `Code`；前端只使用 HTTP + Code，不依赖 exception message、本地化字符串、数据库文本或 500 body。

## 9. D-PROJECT-05 — Explicit open authority

```text
POST /api/projects/{projectId}/open
Permission=Authenticated project read
clientOperationId=NOT_REQUIRED
```

响应：

```json
{
  "projectId": "uuid",
  "lastOpenedAtUtc": "server timestamp"
}
```

- 服务端 UTC clock 是 timestamp authority；并发 open 使用原子/串行的 last-write-wins 更新，结果可安全重复。
- 只更新 `LastOpenedAt`；不得修改 `PersistenceRevision`、Flow、GlobalVariables、assets、Project `ModifiedAt`，不得触发 Save 或复制 Project。
- 不存在/已删除：404 `PROJECT_NOT_FOUND`；未授权仍由 backend permission 拒绝。
- 最近工程只读取此字段；普通 GET detail 不暗中记录 open。
- 当前 `RecordOpen()` 会 `MarkAsModified()`，G3B 不得直接复用造成 `ModifiedAt` 漂移，必须通过批准的专用 repository/application command 更新。

## 10. D-PROJECT-06 — Template create

```text
STATUS=DEFERRED_WITH_REASON
BLOCKS_F04=NO
BLOCKS_DEFAULT_ENTRY=NO
```

原因：没有稳定 template authority；模板版本和 Flow schema 未冻结；创建事务、资产复制与 operation reconcile 未闭合；扩展模板会破坏 F04 blank-only 合同。G3C 不得显示空按钮、disabled 伪入口或暗示已支持。

## 11. Migration、retention 与兼容风险

- operation journal/tombstone cleanup 状态需要 G3B migration；本轮未创建。
- journal 必须有 user/kind/operation unique index、ProjectId index、status/updatedAt recovery index。
- migration rollback 不能删除已产生的 operation/tombstone authority；需要 forward-compatible rollback plan。
- Legacy create with Flow 与 legacy DELETE 继续有兼容入口，但必须通过同一 Application Service/coordinator，不能复制实现。
- Project row 当前没有 owner user identity；user scope 只约束 operation record，不擅自改变既有 Project 可见性模型。

## 12. G3B 测试草案

### Create

- 同用户同 id/同 payload 并发与串行重放只产生一个 ProjectId；
- 同 id/不同 payload 409；跨用户同 id 相互不可见；
- reserve 后 crash、DB commit 后 response loss、reconcile completed；
- blank create 不写 Flow/assets；canonical empty Flow 稳定；
- Legacy Flow request compatibility 与已知风险回归。

### Delete

- expected revision success/conflict；active run/save lease 409；
- tombstone 后 list/detail/open 404；
- cleanup pending/success/retryable failure/crash recovery；
- response loss reconcile；重复相同/不同 operation id；
- Legacy DELETE 204 compatibility；
- cleanup failure不恢复可见性。

### Errors/Open

- 所有 structured code；不泄漏跨用户 operation；
- open 不改 revision/ModifiedAt/Flow；并发 timestamp authority；deleted 404；recent 排序。

## 13. G3C owner 输入

未来唯一 `projectLifecycleCommandOwner`：

- 只调用本文件 allowlist；
- create/delete 每次用户意图生成一个 `clientOperationId` 并在 response loss 时保存到 UI draft/reconcile projection；
- 不按名称猜测、不 optimistic delete；
- create 只展示 blank mode；template 无入口；
- delete 当前 Workspace 先走 leave protection；
- 后端 ProjectDto/PersistenceRevision 始终是正式权威。

## 14. 批准结论

代码事实未发现会导致本目标合同必然数据损坏、Legacy 无法兼容或违反单一 Project authority 的硬冲突。新增 operation journal、delete coordinator、open command 与 migration 属于 G3B，必须按本文件串行实现并验证。

```text
D-PROJECT-01=APPROVED_TARGET_CONTRACT
D-PROJECT-02=APPROVED_BLANK_ONLY
D-PROJECT-03=APPROVED_TARGET_CONTRACT
D-PROJECT-04=APPROVED_STRUCTURED_ERRORS
D-PROJECT-05=APPROVED_TARGET_CONTRACT
D-PROJECT-06=DEFERRED_WITH_REASON

G3A_STATUS=DONE
G3B_ENTRY=APPROVED
```
