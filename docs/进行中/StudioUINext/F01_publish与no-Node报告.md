# F01 Release publish 与 no-Node 本机报告

## 1. 结论

```text
RELEASE_SELF_CONTAINED_PUBLISH=PASS
PUBLISH_STATIC_AUDIT=PASS
DESKTOP_NODE_DESCENDANTS=0
SANITIZED_PATH_STARTUP=PASS
EXTERNAL_CDP_DRIVER_USES_NODE=FACT_RECORDED
LOCAL_NO_NODE_EVIDENCE=PASS
CLEAN_MACHINE_WITHOUT_NODE=NOT_PERFORMED
```

## 2. Release publish

`f01m3` 使用 Desktop project、Release、win-x64、self-contained 与显式 output 生成临时 publish，并从该目录运行真实 WebView2 diagnostics、Canvas 和 missing-assets 场景。

必须存在项全部通过：

- `ClearVision.Product.Desktop.exe`；
- `appsettings.json`；
- legacy `wwwroot/index.html`；
- StudioUI `wwwroot/studio/index.html`；
- StudioUI hashed assets；
- StudioUI `.vite/manifest.json`。

禁止项扫描为 0：

- `/v2`；
- browser fixture；
- test source；
- StudioUI source；
- `node_modules`；
- `package-lock.json`；
- npm cache；
- TypeScript/Vue source；
- source map。

正式 JSON：`.tmp/studio-ui-next/f01/matrix/f01m3/studio-ui-no-node-evidence.json`，其中 `publishStaticScan.status=PASS`、`forbiddenArtifactCount=0`。

临时 publish 已按策略删除，`publishDirectoryRetained=false`；报告引用保存下来的审计 JSON，而不是声称临时目录仍存在。

## 3. Desktop 运行期子进程

15 份 Debug、Release 和性能 evidence 都从真实 Desktop PID 枚举 descendants：

```text
desktopChildProcessAudit.status=PASS
checks=15
all nodeDescendantCount=0
```

Desktop 子进程只包含 `msedgewebview2.exe` 等 WebView2 runtime 进程，没有 Node。

## 4. Sanitized PATH

runner 解析 Node 的绝对路径供外部 driver 使用，然后从传给 Desktop 的 PATH 中移除 Node 安装目录。功能、性能与 cleanup evidence 共 16 项全部通过：

```text
sanitizedPathDesktopStartup.status=PASS
checks=16
runnerSucceeded=true
cleanupPassed=true
environmentRestored=true
```

这证明本机 publish/Debug Desktop 启动和运行不依赖 PATH 中的 Node。

## 5. 外部 CDP driver

CDP scenario 明确使用：

```text
role=external-cdp-driver
executablePath=<absolute node.exe>
insideDesktopProcessTree=false
```

15 份 driver 记录全部位于 Desktop 进程树外。它是测试基础设施，不是产品运行期依赖，也不能被写成“目标机未安装 Node”的证据。

## 6. 干净目标机边界

没有在一台从未安装 Node 的独立 Windows 机器上启动 publish：

```text
CLEAN_MACHINE_WITHOUT_NODE=NOT_PERFORMED
```

因此本轮只得出 `LOCAL_NO_NODE_EVIDENCE=PASS`，不把 publish scan、进程树或 sanitized PATH 替代独立目标机验证。

## 7. 清理

所有 scenario 通过：

- Desktop/native window 关闭；
- WebView2 descendants 退出；
- SQLite WAL/SHM 删除；
- user-data、Conversation store、AgentRun store 删除；
- process environment 恢复；
- `runtimeDirectoryRemoved=true`。

正式 evidence 根：`.tmp/studio-ui-next/f01/matrix/f01m3/`。
