# F06 G1：AI 合同、安全身份与唯一 Owner 地基

## 当前状态

```text
REPORT_STATE=LOCAL_GATES_PASS_REMOTE_CI_PENDING
INITIAL_SHA=267cb8c7e6f25eb666f44ed6873c14678a6304d8
IMPLEMENTATION_COMMIT_SHA=PENDING
REMOTE_SHA=PENDING
F06_G2_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
```

本报告只关闭 G1 地基，不表示完整 AI 产品页、Handoff、默认入口切换或 Legacy AI 退役已经完成。Remote CI/Final Gate 成功前不得写 `F06_G1_STATE=DONE`。

## 1. 稳定线审计与工作树保护

- 2026-07-29 最终重试 `git fetch origin --prune` 成功。
- `origin/studio-ui-next=267cb8c7e6f25eb666f44ed6873c14678a6304d8`，fetch 后与本地提交前 HEAD 为 `0/0`。
- `origin/codex初稿=bea404394ac8cf403cca719c1990c426414a06c2`，自 G0 记录点无新增 commit。
- 重新审计 stable-only 的 `6c0fa1f0`、`988681cf`、`0ebbb6ec`：分别是测试分类/Operator 元数据/参数能力方向；没有可证明属于 F06 owner、安全、operation recovery 或 Project baseline 的最小补丁。本轮实际迁入：`NONE`。
- 用户 `appsettings.json`、8 个 `packages.lock.json` 与 Playwright report 未编辑、未暂存；最终 hash 在提交前复核。

## 2. B1-B5 关闭证据

| 阻断 | 关闭实现 | 测试证据 |
|---|---|---|
| B1 Session 无 owner | AgentRun 同源 owner hash；`OwnerHash` 持久化；owned create/list/get/mutation/delete；非 owner 404 | 双用户隔离、非 owner Session/Run/operation、legacy unowned 不可认领 |
| B2 缺 Session HTTP | Session create/paged list/detail/delete/workspace mutation + narrow DTO | Session CRUD、revision conflict、公开 snapshot |
| B3 mutation policy | Session/Plan/Build/cancel/operation 使用 Admin/Engineer policy；Build 保留 Project edit policy | Admin/Engineer/Operator/401 矩阵 |
| B4 create 不可恢复 | durable receipt，唯一键 `owner + kind + clientOperationId`，payload fingerprint，7 天/1000 条，lookup/replay | 同请求幂等、不同 fingerprint 409、response-loss lookup/reconcile |
| B5 Project baseline | Application Service 重载 Project；读取 `PersistenceRevision`；canonical hash；server Flow 替换 client draft | revision/hash mismatch、server baseline/canonical Flow、receipt/terminal association |

历史 Session 隔离策略：`OwnerHash=null` 只留给 Legacy；Next list/get/mutation/delete 均不可见，不接受 sessionId 认领。未来若迁移，必须另行设计带可信证明的 import，不在 G1 自动补 owner。

## 3. 权限与 redaction

| 主体 | Session / Plan / Build / Cancel | 非 owner 资源 |
|---|---|---|
| Admin | 允许 | 404 |
| Engineer | 允许 | 404 |
| Operator | 403 | 403（policy 先拒绝） |
| 未认证 | 401 | 401 |

公开 DTO 与 AgentRun redactor 测试覆盖 system prompt、Reasoning/chain-of-thought、Authorization/API key/token、绝对路径、IP、PLC 地址、raw attachment/base64、内部异常和非 public tool payload。Next decoder 对未知字段 fail closed。

## 4. Flag、route 与唯一 Owner

- `StudioOptions.AiWorkbenchCapabilityEnabled=false`；Host 只注入 `Studio2.AiWorkbench`，不双写 `Studio2.AiPanel`。
- `/ai` 与 `/projects/:id/ai` 共用 lazy `AiWorkbenchPage`；route/导航均要求 Admin/Engineer + flag；safe returnTo 继续拒绝 scheme、protocol-relative URL、反斜杠、编码 slash 与 `..`。
- `AiWorkbenchPage → AiSessionOwner → AiWorkbenchApi → shared ApiTransport` 是唯一 create/hydrate/reconcile 写入口。
- capability 内无 direct fetch、WebMessage、EventSource、同步 `/api/ai/agent-plan` fallback、第二 EventBus/HTTP、Pinia authority、localStorage 或 Legacy CSS。
- 20-cycle create/dispose 后 request/stream/timer/subscription 全部为 0；flag off 跳转 `/forbidden` 且零 AI owner DOM。

## 5. Handoff 边界

[ADR F06-G1：AI Workspace Handoff Artifact](./ADR-F06-G1-Workspace-Handoff-Artifact.md) 已批准 artifact identity、owner/session/plan/build/baseline、candidate fingerprint、expiry/consume receipt、Workspace authority、敏感字段排除与 G4 停止条件。

当前没有 Handoff endpoint、store、Workspace consume、Canvas replace、Apply Preview 或 Project save integration：

```text
F06_B6_HANDOFF_ADR=APPROVED_IMPLEMENTATION_DEFERRED
```

## 6. 本地门禁

| 门禁 | 结果 |
|---|---|
| StudioUI lint | PASS |
| StudioUI typecheck | PASS |
| AI/route/architecture targeted unit | PASS，6 files / 20 tests |
| StudioUI full unit（隔离 committed formal-default appsettings） | PASS，92 files / 540 tests |
| StudioUI production build | PASS，347 modules |
| bundle gate | PASS；AI lazy JS 13.47 kB，gzip 4.91 kB；CSS 0.40 kB，gzip 0.23 kB |
| Product AI directed | PASS，57/57（Conversation、operation receipt、BuildFromPlan parity） |
| AgentRunEndpointsTests | PASS，57/57 |
| services regression | PASS，514/514 |
| desktop endpoints | PASS，346/346 |

desktop endpoints 第一次执行为 345/346，唯一失败是非 AI `ProjectGlobalVariableEndpointsTests` 的随机临时目录访问拒绝；该用例单独复跑 1/1，通过后完整 346 项再次执行并全部通过。失败与两次通过的 TRX 均保留在 `.tmp/test_results/`，未以单次重试替换事实记录。

## 7. Browser 与真实 WebView2 分层证据

Browser fixture：`.tmp/studio-ui-next/f06/evidence/f06-g1-browser-evidence.json`。

- ready/loading/error/project-bound/flag-off 五状态；1920×1080 与 1366×768；compact/comfortable；零水平溢出、零 console error。
- flag off 路由为 `/forbidden`，`aiOwnerMounted=false`。

真实 Debug WebView2：`.tmp/studio-ui-next/f06/webview2-ai-v2/evidence/`。

- `hostKind=desktop-webview2`、`Studio2.AiWorkbench=true`、`#/ai`、一个 ready owner。
- 真实 `POST /api/ai/sessions`；无 `/api/ai/agent-plan` fallback；零 console/page/request failure。
- 端口、进程、数据库、conversation/AgentRun store 与 WebView2 user-data 全部清理。
- screenshot SHA-256：`A42D3506AA04A29A5DD54CD59BB1951987AE1CFE5EE395BB0898305F7CAD5FD4`。

这些证据证明 G1 flag/route/HTTP/owner skeleton，不证明真实 LLM 质量、G2 Plan UI、G4 Handoff、Release WebView2、真实 Windows DPI、相机/PLC/Station 或生产验收。

## 8. Remote closure

首次实现提交与 push 后运行 Remote CI/Final Gate。通过前保持：

```text
F06_G1_STATE=REMOTE_CI_PENDING
F06_G2_ENTRY=BLOCKED
F06_G2_IMPLEMENTATION=FORBIDDEN
```
