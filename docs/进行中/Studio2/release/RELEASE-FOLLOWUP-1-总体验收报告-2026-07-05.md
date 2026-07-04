# RELEASE-FOLLOWUP-1 总体验收报告 - 2026-07-05

## 结论

- Verdict: `BLOCKED_RELEASE_CUTOVER_GAP`
- Branch: `codex初稿`
- Initial SHA: `0394a2ba0e994b5e6720de2ee65e08b87edda1d7`
- Final SHA: 提交自身 SHA 不写入 tracked 文件；以 push 后核对值为准
- Remote SHA: 启动门禁中本地 HEAD、`origin/codex初稿`、GitHub 远端均为 `0394a2ba0e994b5e6720de2ee65e08b87edda1d7`；提交前会再次 fetch 核对
- 执行窗口: 2026-07-05T00:51:55+08:00 至 2026-07-05T01:22:09+08:00，约 30 分钟

本轮按 G1-G16 总体验收复核。结论不是 `RELEASE_READY`：ClearVision 的工业视觉 Studio + Runtime + Station + 证据体系主线仍成立，测试和发布包证据较 G16 基线明显补强；但 `Studio:WorkspaceV2Enabled=false`，旧 `app.js` 仍是 release production root，`/v2/index.html` 仍是发布资产而非唯一生产入口。因此主阻断为 cutover gap。真实 WebView2、真实无 Node 目标机、DPI/分辨率矩阵与 GitHub workflow_dispatch 仍未完成，作为 evidence gap 记录。

## 产品初衷复核

- WinForms + WebView2 + ASP.NET Core Desktop 保持；未引入 Electron。
- Station 独立运行；测试与 csproj 依赖未发现 Vue/Node/Studio runtime dependency。
- FlowCanvas 与 ImageCanvas/Scene renderer 继续复用既有实现，未出现第二渲染内核。
- Project/Flow/GlobalVariables 正式保存仍经 `ProjectService + ProjectSaveCoordinator`。
- AgentRun/EventStore/Workspace Snapshot/terminal recovery 未重构；AgentRun endpoints 全量 Desktop 测试通过。
- G14 history/detail/compare/previous-success/evidence/export/retention 继续经正式 loader/endpoint，未发现 preview/debug cache 污染。
- 八个 G15 capability flags 默认 true；`WorkspaceV2Enabled` 仍 false；旧 root 未 cutover。

## 修复项

- 更新 NPoint ROI E2E 守卫：当 NPoint calibration draft workbench 默认接管时，断言 legacy `.roi-editor-panel` 不挂载，并验证 draft sample 编辑、禁用、重排、删除行为。
- 在迁移台账中补回 G15.1/G15.2 runtime evidence tokens，同时保留 G16 `PRODUCTION_DEFAULT_ON_G16_PARTIAL` 状态。
- 重新生成 AI operator knowledge graph/cards/report，补齐 PixelToWorldTransform `CalibrationAssetId` 与 `CalibrationBundleId` metadata。
- 修正 CircleCaliperFitV2 dataset `manifest.sha256`，使校验文件匹配已签入 manifest bytes。
- 重跑并更新 G16 benchmark artifacts。

## 证据矩阵

| Gate | Result |
|---|---|
| Baseline branch/remote gate | PASS，三方均为 `0394a2ba0e994b5e6720de2ee65e08b87edda1d7` |
| FrontendV2 `npm ci` / lint / typecheck / unit / build | PASS，unit 43/43 |
| UI unit full | PASS，655/655 |
| UI preview smoke | PASS |
| Playwright full | PASS，85 passed / 1 skipped；修复前发现 stale NPoint guard |
| Targeted edited Playwright | PASS，1/1；仅 WebServer `DEP0066` warning |
| Desktop focused WebView2/architecture/endpoints/AgentRun/Station | PASS，188/188 after ledger token clarification |
| Product services regression | PASS，65/65 |
| Calibration regression | PASS，115/115 |
| Product broad persistence/runtime/inspection/operator batch | PASS，363/363 after knowledge graph regeneration |
| Product full serial | PASS，3289 passed / 4 skipped after checksum refresh |
| Desktop full serial | PASS，501/501 |
| OperatorLibrary package smoke | PASS，40/40 |
| Operator contract quick suite | PASS，86/86 |
| `node --check` JS/MJS sweep | PASS，129 files |
| JSON parse regenerated artifacts | PASS |
| `git diff --check` | PASS，仅 Git LF->CRLF normalization warnings |
| Desktop Release publish | PASS |
| Publish no Node/dev artifact scan | PASS |
| No-Node target-machine startup | `NOT PERFORMED`; only static publish scan performed |
| 真实 WebView2 smoke | `NOT PERFORMED` |
| DPI/resolution matrix | `NOT PERFORMED`; current display probe only |
| GitHub CI workflow_dispatch | `NOT RUN`; workflow supports dispatch, but local `gh` is not authenticated |

## Benchmark

Artifacts:

- `docs/进行中/Studio2/release/artifacts/G16-benchmark-2026-07-04.json`
- `docs/进行中/Studio2/release/artifacts/G16-benchmark-2026-07-04.md`

Environment: Node `v22.17.0`, Windows `10.0.22631 x64`, CPU `13th Gen Intel(R) Core(TM) i5-13600KF`, 20 logical cores, memory `32581 MB`.

| Primitive count | p95 frame ms | p95 update ms | avg frame ms | max frame ms | heap delta MB |
|---:|---:|---:|---:|---:|---:|
| 300 | 0.078 | 0.051 | 0.021 | 0.171 | 0.317 |
| 1000 | 0.164 | 0.160 | 0.051 | 0.264 | 1.355 |

Display probe: `PERFORMED`, primary `1920x1200`, DPI `96x96`, scale `100%`. DPI/resolution mutation matrix remains `NOT_PERFORMED`.

## 重分配记录

- Focused UI and build/publish evidence already passed, so remaining time was reallocated to broader .NET coverage.
- Desktop focused coverage was expanded to full Desktop serial.
- Product focused services/calibration/persistence coverage was expanded to Product full serial.
- Product full exposed stale dataset checksum; fixed and reran full Product.
- OperatorLibrary package smoke and quick operator contract suite were added after full Product/Desktop passed.
- Static no-Node publish scan and G16 benchmark were rerun after release publish evidence.

## Blockers

- `BLOCKED_RELEASE_CUTOVER_GAP`: `/v2/index.html` is not the unique production root; old `app.js` remains release root and `Studio:WorkspaceV2Enabled` remains false.
- Evidence gap: true WebView2 smoke, true no-Node target-machine launch, workflow_dispatch CI, and DPI/resolution matrix were not performed.
- Legacy flag-off libraries are still intentionally retained; deletion gate requires the missing release evidence above.

## 下一步建议

1. Run authenticated workflow_dispatch on `codex初稿` after this verification commit lands.
2. Perform real WebView2 smoke on the published app with console/page/network/error logs and screenshots.
3. Validate a true no-Node target machine, not just publish artifact scanning.
4. Execute DPI/resolution matrix or document a lab machine matrix with screenshots.
5. Only after those pass, plan the actual `/v2` production-root cutover and legacy deletion gate.
