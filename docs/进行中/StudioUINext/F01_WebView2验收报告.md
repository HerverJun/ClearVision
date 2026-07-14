# F01 真实 WebView2 验收报告

## 1. 结论

```text
REAL_WEBVIEW2=PASS
DEBUG_WEBVIEW2=PASS
RELEASE_PUBLISH_WEBVIEW2=PASS
MISSING_ASSETS_FAIL_CLOSED=PASS
AUTHENTICATED_READ_ONLY_API=PASS
PROCESS_AND_RUNTIME_CLEANUP=PASS
```

正式验收只引用 `.tmp/studio-ui-next/f01/matrix/f01m3/`。`f01m1`、`f01m2` 是 runner 调试样本，不计入结论。

## 2. Runner 事实与唯一调用链

F01 没有创建第三套 runner。现有 AI WebView2 release smoke 被参数化为公共底层，StudioUI 只增加 scenario orchestration：

```text
scripts/studio-ui-next/Invoke-StudioUiWebView2Matrix.ps1
  -> scripts/studio-ui-next/Invoke-StudioUiWebView2Evidence.ps1
     -> scripts/run-ai-webview2-release-smoke.ps1
        -> ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/studio-ui-webview2-smoke.cjs
           -> CDP 连接真实 msedgewebview2.exe
```

辅助层：

- `Get-DesktopRuntimeProbe.ps1`：native window、DPI awareness、进程树；
- `Invoke-StudioUiCanvasPerformanceEvidence.ps1`：Legacy/Studio 同条件 A/B；
- `Test-StudioUiDpiEvidence.ps1`：DPI 分层判定；
- `Test-StudioUiNoNodeEvidence.ps1`：publish、进程树、sanitized PATH 与外部 Node driver 分层。

公共 runner 统一拥有：Desktop 启动、HTTP/CDP 等待、认证 setup/login、窗口 resize/close、超时、日志、WebView2 user-data、数据库、Conversation/AgentRun store、环境恢复与有界清理重试。

## 3. 隔离与认证

每个 scenario 使用独立：

- HTTP port 与 CDP port；
- SQLite `Database__Path`；
- `CV_WEBVIEW2_USER_DATA_FOLDER`；
- `CV_CONVERSATION_STORE_ROOT`；
- `CV_AGENT_RUN_EVENT_STORE`；
- Desktop log、host stdout/stderr；
- runtime/evidence/publish 子目录。

认证先在隔离数据库执行 setup/login，再通过同源临时页面挂接错误监听与 sessionStorage token，最后导航目标页。探针成功访问：

| Endpoint | 结果 |
| --- | --- |
| `/health` | 200，public |
| `/api/auth/setup-status` | 200 |
| `/api/auth/me` | 200，authenticated |
| `/api/operators/library` | 200，157 项 |
| `/api/projects` | 200 |

Lab 未调用业务写 endpoint；正式执行仍走既有 authenticated HTTP/SSE authority。

## 4. 最终场景矩阵

| 场景 | Configuration/runtime | Route/expectation | 结果 |
| --- | --- | --- | --- |
| debug-legacy | Debug | legacy flag off | PASS |
| debug-diagnostics | Debug | StudioUI `/diagnostics` | PASS |
| debug-design | Debug | `/labs/design` | PASS |
| debug-canvas-dpi-1 | Debug | Canvas，scale 1 | PASS |
| debug-canvas-dpi-1-25 | Debug | Canvas，scale 1.25 | PASS |
| debug-canvas-dpi-1-5 | Debug | Canvas，scale 1.5 | PASS |
| debug-canvas-dpi-2 | Debug | Canvas，scale 2 | PASS |
| publish-diagnostics | Release self-contained | publish diagnostics | PASS |
| publish-canvas | Release self-contained | publish Canvas | PASS |
| publish-missing-assets | Release mutated copy | Host diagnostic；不回退 legacy | PASS |

矩阵总计 10/10 PASS，顶层文件：

` .tmp/studio-ui-next/f01/matrix/f01m3/studio-ui-webview2-matrix.json `

## 5. StudioUI runtime 断言

StudioUI 场景实际报告：

```text
startup.exists=true
startup.schemaVersion=1
startup.uiKind=studio-ui
startup.hostKind=desktop-webview2
startup.keys=精确六字段
startup.frozen=true
startup.featureFlagsFrozen=true
startup.descriptor.writable=false
startup.descriptor.configurable=false
diagnostics.ready=true
diagnostics.mountCount=1
diagnostics.activeRoot=studio-ui
diagnostics.unhandledErrorCount=0
legacyNavigationCount=0
legacyMainCount=0
```

Canvas 场景额外断言 `canvasOwnerCount=1`；dispose 后变为 0。所有正式场景 `consoleErrors=[]`、`pageErrors=[]`、meaningful request failures 为空。

## 6. Legacy 与 missing-assets

Legacy flag-off 使用独立 Desktop 进程并保持 `/index.html` 根，不挂载 StudioUI。

missing-assets 只在 D: 临时 publish 副本移除 StudioUI `index.html`、`assets` 与 manifest。Host 显示诊断文本并列出缺失路径；实际断言：

```text
studioPageCount=0
legacyNavigationCount=0
legacyMainCount=0
startupType=undefined
studioReadyType=undefined
```

因此不存在 silent fallback 或 CSS 隐藏的双 root。

## 7. 关闭、清理与 no-Node 关系

10 个功能场景与 6 个性能场景都通过 native close/force fallback 和 20×250ms 有界删除重试。正式矩阵记录：

```text
runtimeDirectoryRemoved=true
publishDirectoryRetained=false
processCleanup=PASS
runtimeCleanup=PASS
environmentRestored=PASS
```

15 份进程树 evidence 的 Desktop descendants 中 Node 均为 0。Node 只作为绝对路径外部 CDP driver 存在，且 `insideDesktopProcessTree=false`；这一事实不冒充干净 no-Node 目标机。

## 8. 证据索引

- 顶层矩阵：`.tmp/studio-ui-next/f01/matrix/f01m3/studio-ui-webview2-matrix.json`
- 场景 JSON/cleanup/host logs：`.tmp/studio-ui-next/f01/matrix/f01m3/runs/`
- DPI：`.tmp/studio-ui-next/f01/matrix/f01m3/studio-ui-dpi-evidence.json`
- no-Node：`.tmp/studio-ui-next/f01/matrix/f01m3/studio-ui-no-node-evidence.json`
- 性能：`.tmp/studio-ui-next/f01/matrix/f01m3/performance/`

真实 Windows 系统缩放、跨显示器移动和干净未安装 Node 的独立目标机没有由本报告替代，分别保持 `NOT_PERFORMED`。
