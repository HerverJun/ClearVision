# M00 文件 Owner 与验证矩阵

```text
AUDIT_SOURCE_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
BRANCH=studio-ui-next
WORKTREE=CLEAN_AFTER_DOCUMENTATION_COMMIT
INITIAL_TRACKED_STATUS_ENTRIES=65
INITIAL_UNTRACKED_FILES=54
INITIAL_DIRTY_FILE_TOTAL=119
ARCHIVED_IGNORED_STATUS_ENTRIES=39
RESTORED_GENERATED_OR_STATUS_ENTRIES=11
SCOPED_PRODUCT_COMMITS=6
FINAL_TRACKED_STATUS_ENTRIES=0
FINAL_UNTRACKED_FILES=0
FINAL_DIRTY_FILE_TOTAL=0
OWNER_COORD=COORD-M
```

| 范围 | 当前 owner | 文件白名单/对象 | 接管结论 | 验证 |
| --- | --- | --- | --- | --- |
| Product Host 输入闭包 | `COORD-M` | `ClearVision.Product.Desktop.csproj` | 保留用户改动；补充的 15 个 canonical alias 输入属于增量构建修复，不进入 M02 capability owner | infrastructure unit、Desktop build |
| Workspace shell | `OWN-M02-WORKSPACE` | `StudioUI/src/capabilities/project-workspace/**`、`inspection-run/RunStatusBar.vue`、对应 unit/E2E | 作为 M02 单一纵向 owner 提交；不重复实现 | Workspace unit、F03 Browser |
| Canonical Canvas | `COORD-M` | `platform/canvas/canonicalFlowCanvas.ts` 与 Legacy adapter 边界 | 保留；不得由 capability owner 覆盖或创建第二 Canvas 内核 | Canvas unit、architecture guard |
| Browser fixture | `COORD-M` | `playwright.config.ts`、`tests/support/studio-ui-next-*.cjs`、基础设施 unit | 保留；单一 runner，禁止第二 server owner | Node infrastructure unit、Playwright |
| 生成性能报告 | 原生成 owner | `ClearVision.Product/test_results/*benchmark*`、`*performance*`、`*quality*` | 8 个无法证明属于 M 系列的重新生成结果已恢复为 HEAD；未混入视觉提交 | `git diff`、远端历史 run |
| Review handoff | `COORD-M` | `.tmp/studio-ui-next/review-handoff-9800d6045a9f/**` | 初始 38 个 Git dirty 条目（41 个物理文件）已无损移入 ignored archive；不作为代码通过证据 | 内容/来源审计 |
| Playwright HTML 报告 | `COORD-M` | `.tmp/playwright-reports/clearvision-product-ui/**` | 已将既有报告归档，并把 reporter 固定到 ignored `.tmp`，防止再次污染 worktree | Playwright config load |
| M00 文档 | `COORD-M` | 本目录新增/校正文档 | 当前批次单一文档 owner | `git diff --check` |

## 共享文件白名单

`package.json`、lockfile、Vite/TypeScript/ESLint、router/navigation/ProductLayout、tokens、design-system public exports、platform contracts、Host/CI/evidence scripts 只由 `COORD-M` 修改。capability owner 需要跨界时，先提交接口请求并记录在阶段报告。

## 当前门禁

| Gate | 结果 | 备注 |
| --- | --- | --- |
| Node/npm | PASS | `v24.14.0` / `11.9.0` |
| Playwright config load | PASS | `CV_UI_SCENARIO=studio-ui-next npx playwright test --list` exit 0；HTML reporter 指向 ignored `.tmp` |
| lockfile scope | PASS / RESTORED | 2 个 `packages.lock.json` 仅发生 restore 键序变化，已恢复为 HEAD，未提交 |
| lint/typecheck/unit/build/bundle | PASS / CONTENT-EQUIVALENT CANDIDATE | 提交前候选内容随后原样形成 `f8f581932469f7c52fe547b7bcabe8ad45d89532`；`129/129` files、`800/800` tests、476 modules；未声称命令在 commit SHA 上重跑 |
| Browser baseline | PASS | `146 passed / 26 explicit skipped / 0 failed`；不等价于 WebView2 |
| WebView2 100% | PASS / DIRTY CANDIDATE | 真实 Debug/Release WebView2；native DPI 96；不等价于 final SHA |
| WebView2 125% | BLOCKED | 当前 Windows 会话为原生 100%，未切换或模拟 125% |
| User visual approval | NOT RUN | 不能由自动化截图代替 |
| Field hardware/production acceptance | NOT PERFORMED | 不在本机证据范围 |

## 为什么初始会有 119 个 dirty 条目

初始数量是多个批次累积后的 Git 状态条目数，不是 119 个独立产品修改：

| 来源 | 初始数量 | 性质 | 处理 |
| --- | ---: | --- | --- |
| M 系列实现与构建输入 | 36 | Shell/Design System、Workspace/Canvas、Settings/Stations/Results；其中 Legacy adapter 1 项无内容差异 | 按 owner 保留，禁止跨组混提 |
| 测试与证据基础设施 | 20 | StudioUI unit、Browser、WebView2/DPI、runner 生命周期 | 按验证层与 owner 保留 |
| M00/M06-M09/F09 文档 | 14 | 阶段报告、Owner 矩阵、审计与发布阻断说明 | 独立文档组保留 |
| tracked 生成噪声 | 10 | 8 个 benchmark/performance 报告、2 个 NuGet lockfile 键序变化 | 与 M 系列提交隔离 |
| Review handoff 重复快照 | 38 | zip、patch、源码副本、日志、校验和 | 已移入 ignored `.tmp`，未删除 |
| Playwright HTML 生成物 | 1 | 浏览器测试报告 | 已移入 ignored `.tmp`，后续直接输出 `.tmp` |

因此，初始 `119 = 69` 个有内容的源码/测试/文档条目 `+ 10` 个 tracked 生成噪声
`+ 39` 个已归档的 untracked 生成/交接条目 `+ 1` 个无内容差异状态条目。

## 69 个有意改动的提交分类

以下清单覆盖本轮提交的全部有意改动。已归档到 `.tmp` 的审查副本和 HTML 报告、已恢复的生成噪声
以及无内容差异状态不计入提交。

### A. M 系列实现与构建输入（35）

`COORD-M` 共享边界（14）：

- `ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/layouts/ProductLayout.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/layouts/product-layout.css`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/pages/auth/ChangePasswordPage.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/pages/auth/LoginPage.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/app/pages/auth/SetupPage.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvButton.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvModal.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvToastRegion.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/primitives/CvTypography.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/design-system/tokens/tokens.css`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/labs/canvas/canvasLab.css`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/labs/design/designLab.css`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/platform/canvas/canonicalFlowCanvas.ts`

`OWN-M02-WORKSPACE` / Canvas adapter 边界（10）：

- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/inspection-run/RunStatusBar.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/WorkspaceShell.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/FlowCanvasSurface.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/FlowWorkspace.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/OperatorFlyout.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/OperatorRail.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/flow/workspaceLayoutOwner.ts`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/image/ImageViewport.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/inspector/InspectorPanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/project-workspace/preview/PreviewPanel.vue`
`flowCanvasAdapter.js` 经 `git diff --quiet` 确认为 `NO_CONTENT_DIFF`，已恢复索引/工作树状态，未进入提交。

Capability-local 只读/设置页面（11）：

- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/results-read/ResultsPage.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsAiModelPanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsCameraPanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsCameraPreviewSection.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsDatabasePanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsPage.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsRuntimePanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsSecurityPanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/settings/SettingsTcpPanel.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/stations-read/StationDetailPage.vue`
- `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/src/capabilities/stations-read/StationsPage.vue`

### B. 测试与证据基础设施（20）

- StudioUI unit（7）：`canonicalFlowCanvas.spec.ts`、`runConsole.spec.ts`、`workspaceLayoutOwner.spec.ts`、
  `cameraPanel.spec.ts`、`settingsPage.spec.ts`、`stationAiPanel.spec.ts`、`mSeriesVisualGuard.spec.ts`。
- UI E2E/support（12）：`playwright.config.ts`、`f02-browser-fixture.ts`、`f02-overview.spec.ts`、
  `f03-workspace.spec.ts`、`f04-browser-evidence.ts`、`f04-project-lifecycle.spec.ts`、
  `f06-g5-history.spec.ts`、`studio-ui-webview2-smoke.cjs`、`studio-ui-next-server.cjs`、
  `studio-ui-next-global-setup.cjs`、`m07-accessibility-resilience.spec.ts`、
  `studio-ui-next-infrastructure.test.mjs`。
- M08 script（1）：`scripts/studio-ui-next/Test-StudioUiDpiEvidence.ps1`。

这些文件均归 `COORD-M` 或对应 capability 测试 owner；验证通过后按基础设施、Workspace、Browser、WebView2
边界提交，没有创建第二 runner、Canvas 或 capability owner。

### C. 文档（14）

- tracked（4）：`F09_FinalEvidenceManifest.md`、`F09_OPEN_ISSUES.md`、`F09_完成报告.md`、
  `M00_视觉精修进入基线.md`。
- untracked（10）：`M00_LegacyNext任务与视觉差异矩阵.md`、`M00_文件Owner与验证矩阵.md`、
  `M00_视觉场景与截图索引.md`、`M06_Browser视觉验收报告.md`、
  `M07_可访问性响应式状态韧性审计报告.md`、`M08_WebView2_DPI性能收口报告.md`、
  `M09_最终签收与交接审计报告.md`、`Studio_UI_Next_M系列完整开发计划_FINAL_AUDITED.md`、
  `Studio_UI_Next_整体迁移审计_9800d6045a9f.md`、
  `Studio_UI_Next_迁移发布阻断收口_9800d6045a9f_WORKTREE.md`。

原权威计划仍是仓库外 `C:/Users/HerverJun/Desktop/Studio_UI_Next_M系列完整开发TODO计划_FINAL.md`；
仓库内 `FINAL_AUDITED` 是既有派生文档，不提升为第二权威 TODO。

### D. 已恢复且未提交（11）

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/packages.lock.json`、
  `ClearVision.Product/src/ClearVision.Product.Runtime/packages.lock.json`（2）：restore 只调整 Project dependency 键序；
  已恢复为 HEAD，未提交。
- `ClearVision.Product/test_results/` 下 8 个 benchmark/performance/quality 报告：生成来源与 M 系列实现提交关系
  无法证明；已恢复为 HEAD，未提交。
- `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvasAdapter.js`（1）：
  无内容差异状态；已恢复索引/工作树状态，未提交。

### E. 已归档且不再计入 dirty（39）

- 原 `ClearVision.Product/tests/ClearVision.Product.UI.Tests/playwright-report/index.html`（1）已移动到
  `.tmp/playwright-reports/clearvision-product-ui-9800d6045a9f/index.html`。
- 原 `_review_handoff/**`（38 个 Git dirty 条目、41 个物理文件）已移动到
  `.tmp/studio-ui-next/review-handoff-9800d6045a9f/**`。
- 两个目标都由根 `.gitignore` 的 `/.tmp/` 覆盖；没有删除内容，也不作为当前代码 PASS 证据。

### 提交判定

```text
SCOPED_COMMITS_CREATED=6
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
M00_STATE=PARTIAL_ACCEPTANCE_OPEN
```

| Commit | 范围 |
| --- | --- |
| `d9f2c045828f4e8fee96acada7c55352805733b5` | Desktop/Playwright 构建与 runner 生命周期 |
| `2bc14c0ecc38e08f437738668f7e42037e2a05dc` | Shell、Design System、认证页与视觉守卫 |
| `20ca5b3caa115de74beef568c74fcd0998f69561` | Workspace、Canvas、Formal Run 与对应测试 |
| `0723ce8d047845d295a54ce544ce08006e813652` | Settings、Stations、Results 与局部测试 |
| `ceebd8f7b66c507c4ec1a5562276ae7742d55541` | M06/M07 Browser 验收覆盖 |
| `f8f581932469f7c52fe547b7bcabe8ad45d89532` | M08 WebView2/DPI 证据工具；M00 产品基线 |

69 个有意改动已按 owner 和验证层进入 6 个 scoped commits；11 个生成/状态噪声已恢复，39 个重复/生成条目
已无损归档到 ignored `.tmp`。文档收口提交位于 `M00_BASELINE_SHA` 之上，不改变产品基线。M00 仍因真实
Windows 125%、当前基线 Remote CI 和产品视觉签收未完成而保持 `PARTIAL_ACCEPTANCE_OPEN`。
