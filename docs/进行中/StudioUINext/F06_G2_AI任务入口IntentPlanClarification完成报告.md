# Studio UI Next F06 G2 AI 任务入口、Intent、Plan 与 Clarification 完成报告

> 状态：`DONE`
>
> Initial SHA：`b393b9e7e3693708a3bd09e61cf8eaf6a08e754d`
>
> Implementation SHA：`a1af5f5bc1b493487f7ecb6bb9adb2368068d8a6`
>
> Final / Remote SHA：`44bfd479e7d3e538de066e49abe91a8828372aa8`
>
> Final Remote CI：run `30447730377`，Final Gate job `90570574269`，`PASS`
>
> 模型证据口径：`MODEL_MODE=RULE_FALLBACK`，`REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED`

## 1. 范围结论

G2 已在 G1 的唯一 `AiSessionOwner` 内完成以下产品闭环：

- `/ai` 与 `/projects/:id/ai` 共用同一 capability 与组件体系；
- owner-bound Session 创建/恢复与 canonical Project baseline 加载；
- Intent Router、durable Plan Run、operation lookup、replay、SSE、cancel、reconnect 与 terminal reconcile；
- Strict / Draft 业务表达、canonical field 去重、单批最多 3 个澄清问题；
- optimistic / confirmed answer 分离；
- expected revision、`clientMutationId`、Snapshot 409 canonical reconcile；
- 后端权威 Readiness Preview 与只读 G3 占位状态；
- 默认隐藏内部 run/sequence/trace，诊断区只显示 redacted public timeline。

本轮终点是 Plan Ready。未实现或调用 Build、资源绑定、Pending Parameter、Apply Preview、Handoff、Workspace consume、Canvas 写入、Project save、Formal Run、模型管理、默认入口切换或 Legacy AI 退役。

## 2. 产品形态与布局

页面采用“工作台主导，会话辅助”的工业任务界面：

1. 顶部页头只给出 capability 身份、当前业务状态与 route scope；
2. canonical 工程上下文独立成带状信息区，明确工程名称、保存 revision 与绑定状态；
3. 阶段条持续回答当前阶段、阻断数、下一步与唯一主操作；
4. 空闲态直接聚焦任务描述和 Strict / Draft 选择；
5. 规划阶段显示公开进度，不展示 raw payload、token 或推理过程；
6. 规划结果以任务理解、推荐路线、关键步骤、假设、验收标准和阻断项为主体；
7. 澄清区只呈现最高价值的少量问题，推荐项带简短依据；
8. 工程诊断放在渐进披露区域，不与业务主流程争夺视觉层级。

1920×1080 comfortable、1366×768 compact 与 1366×768 comfortable 均由 Playwright 覆盖；两列结果工作区在窄视口回落为单列，没有横向溢出。

## 3. 状态与 Action Model

纯 `reducer / projection / actionModel` 统一投影状态、说明、阻断原因、主操作、次操作和下一步。核心状态如下：

| 状态 | 用户语义 | 主操作 |
|---|---|---|
| `idle` | 等待描述视觉任务 | 理解并规划任务 |
| `session-loading` | 正在恢复安全会话上下文 | 无 |
| `intent-routing` | 正在判断任务意图与成熟度 | 无 |
| `planning` | Plan Run 正在生成公开规划 | 取消规划 |
| `clarifying` | 需要确认少量关键条件 | 提交答案或采用推荐项 |
| `plan-blocked` | 方案仍有硬阻断 | 预览构建条件 |
| `plan-ready` | 方案已具备构建条件 | 受控 G3 占位，不执行 Build |
| `cancelling` | 正在请求后端终止规划 | 无 |
| `cancelled` | 规划已由后端确认取消 | 开始新任务 |
| `recovering` | 正在通过 replay 补齐状态 | 无 |
| `session-conflict` | Snapshot revision 已冲突 | 重新协调 |
| `plan-failed` | Plan Run 终态失败 | 重试规划 |
| `offline-or-service-unavailable` | 本地服务不可用 | 重新连接 |

`submitTask`、`retryIntent`、`startPlan`、`cancelPlan`、`answerClarification`、`acceptRecommendedAnswers`、`previewReadiness`、`reconcile` 与 `startNewTask` 均由 action model 决定。不存在执行 Build 的 action。

## 4. Owner、SSE 与恢复

G2 延续单一 `AiSessionOwner`，内部使用窄 `agentRunStreamAdapter` 管理文本 SSE 与 `AbortController`：

```text
create / operation lookup
  -> replay current events
  -> stream after last sequence
  -> strict decoder
  -> reducer validates session/run/plan/generation/sequence
  -> gap pauses live consumption and replays
  -> terminal reconcile reloads replay and Session Snapshot
```

保护规则：

- create outcome 不明时先按 durable `clientOperationId` lookup，不盲目创建第二个 Run；
- stale session/run/plan/generation event 不改变 canonical projection；
- duplicate sequence 被忽略；
- sequence gap 触发 replay 补齐；
- terminal 后迟到的非终态事件被忽略；
- cancel 与 completion race 以后端 terminal reservation 为准；
- route change、logout、401 freeze 与 owner dispose 会终止 stream、timer、request 与订阅；
- Vue 组件不持有 `AbortController`、SSE reader 或通用 API transport。

## 5. Clarification、Snapshot 与 Readiness

- 问题先按 canonical field 去重，再按价值排序并限制为最多 3 个；
- 推荐选项显示公开依据，不把模型推断伪装成用户确认；
- 用户输入先进入 optimistic projection，后端 Snapshot 成功后转为 confirmed；
- mutation 始终携带 `expectedRevision` 与唯一 `clientMutationId`；
- 409 响应加载 `latestSnapshot`，Owner 用 canonical revision 和答案集合重新协调；
- Readiness 由 `/api/ai/agent-plan/readiness-preview` 重新计算；
- 前端只消费 `buildReadiness.canBuild`，不会自行宣布 `canBuild=true`；
- 资源缺口只展示摘要，本轮没有资源选择或路径编辑入口。

## 6. 最小 G1 合同修复

真实 G2 消费暴露了两个必须收口的 G1 缺口，均保持原有 authority：

1. `AiSessionSnapshotV1` 原公开 DTO 缺少 G2 必需的 requirement mode、答案集合、answer revision、readiness 与 terminal sequence。修复只扩展现有公开 mapper，继续复用 `AgentRunEventRedactor`，并过滤危险 canonical key / identifier；没有新增 endpoint 或私有 Snapshot authority。
2. Windows 真实宿主中，operation receipt 的 durable `File.Replace` 可能遭遇短暂 sharing violation，导致 `Reserve` 成功后 `MarkCreated` 失败。修复只在现有原子持久化函数内对 `IOException` 做最多 5 次有限退避；权限与 JSON 错误仍立即 fail closed。新增独占目标文件后释放的定向测试，证明短暂占用可恢复。

没有新增第二 storage、第二 operation framework、第二 redactor、第二 HTTP transport 或第二 Owner。

## 7. 本地验证

| 门禁 | 结果 |
|---|---|
| StudioUI lint | PASS |
| StudioUI typecheck | PASS |
| AI / F06 architecture targeted Vitest | PASS，6 files / 24 tests |
| StudioUI full unit（committed tree 干净归档） | PASS，94 files / 554 tests |
| F06 Playwright | PASS，5/5 |
| Product AI directed | PASS，235/235 |
| `AiOperationReceiptStoreTests` | PASS，4/4 |
| Camera restart targeted | PASS，1/1，131 ms |
| Desktop full coverage（受保护 dirty tree） | 675/676；唯一失败为本地正式默认值守卫，目标 camera test 119 ms PASS |
| `AgentRunEndpointsTests` | PASS，57/57 |
| Desktop endpoint regression | PASS，346/346 |
| Desktop G2-related architecture guards | PASS，3/3 |
| services regression | PASS，514/514 |
| StudioUI production build | PASS，364 modules |
| bundle gate | PASS |
| bundle reproducibility | PASS |

工作树 full Vitest 观测为 550 PASS / 4 FAIL；4 个失败只读取受保护的本地 `appsettings.json`，该文件由用户设置 `StudioUiEnabled=true`，而 committed formal-default 断言要求 `false`。因此正式 full Vitest 从 implementation commit 导出的干净归档执行，结果为 94 files / 554 tests 全部通过；没有把工作树配置差异伪装成产品失败或篡改用户配置。

Desktop full coverage 在本地受保护 dirty tree 上执行 676 个测试，675 PASS / 1 FAIL；唯一失败同样是 `DesktopStartupDefaultsAndDpiAuthority_ShouldRemainExplicit` 读取用户本地 `appsettings.json`。本轮新增的 camera restart 用例在该全量负载下 119 ms PASS。最终远端 committed tree 为 676/676 PASS。

## 8. Bundle

- AI route JS：76,018 bytes（Vite 输出 76.01 kB，gzip 21.35 kB）；
- AI route CSS：18,427 bytes（Vite 输出 18.42 kB，gzip 2.81 kB）；
- initial closure：809,936 bytes；
- 全部产物：1,314,585 bytes；
- budget status：PASS；
- reproducibility：PASS。

## 9. Browser 与真实链路证据

### 9.1 Deterministic browser fixture

F06 Playwright 覆盖：

- `/ai` idle、clarifying、plan-ready；
- `/projects/:id/ai` canonical project revision 与超长中文；
- flag off、Operator role fail closed；
- service unavailable；
- 1920×1080 / 1366×768、compact / comfortable；
- keyboard/focus、零横向溢出、零 console/page error；
- 无 Build、Handoff、Apply 或 Workspace save 请求。

截图输出到 `.tmp/studio-ui-next/f06-g2/browser/fc75f752461f/`，每张 PNG 配套 JSON，source SHA 均为 `fc75f752461f4da104f67a58026a72a14806274c`。后续提交只修改测试与报告，PNG 哈希和产品像素未变化：

| 场景 | Viewport / density | PNG SHA-256 |
|---|---|---|
| unbound idle | 1920×1080 / comfortable | `de2caa66166a95da10d6aa5944557a335a08185e9301c3dbfd5fed8851f84cd7` |
| unbound clarifying | 1920×1080 / comfortable | `82e1c4b41a426aee4c38d4b3ee7fe0b9222665f619e1b5835551c67d2e6da71f` |
| unbound plan ready | 1920×1080 / comfortable | `79b7a29b4212d39b6ba9ce61f41c3058cc6c2ad33123c368257bb4c9310bacf8` |
| project-bound long Chinese clarifying | 1366×768 / compact | `d08400adb378975c248bbcb86003dd510d7527fd6cae88b9749f2bc4b8c3d7bf` |
| service unavailable | 1366×768 / comfortable | `1ad38dc420f694bc805fa635b29ebd612346337a4a9a0a49eb3ef28f67933534` |

所有场景均为 `overflow=0`、零 console/page error、零 forbidden G3 request。

### 9.2 真实 Debug Desktop / ASP.NET Core

证据索引：`.tmp/studio-ui-next/f06-g2/real-endpoint/evidence/f06-g2-real-endpoint.json`。

- 真实 Desktop WebView2 + 本地 ASP.NET Core；
- isolated Admin auth；
- Session create 201；
- Intent `actionable_vision_plan`，source=`rule_fallback`；
- Plan Run create 200，operation lookup 200；
- replay 17 个严格递增公开事件，终态 `completed`；
- SSE `text/event-stream` 同时交付 `plan.completed` 与 `run.completed`；
- Plan source=`rule_fallback`，fallback reason=`planner_disabled`；
- readiness 200，后端计算 `canBuild=false`、3 个 hard blocker、4 个待确认项；
- Snapshot revision 0 -> 3；stale mutation 返回 409 和 canonical revision 3；
- cancel endpoint 200，replay terminal=`cancelled`，末事件=`run.cancelled`；
- 无 G3 请求、零 console/page/request failure；
- 实际运行结束后端口、进程、数据库、Conversation/AgentRun/operation store 与 WebView2 user-data 全部清理。

真实 WebView2 截图：`.tmp/studio-ui-next/f06-g2/real-endpoint/evidence/f06-g2-real-webview2-ai.png`，1904×1016，SHA-256 `522b58306a8144aea4290f271114724b8bf70999a01acc07cf245796737d96d4`。

该证据只证明真实本地 HTTP/SSE/持久化与 Debug WebView2 链路，不证明真实 LLM Plan 质量、Release publish、真实 Windows 125% DPI 矩阵或现场硬件。

## 10. 用户 dirty 文件保护

以下既有 dirty 内容未被编辑、清理、stash、暂存或提交：

- `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json`；
- 8 个受保护 `packages.lock.json`；
- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/`；
- 其他与 G2 无关的本地临时内容。

所有提交均使用显式路径清单暂存并核对 staged diff；上述文件未进入任何 G2 commit。

## 11. Remote closure

最终 Remote CI：run `30447730377`，head SHA `44bfd479e7d3e538de066e49abe91a8828372aa8`，`PASS`。

### 11.1 Attempts

| Attempt | Run / SHA | 结果与处理 |
|---|---|---|
| 1 | `30441105088` / `0b6fdba9e01a5a1de5155451b8e94ca48664dfb4` | Guard job `90540374610` 的 Secret Scan 将 CSS class 中的 token-like 子串误报为 compatible token；文件无 secret。类名局部改为 `ai-composer-supporting-text`，不改变 DOM 语义或视觉。 |
| 2 | `30441924658` / `a079b2ba5e61b00e233ae5f77b2aacb56dba0f5b` | Guard、StudioUI、Product、Browser 等通过；Desktop job `90543902418` 为 675/676，camera restart 测试在 1 秒有限等待处 timeout。failed-only rerun（run attempt 2，job `90550186380`）复现同一失败。基线同日成功记录已耗时 780 ms，因此把该测试有限等待统一为同文件既有的 5 秒；生产 camera 逻辑不变。 |
| 3 | `30445054790` / `9e8239c9bda919fab335cafd5ae5540391f0c14a` | Desktop 676/676、Browser 与其余主体 jobs 通过；Product job `90554064709` 为 3845 PASS / 1 FAIL / 2 skipped。失败测试用 `Task.Delay` continuation 释放独占文件锁，在全量 suite 线程池压力下晚于 store 的有限退避窗口。改为专用后台线程释放锁，保留真实 sharing violation，生产 retry budget 与 fail-closed 语义不变。 |
| 4 | `30447730377` / `44bfd479e7d3e538de066e49abe91a8828372aa8` | 所有要求门禁与 Final Gate 通过。 |

### 11.2 Final jobs

| Job | ID | 结果 |
|---|---:|---|
| Guard & Operator Catalog | `90562130713` | PASS |
| StudioUI Quality Gates | `90563055199` | PASS |
| Product Tests | `90563055234` | PASS；3848 total / 3846 passed / 2 skipped；performance budget 10/10 |
| Desktop Tests | `90563055153` | PASS；676/676；camera restart 625 ms |
| Legacy UI & StudioUI Browser | `90563055162` | PASS |
| Contracts & Vision Agent | `90563055262` | PASS |
| Detection / Measurement / Data | `90563055227` | PASS |
| Operator Industrial Gate | `90563055161` | PASS |
| OperatorLibrary Package & Benchmark | `90563055147` | PASS |
| Coverage Summary | `90570363653` | PASS |
| Final Gate | `90570574269` | PASS |

`Code Quality` 按 `workflow_dispatch` 条件正常 skipped；未提供 release version，因此 `Release Build` 与 `Create Release` 正常 skipped，不属于 G2 门禁失败。

## 12. 当前阶段状态

G2 本地、真实链路、Browser、bundle 与 Remote Final Gate 已完成。进入 G3 仍需独立评审，不授权本轮继续实现：

```text
F06_G2_STATE=DONE
F06_INTENT_PLAN_CLARIFICATION=COMPLETE
F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
F06_G3_ENTRY=AWAITING_REVIEW
F06_G3_IMPLEMENTATION=FORBIDDEN
F06_HANDOFF_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_AI_RETIREMENT=NOT_APPROVED
```
