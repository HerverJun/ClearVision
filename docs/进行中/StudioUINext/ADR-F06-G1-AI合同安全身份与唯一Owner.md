# ADR F06-G1：AI 合同、安全身份与唯一 Owner

## 状态

```text
STATUS=ACCEPTED
DECISION_SCOPE=F06_G1
IMPLEMENTATION_SCOPE=B1_TO_B5
IMPLEMENTATION_STATE=COMPLETE_REMOTE_CI_PASS
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
```

## 问题

Legacy AI 会话原本没有认证用户 owner，Session HTTP、Plan/Build create 的 durable operation identity、既有工程 Build baseline 以及窄公开 DTO 也不完整。仅隐藏前端入口不能阻止跨用户访问、响应丢失后的重复创建或用旧 Flow 覆盖新工程状态。

## 决策

### 1. Owner identity 与历史 Session

- 认证用户身份取 `ClaimTypes.NameIdentifier`，复用 AgentRun 的 `SHA-256("agent-run-owner:" + userId)` 计算并保存 `usr_<hex>` owner hash。
- 新 Session 在第一次持久化前写入 `OwnerHash`。create/list/get/delete/workspace mutation 全部在同一个 `IConversationalFlowService` 上按 owner 过滤，不建立第二会话服务。
- 非 owner 的 Session、Run 与 operation 统一返回不泄漏存在性的 `404` 公共响应。
- 历史 `OwnerHash=null` Session 继续供 Legacy 链路使用，但 Next list/get/mutation/delete 均不可见；禁止凭旧 `sessionId` 认领或自动绑定给首个访问者。

### 2. Session HTTP 与公开 DTO

G1 冻结以下 authenticated HTTP 合同：

| 合同 | 身份与并发 | 公开响应 |
|---|---|---|
| `POST /api/ai/sessions` | owner + `clientOperationId` | operation + Session detail |
| `GET /api/ai/sessions` | owner-scoped，offset/limit | paged summaries |
| `GET /api/ai/sessions/{sessionId}` | owner-scoped | public detail/snapshot |
| `DELETE /api/ai/sessions/{sessionId}` | owner + expected revision + client mutation id | delete receipt；active run/revision conflict 为 `409` |
| `POST /api/ai/sessions/{sessionId}/workspace-snapshot` | owner + expected revision + client mutation id | public snapshot；冲突返回 latest public snapshot |
| `GET /api/ai/operations/{clientOperationId}` | owner + 可选 kind | public operation projection |

公开 DTO 只包含 Session/Run id、生命周期、revision、Project id、baseline identity、公开错误与时间；不直接序列化内部 `ConversationSession`、History、Reasoning、raw tool payload、附件、绝对路径或异常。

### 3. Mutation identity 与 operation receipt

- Session workspace mutation 继续使用 Session 内 `clientMutationId + payload fingerprint` receipt 和 expected revision。
- Session create/delete、Plan Run 与 Build Run 使用 capability-local durable receipt store；唯一键为：

  ```text
  ownerHash + operationKind + clientOperationId
  ```

- receipt 保存 SHA-256 payload fingerprint、pending/created/failed/rejected、sessionId、runId、服务端确认的 Project baseline、公共错误、创建/更新时间与过期时间。
- 同键同 fingerprint 返回原 operation/run；同键不同 fingerprint 返回 `409 operation_identity_conflict`。响应丢失后由 owner-scoped lookup 恢复，前端不得盲目创建第二个 Run。
- store 在进程内串行化 reserve/update，先写临时文件并 flush，再原子替换主文件；保留 7 天且最多 1000 条。它只负责 AI operation outcome，不是第二个全局 operation framework、Session store 或 Project repository。
- 日志与 fingerprint 输入不输出任务全文、prompt、附件或 secret。

### 4. Project baseline

Build 请求必须显式区分：

```text
targetKind=new
  projectId/revision/hash 全部为空

targetKind=existing
  projectId + observed PersistenceRevision + canonical flow hash 全部存在
```

existing Project 的校验链固定为：

```text
IProjectApplicationService.GetByIdAsync(projectId)
→ 读取 canonical ProjectDto.PersistenceRevision
→ 读取 canonical ProjectDto.Flow
→ ExecutionFlowIdentity.ComputeFlowHash(flow.ToEntity())
→ 与请求 revision/hash 比较
→ 仅使用服务端 canonical Flow 启动 Build
→ 将确认后的 baseline 写入 operation receipt 与 workspace terminal projection
```

请求 revision/hash 只是比较条件。revision 或 hash 不一致返回明确 `409` 与当前公开 baseline；client draft `ExistingFlowJson` 不能替换 existing Project 的服务端 canonical Flow。

### 5. 权限与失败策略

- Session、Plan/Build create、cancel 和 operation lookup 复用 `RequireEngineerOrAdmin`；project-bound Build 同时保留现有 `CanEditProject` endpoint policy。
- Admin、Engineer 允许；Operator 拒绝；未认证返回 `401`。
- route/导航隐藏不是后端安全边界。
- redaction 清除 system prompt、chain-of-thought/Reasoning、Authorization/API key/token、绝对路径、IP、PLC 地址、base64/raw attachment、内部异常和非 public tool payload。

### 6. Next 唯一 Owner

- `/ai` 与 `/projects/:id/ai` 共用一个 lazy capability。
- `AiWorkbenchPage` 是 route composition surface；每次 route path 只有一个 `AiSessionOwner`，owner 独占 create/hydrate/reconcile 与 resource ledger。
- owner 只使用共享 `ApiTransport`；不使用 WebMessage AI 通道、同步 `/api/ai/agent-plan` fallback、第二 HTTP/EventBus、capability-global Pinia 或 localStorage authority。
- unmount 必须 dispose；request、stream、timer、subscription 全部清零。flag 关闭时 route guard 在页面 mount 前拒绝，零 owner DOM。

## 未包含

- G1 不实现正式 Intent、Plan、Clarification、Build、Resource、History 产品 UI。
- G1 不实现 Handoff endpoint/store/consume、Canvas replace、Apply Preview 或 Project 保存接入。
- G1 不修改 `ProjectSaveCoordinator`、Runtime Package、RuntimeHost、Station、Inspection 或正式结果权威。

## 后果

- B1-B5 可作为 G2/G3 的安全前置合同；G2 仍需产品与安全复审后单独授权。
- 关闭 `Studio2.AiWorkbench` 并重启即可阻止两个 Next route mount，不影响 `Studio2.AiPanel` 与 Legacy 回退。
- operation receipt store 的 schema/retention 变更必须保持 owner scope、幂等与 response-loss recovery，不得演化成通用第二 operation framework。
