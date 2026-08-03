# ClearVision Studio UI Next F08 Final：工程收口与远程准入报告

本报告是 F08-R 最终工程状态的唯一收口记录。旧 R1、G7 和完成报告中的结论全部保留用于审计追踪；它们的当前状态由本报告取代。

## 1. 当前状态

本地工程门禁已经完成，Remote CI 尚未在本报告初始提交上触发，故暂记为等待远程审计：

```text
F08_FINAL_CLOSURE=AWAITING_REMOTE_CI
INITIAL_SHA=dc4eb93ea35adc2d277d90501179116747f68058
SOURCE_EVIDENCE_SHA=a7782d85e27adfa82d1cfac6f907d10d53cf16bc
FINAL_DOC_SHA=PENDING_FINAL_DOC_COMMIT
F08_ORIGINAL_DONE_CLAIM=SUPERSEDED_BY_FINAL_AUDIT

F05_ROOT_CAUSE=STALE_EXPECTATION_AFTER_FINITE_SSE_RECONNECT
F05_FIX_CLASSIFICATION=TEST_BUG_FIXED
F05_CONTINUOUS_REPEAT=20/20_PASS

PRODUCT_FAILURE_ROOT_CAUSES=SHARED_STATE_LEAK;TEMP_OR_DATABASE_COLLISION;ORDER_DEPENDENCY;TIMEOUT_RACE
PRODUCT_FIX_CLASSIFICATION=ASYNC_LIFECYCLE_FIXED;TEST_ISOLATION_FIXED
PRODUCT_FULL_RUN_1=3872_PASS / 2_SKIP / 0_FAIL
PRODUCT_FULL_RUN_2=3872_PASS / 2_SKIP / 0_FAIL
PRODUCT_FULL_RUN_3=3872_PASS / 2_SKIP / 0_FAIL
PRODUCT_FULL_RUN_4=3872_PASS / 2_SKIP / 0_FAIL
PRODUCT_FULL_RUN_5=3872_PASS / 2_SKIP / 0_FAIL

F03_WORKSPACE=54/54_PASS
BROWSER_FULL_RUN_1=141_PASS / 26_SKIP / 0_FAIL
BROWSER_FULL_RUN_2=141_PASS / 26_SKIP / 0_FAIL
BROWSER_FULL_RUN_3=141_PASS / 26_SKIP / 0_FAIL
BROWSER_UNEXPECTED_FAILURES=0

SERVICES=516/516_PASS
DESKTOP=772/772_PASS
DESKTOP_ENDPOINTS=423/423_PASS
VIRTUAL_STATION=39/39_PASS
ARCHITECTURE_GUARD=9/9_PASS
BUNDLE_GATE=PASS
BUNDLE_REPRODUCIBILITY=PASS

REAL_WEBVIEW2=12/12_PASS_REUSED
RELEASE_PUBLISH=PASS_REUSED
LOCAL_NO_NODE_PROCESS_TREE=PASS_REUSED

ARTIFACT_MANIFEST_SHA256=8881bf567ea6b55d6fcb8724759621b508a90aebb2c12a14a5be6278add5fddd
ARTIFACT_REHASH_ERRORS=0

REMOTE_AUDIT_BRANCH=audit/f08-final-a7782d85
REMOTE_AUDIT_SHA=PENDING_FINAL_DOC_COMMIT
REMOTE_CI_RUN_URL=PENDING
REMOTE_CI_STATE=AWAITING

RUN_ID_AUTHORITY_DECISION=ABSENT_RETURN_NULL
SESSION_ID_RUN_ID_CONFLATION=REMOVED
WORKSPACE_LIFECYCLE_FIX=PRESERVED

F08_ENGINEERING_STATE=READY_FOR_REMOTE_AUDIT
F08_PLAN_STATE=READY_FOR_REMOTE_AUDIT
PRODUCTION_ACCEPTANCE=NOT_GRANTED
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

`F08_ORIGINAL_DONE_CLAIM=SUPERSEDED_BY_FINAL_AUDIT`。`AWAITING_REMOTE_CI` 不是最终准入结论；只有远程审计分支上的真实 workflow 通过后，才将本段更新为 `F08_FINAL_CLOSURE=PASS` 和 `F08_ENGINEERING_STATE=DONE`。

## 2. Evidence manifest

最终 evidence 根目录：

```text
.tmp/studio-ui-next/f08-final/a7782d85e27a/
```

`artifact-manifest.json` 覆盖 251 个 artifact，逐项记录相对路径、大小、SHA-256、source SHA、命令、时间、退出码、PASS/FAIL/SKIP 计数和 Windows/Node/.NET runtime。`artifact-manifest.sha256` 认证 manifest 本身，validation sidecar 的最终结果为：

```text
REHASH_ERRORS=0
SIDECAR_MATCH=TRUE
MISSING_ARTIFACTS=0
SOURCE_SHA_MISMATCH=0
```

第一次 F03 命令把 `CV_F03_VISUAL_EVIDENCE_DIR` 错设到 F08 根下，fixture 按设计拒绝该路径；原始 `49 passed / 5 failed` trace 作为 `diagnostic` artifact 保留。修正为绝对路径 `.tmp/studio-ui-next/f03/a7782d85e27a-final/` 后，F03 JSON 为 `54 expected / 0 unexpected / 0 skipped`。这不是产品失败。

## 3. 本地工程门禁

| 范围 | 最终结果 | 证据 |
| --- | --- | --- |
| StudioUI lint | PASS | `studio-ui/lint.log` |
| StudioUI strict typecheck | PASS | `studio-ui/typecheck.log` |
| StudioUI unit | 128 files / 786 passed | `studio-ui/unit.log` |
| Production build | PASS | `studio-ui/build-production.log` |
| Bundle gate | PASS | `studio-ui/bundle-report/report.json` |
| Bundle reproducibility | PASS | `studio-ui/bundle-reproducibility.log` |
| Services regression | 516/516 PASS | `dotnet/services-regression/services-regression.log` |
| Product full，连续五轮 | 每轮 3874 total / 3872 executed / 3872 PASS / 2 SKIP / 0 FAIL | `dotnet/product-full-r1..r5/*.trx` |
| Desktop full | 772/772 PASS | `dotnet/desktop-full/desktop-full.trx` |
| Desktop endpoints | 423/423 PASS | `dotnet/desktop-endpoints/desktop-endpoints.log` |
| StudioUI architecture guard | 9/9 PASS | `dotnet/architecture/architecture.trx` |
| Virtual Station | 39/39 PASS | `dotnet/virtual-station/virtual-station.trx` |
| F03 Workspace | 54/54 PASS | `browser/f03-full-final/report.json` |
| F05 Continuous isolated | 20/20 PASS | `browser/f05-continuous-repeat/report.json` |
| Browser full，连续三轮 | 每轮 141 PASS / 26 合法 SKIP / 0 FAIL | `browser/browser-full-r1..r3/` |

Product full 的两个 SKIP 是已有明确标记的 `FlowEditorTests.FlowEditor_PageLoad_ShouldDisplayCanvas` 和 `FlowEditorTests.FlowEditor_DragOperator_ShouldCreateNode`，没有新增功能性 skip。

## 4. F05 Continuous 根因与修复

fixture 的 SSE 初始流按正式语义发出 `stateChanged` 和 `resultProduced` 后结束连接；页面 owner 在 route leave 后返回时会先显示合法的“实时恢复中”，随后由 authority state 恢复。旧测试只接受瞬时文案“连续检测中”，因此把合法 reconnect projection 判成失败。正式合同、start/stop、session identity、result projection 和 owner 生命周期没有回滚或绕过。

修复分类为 `TEST_BUG_FIXED`，变更文件为：

```text
ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f05-inspection-run.spec.ts
```

保留的断言包括：连续流使用唯一 `sessionId`、start 只发生一次、结果来自 SSE、route leave 不 stop、返回页面从 authority 恢复。完整 route mount/unmount 重复 20 次均通过；该测试没有 page error、console error 或 request failure artifact，最终 Playwright JSON/HTML 报告均为 `20/0/0`。未采集到的运行时指标不写成额外 PASS。

## 5. Product full 根因与修复

两轮旧 Product full 的非确定性失败均落在测试隔离和异步资源生命周期，不是生产执行合同失败。最终修复和证据覆盖：

- `PerformanceAcceptanceTests` 在构造和 dispose 时 trim `MatPool`，不再污染后续测试；测量性能 warmup 使用实际尺寸并稳定迭代次数。
- OCR performance fixture 在测量前清理共享池并加入完整尺寸 warmup，结束后由 disposable 生命周期释放大图资源。
- `ProjectServiceTests` 为每个 fixture 使用唯一 transaction root，并在 dispose 时清理，避免 SQLite/文件路径碰撞。
- Phase42 operator tests 进入既有 performance collection，消除共享内存池的顺序竞争。
- route guard 单测只修正合理的异步完成等待预算，不改变产品路由合同。

相关文件：

```text
ClearVision.Product/tests/ClearVision.Product.Tests/Integration/PerformanceAcceptanceTests.cs
ClearVision.Product/tests/ClearVision.Product.Tests/Integration/MeasurementPerformanceBudgetAcceptanceTests.cs
ClearVision.Product/tests/ClearVision.Product.Tests/Operators/OcrRecognitionOperatorTests.cs
ClearVision.Product/tests/ClearVision.Product.Tests/Operators/Phase42MeasurementAndSignalOperatorTests.cs
ClearVision.Product/tests/ClearVision.Product.Tests/Services/ProjectServiceTests.cs
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/tests/unit/auth/routeGuard.spec.ts
scripts/run-tests-measurement-performance.ps1
```

最终五轮的 total、executed、PASS 和合法 SKIP 完全一致；没有通过无限重跑、降低测试数、串行化整个仓库或新增 skip 掩盖泄漏。

## 6. 原 F03 37 项最终冻结

来源为旧 G7 的 `17 PASS / 37 FAIL` 集合。分类只使用本计划允许的四类；最终结果均为当前 source SHA 上 F03 全量通过。`changed file` 是导致该失败恢复或合同更新的最小相关文件；完整 hash 和 trace 位于 manifest。

| # | test name | original failure | final classification | root cause | changed file | final result |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | node selection, move, copy/paste, undo/redo, delete and focus/IME gates stay scoped | 节点编辑断言失败 | PRODUCT_REGRESSION_FIXED | Workspace/Canvas 最小轨道与投影修复 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 2 | pointer wiring creates and disconnects connections with stable feedback | pointer/连线状态未稳定 | PRODUCT_REGRESSION_FIXED | Workspace/Canvas 布局与状态投影 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 3 | G3 Inspector follows empty, node, multi-node and connection selection from Canvas | Inspector 选择态失败 | PRODUCT_REGRESSION_FIXED | Canvas selection projection | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 4 | G3 Inspector edits primitive, slider and nullable parameters with validation/history/focus isolation | 参数编辑/校验失败 | PRODUCT_REGRESSION_FIXED | Workspace 参数区可用尺寸与投影 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 5 | G3 connection Inspector selects endpoints and disconnects through the typed command | connection Inspector 断言失败 | PRODUCT_REGRESSION_FIXED | Canvas/Inspector 共同布局回归 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 6 | G3 Inspector shows metadata missing without enabling parameter writes | metadata missing 状态断言失败 | PRODUCT_REGRESSION_FIXED | Inspector 状态投影回归 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 7 | G3 Inspector shows metadata decode failure without enabling parameter writes | metadata decode error 状态失败 | PRODUCT_REGRESSION_FIXED | Inspector fail-closed 投影回归 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 8 | G4 Preview and ImageCanvas render artifacts, probe pixels and commit ROI once with undo redo | Preview/ROI 断言失败 | PRODUCT_REGRESSION_FIXED | Preview 工作区最小轨道修复 | `PreviewPanel.vue`; `f03-workspace.spec.ts` | PASS |
| 9 | G5 GET PUT GET saves one canonical payload and preserves null, falsy and opaque values | 保存 payload 断言过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | 正式保存/nullable identity 合同已更新 | `f03-workspace.spec.ts`; `ApiEndpoints.cs` | PASS |
| 10 | G6 runs only the saved Project identity, stays in Workspace, and hands off the current result explicitly | 旧 RunId/结果导航期望 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | local RunId 缺失时返回 null | `f03-workspace.spec.ts`; `ResultsPage.vue` | PASS |
| 11 | F04-R G3 golden journey closes Camera, Variables, Decision, Preview, Save, Run, Evidence and Package at 1920x1080 | golden journey fixture 期望漂移 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | 当前 Preview/identity 合同 | `f03-workspace.spec.ts`; `PreviewPanel.vue` | PASS |
| 12 | F04-R G3 golden journey closes Camera, Variables, Decision, Preview, Save, Run, Evidence and Package at 1366x768 | 短屏 golden journey 期望漂移 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | 当前短屏布局合同 | `f03-workspace.spec.ts`; `PreviewPanel.vue` | PASS |
| 13 | G6 blocks execute after admission rejection and keeps the saved Workspace editable | admission refresh 请求数/断言过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | 只读 admission refresh 语义 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 14 | G6 genuine running Stop cancels before execute completion and unlocks without Results navigation | Stop 请求路径期望过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | Formal Run 与只读 admission 分离 | `f03-workspace.spec.ts`; `runCommandOwner.ts` | PASS |
| 15 | G6 explicit reconcile recovers a successful result after execute response loss | response loss 后旧顺序期望 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | authority reconcile 合同 | `f03-workspace.spec.ts`; `RunConsole.vue` | PASS |
| 16 | G6 reconcile still-running and identity mismatch remain fail-closed | identity mismatch fixture 旧字段 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | 五字段 execution identity 合同 | `f03-workspace.spec.ts`; `ApiEndpoints.cs` | PASS |
| 17 | G6 protects route leave while Formal Run is still executing | Leave Guard 旧 stop 期望 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | Leave Guard 不隐式 stop/reconcile | `f03-workspace.spec.ts`; `runCommandOwner.ts` | PASS |
| 18 | G6 protects project switch while Formal Run is still executing | project switch 旧流程期望 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | running owner leave 合同 | `f03-workspace.spec.ts`; `runCommandOwner.ts` | PASS |
| 19 | G6 Host close flush keeps the owner alive when Formal Run cannot be settled | Host close 旧终态期望 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | unknown outcome fail-closed | `f03-workspace.spec.ts`; `RunConsole.vue` | PASS |
| 20 | G5 saves and reloads a catalog-added numeric operator type through the formal Project PUT | PUT fixture 缺新 operator 合同 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | canonical Project payload | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 21 | G5 save failure retries explicitly, PSV011 reconciles fail closed, and unknown outcome reconciles by GET | save failure 后旧时序断言 | PRODUCT_REGRESSION_FIXED | reconcile/Workspace projection | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 22 | G5 settles a delayed reconcile before route leave and cannot overwrite the next Project | delayed reconcile 生命周期失败 | PRODUCT_REGRESSION_FIXED | stale generation 保护 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 23 | G5 protects route leave and project switch while readonly and running responses disable saving | readonly/running layout 与禁用态失败 | PRODUCT_REGRESSION_FIXED | Workspace owner 生命周期 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 24 | G4 unified leave prompt traps keyboard focus, Escape stays, and discard leaves | Leave prompt focus 断言失败 | PRODUCT_REGRESSION_FIXED | Leave Guard/Workspace 资源生命周期 | `f03-workspace.spec.ts`; `runCommandOwner.ts` | PASS |
| 25 | G4 Preview exposes structured, empty, business failure, safety block, network failure and cancellation states | Preview 多状态断言失败 | PRODUCT_REGRESSION_FIXED | Preview layout/state projection | `PreviewPanel.vue`; `f03-workspace.spec.ts` | PASS |
| 26 | F04 design handoff captures a deterministic complex flow without static showcase data | handoff 画布流程失败 | PRODUCT_REGRESSION_FIXED | Canvas/Preview 工作区尺寸 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 27 | G4 Preview keeps the latest node when an older response arrives late | late response/布局断言失败 | PRODUCT_REGRESSION_FIXED | Preview generation projection | `f03-workspace.spec.ts`; `PreviewPanel.vue` | PASS |
| 28 | G5 passes 20 save and project-switch cycles with one PUT per save and a zero final ledger | cycle fixture 的保存合同过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | canonical PUT/ledger 合同 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 29 | G6 passes 20 formal Run, Project switch, and route-leave cycles with a zero final ledger | cycle fixture 的 Run identity 过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | Formal Run authority identity | `f03-workspace.spec.ts`; `runCommandOwner.ts` | PASS |
| 30 | G6 passes 20 run, stop/reconcile, project, and route lifecycle cycles with zero resources | stop/reconcile 顺序期望过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | authority reconcile/leave 合同 | `f03-workspace.spec.ts`; `runCommandOwner.ts` | PASS |
| 31 | formal Workspace records 100/150 route-ready and interaction samples | 性能样本布局失败 | PRODUCT_REGRESSION_FIXED | Workspace route-ready 尺寸 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 32 | formal Workspace records 300/450 route-ready and interaction samples | 性能样本布局失败 | PRODUCT_REGRESSION_FIXED | Workspace route-ready 尺寸 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 33 | Workspace splitters preserve bounds, Preview recovery and layout preferences across re-entry | splitter/Preview 恢复合同过时 | VALID_CONTRACT_CHANGE_FIXTURE_UPDATED | splitter 与 Preview 合同 | `f03-workspace.spec.ts`; `PreviewPanel.vue` | PASS |
| 34 | narrow Workspace overlays restore focus and remain inside the viewport | 窄屏 overlay 越界 | PRODUCT_REGRESSION_FIXED | 窄屏 Workspace 布局 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 35 | Prompt 3 refines Operator Rail and populated Inspector across width and long-Chinese states | 长中文/宽度断言失败 | PRODUCT_REGRESSION_FIXED | Canvas/Inspector 最小轨道 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 36 | Prompt 3 Inspector explains disabled parameters without exposing internal metadata terms | disabled parameter 文案布局失败 | PRODUCT_REGRESSION_FIXED | Inspector 状态投影与布局 | `f03-workspace.spec.ts`; `workspaceOwner.ts` | PASS |
| 37 | Prompt 3 Preview preserves image, result, ROI, empty and error hierarchy on a short comfortable viewport | short comfortable Preview 失败 | PRODUCT_REGRESSION_FIXED | Preview 最小可用高度 | `PreviewPanel.vue`; `f03-workspace.spec.ts` | PASS |

`ENVIRONMENT_FAILURE=0`，`TEST_RETIRED=0`。F03 原 37 项没有通过 skip、删除或降低断言处理。

## 7. 生产影响面与可复用证据

`git diff 582990edd..a7782d85e` 的生产代码和构建链 diff 为空；差异仅为测试、测试脚本和审计文档。当前 bundle report 与 `source-582990edd` 旧 bundle report 内容级比较为 54/54 文件 SHA 相同、总计 `1,886,498` bytes 相同：

```text
PRODUCTION_SOURCE_DIFF=NONE
PRODUCTION_BUNDLE_SHA_UNCHANGED=YES
```

因此复用 source `582990edd` 上已完成的：

```text
REAL_WEBVIEW2=12/12_PASS
RELEASE_PUBLISH=PASS
PUBLISH_STATIC_AUDIT=PASS
LOCAL_NO_NODE_PROCESS_TREE=PASS
```

这不改变证据边界：本机使用外部 Node CDP driver 的 no-Node 进程树不是独立无 Node 目标机，force-device-scale-factor 不是真实 Windows 125% 系统 DPI。

## 8. 未执行的现场边界

```text
REAL_WINDOWS_125_PERCENT_DPI=NOT_RUN
INDEPENDENT_CLEAN_MACHINE_WITHOUT_NODE=NOT_RUN
REAL_STATION_CAMERA_PLC_TCP=NOT_RUN
FIELD_NETWORK_FAILURE_RECOVERY=NOT_RUN
LONG_RUNNING_PRODUCTION_SOAK=NOT_RUN

PRODUCTION_ACCEPTANCE=NOT_GRANTED
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

## 9. Remote audit

审计分支固定为 `audit/f08-final-a7782d85`，不移动或 force push `origin/studio-ui-next`。初始本地门禁完成后，将显式 push 该分支并用仓库真实 `ClearVision CI/CD` workflow dispatch；记录 run URL、run id、attempt、jobs、logs、artifacts 和最终 SHA。CI 新失败必须回到本轮继续修复并重新冻结 source SHA；普通测试失败不是 Remote CI blocker。

```text
REMOTE_AUDIT_BRANCH=audit/f08-final-a7782d85
REMOTE_AUDIT_SHA=PENDING_FINAL_DOC_COMMIT
REMOTE_CI_RUN_URL=PENDING
REMOTE_CI_STATE=AWAITING
```

Remote CI 通过后，本节和第 1 节必须回填实际 URL、run id、attempt、job/artifact 摘要，并把 `F08_FINAL_CLOSURE`、`F08_ENGINEERING_STATE` 和 `F08_PLAN_STATE` 更新为最终 PASS/DONE。生产准入三项仍不得改变。
