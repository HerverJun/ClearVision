# Studio UI Next F06 G3 Build、Validation、参数与资源完成报告

> 状态：`DONE`
>
> 任务 Initial / Remote Baseline SHA：`f7ae453fb58207d7dea0bfcb191a0bbbdff5ed00`
>
> G3 产品 Checkpoint SHA：`526f10fa62f9c838552c8fda55fde6b3b3ab9532`
>
> Checkpoint Remote Closure SHA：`3f64ceec301222792213c777c256a77959e52b09`
>
> 恢复后的产品基线 SHA：`85f79bc59d6495360905a09a3e3b8a55e95aeb16`
>
> Implementation / Final Product / Remote CI SHA：`d93831567b7d426e8f4da48315142f1c2f999c69`
>
> Docs-only closure SHA：见本报告所在提交；本报告不自引用其提交 SHA
>
> 模型证据口径：`MODEL_MODE=RULE_FALLBACK`，`REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED`

## 1. 范围结论

F06 G3 已在 G2 的唯一 `AiSessionOwner`、既有 HTTP/SSE transport、后端 Session/AgentRun authority 和 Project baseline 合同内完成：

- Plan Ready 到 Build create、operation reconcile、replay、SSE、Validation / DryRun、Pending Parameter、Camera Resource Decision、Revalidate 与 Build Ready/Blocked 的完整旅程；
- response loss、duplicate、sequence gap、stale event、迟到 terminal、cancel race 与 terminal Snapshot 恢复；
- typed 参数建议、草稿、确认、分组条件和服务端 revision 闭环；
- 仅支持 `camera_binding` 的后端只读资源 authority 与 `resourceRevision` 闭环；
- structural validation、dry run、manifest/package readiness、workflow diff、blocker/warning、first-fix recommendation 与只读 ApplyGate 投影；
- Build 工作台状态矩阵、窄视口、长中文、键盘/焦点、失败、取消、恢复、基线冲突和服务不可用证据。

本轮没有实现 Handoff endpoint/store、artifact create/consume、Workspace route handoff、Workspace consume、FlowCanvas/ImageCanvas 写入、Project Save、自动运行/部署、模型管理、Settings、默认入口切换或 Legacy AI 退役。

## 2. Checkpoint Remote CI

产品扩展开始前先验证包含 checkpoint `526f10fa...` 的 clean runner 链路。

| Attempt | Run / Head SHA | 结果与处理 |
|---|---|---|
| 1 | [30516321191](https://github.com/HerverJun/ClearVision/actions/runs/30516321191) / `f7ae453fb58207d7dea0bfcb191a0bbbdff5ed00` | `Legacy UI & StudioUI Browser` 95 PASS / 2 FAIL / 21 skipped，Final Gate 随之失败；其余要求作业通过。失败来自 deterministic F06 fixture 仍使用旧参数条件 nullability、旧 Camera candidate endpoint/过宽 DTO，并且资源 mutation 未返回 canonical mapping 与推进 `resourceRevision`，导致 Build Ready 与 terminal recovery fixture 无法到达预期状态。 |
| 2 | [30517737716](https://github.com/HerverJun/ClearVision/actions/runs/30517737716) / `3f64ceec301222792213c777c256a77959e52b09` | PASS。`3f64ceec...` 只对齐一份 F06 Browser fixture 合同，没有修改产品代码、超时、重试预算、安全语义或后端 authority；所有必需作业与 Final Gate 通过。 |

`526f10fa...` 是 `3f64ceec...` 的祖先。Checkpoint 成功门禁覆盖 StudioUI、bundle、Product、Desktop、Browser、architecture guards、Contracts / Vision Agent 与 Final Gate；之后才继续 G3 产品实现。

## 3. Build identity 与恢复链

Build 继续由唯一 `AiSessionOwner` 串行拥有，一次只允许一个 active Plan 或 Build。Build 接受条件严格绑定：

- `sessionId`、`planId`、`planHash`；
- `answerRevision`、`resourceRevision`；
- new/existing Project baseline、`PersistenceRevision` 与 canonical flow hash；
- Build run / operation identity 与当前 Session Snapshot。

恢复链为：

```text
Build create
  -> create outcome 不明时按 clientOperationId operation lookup
  -> replay 公开事件与 canonical Session Snapshot
  -> SSE 从 last sequence 继续
  -> identity / revision / baseline 校验
  -> Validation / Pending / Resource / Revalidate
  -> Build Ready、明确 Blocked 或后端 terminal
```

response-loss terminal Build 可从 canonical Session Snapshot 恢复，且仍执行原有 event identity 校验；没有为恢复放宽 session、plan、revision、baseline、sequence 或 terminal 约束。Plan replay 的 typed/redacted 数据继续保留。旧 Build 在 Plan、答案、资源或 Project baseline 改变后立即 stale，不会恢复成当前 ApplyGate。

route change、logout、401、owner dispose 后停止 request、SSE、timer 与订阅，资源 ledger 归零；Vue 不直接持有 `EventSource`、`AbortController`、Canvas 或 WebView2 bridge。

## 4. 参数合同

参数闭环保留后端合同和 typed projection：

- 类型、枚举、范围、空值与标量 pending 校验；
- All / Any、required / enabled / disabled；
- at-least-one、mutually-exclusive 与条件依赖；
- suggested、draft、confirmed 三类值分离；
- 错误可定位到 operator 与 parameter；
- 参数确认推进 `answerRevision`，并使旧 Validation / ApplyGate stale；
- Revalidate 只更新 AI Session candidate，不写正式 Project。

最终修复纠正了 pending scalar 的验证路径，同时保持 Camera 参数由 resource authority 决策；没有使用 `any`、前端强制断言或猜测值绕过服务端合同。

## 5. Camera resource authority

G3 正式资源支持范围冻结为：

```text
SUPPORTED_RESOURCE_DECISION=camera_binding
MODEL_TEMPLATE_CALIBRATION=EXPLICIT_BLOCKER
```

Camera candidate 来自现有 authenticated 后端只读 authority。真实 WebView2 证据中 candidate 只公开 `id`、`displayName`、`isEnabled`，写入只提交 canonical identity 与 `resourceKey`；服务端校验资源存在、唯一、启用、类型和目标参数。

受信任 Camera 决策同时更新 candidate Flow 和 parameter mapping：

```text
Source=camera_binding_authority
Pending=false
deployment blocker=cleared
```

`resourceRevision` 由服务端推进；连续修改、409、response loss、reload 与旧 Build stale 都回到 canonical Snapshot 协调。只读 candidate 查询没有放宽 `CanOperateHardware`。

Model、Template、Calibration 缺少安全 authority，继续显示明确 blocker；没有自由文本路径、假选择器或前端私有资源持久化。

## 6. Validation、DryRun 与只读 ApplyGate

canonical `OperatorFlowDto` 被投影到既有 validation/dry-run draft schema，保留：

- operator type 与临时 ID；
- typed parameters；
- GUID topology；
- input/output port names；
- workflow diff 与 package/manifest readiness 所需结构。

结构化结果覆盖 structural validation、dry run、manifest/package readiness、workflow diff、blocker/warning、first-fix recommendation、pending parameter 与 missing resource。Camera 决策和参数确认后重新校验可到达真实 `build-ready`；没有跳过 validator，也没有在前端伪造 Ready。

ApplyGate 仍是只读投影，只说明 candidate 是否具备未来 Workspace 审核条件；页面不存在 Handoff action、artifact 写入或 Project Save。

## 7. 产品状态与界面

G3 状态投影覆盖：

```text
build-starting
building
validating
parameters-pending
resources-pending
revalidating
build-blocked
build-ready
build-failed
build-cancelling
build-cancelled
recovering
baseline-conflict
unknown-outcome
```

工作台持续显示当前阶段、主要 blocker、下一步与唯一主操作；runId、sequence、raw event、token 与 Trace 默认隐藏。参数和资源使用可扫描列表，1366x768 下仍能看到阶段、主要阻断和主操作。

## 8. 本地门禁

最终产品证据均绑定 `d93831567b7d426e8f4da48315142f1c2f999c69`。

| 门禁 | 结果 |
|---|---|
| StudioUI lint / typecheck | PASS |
| G3 targeted Vitest | PASS，28/28 |
| `AiSessionOwner` | PASS，18/18 |
| StudioUI full unit，implementation archive | PASS，588/588 |
| StudioUI production build | PASS，377 modules |
| bundle reproducibility / gate | PASS |
| Product AI | PASS，1282/1282 |
| Services regression | PASS，514/514 |
| Product full | PASS，3857 passed / 2 existing skipped |
| `AgentRunEndpoints` | PASS，64/64 |
| Desktop endpoint regression | PASS，353/353 |
| AI / StudioUI architecture guards，implementation archive | PASS，16/16 |
| Desktop full，implementation archive | PASS，683/683 |
| F06 Browser fixture | PASS，12/12；16 PNG + 16 JSON |
| Debug Desktop build | PASS，0 warning / 0 error |
| Real Debug Desktop / WebView2 | PASS |

Implementation archive：`.tmp/studio-ui-next/f06-g3/implementation-d9383156/`；归档文件：`.tmp/studio-ui-next/f06-g3/implementation-d9383156.zip`。

## 9. Browser 证据索引

证据目录：`.tmp/studio-ui-next/f06-g3/browser-d9383156/evidence/`。

- 16 个 PNG 与 16 个同名 JSON；
- 每份 JSON 的 `sourceSha` 均为 `d93831567b7d426e8f4da48315142f1c2f999c69`；
- 1920x1080 comfortable 与 1366x768 compact/comfortable；
- 两 route、Admin/Engineer、Operator/flag-off、keyboard/focus 与超长中文由 12/12 Playwright 覆盖；
- 所有记录 `overflow=0`，零 forbidden request，零 console error，零 page error；
- `WINDOWS_DPI=NOT_PERFORMED`，不能把 Chromium viewport/DPR 当成真实 Windows DPI。

截图场景索引：

| 场景 | Viewport / density |
|---|---|
| Build building | 1920x1080 / comfortable |
| parameters pending | 1920x1080 / comfortable |
| resources pending | 1920x1080 / comfortable |
| Validation / revalidating | 1366x768 / compact |
| Build Ready / read-only ApplyGate | 1920x1080 / comfortable |
| failed / cancelled / terminal recovery | 1366x768 / compact |
| baseline conflict | 1366x768 / compact |
| unknown outcome | 1920x1080 / comfortable |
| idle / clarifying / Plan Ready | 1920x1080 / comfortable |
| project-bound long Chinese | 1366x768 / compact |
| service unavailable | 1366x768 / comfortable |

## 10. 真实 Desktop / WebView2 证据

正式 G3 WebView2 证据目录：`.tmp/studio-ui-next/f06-g3/webview2/implementation-d9383156/evidence/`。

主索引：`studio-ui-webview2-f06-f06-g3-implementation-d9383156.json`。

- `status=PASS`，`sourceSha=d93831567b7d426e8f4da48315142f1c2f999c69`；
- Session `a7118930c2ef489e9a45dd0dfa01e412`；
- `revision=12`、`answerRevision=2`、`resourceRevision=1`；
- `lifecycleState=build_ready`；
- operation lookup 1 次、replay 4 次、SSE 2 次；
- request 40 次，forbidden 0；
- console/page/request error 0，horizontal overflow 0；
- Camera candidate count 1，resource write 200；
- `MODEL_MODE=RULE_FALLBACK`，`REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED`。

截图：

| 状态 | 文件 | 像素 | SHA-256 |
|---|---|---:|---|
| cancelled | `f06-f06-g3-implementation-d9383156-cancelled.png` | 1584x936 | `6e097904509c57a08648fd8653f6f8ae5e76c4f64fa799e37df0fe007607b6fd` |
| ready | `f06-f06-g3-implementation-d9383156-ready.png` | 1584x936 | `7fd20b7217a5c23a583d38ad23d69772242e992272ad5b89f1792a570b0366b3` |

清理索引：`studio-ui-webview2-f06-g3-implementation-d9383156-cleanup.json`。Desktop/WebView2 进程、HTTP/CDP 端口、runtime root、WebView2 user-data、数据库与 Conversation/AgentRun store 清理均 PASS，环境已恢复。

该证据证明真实 Debug Desktop、WebView2、本地 ASP.NET Core、HTTP/SSE、持久化与清理链路；不证明 Release WebView2、完整 Windows DPI 矩阵、publish/no-Node、真实 LLM 产品质量或现场 Camera/PLC/Station。

## 11. Implementation Remote closure

最终 Remote CI：[run 30540442828](https://github.com/HerverJun/ClearVision/actions/runs/30540442828)，head SHA `d93831567b7d426e8f4da48315142f1c2f999c69`，`PASS`。

| Job | ID | 结果 |
|---|---:|---|
| Guard & Operator Catalog | `90863712506` | PASS |
| Contracts & Vision Agent | `90864509727` | PASS |
| Desktop Tests | `90864509729` | PASS |
| Detection / Measurement / Data | `90864509748` | PASS |
| StudioUI Quality Gates | `90864509750` | PASS |
| Legacy UI & StudioUI Browser | `90864509757` | PASS |
| Operator Industrial Gate | `90864509790` | PASS |
| OperatorLibrary Package & Benchmark | `90864509800` | PASS |
| Product Tests | `90864509803` | PASS |
| Coverage Summary | `90872200375` | PASS |
| Final Gate | `90872391137` | PASS |

`Code Quality` 按 `workflow_dispatch` 条件正常 skipped；没有 release version，因此 `Release Build` 与 `Create Release` 正常 skipped，不属于 G3 门禁失败。

GitHub check annotations 仍提示 actions Node.js 20 deprecation（runner 强制使用 Node.js 24）以及 checkout post-job `git` exit 128。两者发生在 action runtime/post-job 清理层；所有上述 required jobs 和 Final Gate 的 conclusion 均为 `success`，不构成 G3 产品或测试失败。本轮不以 G3 docs-only 收口扩权修改共享 CI。

## 12. 受保护 dirty 文件

以下既有内容未被编辑、清理、stash、revert、暂存或提交：

- `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`；
- 8 个受保护 `packages.lock.json`；
- 5 个既有 `ClearVision.Product/test_results/*benchmark*` / quality report；
- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/`。

产品实现提交后的工作树只包含上述受保护项目。Docs-only 收口使用显式文档路径暂存，受保护内容不进入提交。

权威 F06 计划已检查：其中没有独立的 docs-only `F06_G3_*` 当前状态字段，只有历史 G2 状态块；为避免重写共享计划，本轮不修改该文件，G3 当前状态以本完成报告为准。

## 13. 阶段状态

G3 的本地门禁、Browser、真实 Debug WebView2、Implementation Remote CI 与 Final Gate 已完成。G3 具备提交评审条件，但这不自动批准 G4；Handoff、artifact、Workspace consume、Canvas 写入和 Project Save 继续禁止。

```text
F06_G3_STATE=DONE
F06_BUILD_VALIDATION_PARAMETERS_RESOURCES=COMPLETE
F06_SUPPORTED_RESOURCE_DECISION=camera_binding
F06_OTHER_RESOURCE_TYPES=EXPLICIT_BLOCKER
F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
F06_G4_ENTRY=AWAITING_REVIEW
F06_G4_IMPLEMENTATION=FORBIDDEN
F06_HANDOFF_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
```
