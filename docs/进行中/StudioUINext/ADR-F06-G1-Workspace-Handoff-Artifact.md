# ADR F06-G1：AI Workspace Handoff Artifact

## 状态

```text
STATUS=ACCEPTED
DECISION_SCOPE=F06_G1
ADR_STATE=ADR_APPROVED_IMPLEMENTATION_DEFERRED_TO_G4
IMPLEMENTATION_PHASE=G4
ENDPOINT_IMPLEMENTED=NO
ARTIFACT_STORE_IMPLEMENTED=NO
WORKSPACE_CONSUME_IMPLEMENTED=NO
PROJECT_SAVE_INTEGRATION_IMPLEMENTED=NO
```

## 问题

AI Build 的候选 Flow 未来需要进入 Project Workspace，但不能由 AI owner 直接持有 Canvas、替换当前 Flow 或自动保存 Project。仅传递 Flow JSON、localStorage key 或前端缓存无法证明 owner、Plan/Build、Project baseline、候选内容与一次消费结果。

## 决策

G4 实现前冻结一个后端权威、短期、owner-bound 的 `AiWorkspaceHandoffArtifactV1`。G1 只批准合同方向，不创建 endpoint、store 或前端产品能力。

### 1. Artifact identity

服务端记录至少包含：

| 字段 | 语义 |
|---|---|
| `schemaVersion` | 固定 `1` |
| `artifactId` | 服务端不可预测 id |
| `ownerHash` | 认证 owner；不作为可伪造客户端字段 |
| `clientOperationId` | artifact create 幂等 identity |
| `sessionId` | owner-bound Session |
| `planRunId`、`planId`、`planHash` | 被 Build 消费的 canonical Plan identity |
| `buildRunId`、`buildClientOperationId` | eligible terminal Build identity |
| `projectBaseline` | `new` 或服务端确认的 existing Project id/revision/canonical flow hash |
| `candidateFlow` | 候选 Flow；不是正式 Project Flow |
| `candidateFlowFingerprint` | canonical candidate SHA-256 |
| `createdAtUtc`、`expiresAtUtc` | 创建与有效期 |
| `status` | `available`、`consuming`、`consumed`、`expired`、`rejected` |
| `consumeReceipt` | Workspace 接收 operation、目标 Project、接收时间与结果；不代表 Project 已保存 |

create 的唯一键为 `ownerHash + handoff_create + clientOperationId`。同 identity 同 fingerprint 返回同 artifact；不同 fingerprint 返回 `409`。有效期、最大容量、原子 reserve/consume 与 crash recovery 必须在 G4 实现 ADR 附录和测试中给出具体值。

### 2. 创建条件

只有同时满足以下条件才能创建 artifact：

- Session、Plan Run、Build Run 与 client operation 均属于当前 owner；
- Build 已进入单一成功终态，ApplyGate 明确 eligible；
- Plan/Build/baseline 关联与 operation receipt 一致；
- candidate Flow 通过 canonical serialization 与 fingerprint 计算；
- existing Project baseline 仍由服务端重载 Project 后复核；
- payload 已通过公开字段与敏感数据过滤。

### 3. Workspace 接收 authority

```text
AI owner 请求创建 artifact
→ 后端冻结 candidate + identity
→ AI route unmount/dispose
→ Project Workspace owner 查询 artifact
→ 后端复核 owner/project/expiry/status/baseline/fingerprint
→ Workspace owner 接收为 staged local draft
→ 用户显式检查并调用现有 Save
→ 现有 Application Service → ProjectSaveCoordinator
```

- AI capability 不 import `FlowCanvas`，不调用 `replaceFlow()`，不持有 Workspace owner。
- Workspace 是 candidate 的唯一接收与 Canvas owner；接收不得自动保存、自动运行或自动部署。
- artifact/consume receipt 不是 Project、Flow、Runtime Package 或正式 asset authority。
- dirty Workspace、baseline mismatch、artifact expired/consumed 或 unknown outcome 必须停止并进入显式 reconcile；不得静默覆盖。

### 4. 敏感字段排除

artifact 及公开投影不得包含：system prompt、Reasoning/chain-of-thought、模型 secret/token、Authorization、内部异常、未脱敏路径/IP/PLC 地址、raw attachment/base64、非 public tool payload、Station credential 或 Runtime 私有状态。

### 5. G4 停止条件

出现任一情况，G4 必须停止且不得以缓存/fallback 绕过：

- 无法证明 artifact owner、Plan、Build 或 Project baseline；
- existing Project revision/hash 已变化且未重新 Build；
- candidate fingerprint 无法 canonical 复算；
- consume 不能实现一次性/幂等/可恢复；
- Workspace 需要第二个 Canvas owner 或第二 Project save endpoint；
- 正式保存 trace 不能证明只经过既有 Project PUT 与 `ProjectSaveCoordinator`；
- artifact 需要包含上述敏感字段才能工作。

## G1 明确未实现

代码中不得出现 Handoff endpoint、artifact store、Workspace consume port、Canvas replace、Apply Preview 页面或 Project 保存接入。B6 当前状态只能写为：

```text
F06_B6_HANDOFF_ADR=APPROVED_IMPLEMENTATION_DEFERRED
```
