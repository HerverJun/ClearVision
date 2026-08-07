# ClearVision Studio UI Next 迁移发布阻断收口

日期：2026-08-06（Asia/Shanghai）
工作区：`C:\Users\HerverJun\Desktop\ClearVision-UI-Next`
分支：`studio-ui-next`
本报告用于收口 `Studio_UI_Next_整体迁移审计_9800d6045a9f.md` 中的增量构建与 Browser 生命周期阻断。它不把本地静态 fixture、历史 SHA 或未运行的 Remote CI 当作发布通过证据。

## 结论

```text
RELEASE_BLOCKERS_PARTIALLY_CLOSED
```

本轮已关闭或显著收窄两个产品级阻断：

1. `StudioUiBuildInput` 已覆盖当前 Vite canonical alias 的实际外部依赖闭包，并新增回归测试；增量构建实验证明 alias 源变化会导致 bundle fingerprint 变化，恢复源文件后 fingerprint 回到基线。
2. StudioUI Browser fixture 的正常退出链已改为由 Node global setup 管理，并补齐 stdin EOF、`disconnect`、信号和幂等 shutdown；F05 三项 Browser 用例连续两轮均正常结束，Browser full 也能在测试结束后退出，不再卡在 teardown。

发布仍未闭合。Browser 相关业务子集仍有 Workspace selection、Inspector、Preview、save/run/reconcile/handoff 失败或超时；publish 虽可生成当前 StudioUI bundle，但产物仍包含 Legacy `wwwroot/index.html` 与 `wwwroot/src`；Remote CI、真实 WebView2、Windows 125% DPI、no-Node 目标机启动和完整 Desktop 测试没有形成当前 SHA 的通过证据。因此不能进入迁移验收或正式发布阶段。

## 1. 初始 Git 状态

| 项目 | 初始事实 |
| --- | --- |
| `WORKTREE_ROOT` | `C:\Users\HerverJun\Desktop\ClearVision-UI-Next` |
| `CURRENT_BRANCH` | `studio-ui-next` |
| `INITIAL_SHA` | `9800d6045a9f5fdfc62a166242e83529b833dc7d` |
| `TRACKING_BRANCH` | `origin/studio-ui-next` |
| `REMOTE_SHA` | `7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5`（本地 remote-tracking 记录） |
| 初始工作树 | 非干净；17 个 tracked 修改、未跟踪 `RunStatusBar.vue`、未跟踪原整体迁移审计报告 |
| 本轮 Git 操作 | 未 reset、未 checkout、未 stash、未切换分支、未提交、未推送 |

本轮尝试使用 `git ls-remote origin refs/heads/studio-ui-next` 刷新远端 SHA，但当前环境因 Git HTTPS Schannel 凭据错误失败：`SEC_E_NO_CREDENTIALS (0x8009030e)`。因此上表的 Remote SHA 只代表工作开始时的本地 remote-tracking 记录，不代表本轮已向远端确认。

### 1.1 用户已有修改与本轮修改边界

任务开始前已有的 17 个 tracked 内容修改保留不动：

- 9 个 StudioUI 源码/测试文件：`WorkspaceShell.vue`、`FlowWorkspace.vue`、`flowCanvasOwner.ts`、`workspaceLayoutOwner.ts`、`PreviewPanel.vue`、`canonicalFlowCanvas.ts`、`canonicalFlowCanvas.spec.ts`、`runConsole.spec.ts`、`workspaceLayoutOwner.spec.ts`。
- 8 个 `ClearVision.Product/test_results` 性能报告文件：calibration、detection、measurement、operator、preprocessing 和 stage2 报告。

另有用户已有的未跟踪 `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/inspection-run/RunStatusBar.vue` 和原审计报告。本轮没有回滚、覆盖或提交这些文件。`wwwroot/src/core/canvas/flowCanvasAdapter.js` 最终仍显示工作树 `M`，但 `git diff` 内容为空；这是父仓库 `.git` 只读导致的 index stat 假阳性，不是本轮内容修改。

本轮只修改或新增以下文件：

1. `ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj`
2. `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright.config.ts`
3. `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/support/studio-ui-next-server.cjs`
4. `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/support/studio-ui-next-global-setup.cjs`
5. `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/studio-ui-next-infrastructure.test.mjs`
6. 本报告文件

本轮生成的 publish 输出、Playwright HTML 报告和临时依赖目录已清理；`.tmp` 中其他已有证据文件未删除。

## 2. 增量构建依赖修复

### 2.1 实际根因

`ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/vite.config.ts` 的 7 个 canonical alias 仍解析到 Legacy `wwwroot/src`。Vite 的实际本地依赖闭包包含 FlowCanvas、Flow interaction、Preview、ImageCanvas、ROI 和 image pixel probe 等 15 个 StudioUI 外部源文件，而 `.csproj` 原来的 `StudioUiBuildInput` 只覆盖 `StudioUI/src`、Vite 配置和包配置。

因此，修改 alias 指向的 Legacy 源文件时，MSBuild 可能看不到 StudioUI 输入发生变化，直接复用旧的 `StudioUI/dist` bundle。问题是构建输入清单不完整，不是通过删除 Legacy、重写 Canvas 或每次无条件 clean 来规避。

### 2.2 修复方案

在 `ClearVision.Product.Desktop.csproj` 中显式加入当前 canonical alias 闭包的 15 个外部文件：

- `wwwroot/src/core/canvas/flowCanvasAdapter.js`
- `wwwroot/src/core/canvas/flowCanvas.js`
- `wwwroot/src/core/canvas/portTypeCompatibility.mjs`
- `wwwroot/src/features/flow-editor/flowEditorInteraction.js`
- `wwwroot/src/shared/components/uiComponents.js`
- `wwwroot/src/shared/operatorVisuals.js`
- `wwwroot/src/core/logging/debugLogger.js`
- `wwwroot/src/features/flow-editor/previewCoordinator.js`
- `wwwroot/src/features/flow-editor/previewOutputFormatter.mjs`
- `wwwroot/src/shared/parameterDependencyRules.js`
- `wwwroot/src/core/canvas/imageCanvas.js`
- `wwwroot/src/features/flow-editor/roiGeometry.mjs`
- `wwwroot/src/features/flow-editor/roiEditorSupport.mjs`
- `wwwroot/src/shared/featureRegistry.js`
- `wwwroot/src/features/flow-editor/imagePixelProbe.mjs`

没有搬迁或重写 Legacy 模块，也没有新增第二套 Canvas、ServiceRegistry、HTTP transport 或保存链。

`studio-ui-next-infrastructure.test.mjs` 解析 Vite canonical declarations，递归检查本地 JS/MJS import closure，并断言每个外部文件都出现在 `StudioUiBuildInput` 中；这使依赖列表遗漏能在构建前被发现。

### 2.3 增量构建实验证据

| 步骤 | 结果 |
| --- | --- |
| 干净构建后的 StudioUI manifest fingerprint | `CA3B77EC1DD2B7C61A69A7FFE47DCC65C334049DC8B8CE30DF7E990EE4FBD73B` |
| 只修改一个 alias 源文件，直接执行标准 Desktop build，未 clean | fingerprint 变为不同值，采集输出前缀/后缀为 `09AEB478...507777` |
| 恢复临时源文件修改后再次构建 | fingerprint 回到 `CA3B77EC1DD2B7C61A69A7FFE47DCC65C334049DC8B8CE30DF7E990EE4FBD73B` |
| Debug / Release build | 均完成，0 warning / 0 error |
| 与普通后端源变更的边界 | StudioUI 输入仍由 StudioUI source、配置和 canonical alias 闭包组成；没有用无条件 clean 作为正确性条件 |

## 3. Browser 挂起与生命周期收口

### 3.1 根因定位

旧路径把 StudioUI fixture 交给 Playwright `webServer` 管理。在 Windows teardown 中，Playwright 的 child-process/taskkill 兼容链与 fixture 的 stdin 生命周期没有形成可靠的关闭确认：测试主体可以到达最后一个用例，但 server 子进程、pipe 或 close flush 未完成，导致 runner 在 teardown 阶段等待。

这不是通过提高 timeout 或正常流程强制杀进程解决的。本轮把 StudioUI Next fixture 的 owner 迁移到直接 Node `globalSetup`：

- `playwright.config.ts` 对 StudioUI Next 使用 `studio-ui-next-global-setup.cjs`，不再使用 Playwright `webServer` teardown。
- global setup 直接 spawn server，等待 `/studio/index.html` readiness，并在 teardown 先关闭 stdin、等待退出，再只对异常超时的 fixture 使用有界 fallback。
- `studio-ui-next-server.cjs` 增加 stdin `end/close`、child `disconnect`、SIGINT/SIGTERM 和幂等 shutdown；正常关闭路径等待 HTTP server close callback。
- 新增 Node 回归测试，验证 launcher 关闭 stdin 后 server 退出码为 0、没有 signal、端口被释放。

### 3.2 Browser 证据

| 验证 | 结果 | 分类 |
| --- | --- | --- |
| F05 三项关键 Browser 用例，第 1 轮 | 3/3 PASS，正常退出 | `CONFIRMED` |
| F05 三项关键 Browser 用例，第 2 轮 | 3/3 PASS，结果一致 | `REPRODUCED` |
| Browser full | 167 total；108 passed、33 failed、26 skipped；测试结束后正常退出 | fixture teardown 已收口，但业务 gate 未通过 |
| 相关 F03/F04/F05 子集 | 59 tests 在 300 秒内未完成；失败集中在 Workspace selection、Inspector、Preview、save/run/reconcile/handoff | `NOT_VERIFIED`，不能归为单一生命周期问题 |
| 5177 残留监听 | 无监听 | `CONFIRMED` |

因此 `BROWSER_HANG_ROOT_CAUSE` 已定位并修复到 fixture 生命周期层，但 `BROWSER_TEST_RESULT` 仍不是全量通过。Workspace、Inspector、Preview、保存/运行、reconcile 和 handoff 的失败需要按真实 UI 状态、后端投影和测试 fixture 逐项复现，不能用 teardown 修复掩盖。

## 4. 生命周期与权威边界

当前代码路径核对为：

```text
MainForm
  -> WebView2Host
  -> Startup Injection
  -> mountStudioApp
  -> AuthLifecycleRoot
  -> ProductRuntime
  -> WorkspaceRuntime
  -> capability Owners
  -> unmount / dispose
  -> window close acknowledgement
```

本轮新增的 global setup 只拥有静态 Browser fixture server，不拥有 Product、Flow、Project、AgentRun 或执行结果权威。Vue state、DOM、localStorage 和测试 fixture 仍不是后端 authority；正式保存、运行与检测控制仍应通过既有 authenticated HTTP/SSE 和 application service。

StudioUI unit 与 Desktop architecture guard 的当前结果支持 `ProductRuntime`、`WorkspaceRuntime` 和 capability-local owner 的代码边界，但由于 Browser 业务子集仍失败，不能把静态 unit/dispose 证据扩展为真实 WebView2 下所有 owner 无泄漏的证明。

## 5. 验证矩阵

| 验证项 | 当前结果 | 分类/备注 |
| --- | --- | --- |
| StudioUI `npm ci` | 失败：锁定的 `@rolldown/...node` 文件持续 `EPERM`；使用日期临时目录完成其余本地验证 | `ENVIRONMENT_BLOCKED`，未修改 lockfile |
| StudioUI lint | PASS | `CONFIRMED` |
| StudioUI typecheck（三个项目） | PASS | `CONFIRMED` |
| StudioUI Vitest | 128 files / 795 tests PASS | `CONFIRMED` |
| UI Tests Node unit | 974/974 PASS | 含本轮 alias closure 与 stdin teardown 回归 |
| Vite production build | 476 modules PASS | `CONFIRMED` |
| bundle gate / reproducibility | PASS | `CONFIRMED` |
| Desktop Debug build | PASS，0 warning / 0 error | `CONFIRMED` |
| Desktop Release build | PASS，0 warning / 0 error | `CONFIRMED` |
| Desktop 架构守卫 | 9/9 PASS | `CONFIRMED` |
| Desktop 全量测试 | 300 秒超时；已见 Windows EventLog `.NET Runtime` 权限、AgentRun、Preview、Station 相关失败 | `NOT_VERIFIED`；失败成分含环境阻断和产品路径失败 |
| Release self-contained publish | 可生成，manifest SHA 为 `CA3B77EC1DD2B7C61A69A7FFE47DCC65C334049DC8B8CE30DF7E990EE4FBD73B` | 生成通过，不等于发布验收通过 |
| Publish 资产审计 | `wwwroot/studio` 存在，但仍含 Legacy `wwwroot/index.html`、`wwwroot/src` | `MIGRATION_DEFECT` |
| Remote CI | 未运行；branch workflow 未自动绑定当前 `studio-ui-next` HEAD | `NOT_VERIFIED` |
| 真实 WebView2 / Windows 125% DPI | 未运行 | `NOT_VERIFIED` |
| no-Node 目标机启动 | 未运行 | `NOT_VERIFIED` |

旧整体审计中“publish 因磁盘空间阻断”的结论已被本轮实际结果更新：本轮在独立输出目录中完成了 self-contained publish 并取得 manifest fingerprint；新的阻断是产物边界仍包含 Legacy，而不是把环境失败伪装为代码通过。

## 6. Legacy 与 compatibility 边界

本轮没有删除 Legacy。当前分类如下：

| 分类 | 当前内容 | 结论 |
| --- | --- | --- |
| `SHARED_CORE` | Vite alias 当前复用的 FlowCanvas、Flow interaction、Preview、ImageCanvas、ROI 和 image probe 源模块 | 仍在生产 Next bundle 输入闭包中；中期迁入 `StudioUI/src`，本轮只补输入依赖 |
| `COMPATIBILITY_REQUIRED` | Legacy fallback、现有 WebMessage compatibility chain、宿主/AI 历史消息适配 | 仍需隔离和收敛，不能直接删除 |
| `CONTROLLED_FALLBACK` | `/index.html`、`LEGACY_DEFAULT`、`LEGACY_FALLBACK` 及相关启动 profile | 当前仍可达，尚未满足“显式诊断 fixture only”条件 |
| `MIGRATE_LATER` | Legacy `wwwroot/src`、Legacy CSS/HTML、非 `/studio` static provider、普通 CI Legacy 路径 | 迁移资产单一化与入口单一化的后续阻断 |
| `SAFE_TO_RETIRE` | 当前没有经过生产 profile、publish、compatibility 和 WebView2 证据证明可安全退役的项 | 不得删除 |

`LEGACY_UI_STATUS=RETAINED_AND_PRODUCTION_REACHABLE`。`SHARED_LEGACY_MODULE_STATUS=SHARED_CORE_PLUS_MIGRATE_LATER`。`WEBMESSAGE_COMPATIBILITY_STATUS=COMPATIBILITY_REQUIRED_BUT_NOT_YET_ISOLATED`。本轮没有新增 WebMessage 执行通道，也没有把 WebMessage bridge 变成正式 HTTP/SSE authority 的替代品。

## 7. 剩余阻断与下一阶段建议

1. P1：拆分并修复 59 个 Browser 子集中的 Workspace selection、Inspector、Preview、save/run/reconcile/handoff 失败；为每个 fixture 验证 page、SSE、timer、AbortController 和 owner ledger 归零。
2. P1：在稳定的权限/日志环境中完成 Desktop 相关测试，分别处理 EventLog 权限阻断与 AgentRun、Preview、Station 真实路径失败，不能把整轮 timeout 记为通过。
3. P1：建立 Next-only production profile，移除 Legacy `/index.html`、Legacy source 和普通生产 fallback 的可达性；保留隔离的诊断/fixture 入口直到迁移矩阵闭合。
4. P1：在 `studio-ui-next` 的 Remote CI trigger 或手工受保护 workflow 中绑定最终 SHA，并补齐 Release publish、no-Node、WebView2 和 DPI 证据。
5. P2：修复 `npm ci` 的 Windows `EPERM` 环境问题，确保锁定依赖可以在干净工作区复现安装。

当前只能安全进入“阻断项修复与证据重跑”阶段，不可进入发布验收阶段。

## 8. 最终汇总

```text
INITIAL_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
FINAL_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
REMOTE_SHA=7d43af9e19ad5a98240651fd5519a8e0f5a1e9f5 (local remote-tracking; git ls-remote refresh failed)
WORKTREE_STATE=DIRTY; 20 tracked content diffs (17 pre-existing + 3 this task), one pre-existing stat-only M, 5 untracked files including user files and this report; no commit

INCREMENTAL_BUILD_FIX=PASS; StudioUiBuildInput includes the 15-file canonical alias closure and infrastructure regression coverage
INCREMENTAL_BUILD_TEST=PASS; baseline CA3B77EC...FBD73B, changed fingerprint 09AEB478...507777, restored baseline
BROWSER_HANG_ROOT_CAUSE=PLAYWRIGHT_WINDOWS_WEBSERVER_TEARDOWN_AND_FIXTURE_STDIN_LIFECYCLE
BROWSER_TEST_RESULT=PARTIAL; F05 3/3 in two consecutive rounds and full suite exits, but 108 passed / 33 failed / 26 skipped and 59-test subset timed out
RELEASE_PUBLISH_RESULT=PASS_WITH_LEGACY_ASSETS; current StudioUI manifest CA3B77EC...FBD73B, publish still contains Legacy wwwroot/index.html and wwwroot/src
REMOTE_CI_RESULT=NOT_RUN; current branch/HEAD has no bound Remote CI evidence and remote refresh was blocked by Git credentials

LEGACY_UI_STATUS=RETAINED_AND_PRODUCTION_REACHABLE
SHARED_LEGACY_MODULE_STATUS=SHARED_CORE_PLUS_MIGRATE_LATER
WEBMESSAGE_COMPATIBILITY_STATUS=COMPATIBILITY_REQUIRED_BUT_NOT_YET_ISOLATED

FINAL_DECISION=RELEASE_BLOCKERS_PARTIALLY_CLOSED
REMAINING_BLOCKERS=BROWSER_BUSINESS_FAILURES; DESKTOP_FULL_TEST_UNVERIFIED; LEGACY_ASSET_AND_ENTRY_SINGLEIZATION; REMOTE_CI; WEBVIEW2_DPI_NO_NODE_EVIDENCE; NPM_CI_EPERM_ENVIRONMENT
REPORT_PATH=docs/进行中/StudioUINext/Studio_UI_Next_迁移发布阻断收口_9800d6045a9f_WORKTREE.md
```

注：`NPM_CI_EPERM_ENVIRONMENT` 只是汇总键；实际问题是 Windows `EPERM`，不代表新的业务错误码。
