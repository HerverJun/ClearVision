# Evidence Index

## Baseline

| Item | Value |
| --- | --- |
| Legacy branch/head | `codex初稿 @ e76c74e392bb14ffe02ef9ea9c7a614cb8987f04` |
| Legacy status | clean, tracks `origin/codex初稿` |
| Next branch/head | `studio-ui-next @ 22a3d26a00a2d3b8098165aab5489ce54f5bc95b` |
| Next status | ahead 7, heavily dirty before audit |
| Current startup | `appsettings.json:33` = `NEXT_DEFAULT` |
| Enabled flags | Workspace, Inspector, Preview, Globals, Settings, Projects, Inspection, Stations, Results, AI, NPoint all true at `appsettings.json:34-49` |

## Foundational Decision Sources

- `docs/进行中/StudioUINext/F09_G1_LegacyNext终局能力矩阵.md`: `MIGRATED` 只代表 code/route/owner；deferred 必须保留 Legacy fallback。
- `docs/进行中/StudioUINext/F10_ContractAndProductionPlan.md`: 当前 truth，`PRODUCTION_ACCEPTANCE=NOT_GRANTED`、`LEGACY_RETIREMENT=NOT_APPROVED`。
- `docs/进行中/StudioUINext/ADR-G2-合同解阻与能力处置.md`: MIGRATE/RELOCATE/DEFER 的 owner、权限、identity、reconcile 与重新进入条件。
- 根 `AGENTS.md`: 后端 authority、单 owner、保存链、Canvas、HostBridge 与测试约束。

## Static Evidence Anchors

| Finding | Legacy | Next / backend |
| --- | --- | --- |
| Canvas run-to-node | `flowCanvas.js:2715,2851-2871` | `canonicalFlowCanvas.ts:711` |
| Canvas dblclick/subgraph | `flowCanvas.js:2385`; `app.js:539-556,2150-2157` | no flow owner command |
| Parameter recommendation | `propertyPanel.js:442,2358,2449-2456` | `ApiEndpoints.cs:1672`; no Inspector caller |
| Demo project | `projectView.js:475,525-527`; `projectManager.js:161-186` | `DemoEndpoints.cs:12,26,40`; no Next caller |
| Database advanced | `settingsApi.js:11-15`; `systemTabs.js` | `SettingsEndpoints.cs:185,227,254`; `contracts.ts:520-527` exclusion |
| Runtime Pilot | `settingsApi.js:33-58`; `runtimePreviewPilotConsole.js` | `SettingsEndpoints.cs:590-1429`; exclusion only |
| Station confirmation | `stationMonitorView.js:942-1025` | `StationAdminPanel.vue:187-193,263-281` |
| Station token | `stationTab.js:149-160,309-359` | `SettingsStationPanel.vue:239-255,392-424` |
| Global search/locate | `globalVariablePanel.js:226,463,1142-1155` | `GlobalVariablesWorkbench.vue` raw lists, no locate command |
| Standalone image / annotations | `features/image-viewer/imageViewer.js:273,670,676` | ImageViewport has no corresponding control/owner API |
| Status context | `index.html:804,837,857,860`; `app.js:3399-3410` | ProductLayout session/service only |

## Validation Performed Against Dirty Next Snapshot

| Validation | Result | Scope / limitation |
| --- | --- | --- |
| `npm run lint` | PASS | StudioUI working tree |
| `npm run typecheck` | PASS | StudioUI working tree |
| `npm run test:unit` | PASS: 144 files / 946 tests | Unit evidence, not real host |
| `npm run build` | PASS: 544 modules | Build only |
| Targeted Chromium fixture journeys | PASS: 121 passed / 57 evidence-only skipped / 0 failed | Static fixture, not WebView2/DPI/hardware |
| Desktop Debug build | PASS: 0 warnings / 0 errors | Desktop compile |
| Isolated WinForms/WebView2 golden journey | PASS at 100% DPI | Real local host, harness-seeded session/project |
| WebView2 cleanup | PASS | No forced exit/deadline violation; isolated stores removed |
| Additional targeted .NET tests | NOT COMPLETED | Child task exceeded 10-minute threshold and returned no result; no claim made |

The run directory contains no durable command logs for npm/Chromium/Desktop counts; those outcomes are session execution evidence. The WebView2 JSON files below are durable and machine-readable.

## Durable WebView2 Evidence

All files copied under `evidence/webview2/`; original disposable source was `.tmp/studio-ui-next/f09/parity-audit-20260814-0127-r3/evidence/`.

- `studio-ui-webview2-parity-audit-20260814-0127-r3.json`: `status=pass`, source SHA `22a3d26...`, Debug, scale 1, deep canvas, seeded workspace, formal run and golden journey true; no meaningful console errors/request failures.
- `studio-ui-webview2-parity-audit-20260814-0127-r3-cleanup.json`: runner/process/port/runtime/shutdown cleanup all pass; forced exit false; isolated DB removed.
- Nine `g4b-*.png` screenshots, each 1584x936: workspace, preview ROI, final decision, global variables, camera binding, result evidence, saved state, runtime package.
- `real-webview2-...workspace...png`, 1584x936: final real WebView2 state at Runtime Package dialog.
- `g4-preview-input.ppm`, 100x100: deterministic preview input.

## Visual Inspection

Manually inspected:

- `g4b-...-workspace.png`: nonblank; Canvas, Inspector, Preview, toolbar and status visible without incoherent overlap.
- `g4b-...-preview-roi.png`: nonblank; selected ROI node, preview image, edit ROI action, result summary and key outputs visible.
- `real-webview2-...workspace...png`: nonblank; Runtime Package modal shows generated package identity and deployable registration. It proves this journey at 100% DPI only.

## Evidence Layers Not Interchangeable

- Chromium fixture != real WebView2.
- WebView2 scale 1 != real Windows 125%.
- Local Debug build != independent no-Node deployment.
- Seeded camera/result data != real Camera/PLC/TCP/Station/AI.
- Local branch validation != Remote CI or production acceptance.
