# Studio UI Next F05 G6 隔离 E2E 与 Final Evidence 闭环

> 执行日期：2026-07-27
> 基线：`58cc0a7fc95222da7f51fd911e337d902b22b52c`
> Initial / Evidence SHA：`3993b8e49b65e805afbcb38c8edf353d2d21a309`
> Fix / Final SHA：`dc6d38737a2e183edd1bbe55590c6e8de1d854d2`
> Tracking / Remote SHA：由本次 Final Evidence 提交本身定义；实际 SHA 与后置 workflow_dispatch run 在最终交付回报记录，避免文档自引用
> Remote CI：初次 run [`30259428377`](https://github.com/HerverJun/ClearVision/actions/runs/30259428377) 暴露 Desktop 架构守卫漂移；修复 SHA run [`30276133719`](https://github.com/HerverJun/ClearVision/actions/runs/30276133719) attempt 2 与 Final Gate 已通过。

证据根目录：`.tmp/studio-ui-next/f05/g6-3993b8e4/`。

## 1. 范围、现场保护与结论口径

本轮只执行最终验证、阻断修复和证据闭环。没有新增产品功能，没有视觉重设计，没有修改正式默认入口，没有进入 AI、设置、Import/Export、F06 或 Legacy 退役。

- 分支与 tracking：`studio-ui-next` -> `origin/studio-ui-next`。
- remote：`https://github.com/HerverJun/ClearVision.git`。
- Legacy 工作树未进入、未修改、未清理。
- 用户 `appsettings.json` 始终未暂存、未提交；初始与复核 SHA-256 均为 `1651b55c6a0b738bdd39cb93832b89e8f72036e5b1f29abb1909d8f9dff72bc4`。
- 八个用户已有 `packages.lock.json` 修改和未跟踪 `ClearVision.Product.UI.Tests/playwright-report/` 均保持未暂存、未提交。
- Browser、WebView2 和 publish 均使用 `.tmp` 隔离目录、环境变量与现有 harness 注入 flags，没有通过修改用户配置取证。

最终代码 SHA 的本地门禁、Remote CI 与 Final Gate 已闭合；Tracking 提交仅收录证据与状态，不改变产品代码。

## 2. 本地权威门禁

| 门禁 | 实际命令/入口 | 退出码与数量 | 证据 |
|---|---|---:|---|
| StudioUI install | `npm.cmd ci` | `0` | `.tmp/studio-ui-next/f05/g6-3993b8e4/logs/studio-ui-npm-ci.log` |
| lint | `npm.cmd run lint` | `0` | `logs/studio-ui-lint.log` |
| typecheck | `npm.cmd run typecheck` | `0` | `logs/studio-ui-typecheck.log` |
| 完整 unit | clean `git archive HEAD` 快照中 `npm.cmd run test:unit` | `0`，`530/530`，88 files | `logs/studio-ui-unit-candidate-snapshot.log` |
| production build | `npm.cmd run build` | `0`，334 modules | `logs/studio-ui-production-build.log` |
| bundle verify | `npm.cmd run bundle:verify` | `0` | `logs/studio-ui-bundle-verify.log` |
| bundle gate | `npm.cmd run bundle:gate` | `0` | `logs/studio-ui-bundle-gate.log` |
| Product / Inspection | `./scripts/run-tests-services-regression.ps1` | `0`，`514/514` | `.tmp/test_results/services-regression/services-regression.trx` |
| Product Station contract | serial test，`StationSyncContractsSerializationTests` | `0`，`4/4` | `reports/product-station/product-station.trx` |
| Desktop endpoint | `./scripts/run-tests-desktop-endpoints.ps1` | `0`，`341/341` | `.tmp/test_results/desktop-endpoints/desktop-endpoints.trx` |
| Desktop Station / Inspection focused | serial test，6 个 Station/Inspection 类 | `0`，`44/44` | `reports/desktop-station-inspection/desktop-station-inspection.trx` |
| Browser F05 全量 | `CV_UI_SCENARIO=studio-ui-next` + `npx playwright test` | `0`，`91 passed / 21 optional visual skipped / 0 failed` | `logs/browser-playwright-full.log`、`browser/playwright-report/index.html` |
| diff hygiene | `git diff --check` | `0` | 提交前终局复核 |

依赖安装继续报告既有 npm audit 债务：StudioUI `6 high`，UI tests `1 moderate`。本轮未执行 `npm audit fix`，未夹带依赖升级；lint、typecheck、unit、build、bundle 与 Browser 门禁均独立通过。

完整 Desktop Remote CI 初次执行 `671` 项，`669` 通过，两个旧 F02 架构守卫失败。最小修复只更新精确允许列表：共享 transport 必须恰好一个 `patch`，检测两条 route 和 `/stations` 导航必须存在，三个 F05 `AbortController` owner 必须在精确白名单中。没有改产品代码。

修复后本地受影响门禁：

- 两个架构守卫：`2/2 PASS`，`reports/desktop-architecture-fix-r2/desktop-architecture-fix-r2.trx`。
- 排除用户本地 `StudioUiEnabled=true` 所影响的唯一默认值断言后，完整 Desktop：`670/670 PASS`，`reports/desktop-full-excluding-user-config-r2/desktop-full-excluding-user-config-r2.trx`。
- 首次本地全量为 `669/670`，唯一失败是临时目录 `Access denied`；该项单独串行复跑 `1/1 PASS`，随后完整复跑 `670/670 PASS`。失败 TRX 保留，不作为 PASS 证据。
- 干净 checkout 的默认值断言已在远端执行；最终修复 SHA 的完整 Desktop `671/671 PASS`。

## 3. Browser 隔离旅程

全量 Browser 日志实际覆盖：

- Projects / Workspace 深层 hash 直达；
- Formal Run 与 Continuous Inspection 后端互斥投影；
- `/inspection` 工程选择、persisted admission/start、SSE 最新结果、stop-on-leave 与 Results 跳转；
- Station Admin 命令、identity、package deploy；
- Engineer 控制域不挂载且无写请求；
- lazy route 冷启动、chunk 失败恢复和深层 hash route；
- 20 次 route mount/unmount 后所有资源释放；
- 20 次工程切换，以及 20 次 run、stop/reconcile、project、route 生命周期后最终资源 ledger 为零。

Playwright 报告已通过内置浏览器读取，显示 `Passed 91`、`Failed 0`。21 个 skip 均为仅在显式视觉证据目录启用时执行的可选 capture case，不是功能跳过。

## 4. 冷启动资源与 bundle 闭包

Release WebView2 最终 F05 旅程记录实际资源请求：

| 入口 | 首次加载的 route 资源 | 结果 |
|---|---|---|
| Shell | `index`、`runtime-dom`、`primitives`、`design-system`、`canvas`、`productRuntime`、`vue-router` 及 3 个 CSS | 每项一次 |
| Workspace | `WorkspacePage` JS/CSS、`operatorViewModel` | 深层 route 成功 |
| Inspection selector | `InspectionProjectsPage` JS/CSS、`projectQueries` | 深层 route 成功 |
| Inspection run | `InspectionRunPage` JS/CSS | 深层 route 成功 |
| Stations | `StationDetailPage` JS/CSS、`stationsReadRuntime` | 深层 route 成功 |
| Results | `ResultsPage` JS/CSS | 跳转成功 |

最终 F05 driver JSON：`webview2/f05-release-100-1920-admin-final/evidence/studio-ui-webview2-f05-f05-release-100-1920-admin-final.json`。该记录为 `65` 个响应、`0` HTTP error、`0` asset 404、`0` duplicate Studio asset、无 console/page/request failure、所有场景横向 overflow 为 `0`。

Workspace 黄金旅程 harness 会在主动路由切换时产生被分类为预期的 request abort 和一条 fixture `599` console 消息；`meaningfulConsoleErrors=0`、`meaningfulRequestFailures=0`、page error 为零。它们与最终 F05 cold driver 的零错误记录分开保留，未被删除。

G5 冻结预算未修改：

| 闭包 | 实测 | 阈值 | 结论 |
|---|---:|---:|---|
| Shell | 809,031 B | 850,000 B | PASS |
| Workspace | 853,828 B | 900,000 B | PASS |
| Inspection | 735,853 B | 790,000 B | PASS |
| Stations | 765,378 B | 820,000 B | PASS |
| Results | 760,022 B | 820,000 B | PASS |

hard initial maximum 为 `963,630 B`；report 位于 `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/.tmp/bundle/report.json`。

## 5. 真实 WebView2、DPI 与 Release publish

真实 WebView2 通过仓库 harness、CDP 与内置浏览器能力执行，没有使用 Computer Use 或直接操作用户屏幕。

| 矩阵 | 结果 |
|---|---|
| Debug 100%：1920x1080 Admin、1366x768 Engineer | PASS |
| Release 100%：1920x1080 Admin、1366x768 Engineer | PASS |
| Native Windows 125%：Debug / Release 的 1920x1080 Admin 与 1366x768 Engineer | PASS |
| Workspace：Debug 100% 1920、Release 100% 1366、Debug 125% 1366、Release 125% 1920 深层 route | PASS |

125% 证据：系统 relative scale `0 -> 1 -> 0`，native DPI `120`、native scale `1.25`、JS DPR `1.25`；测试后已恢复 relative scale `0`。状态 JSON 位于 `reports/windows-scale-*.json`。

所有最终场景均覆盖 Product Shell、Workspace、Continuous Inspection、Station Admin/Engineer、Results、lazy route 和深层 hash route；关键操作可达，无白屏、无横向溢出。截图与 JSON 位于 `webview2/`，最终 Admin 截图为 `webview2/f05-release-100-1920-admin-final/evidence/real-webview2-f05-stations-admin.png`。

正式 self-contained `win-x64` publish：

- matrix：`.tmp/studio-ui-next/f05/g6-3993b8e4/publish-matrix/studio-ui-webview2-matrix.json`，`status=PASS`；
- release build、publish、static audit、published runtime、local no-Node audit 均为 PASS；
- 发布目录：`.tmp/publish-check/studio-ui-next-f05/g6-3993b8e4/publish/`；
- EXE SHA-256：`e68c8109e2c0f504eaa8a7d4768b971eba54112ba75ff566d791c8a23baea019f`；
- `/studio/` base path 与 published lazy assets 可加载；missing-assets 诊断按预期执行；
- 独立无 Node 目标机未执行，本机 no-Node audit 不能替代独立目标机。

## 6. Evidence harness 说明与失败尝试

仓库现有 wrapper 的 `EvidencePhase` 只接受 `f01..f04`。本轮没有为取证修改候选代码，而是使用 `EvidencePhase=f04` 作为既有 transport wrapper；所有输出目录、run name、场景 JSON 和旅程均显式标记 F05。曾尝试的两行 phase whitelist 修改已在正式证据前回退。

F05 WebView2 committed scenario 在快速 Release 启动时把 Host 首次加载与显式 `page.goto()` 冷启动合并计数，造成重复 chunk 假阳性。为保持候选 SHA 不变，最终使用 `.tmp` driver，在显式 cold navigation 前清零 request/error 计数，并补齐 Station fixture health、command、deploy 合同。driver：`.tmp/studio-ui-next/f05/g6-3993b8e4/tools/studio-ui-webview2-f05-e2e.cjs`，SHA-256 `2ea93ed0d5e8a87fd008f2a198ef93adeaccb9ae000e559440934536314ae17d`。这是证据基础设施，不是候选产品代码。

保留并如实记录的失败尝试：相对日志路径错误、snapshot restore 磁盘空间不足、Release 重复加载假阳性、临时 driver module resolution 错误、Admin fixture 合同不完整、首次本地 Desktop 临时目录 Access denied。它们均有后续纠正证据，没有删除或改写成 PASS。

## 7. Remote CI 与 Final Gate

初次 workflow_dispatch run `30259428377` 绑定 `3993b8e49b65e805afbcb38c8edf353d2d21a309`。StudioUI Quality Gates、bundle production build / budget gate、Product、Browser、Operator Industrial 等已完成 job 通过；Desktop `669/671` 暴露两个旧 F02 架构守卫漂移，因此该 run 的 Final Gate 不能通过。

修复提交为 `dc6d38737a2e183edd1bbe55590c6e8de1d854d2`；第二次 workflow_dispatch run `30276133719` 精确绑定该 SHA。Attempt 1 已确认 Desktop `671/671 PASS`、StudioUI Quality Gates 与 Operator Industrial Gate 为 success；Detection performance 出现独立波动：`WidthMeasurement p95=51.13ms` 超过 standard `45.00ms`，同时 Product 的 PPF matcher 因运行方差达到 60 分钟 timeout 而取消。本轮未修改性能预算或产品代码。

在同一 SHA 上执行 `gh run rerun 30276133719 --failed` 后，attempt 2 整体 conclusion 为 `success`：StudioUI Quality Gates（含 production bundle 与 budget gate）、Product Tests、Desktop Tests、Detection / Measurement / Data、Legacy UI & StudioUI Browser、Product / Desktop coverage、Operator Industrial Gate 与其他 required jobs 均为 success。Product 主集总计 `3841`，`3839` passed、`2` skipped，performance `10/10`；Desktop `671/671`。Coverage Summary job `90037789158` success，Final Gate job [`90038124936`](https://github.com/HerverJun/ClearVision/actions/runs/30276133719/job/90038124936) 明确输出所有 required needs 为 success。`Release Build`、`Create Release` 与 `Code Quality` 按 workflow_dispatch 条件预期 skipped。

本机按相同 standard profile 的诊断复跑为 `1/1 PASS`，报告 15 项完整，位于 `reports/detection-performance-local-r2/` 与 `reports/detection-performance-local-report-r2/`。首次带 `-NoBuild` 的尝试因本机不存在 Product test DLL 而未执行测试；随后允许构建测试项目，只有既有 `System.Collections.Immutable` 冲突警告，未夹带修复。

Attempt 2 下载的关键 artifacts：

| Artifact | ID | ZIP SHA-256 |
|---|---:|---|
| `detection-performance-report` | `8659147274` | `f2ec0bd94a39fe716dd8bb182964431c8aa950a7d303bf4454e580636218f323` |
| `detection-measurement-data-results` | `8659148718` | `4048f7188198c40bc1b64de64e8653c7814691a5fd9ee71cd7d814339d5ff1ca` |
| `product-test-results` | `8660062268` | `8c08fc6bc2f20c9c89b4e61985e091fc1f2e026da3c3322c6076f90c7dbddee2` |
| `test-results` | `8660109665` | `11bd8e119c111ad8478262ba5d646d567a9c5ebbb65caaf6f4ec5b56e0030255` |

ZIP 位于 `remote-ci/final-code-run-30276133719/artifact-zips-attempt-2/`。完整 artifact ID 列表和 run/job JSON、日志位于同一 `remote-ci/final-code-run-30276133719/` 证据目录。

## 8. Evidence manifest 与现场限制

SHA-256 manifest：`.tmp/studio-ui-next/f05/g6-3993b8e4/f05-g6-evidence-sha256.json`。最终包含 `294` 个 JSON、日志、TRX、截图、artifact ZIP 和证据脚本；manifest 自身 SHA-256 为 `5fdd0c550118f12847147b3eb29e62fb031262b2e87fdae60ca4ef1f4cb64a5d`。

```text
REAL_CAMERA=NOT_PERFORMED
REAL_PLC=NOT_PERFORMED
REAL_STATION=NOT_PERFORMED
INDEPENDENT_NO_NODE_TARGET=NOT_PERFORMED
```

Fixture、本机替身、Browser、真实 WebView2 与 publish 只关闭 F05 工程门禁，不构成生产现场验收。

## 9. 最终状态

```text
F05_ENGINEERING_STATE=DONE
F05_FINAL_GATE=PASS
F05_REAL_WEBVIEW2_DEBUG=PASS
F05_REAL_WEBVIEW2_RELEASE=PASS
F05_WINDOWS_125_DPI=PASS
F05_RELEASE_PUBLISH=PASS
F05_REMOTE_CI=PASS

PRODUCTION_ACCEPTANCE=BLOCKED
DEFAULT_ENTRY_CHANGE=BLOCKED
F06_IMPLEMENTATION=FORBIDDEN
```
