# ADR F06-G1：AI Workspace Handoff Artifact

## 状态

```text
STATUS=ACCEPTED_IMPLEMENTED
DECISION_SCOPE=F06_G1_TO_G4
ADR_STATE=IMPLEMENTED_AND_VERIFIED_IN_G4
IMPLEMENTATION_PHASE=G4_COMPLETE
ENDPOINT_IMPLEMENTED=YES
ARTIFACT_STORE_IMPLEMENTED=YES
WORKSPACE_CONSUME_IMPLEMENTED=YES
PROJECT_SAVE_INTEGRATION_IMPLEMENTED=YES_AUTHORITY_PRESERVED
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

## G4 实际落地

### 1. Store 参数与持久化

```text
ARTIFACT_TTL=30_MINUTES
TERMINAL_AUDIT_RETENTION=24_HOURS
MAX_ACTIVE_PER_OWNER=16
MAX_ACTIVE_GLOBAL=256
MAX_STORED_ARTIFACTS=512
MAX_CANDIDATE_FLOW_BYTES=2097152
```

`AiWorkspaceHandoffArtifactStore` 将公开、脱敏后的 artifact 持久化到
`ai_workspace_handoff_artifacts.json`。文件位于 AI conversation store root，测试或受控运行可通过
`CV_AI_HANDOFF_STORE_ROOT` 隔离。写入使用同目录临时文件、durable flush 与原子 replace/move；进程重启后重载，
加载失败时 fail closed。artifact store 只保存短期候选及审计状态，不是 Project store。

### 2. Endpoint 与状态机

已实现 authenticated Engineer/Admin HTTP 入口：create、按 artifact/Build/operation lookup、consume reserve、
acknowledge 与 reject。create 以 `ownerHash + handoff_create + clientOperationId` 幂等；consume/acknowledge
以同一 consume operation 两阶段确认，响应未知时必须 lookup/reconcile，禁止盲目创建或重复写入。

```text
available
→ consuming
→ consumed

available/consuming
→ rejected

available/consuming
→ expired
```

`available` 与 `consuming` 是 active 状态。TTL 到期后转为 `expired`；`consumed`、`rejected`、`expired`
保留 24 小时用于终态审计，并受 512 条总容量上限约束。`consuming` 会随磁盘文档恢复，允许同一 operation
继续协调，不会因进程重启自动猜测为成功或失败。

### 3. Workspace 与保存 authority

- Apply Preview 只从 canonical terminal Build 创建 artifact，并复核 owner、Session、Plan、Build、revision、
  Project baseline 与 candidate fingerprint。
- AI route 在打开工作区前 dispose；AI 不 import、不持有也不调用 Canvas。Workspace 是唯一 staged draft 与
  Canvas owner。
- Workspace 在 reserve 前和 staging 前都检查 dirty 状态；已有未保存修改时不覆盖。existing Project 还会复核
  `PersistenceRevision` 与 canonical flow hash，冲突时返回 AI 基于最新基线重新 Build。
- new Project 在显式保存前没有正式 Project id；artifact consume 只得到 staged unsaved draft。
- consume receipt 固定 `projectSaved=false`，只证明 Workspace 已接收候选，不代表 Project 已保存。
- 正式保存仍由既有 Workspace persistence 发起既有 Project POST/PUT，并最终进入
  `ProjectSaveCoordinator`；没有新增 AI Project save endpoint。
- 接收不会自动保存、自动运行或自动部署，也不会创建 Runtime Package、Station 或 Inspection authority。

## G1 阶段历史：明确未实现

以下内容保留 G1 退出时的阶段边界，只是历史记录，不代表 G4 完成后的 live 实现状态。

G1 当时要求代码中不得出现 Handoff endpoint、artifact store、Workspace consume port、Canvas replace、Apply Preview 页面或 Project 保存接入。B6 当时状态只能写为：

```text
F06_B6_HANDOFF_ADR=APPROVED_IMPLEMENTATION_DEFERRED
```
