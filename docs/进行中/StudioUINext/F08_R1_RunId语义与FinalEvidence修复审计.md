# ClearVision Studio UI Next F08-R1：RunId 语义与 Final Evidence 修复审计

## 1. 当前唯一状态

```text
F08_PLAN_STATE=REOPENED_FOR_R1
F08_R1_STATE=BLOCKED
F08_R1_1_STATE=BLOCKED
F08_ENGINEERING_STATE=PARTIAL
F08_G1_STATE=PASS
F08_G2_WORKSPACE_RECONCILE=PASS
F08_G3_STATE=PASS
F08_G4_STATE=PASS
F08_G5_IDENTITY_REPAIR=PASS
F08_G6_IDENTITY_REPAIR=PASS
F08_G7_STATE=BLOCKED
F08_ORIGINAL_DONE_CLAIM=SUPERSEDED_BY_R1_AUDIT
F08_NEXT_GOAL=F08_R1_BLOCKER_RECONCILE
F08_PRODUCTION_ACCEPTANCE=NOT_GRANTED
```

本文件是 F08 当前唯一状态入口。`F08_完成报告.md` 与
`F08_G7_角色异常矩阵与FinalEvidence准入审计.md` 保留 2026-08-03 R1 重开前的历史审计记录，
其中的 F08 DONE 结论已被本审计取代，不代表当前状态。

F08-R1.1 的 Workspace 路由卸载产品缺陷已修复，F03 已达到 54/54；但其余 Browser suite
仍有 1 个既有 F05 测试缺陷，Product full 两轮均出现非零失败。当前任务禁止扩大修复范围，
因此没有创建或推送远程审计分支，也没有触发 Remote CI，F08 不得恢复为 DONE。

## 2. 审计身份与提交链

```text
F08_REPORTED_FINAL_SHA=9b0525cdc6904d2e6ccc0da125b80cec15c7a061
F08_SOURCE_EVIDENCE_SHA_BEFORE_R1=1ec94a647cae137a1fa6ae89bd02a9710691766d
F08_R1_INITIAL_SHA=9b0525cdc6904d2e6ccc0da125b80cec15c7a061
F08_R1_RUN_ID_SOURCE_SHA=a86c62c5500c5d284673be3b743f5bc08eb758dd
F08_R1_1_INITIAL_SHA=a86c62c5500c5d284673be3b743f5bc08eb758dd
SOURCE_EVIDENCE_SHA=582990edd9bceecbf1a18943f56b3b46caa4cde6
PREVIOUS_REMOTE_AUDIT_BRANCH=audit/f08-9b0525cdc
REMOTE_AUDIT_BRANCH=NOT_CREATED
REMOTE_CI=NOT_RUN
```

源码提交链：

1. `a86c62c55 fix(f08): separate local run and session identities`
2. `582990edd fix(studio-ui): close workspace route disposal lifecycle`

## 3. RunId authority 决策

从 `InspectionRuntimeCoordinator`、`InspectionService`、RuntimeHost、`InspectionResult`、持久化快照、
repository、background spool、history list/detail/compare/previous-success、StudioUI Results 与 Station
结果链追踪后，没有发现本机正式检测结果可独立于 SessionId 证明的 RunId authority。采用路径 B：

```text
RUN_ID_AUTHORITY_DECISION=ABSENT_RETURN_NULL
SESSION_ID_RUN_ID_CONFLATION=REMOVED
LOCAL_RESULT_RUN_ID=NULL
STATION_REAL_RUN_ID=PRESERVED
```

- 本机 history、evidence 与 UI 不再把 `SessionId`、`ExecutionSnapshotId` 或 ResultId 投影成 RunId。
- 本机 legacy 结果继续返回 nullable `runId=null`；界面显示身份未记录，不生成随机值。
- 结果导航继续使用现有 ResultId、ProjectId 和 ExecutionSnapshotId，不把 SessionId 放进伪 RunId deep link。
- Station 已有真实 RunId 的远程结果保留原值，不受本机缺失策略影响。
- 没有新增 RunId authority、第二 result store 或第二持久化链。

### 3.1 migration 与兼容策略

本轮选择 `ABSENT_RETURN_NULL`，不新增数据库列、不执行 migration，也不修改 spool schema。现有 wire contract
继续容忍 nullable RunId；legacy 数据不回填。定向测试覆盖 null round-trip、history detail、spool replay、
deep link 和 Station 实值保留。RunId/结果定向测试为 32/32，Desktop identity 定向测试为 15/15。

## 4. 原 37 项失败的最终分类

原始失败集合来自：

```text
.tmp/studio-ui-next/f08/g7/browser-f03-full/.last-run.json
ORIGINAL_F03_RESULT=17 PASS / 37 FAIL
```

本轮将该文件的 37 个 Playwright test ID 与最终 `browser/f03/report.json` 逐项映射，并与
`9b0525cdc..a86c62c55` 的产品/测试 diff 对照。最终分类计数为：

```text
PRODUCT_REGRESSION=21
VALID_CONTRACT_CHANGE_FIXTURE_STALE=16
TEST_BUG=0
ENVIRONMENT_FAILURE=0
OBSOLETE_TEST_WITH_APPROVED_RETIREMENT=0
TOTAL=37
```

### 4.1 PRODUCT_REGRESSION（21 项）

以下测试本身没有为了变绿而修改；`a86c62c55` 修正 Preview 工作区最小网格轨道及本机结果身份投影后，
它们在单 worker 完整 F03 中恢复。旧 G7 报告把它们笼统写成 Camera 坐标/fixture 漂移不准确。

1. `node selection, move, copy/paste, undo/redo, delete and focus/IME gates stay scoped`
2. `pointer wiring creates and disconnects connections with stable feedback`
3. `G3 Inspector follows empty, node, multi-node and connection selection from Canvas`
4. `G3 Inspector edits primitive, slider and nullable parameters with validation/history/focus isolation`
5. `G3 connection Inspector selects endpoints and disconnects through the typed command`
6. `G3 Inspector shows metadata missing without enabling parameter writes`
7. `G3 Inspector shows metadata decode failure without enabling parameter writes`
8. `G4 Preview and ImageCanvas render artifacts, probe pixels and commit ROI once with undo redo`
21. `G5 save failure retries explicitly, PSV011 reconciles fail closed, and unknown outcome reconciles by GET`
22. `G5 settles a delayed reconcile before route leave and cannot overwrite the next Project`
23. `G5 protects route leave and project switch while readonly and running responses disable saving`
24. `G4 unified leave prompt traps keyboard focus, Escape stays, and discard leaves`
25. `G4 Preview exposes structured, empty, business failure, safety block, network failure and cancellation states`
26. `F04 design handoff captures a deterministic complex flow without static showcase data`
27. `G4 Preview keeps the latest node when an older response arrives late`
31. `formal Workspace records 100/150 route-ready and interaction samples`
32. `formal Workspace records 300/450 route-ready and interaction samples`
34. `narrow Workspace overlays restore focus and remain inside the viewport`
35. `Prompt 3 refines Operator Rail and populated Inspector across width and long-Chinese states`
36. `Prompt 3 Inspector explains disabled parameters without exposing internal metadata terms`
37. `Prompt 3 Preview preserves image, result, ROI, empty and error hierarchy on a short comfortable viewport`

### 4.2 VALID_CONTRACT_CHANGE_FIXTURE_STALE（16 项）

以下测试在 `a86c62c55` 中有对应 fixture 或断言更新：自动只读 admission refresh 增加一次 admission，
execute response loss 会先自动 reconcile，Leave Guard 不再隐式 stop/reconcile，local RunId 改为 null，
结果详情使用真实 SessionId/ExecutionSnapshotId，Preview 默认高度合同为 160px。测试仍保留原业务断言和
20-cycle 零账本，没有删除、skip 或放宽为仅检查 DOM 消失。

9. `G5 GET PUT GET saves one canonical payload and preserves null, falsy and opaque values`
10. `G6 runs only the saved Project identity, stays in Workspace, and hands off the current result explicitly`
11. `F04-R G3 golden journey closes Camera, Variables, Decision, Preview, Save, Run, Evidence and Package at 1920x1080`
12. `F04-R G3 golden journey closes Camera, Variables, Decision, Preview, Save, Run, Evidence and Package at 1366x768`
13. `G6 blocks execute after admission rejection and keeps the saved Workspace editable`
14. `G6 genuine running Stop cancels before execute completion and unlocks without Results navigation`
15. `G6 explicit reconcile recovers a successful result after execute response loss`
16. `G6 reconcile still-running and identity mismatch remain fail-closed`
17. `G6 protects route leave while Formal Run is still executing`
18. `G6 protects project switch while Formal Run is still executing`
19. `G6 Host close flush keeps the owner alive when Formal Run cannot be settled`
20. `G5 saves and reloads a catalog-added numeric operator type through the formal Project PUT`
28. `G5 passes 20 save and project-switch cycles with one PUT per save and a zero final ledger`
29. `G6 passes 20 formal Run, Project switch, and route-leave cycles with a zero final ledger`
30. `G6 passes 20 run, stop/reconcile, project, and route lifecycle cycles with zero resources`
33. `Workspace splitters preserve bounds, Preview recovery and layout preferences across re-entry`

## 5. F08-R1.1 Workspace 路由卸载根因

### 5.1 排除项

- 路由链是直接 `RouterView -> WorkspacePage`，没有 `KeepAlive` 或 `Suspense` 保活。
- 失败不是 stale iframe/page/global diagnostics；trace 中 URL 仍停留在 Workspace，当前 generation ledger
  真实保留 owner 与订阅。
- `onBeforeUnmount`/owner dispose 没有机会执行，因为路由根本没有完成离开。
- 延长 5 秒 timeout 不会改变结果；失败 trace 在持续轮询中保持相同账本。

### 5.2 根因调用链

```text
Workspace 自动 refreshAdmission()
-> Run owner phase=admitting
-> 该只读 refresh 只有 admissionPromise，没有 Formal Run activeController
-> Router Leave Guard 调用 WorkspaceOwner.prepareForLeave()
-> RunCommandOwner.prepareRunForLeave() 把所有 admitting 都当成真实 Run admission
-> 旧实现 cancelAdmission() 后立即返回 true
-> 只读 refresh 未被取消，phase 仍为 admitting
-> Leave Guard 二次保护检查拒绝导航
-> route 保持 project-workspace
-> WorkspacePage 不卸载，composition owner 与 17 个 subscription 继续存活
```

分类：`PRODUCT_BUG`。修复后，真实 Formal Run admission（存在 `activeController`）仍可取消；只读 admission
refresh 则等待已有 `admissionPromise` settle，Leave Guard 再复检，不伪造全局 diagnostics 清零。

### 5.3 修复文件

```text
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/run/runCommandOwner.ts
ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/tests/unit/capabilities/project-workspace/run/runCommandOwner.spec.ts
ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/f03-workspace.spec.ts
```

### 5.4 修复前后 lifecycle ledger

| 证据 | route | generation / mount-dispose | Workspace/Flow/Image/Preview/ROI owner | subscriptions | 其他资源 |
| --- | --- | --- | --- | --- | --- |
| 修复前失败 trace | 仍为 `project-workspace` | `14 / 13` | `1 / 1 / 1 / 1 / 1` | `17` | 最终未卸载；等待更久不归零 |
| 修复后最终 observation | `about` | generation `21`，`21 / 21` | `0 / 0 / 0 / 0 / 0` | `0` | timer/observer/animation/abort/read/write/preview/execute 全为 `0` |

修复前 trace SHA-256：
`4839f0272975380362edffdc9a2fcc97121f944a225fec353c36de6fa84455a2`。

修复后的 F03 JSON 内嵌 `workspace-lifecycle-observations` attachment，共 42 条（20 轮 active/left，
加最终 active/left）。每轮读取当前 generation，最终 `routeName=about`、`ownerGeneration=21`、
`totalWorkspaceDisposals=21`、`ownerConflictCount=0`。

## 6. 验证入口与结果

所有最终工程证据绑定 `SOURCE_EVIDENCE_SHA=582990edd9bceecbf1a18943f56b3b46caa4cde6`。
以下是可重放入口；Playwright 的 report/output 路径由证据目录中的
`playwright.evidence.config.ts` 固定。命令从仓库根目录执行，且 `.NET` 测试保持串行：

```powershell
# StudioUI lifecycle 定向、全量 unit、类型与 lint
$evidenceRoot = (Resolve-Path ".tmp/studio-ui-next/f08-r1-1/source-582990edd").Path
Push-Location "./ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI"
& ".\node_modules\.bin\vitest.cmd" run `
  "tests/unit/capabilities/project-workspace/run/runCommandOwner.spec.ts" `
  "tests/unit/capabilities/project-workspace/workspacePage.spec.ts" `
  "tests/unit/capabilities/project-workspace/workspaceLifecycleDiagnostics.spec.ts" `
  "tests/unit/capabilities/project-workspace/persistence/workspacePersistenceOwner.spec.ts" `
  "tests/unit/leave/productLeaveGuardOwner.spec.ts" `
  "tests/unit/studioUiLifecycleDiagnostics.spec.ts"
npm run test:unit
npm run typecheck
npm run lint
Pop-Location

# F03 生命周期隔离 10 次、完整 F03、其余 Browser、F05 诊断 10 次
$playwright = "./ClearVision.Product/tests/ClearVision.Product.UI.Tests/node_modules/.bin/playwright.cmd"
$playwrightConfig = Join-Path $evidenceRoot "playwright.evidence.config.ts"
$env:CV_UI_SCENARIO = "studio-ui-next"

$env:CV_UI_PORT = "5187"
$env:CV_F08_EVIDENCE_DIR = Join-Path $evidenceRoot "browser/isolated"
& $playwright test "studio-ui-next/f03-workspace.spec.ts" --config $playwrightConfig --workers=1 --repeat-each=10 --grep "passes 20 real Browser route mount/unmount cycles with a zero ledger"

$env:CV_UI_PORT = "5188"
$env:CV_F08_EVIDENCE_DIR = Join-Path $evidenceRoot "browser/f03"
& $playwright test "studio-ui-next/f03-workspace.spec.ts" --config $playwrightConfig --workers=1

$otherBrowserSpecs = @(
  "studio-ui-next/canvas-foundation.spec.ts"
  "studio-ui-next/design-foundation.spec.ts"
  "studio-ui-next/f02-operators.spec.ts"
  "studio-ui-next/f02-overview.spec.ts"
  "studio-ui-next/f02-projects-read.spec.ts"
  "studio-ui-next/f02-results.spec.ts"
  "studio-ui-next/f02-stations.spec.ts"
  "studio-ui-next/f04-auth.spec.ts"
  "studio-ui-next/f04-project-lifecycle.spec.ts"
  "studio-ui-next/f05-inspection-run.spec.ts"
  "studio-ui-next/f06-ai-workbench.spec.ts"
  "studio-ui-next/f06-g4-handoff.spec.ts"
  "studio-ui-next/f06-g5-history.spec.ts"
  "studio-ui-next/f07-device-workbench.spec.ts"
  "studio-ui-next/f07-settings-shell.spec.ts"
)
$env:CV_UI_PORT = "5189"
$env:CV_F08_EVIDENCE_DIR = Join-Path $evidenceRoot "browser/other"
& $playwright test @otherBrowserSpecs --config $playwrightConfig --workers=1
$otherBrowserExitCode = $LASTEXITCODE # 既有 F05 TEST_BUG，预期非零并记录为 BLOCKED

$env:CV_UI_PORT = "5190"
$env:CV_F08_EVIDENCE_DIR = Join-Path $evidenceRoot "browser/other-f05-diagnostic"
& $playwright test "studio-ui-next/f05-inspection-run.spec.ts" --config $playwrightConfig --workers=1 --repeat-each=10 --grep "continuous inspection persists across route leave"
$f05DiagnosticExitCode = $LASTEXITCODE # 0/10，预期非零并记录为 BLOCKED
Remove-Item Env:CV_F08_EVIDENCE_DIR, Env:CV_UI_PORT, Env:CV_UI_SCENARIO

# Bundle gate 与双构建 reproducibility
Push-Location "./ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI"
npm run bundle:ci
npm run bundle:verify
Pop-Location

# .NET 串行入口；每个 FQN 数组合并为同一测试进程
$productFocusedTests = @(
  "ClearVision.Product.Tests.Repositories.InspectionResultRepositoryTests"
  "ClearVision.Product.Tests.Services.InspectionEvidenceManifestServiceTests"
  "ClearVision.Product.Tests.Services.InspectionResultBackgroundServiceTests"
  "ClearVision.Product.Tests.Services.InspectionResultPersistenceSnapshotTests"
  "ClearVision.Product.Tests.Services.InspectionServiceHistoryComparisonTests"
)
& "./scripts/run-dotnet-test-serial.ps1" -Project "./ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" -FullyQualifiedName $productFocusedTests
& "./scripts/run-tests-services-regression.ps1"
& "./scripts/run-dotnet-test-serial.ps1" -Project "./ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj"

$desktopFocusedTests = @(
  "ClearVision.Product.Desktop.Tests.ApiEndpointsInspectionHistoryTests"
)
& "./scripts/run-dotnet-test-serial.ps1" -Project "./ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" -FullyQualifiedName $desktopFocusedTests
& "./scripts/run-dotnet-test-serial.ps1" -Project "./ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj"
& "./scripts/run-tests-desktop-endpoints.ps1"
& "./scripts/run-dotnet-test-serial.ps1" -Project "./ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" -FullyQualifiedName "ClearVision.Product.Desktop.Tests.Architecture.StudioUiArchitectureGuardTests"
& "./scripts/run-dotnet-test-serial.ps1" -Project "./ClearVision.Product/tests/ClearVision.Product.VirtualStation.Tests/ClearVision.Product.VirtualStation.Tests.csproj"

# 真实 WinForms + WebView2 Debug/Release、publish、DPI probe 与本机 no-Node process tree
& "./scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1" -RunName "f08-r1-1-source-582990edd" -EvidenceDirectory ".tmp/studio-ui-next/f08-r1-1/source-582990edd/webview2" -EvidencePhase f04 -RunScope full -BaseWebPort 5480 -BaseCdpPort 9880 -WindowWidth 1366 -WindowHeight 768 -SkipPerformance
```

生命周期 6 个 spec 共 67 项；Product 定向 5 个 class 共 32 项；Desktop identity 定向 1 个 class
共 15 项。对应日志/TRX 分别保存于 `unit/lifecycle`、`dotnet/product-focused` 与
`dotnet/desktop-focused`，实际结果以同 SHA 的日志、TRX 和结构化报告为准。

### 6.1 通过项

| 门禁 | 结果 | 主要 artifact |
| --- | --- | --- |
| lifecycle isolated repeat | 10/10 PASS；每次 42 observations，最终零账本 | `browser/isolated/report.json` |
| lifecycle unit | 6 files / 67 tests PASS | `unit/lifecycle/vitest.log` |
| F03 Workspace | 54/54 PASS；unexpected/flaky/skipped 均 0 | `browser/f03/report.json` |
| StudioUI unit | 128 files / 786 tests PASS | `studio-ui/unit.log` |
| typecheck / lint | PASS / PASS | `studio-ui/typecheck.log`、`studio-ui/lint.log` |
| bundle gate / reproducibility | PASS / PASS | `bundle/` |
| RunId/结果定向 | 32/32 PASS | `dotnet/product-focused/product-focused.trx` |
| Services regression | 516/516 PASS | `dotnet/services-regression/services-regression.trx` |
| Desktop identity 定向 | 15/15 PASS | `dotnet/desktop-focused/desktop-focused.trx` |
| Desktop full | 772/772 PASS | `dotnet/desktop-full/desktop-full.trx` |
| Desktop endpoints | 423/423 PASS | `dotnet/desktop-endpoints/desktop-endpoints.trx` |
| architecture guard | 9/9 PASS | `dotnet/architecture/architecture.trx` |
| Virtual Station | 39/39 PASS | `dotnet/virtual-station/virtual-station.trx` |
| WebView2 matrix | 12/12 PASS | `webview2/studio-ui-webview2-matrix.json` |
| Release publish / static audit | PASS / PASS | WebView2 matrix + no-Node evidence |
| local Desktop process tree | PASS；12 次 Desktop 树均无 Node descendant | `webview2/studio-ui-no-node-evidence.json` |

Debug DPR 1、force-scale 1.25 与 Release Workspace 截图已人工复核：画面非空，Canvas、Inspector、
Preview 与运行控制台可用，没有非预期重叠。native window 始终为 96 DPI/100%；1.25/1.5/2 仅是
`force-device-scale-factor` probe，不能冒充真实 Windows 125% DPI。

## 7. 当前阻断

### 7.1 OTHER_BROWSER=BLOCKED

其余 Browser 首轮为 `86 PASS / 26 SKIP / 1 FAIL`。唯一失败是 F05：

```text
continuous inspection persists across route leave and restores from authority on return
```

隔离重复为 `0/10`。权威 session、累计结果和停止能力均已恢复，且没有重复 start；有限 SSE fixture
结束后 owner 合法进入 `reconnecting`，旧断言只接受瞬时文案“连续检测中”。分类为既有 F05
`TEST_BUG`，不属于本轮 Workspace 生命周期修复白名单，因此未修改。

首轮失败 trace SHA-256：
`e2e52a029be1daf0aef2412ecd3e36cda5d1dd5955e2273f53632a6d7bbf1cef`。

### 7.2 PRODUCT_FULL=BLOCKED

- 首轮：`3871 PASS / 2 SKIP / 1 FAIL`，measurement performance p95 波动。
- R2：`3870 PASS / 2 SKIP / 2 FAIL`，另一项 performance p95 波动，并出现一次临时文件
  `UnauthorizedAccessException`。
- 两个失败项各自串行重跑均 PASS。
- 明确排除 Performance 后为 `3852 PASS / 2 SKIP / 0 FAIL`；wrapper 仅因沿用旧 minimum threshold
  返回非零，不把该非零改写成产品 full PASS。

因此 Product full 没有形成单次 0-failure 证据，Remote audit/CI 准入不成立。

## 8. Evidence manifest

证据根目录：

```text
.tmp/studio-ui-next/f08-r1-1/source-582990edd
```

`artifact-manifest.json` 按相对路径排序，覆盖生成前的全部 219 个原始 artifact、46,355,105 bytes，
每项记录 SHA-256。manifest 与 sidecar 因循环哈希不可同时自包含，故从 entries 中排除；sidecar
单独认证 manifest。

```text
ARTIFACT_MANIFEST_SHA256=b9c3d98cc5430b717459408f8f9174310db823a89dc73babc823daa734b00d1c
ARTIFACT_MANIFEST_SIDECAR_SHA256=bcfbdff98699a7142820b4da8b527ae19832d04cfd7d2b0de1a97d4cd467d0a7
F03_JSON_SHA256=963391eadb669f48335ecd60d0d778a8ccf18af906c08be8af000e7b5ee4ae9f
ISOLATED_JSON_SHA256=ea42775338ace07d18ff770f32b7833ba36e0ef15c4e0633e565d56182007afa
OTHER_BROWSER_JSON_SHA256=1bb04755f855dbf90c007fc7970ca501d2446d1dd159c185ec07f5e92e83736b
F05_DIAGNOSTIC_JSON_SHA256=0f333ced8eaa924f8e5c9c6fed3cc0d43456b688042ccaa6fd1b97540c7bcb27
WEBVIEW2_MATRIX_SHA256=e3542d34e886b01aaa615db0e2c09453f92c388d0408db844446866b3b3bbcb4
NO_NODE_EVIDENCE_SHA256=57697a7b3e83e2e5ccc0610b5f78fce67cc8b4bbb153d7162d186f6cdf55c130
```

其余每个 artifact 的路径、字节数与 SHA-256 以 manifest 为完整索引；HTML、trace、截图、TRX、
bundle report、host logs 和 cleanup evidence 均已纳入。

## 9. 未执行边界与最终结论

```text
REMOTE_AUDIT_BRANCH=NOT_CREATED
REMOTE_CI_RUN_URL=NOT_AVAILABLE
REMOTE_CI_STATE=NOT_RUN
INDEPENDENT_NO_NODE_TARGET=NOT_RUN
REAL_WINDOWS_125_PERCENT_DPI=NOT_RUN
REAL_STATION_CAMERA_PLC_TCP=NOT_RUN
FIELD_NETWORK_RECOVERY=NOT_RUN
LONG_RUNNING_SOAK=NOT_RUN
PRODUCTION_ACCEPTANCE=NOT_GRANTED
DEFAULT_ENTRY_CHANGE=NOT_PERFORMED
LEGACY_RETIREMENT=NOT_APPROVED
```

本机发布后的 Desktop 进程树没有 Node 子进程，但同机使用外部 Node CDP driver；这不是独立无 Node
目标机证据。Virtual Station、静态 Chromium、真实 WebView2、force-scale DPR 和 Release publish
均不能替代真实 Windows DPI、真实相机/PLC/Station、现场网络恢复、长期 soak 或生产验收。

最终状态：RunId 语义和 Workspace 路由卸载产品缺陷均已修复，F03 54/54 已闭环；由于 OTHER_BROWSER
和 Product full 门禁阻断，`F08_R1_1_STATE=BLOCKED`、`F08_G7_STATE=BLOCKED`。不创建审计分支，
不推送 `studio-ui-next`，不触发 Remote CI，不宣布 F08 DONE。
