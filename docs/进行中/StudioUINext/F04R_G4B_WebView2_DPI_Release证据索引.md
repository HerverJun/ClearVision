# F04-R G4B WebView2 / DPI / Release 证据索引

> 证据日期：2026-07-25
> Initial SHA：`f0ef6a06cdf50f915f9959793b34e9ceae5140cc`
> 修复 SHA / 真实宿主证据 SHA：`25f811c3cce697d4542fd59d4667804901f0e356`
> CI hygiene SHA：`5f3cf2ae2d21022c3feb2142bd7bd6cbcf58ab5c`
> Source secret scan 修复 SHA / Remote code SHA：`70a0ab7018270d0c467d556f263dff6b812cf48f`
> Remote workflow_dispatch：[`30143297252`](https://github.com/HerverJun/ClearVision/actions/runs/30143297252)（PASS，含 Coverage Summary 与 Final Gate）

## 1. 证据边界

- Browser、Playwright、WebView2 Debug、WebView2 Release、Windows 125% 系统缩放、Release publish 分别记录，互不替代。
- 所有真实宿主运行均使用 WinForms + WebView2 + 同进程 ASP.NET Core API；WebView2 Runtime 为 Edge `150.0.4078.83`。
- Windows 125% 通过 DisplayConfig 系统缩放档位从 `cur=0` 切换到 `cur=1`，证据内同时记录 native DPI `120`、native scale `1.25` 与 JS DPR `1.25`；完成后已恢复 `cur=0`。
- `Scale=1.25` 仍由 runner 记录为 WebView2 分层证据，但不能单独证明系统 DPI；本轮以 native DPI `120` 为系统 125% 的判定依据。
- `appsettings.json` 的用户本地 `StudioUiEnabled=true` 未覆盖、未暂存、未提交。

## 2. 真实 WebView2 黄金旅程

每条黄金旅程均完成：

```text
工程
→ 流程工作区
→ Camera Binding
→ GlobalVariables
→ FinalDecision
→ Preview / ROI
→ Save
→ Formal Run / Stop / Reconcile
→ Result / Evidence
→ Admin Runtime Package
```

共同结果：`status=pass`、light/compact、HostBridge 可用、水平溢出为 0、console/page/meaningful request errors 为 0、Evidence export 建议名为 `g4b-webview2-evidence.zip`、Runtime Package HTTP 为 `200`。

| 宿主 | 尺寸 | 系统 DPI | JS DPR | 证据 JSON |
|---|---:|---:|---:|---|
| WebView2 Debug | 1920×1080 | 96 / 100% | 1.0 | `.tmp/studio-ui-next/f04/g4b-fixsha/debug-1920/evidence/studio-ui-webview2-g4b-fix-debug-1920.json` |
| WebView2 Debug | 1366×768 | 96 / 100% | 1.0 | `.tmp/studio-ui-next/f04/g4b-fixsha/debug-1366/evidence/studio-ui-webview2-g4b-fix-debug-1366.json` |
| WebView2 Release | 1920×1080 | 96 / 100% | 1.0 | `.tmp/studio-ui-next/f04/g4b-fixsha/release-1920/evidence/studio-ui-webview2-g4b-fix-release-1920.json` |
| WebView2 Release | 1366×768 | 96 / 100% | 1.0 | `.tmp/studio-ui-next/f04/g4b-fixsha/release-1366/evidence/studio-ui-webview2-g4b-fix-release-1366.json` |
| WebView2 Debug | 1920×1080 | 120 / 125% | 1.25 | `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-debug-1920/evidence/studio-ui-webview2-g4b-fix-debug-125-1920.json` |
| WebView2 Debug | 1366×768 | 120 / 125% | 1.25 | `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-debug-1366/evidence/studio-ui-webview2-g4b-fix-debug-125-1366.json` |
| WebView2 Release | 1920×1080 | 120 / 125% | 1.25 | `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-release-1920/evidence/studio-ui-webview2-g4b-fix-release-125-1920.json` |
| WebView2 Release | 1366×768 | 120 / 125% | 1.25 | `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-release-1366/evidence/studio-ui-webview2-g4b-fix-release-125-1366.json` |

125% 下 1920 物理屏幕的有效逻辑工作区约为 1536×864，WinForms 窗口受工作区限制；证据中的实际 window/client size 与截图像素均已保留，不把请求尺寸冒充实际 client size。

## 3. Release publish

- 发布目录：`.tmp/publish-check/studio-ui-next-f04/g4b-fixsha-r1/publish/`
- 发布模式：Release、`win-x64`、self-contained。
- 发布目录 EXE 启动黄金旅程：
  - 1920×1080：`.tmp/studio-ui-next/f04/g4b-fixsha/publish-1920/evidence/studio-ui-webview2-g4b-fix-publish-1920.json`
  - 1366×768：`.tmp/studio-ui-next/f04/g4b-fixsha/publish-1366/evidence/studio-ui-webview2-g4b-fix-publish-1366.json`
- 静态资源与发布目录启动审计：`.tmp/studio-ui-next/f04/g4b-fixsha/publish-static-audit.json`
- 审计结果：`publishStaticScan=PASS`、`publishedProductRuntime=PASS`、`localNoNodeEvidence=PASS`、forbidden artifacts `0`。
- 独立无 Node 目标机：`NOT_PERFORMED`；本机外置 Node 只作为 CDP driver，未进入 Desktop 进程树。
- 主 JS chunk：`963.63 kB`，继续作为已知警告记录；未形成 Release 或宿主阻断，本轮未做大规模拆包。

## 4. 代表截图

Debug、Release、125% 与 publish 每个证据目录均包含以下场景：

- `workspace`
- `camera-binding`
- `global-variables`
- `final-decision`
- `preview-roi`
- `saved`
- `result-evidence`
- `runtime-package`

代表路径：

- `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-release-1366/evidence/g4b-g4b-fix-release-125-1366-workspace.png`
- `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-release-1366/evidence/g4b-g4b-fix-release-125-1366-result-evidence.png`
- `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-release-1366/evidence/g4b-g4b-fix-release-125-1366-runtime-package.png`
- `.tmp/studio-ui-next/f04/g4b-fixsha/dpi125-release-1920/evidence/g4b-g4b-fix-release-125-1920-workspace.png`

人工抽查未发现关键遮挡、弹窗越界、异常横向滚动或焦点丢失。1366 结果详情允许纵向滚动；诊断码发生受控换行但未截断关键操作。

## 5. 测试与门禁

| 门禁 | 结果 |
|---|---|
| typecheck | PASS |
| lint | PASS |
| build | PASS；主 chunk 警告保留 |
| 全量 Vitest | `495/499 PASS`；4 个失败均为用户本地 `StudioUiEnabled=true` 的正式默认值 guard |
| 排除受保护配置断言后的 Vitest | `495/495 PASS`（482 通用 + 13 architecture） |
| CameraId / CameraBindingId 定向测试 | `3/3 PASS` |
| single-owner / import / lifecycle 定向 | `19/19 PASS` |
| Desktop endpoints | `335/335 PASS` |
| Desktop architecture | `8/9`；唯一失败为同一用户配置默认值断言，排除后 `8/8 PASS` |
| Studio Next Playwright | `89 passed / 21 skipped / 0 failed` |
| Browser 黄金旅程 | PASS，包含 1920×1080 与 1366×768 |
| WebView2 Debug / Release | PASS |
| Windows 125% | PASS，native DPI=120 |
| Release publish | PASS |

## 6. Remote CI 与 Final Gate

- Run `30142587999` / SHA `25f811c3...`：Diff Hygiene 失败；根因是历史文档/技能参考中的尾随空白，未伪造为 PASS。
- Run `30142748870` / SHA `5f3cf2ae...`：Contracts & Vision Agent 的 source secret scan 命中两个测试/vendor 假阳性；在不放宽扫描器、不删除断言的前提下完成最小修复后取消该 run。
- Run `30143297252` / SHA `70a0ab70...`：所有实际门禁 job、Product Tests、Coverage Summary 与 Final Gate 均 `success`；`Code Quality` 因 workflow 条件正常 `skipped`。
- Final Evidence 提交只包含本索引、完成报告、README 与 F04-R 主计划状态；该提交的 Remote CI / Final Gate 作为提交后的后置验证，由最终交付回报记录，避免文档对自身 SHA/run ID 形成不可实现的自引用。

## 7. 未执行证据

```text
REAL_CAMERA=NOT_PERFORMED
REAL_PLC=NOT_PERFORMED
REAL_STATION=NOT_PERFORMED
INDEPENDENT_NO_NODE_TARGET=NOT_PERFORMED
```

证据 harness 使用受控 Camera Binding 与 fixture frame 验证真实宿主 UI/合同链，不冒充真实相机现场联调。
