# M08 WebView2、Windows DPI 与性能收口报告

```text
STAGE=M08
STATE=BLOCKED_REAL_HOST_125
EVIDENCE_SOURCE_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
BRANCH=studio-ui-next
EVIDENCE_WORKTREE_STATE=DIRTY_SCOPE_CLASSIFIED
OWNER=COORD-M
AUTHORITY_CHANGED=NO
OWNER_TOPOLOGY_CHANGED=NO
WEBVIEW2_100=PASS_DIRTY_CANDIDATE
WEBVIEW2_125=BLOCKED
WINDOWS_DPI_100=PASS
WINDOWS_DPI_125=NOT_PERFORMED
INDEPENDENT_NO_NODE=NOT_PERFORMED
```

## 已完成的本地检查

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| StudioUI lint | `PASS` | `npm run lint` |
| StudioUI typecheck | `PASS` | `npm run typecheck` |
| StudioUI unit | `PASS` | `129 files / 800 tests` |
| StudioUI build | `PASS` | `476 modules transformed` |
| bundle gate | `PASS` | `.tmp/bundle/report.json` |
| bundle reproducibility | `PASS` | `.tmp/bundle/report.json` |
| M-series architecture guard | `PASS` | `1 file / 5 tests` |
| Browser full | `PASS` | `146 passed / 26 explicit skipped / 0 failed` |
| Desktop host minimum tests | `PASS` | 5 classes / `95 passed` / 0 failed，单一 `.csproj` 串行调用 |
| Real Debug WebView2 100% DPI | `PASS` | `.tmp/studio-ui-next/m-series/m08/9800d604/webview2-100-dpi-f09/evidence/` |
| Real Debug WebView2 Golden Journey | `PASS` | `.tmp/studio-ui-next/m-series/m08/9800d604/webview2-100-golden-f09-v3/evidence/` |
| Release self-contained publish/runtime | `PASS` | `.tmp/studio-ui-next/m-series/m08/9800d604/publish-matrix-f09-v5/` |
| publish static/runtime audit | `PASS` | v5 matrix：7/7 runs、static scan、published runtime、local no-Node audit 均 PASS |

## 真实宿主边界

### 尝试记录

| 尝试 | 结果 | 事实与处置 |
| --- | --- | --- |
| f04 DPI | `FAIL_PRESERVED` | 旧 F04 navigation contract 禁止当前 M-series route；未把失败改写为 PASS |
| f09 DPI | `PASS` | `webview2-100-dpi-f09/evidence/`；真实 Windows 100% / native DPI 96 |
| f09 Golden v1 | `FAIL_PRESERVED` | formal request-chain 断言未记录允许的 pre-stop reconcile |
| f09 Golden v2 | `FAIL_PRESERVED` | admission timing race；未形成成功 evidence |
| f09 Golden v3 | `PASS` | 20-cycle、Canvas/ROI、formal run/reconcile/stop 与 shutdown 全部通过 |
| publish matrix v1 | `FAIL_PRESERVED` | Stations live page 的 `networkidle` 永不稳定；harness 改用 `domcontentloaded` |
| publish matrix v2 | `FAIL_PRESERVED` | Results 测量了 `v-show=false` 的日期控件；产品页面未换行 |
| publish matrix v3 | `FAIL_ENVIRONMENT` | sandbox 拒绝 `Get-CimInstance Win32_Process`；build/publish PASS，宿主未开始；临时目录已清理 |
| publish matrix v4 | `FAIL_PRESERVED` | 同一 `align-items:end` 行内字段与按钮顶边不同；harness 改为可见控件底边对齐 |
| publish matrix v5 | `PASS` | Release build/publish、7/7 host runs、static/runtime/local no-Node audit PASS，清理 PASS |

v2/v4 的实际几何证明 failure 来自证据 harness 的测量定义，不是通过修改产品 CSS 绕过。相应 Node
infrastructure guard 为 `3/3 PASS`，并禁止恢复 `networkidle`、隐藏日期控件测量和硬编码 client width。

### Publish/no-Node

v5 publish-only matrix 在真实 Release Desktop/WebView2 上串行通过 `LEGACY_FALLBACK`、Overview、Projects、
Operators、Stations、Results 和 missing-assets failure mode，共 `7/7 PASS`；Release build/publish、静态资源扫描、
published runtime 和本机 no-Node 审计均为 `PASS`，publish/runtime/artifacts 临时目录均已清理。manifest：
`.tmp/studio-ui-next/m-series/m08/9800d604/publish-matrix-f09-v5/studio-ui-webview2-matrix.json`。

这台机器存在 `node.exe`，外部 CDP driver 也由 Node 启动，因此 `cleanMachineWithoutNode=NOT_PERFORMED`。
本机 local no-Node audit 只证明发布包不携带 Node 开发依赖且 Desktop 运行投影完整，不能替代独立无 Node 目标机。

### DPI

当前显示器物理分辨率为 `1920x1080`，Windows 原生 DPI 为 `96`（100%），WebView2 Runtime 为
`151.0.4129.59`。Debug f09 DPI 证据记录 native window `1920x1080`、client `1904x1041`、CSS viewport
`1904x1016`、DPR `1`、Canvas logical/backing `1212x840`、pointer hit 有效、global overflow `0`，且
`PerMonitorV2=true`。仅要求 scale `1.0` 的审计报告
`.tmp/studio-ui-next/m-series/m08/9800d604/m08-windows-100-dpi-audit.json` 为 `PASS`、evidence count `2`。

当前 Windows session 无法取得原生 125% scale。本轮没有修改系统缩放，也没有用 Browser DPR、
`force-device-scale-factor` 或 CSS 文本压力替代，因此 `WINDOWS_DPI_125=NOT_PERFORMED`、
`WEBVIEW2_125=BLOCKED`。

### 性能与现场

Debug f09 Golden Journey 已完成 Workspace Canvas/ROI/pointer、正式运行成功、response-loss reconcile、
stop/reconcile、结果与 runtime package，并完成 `20` 次 mount/unmount lifecycle；disposed owner/resource ledger
归零，宿主自然 shutdown，无 forced exit。该证据不是现场 Camera/PLC/TCP/Station 或 production soak；后两类
仍为 `NOT_PERFORMED`。

## 结论

M08 已从“无真实宿主证据”推进为“真实 WebView2 Windows 100% 与 Release publish runtime PASS”，但阶段仍为
`BLOCKED_REAL_HOST_125`。证据绑定的 Git HEAD 为 `9800d6045a9f5fdfc62a166242e83529b833dc7d`，采集时工作树为
dirty audit candidate，不能当作当前 `f8f581932469f7c52fe547b7bcabe8ad45d89532` 产品基线的同 SHA 证据。
解除条件是：scope-clean candidate 上完成真实 Windows 125% Debug/Release
WebView2 核心矩阵，并在独立无 Node 目标机、现场设备和生产环境完成各自独立验收。

| Blocker | 当前状态 | 责任方 | 解除条件 |
| --- | --- | --- | --- |
| Windows 125% WebView2 | `BLOCKED_NOT_PERFORMED` | Host/QA | 在真实 125% Windows session 对 scope-clean candidate 运行同一 Debug/Release 旅程 |
| Independent no-Node | `NOT_PERFORMED` | Release/QA | 在未安装 Node 的独立目标机运行发布包与既有 no-Node audit |
| Field devices | `NOT_PERFORMED` | 现场集成 owner | 真实 Camera/PLC/TCP/Station 隔离端口、数据库与设备后执行 |
| Production acceptance | `NOT_GRANTED` | 产品/生产 owner | 完成单独批准的 soak、回滚和验收记录 |
