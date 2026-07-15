# F02 Initial 性能与生命周期基线

## 1. 结论

```text
F02_PERFORMANCE_BASELINE_CAPTURED=PASS
F02_PERFORMANCE_METHOD_REPRODUCIBLE=PASS
F02_INITIAL_SHA=f6d4d98a53914bac088cd62cda261b2c08a11670
```

捕获发生在任何 F02 产品代码修改前。证据写入
`.tmp/studio-ui-next/f02/initial/webview2/evidence/`，未覆盖 F01 evidence。

## 2. 实验环境

| 项 | 值 |
| --- | --- |
| 数据来源 | `REAL_WEBVIEW2_EMPTY_AUTHORITY` |
| 认证来源 | `HARNESS_SEEDED_SESSION` |
| Configuration | Debug |
| Runtime kind | debug Desktop executable |
| Desktop executable SHA256 | `fc2ef8ea3656f053ad4bdb5a13f6d6283eb25162c60f93631f6bae3eb10613ee` |
| WebView2 | `Edg/150.0.4078.65` |
| 窗口参数 | 1366×768，inner viewport 1350×704 |
| DPR | 1；真实进程 DPI 由既有 runner 单独取证，不与 DPR 混用 |
| CPU | Intel Core i7-12700F，20 logical CPU |
| 内存 | 68,520,009,728 bytes |
| fixture | schemaVersion 1，隔离空 SQLite |

每条页面路由先完整 warmup 一次，再记录 5 次完整导航；首次可交互区间从 navigation start 到目标
ready selector/probe 完成，再等待两个 animation frame。路由切换记录 20 个样本，p95 使用排序后
第 19 个样本。资源基线在 20 次 Diagnostics/Design Lab 往返前后调用 CDP GC、
`Performance.getMetrics`、`Runtime.getHeapUsage` 与页面 instrumentation。

## 3. 页面与路由结果

| 指标 | 结果 |
| --- | --- |
| Diagnostics warmup | 120.95 ms |
| Diagnostics 5 样本 | 34.21 / 41.86 / 38.54 / 41.59 / 48.16 ms |
| Diagnostics 中位 | 41.59 ms |
| Design Lab warmup | 293.01 ms |
| Design Lab 5 样本 | 33.92 / 27.78 / 37.46 / 36.37 / 40.70 ms |
| Design Lab 中位 | 36.37 ms |
| 20 次空 Shell RouterView 往返 p95 | 42.39 ms |
| runtime console/page error | 0 |
| runner/process/runtime/environment cleanup | PASS |

“空 Shell RouterView 往返”指 F01 `App.vue` 仅含 `RouterView` 时，在 Diagnostics 与 Design Lab
之间切换；它不是 F02 产品页面性能结论。

## 4. 生命周期观测

| 指标 | before | after | delta |
| --- | ---: | ---: | ---: |
| active timeout | 0 | 0 | 0 |
| active interval | 0 | 0 | 0 |
| 当前 DOM element | 42 | 42 | 0 |
| instrumented listener registration | 146 | 346 | +200 |
| CDP JSEventListeners | 152 | 342 | +190 |
| CDP Nodes | 3238 | 8488 | +5250 |
| JSHeapUsedSize | 4,764,608 | 7,052,976 | +2,288,368 bytes |
| constructed minus aborted AbortController | 7 | 17 | +10 |

这些值是 Initial 观测值，不自动等同于泄漏判定：listener instrumentation 不能观察 `{ once: true }`
的浏览器自动移除；正常完成但无需主动 `abort()` 的 controller 也会进入 constructed-minus-aborted。
F02 Final 必须使用同一机器、Configuration、WebView2、窗口、DPR、fixture schema、warmup、样本数和
测量区间复测，并以 owner/dispose 单测与可解释的稳定资源计数共同判断。

## 5. 可复现命令

```powershell
& "./scripts/studio-ui-next/Invoke-StudioUiWebView2Evidence.ps1" `
  -Expectation studio-diagnostics `
  -Configuration Debug `
  -RuntimeKind debug `
  -NodeScenarioPath ".tmp/studio-ui-next/f02/initial/f02-initial-performance.cjs" `
  -RunName "f02-initial-perf" `
  -Route "/diagnostics" `
  -EvidenceDirectory ".tmp/studio-ui-next/f02/initial/webview2/evidence" `
  -WebPort 5182 -CdpPort 9482 -Scale 1 `
  -WindowWidth 1366 -WindowHeight 768 -NoBuild
```

主证据 SHA256：
`E7F251517889A84408ABBC1D36D0E7C52CD95D37769C4A51075A865300179746`。
