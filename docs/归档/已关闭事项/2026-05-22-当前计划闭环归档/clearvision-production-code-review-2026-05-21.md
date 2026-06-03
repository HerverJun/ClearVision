# ClearVision 深度代码审查报告

> **审查日期**: 2026-05-21
> **审查范围**: ClearVision.Product（530+ C# 文件）、ClearVision.PlcComm、Runtime、Desktop、Station 等核心模块
> **审查视角**: 产线操作员、视觉工程师、产线管理员、运维人员
> **审查人**: Kimi Code CLI (AI Agent)

---

## 一、产线操作员视角：人机交互与现场安全

### 🔴 问题 1：未处理异常直接弹 MessageBox，会打断产线作业

**位置**: `Desktop/Program.cs` 第 74–90 行

```csharp
Application.ThreadException += (s, e) =>
{
    MessageBox.Show($"UI线程异常:\n{e.Exception.Message}...", "错误", ...);
};
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    MessageBox.Show($"未处理异常:\n{ex?.Message}...", "严重错误", ...);
};
```

**现场影响**:
- 产线 7×24 运行，夜间无人值守时，弹窗会**阻塞整个进程**，后续检测全部停止。
- 操作员可能不懂技术，看到异常堆栈只会恐慌性重启软件，丢失当前批次数据。

**建议**:
- 区分 **"用户可见错误"** 和 **"系统致命错误"**。
- 无人值守模式下：弹窗改为写入错误日志 + 通知推送（SignalR/Station Hub），不要让 UI 阻塞。
- 增加 `AppConfig.QuietFailureMode` 配置，现场部署时默认开启静默失败。

---

### 🔴 问题 2：WebView2 + Kestrel 架构对工控机环境过于"奢侈"

**位置**: `Desktop/Program.cs` 整个启动流程

**现场影响**:
- WebView2 依赖 Edge Runtime，工厂工控机往往镜像精简、无法联网更新，首次部署或升级时容易因 WebView2 版本不匹配导致白屏。
- Kestrel 端口扫描（5000–5010）在某些工厂网络策略下会被安全软件拦截。
- `localhost` 绑定在存在多个网卡或 VPN 的工控机上可能出现解析异常。

**建议**:
- 提供 **"离线诊断模式"**：当 WebView2 初始化失败时，退化为纯 WinForms 界面，至少保证产线能启停检测。
- 端口冲突时，当前逻辑是 `catch (Exception) { MessageBox.Show(...) }`，建议改为自动回退到本地命名管道或随机端口，并记录到状态栏而非弹窗。
- 考虑将前端资源预编译为单文件 HTML（或内嵌资源），减少对文件系统的依赖。

---

### 🟡 问题 3：缺少"一键急停 / 屏蔽"安全机制

**现场场景**: 操作员发现相机图像异常或设备报警，需要立即暂停当前工位但不影响其他工位。

**现状**: 停止实时检测需要调用 `/api/inspection/realtime/stop`，如果前端卡死或后端正在执行长耗时算子，操作员无法快速干预。

**建议**:
- 在 `MainForm` 的 `WndProc` 中（已有光电触发消息处理），增加物理急停按钮或键盘快捷键（如 `Ctrl+Shift+Pause`）的钩子。
- 急停信号应直接作用于 `InspectionRuntimeCoordinator`，走最高优先级取消令牌，而非等待 HTTP 往返。

---

## 二、视觉工程师视角：调试效率与流程可靠性

### 🔴 问题 4：错误处理"大锅烩"，前端无法精准定位问题

**位置**: `ApiEndpoints.cs` 中大量端点

```csharp
catch (Exception ex)
{
    return Results.BadRequest(new { Error = ex.Message });
}
```

**现场影响**:
- 400 Bad Request 被滥用：数据库连接断开、磁盘满、相机掉线、算子超时全都返回 400，前端只能显示 "Bad Request"。
- 工程师在产线调试时，无法从状态码区分是 **参数配错了** 还是 **硬件断了**。

**建议**:
- 统一异常中间件，按异常类型返回正确的 HTTP 状态码：
  - `IOException` / `DiskFullException` → 503 Service Unavailable
  - `CameraDisconnectedException` → 504 Gateway Timeout（或自定义 590 Camera Error）
  - `TimeoutException` → 408 Request Timeout
  - 参数校验失败 → 422 Unprocessable Entity
- 异常响应体增加 `ErrorCode` 枚举，前端据此显示不同的用户提示和排查指引。

---

### 🔴 问题 5：流程存储"双轨制"导致困惑

**位置**: `InspectionService.cs` 第 578–609 行

```csharp
var fileFlow = await LoadFlowFromStorageAsync(projectId);
if (HasExecutableFlow(project.Flow) && !HasExecutableFlow(fileFlow))
{
    return project.Flow;
}
if (HasExecutableFlow(fileFlow))
{
    _logger.LogWarning("...已回退到 ProjectFlows 文件流程...");
    return fileFlow!;
}
```

**现场影响**:
- 工程师保存流程后，由于某种原因数据库写入失败（或 EF 状态跟踪问题），系统自动 fallback 到文件系统中的旧流程。
- 工程师明明修改了参数，运行结果却还是旧的，排查数小时后才发现用的是文件缓存。

**建议**:
- Fallback 机制本身是好的，但应该 **显式通知用户**：
  ```csharp
  // 建议增加
  result.UsedFallbackSource = "FileStorage";
  result.Warning = "数据库流程为空，已自动回退到本地文件流程（最后修改：2026-05-01）";
  ```
- 在 Desktop UI 中增加流程来源标识，让工程师一眼看到当前运行的是"数据库流程"还是"文件流程"。

---

### 🟡 问题 6：算子超时 30s 与 PLC 轮询 300s 的配置冲突

**位置**: `FlowExecutionService.cs` 硬编码 `DefaultOperatorTimeoutMs = 30000`

**现场影响**:
- PLC `WaitForValue` 轮询模式可以配置为等待 300 秒，但外层的 `FlowExecutionService` 会在 30 秒时取消整个算子。
- 工程师配置了一个"等待启动信号"的 PLC 读算子，结果每次 30 秒就报超时失败，完全无法用于产线节拍不固定的场景。

**建议**:
- 超时不应全局硬编码。应在执行前检查算子的 `ExpectedMaxDuration` 属性，取 `max(30000, operator.ExpectedMaxDuration + 5000)`。
- 对于 PLC 通信算子，至少应支持配置为"不计入全局超时"或走独立的 `LongRunning` 执行路径。

---

### 🟡 问题 7：Mono8 相机被强制转 BGR，浪费内存且可能改变算法行为

**位置**: `ImageAcquisitionOperator` 使用 `ImreadModes.Color`

**现场影响**:
- 工业检测大量使用黑白相机（Mono8/Mono12），强制转 3 通道 BGR 会使内存占用变为 3 倍（500 万像素从 5MB 变成 15MB）。
- 部分传统视觉算法（Blob 分析、边缘检测）在灰度图上参数经验值与彩色图不同，强制转色后阈值失效。

**建议**:
- 根据相机配置的 `PixelFormat` 决定解码模式：
  - `Mono8/Mono10/Mono12` → `ImreadModes.Grayscale`
  - `RGB/Bayer` → `ImreadModes.Color`
- 或者在算子参数中增加"强制灰度"开关，由工程师显式控制。

---

## 三、产线管理员视角：运维监控与可观测性

### 🔴 问题 8：健康上报中 CameraStatusSummary 永远是 "Unknown"

**位置**: `StationSyncHostedService.cs` 第 806 行

```csharp
CameraStatusSummary = "Unknown",
```

**现场影响**:
- 管理员在 Studio 端查看数十个工站的健康状态时，所有相机的状态都是 Unknown。
- 无法远程判断是相机掉线、曝光异常，还是正常工作中。

**建议**:
- `StationSyncHostedService` 应定期向 `CameraFrameStreamCoordinator` 查询各绑定的 `IsProducerCameraAcquiring` 状态。
- 按相机聚合状态：`Healthy` / `Disconnected` / `FrameRateLow` / `ConfigurationMismatch`。

---

### 🟡 问题 9：日志分级靠字符串匹配，不可靠

**位置**: `StationSyncHostedService.cs` 第 710–731 行

```csharp
private static string DetectLogLevel(string message)
{
    if (message.Contains("fatal", ...)) return "FATAL";
    if (message.Contains("error", ...) || message.Contains("异常", ...)) return "ERROR";
    if (message.Contains("warn", ...)) return "WARN";
    return "INFO";
}
```

**现场影响**:
- 当日志消息为 `"Retry operation after error count exceeded"` 时，字符串匹配将其标记为 ERROR，实则是正常的重试日志。
- 中文关键词 `"异常"` 在英文日志环境下失效。

**建议**:
- 日志分级应在写入时就确定级别，通过结构化日志（`ILogger.LogError`、`LogWarning`）直接传递级别，而非事后解析字符串。
- `RuntimeHost` 的 `LogMessage` 事件应改为 `(string message, LogLevel level)` 签名。

---

### 🟡 问题 10：图像保存路径使用本地时间，跨时区产线混乱

**位置**: `InspectionService.cs` 第 678–686 行

```csharp
var dateFolder = DateTime.Now.ToString("yyyyMMdd");  // 本地时间
var statusFolder = result.Status switch { ... };
var targetDir = Path.Combine(rootPath, dateFolder, statusFolder);
```

**现场影响**:
- 跨国企业在中国和德国的产线同时运行，同一批次产品的时间文件夹对不上。
- 夏令时切换时，`20260521` 文件夹可能会出现 23 小时或 25 小时的奇怪边界。

**建议**:
- 统一使用 `DateTime.UtcNow.ToString("yyyyMMdd")` 作为文件夹名。
- 在 UI 显示层再做本地时区转换。

---

### 🟡 问题 11：保存图像时无磁盘满检测

**位置**: `InspectionService.cs` 第 693 行

```csharp
await File.WriteAllBytesAsync(targetPath, result.OutputImage, cancellationToken);
```

**现场影响**:
- 产线连续运行数周，C 盘被检 images 占满后，`File.WriteAllBytesAsync` 抛出 `IOException`，整个检测被标记为 Error。
- 操作员看到的是"检测失败"，实则是磁盘空间问题，排查方向完全错误。

**建议**:
- 写入前检查目标分区剩余空间：`new DriveInfo(root).AvailableFreeSpace`。
- 预留安全水位（如 500MB），低于水位时：
  - 停止保存图像但保持检测运行；
  - 触发 `StationSyncHostedService` 上报 `DiskFullWarning`；
  - UI 状态栏显示红色警告"磁盘空间不足，图像停止保存"。

---

## 四、系统稳定性视角：长期运行的隐患

### 🔴 问题 12：`Dispose()` 中大量使用同步等待，存在死锁风险

**位置**:
- `PlcBaseClient.Dispose()`: `DisconnectAsync().GetAwaiter().GetResult()`
- `HaoPlcClientBase.Dispose()`: 同上
- `Program.cs` `StopWebServer()`: 虽为 async，但其他多处有 sync-over-async

**现场影响**:
- WinForms 有 `SynchronizationContext`，在 UI 线程调用 `Dispose()` 时，`GetAwaiter().GetResult()` 极易死锁。
- PLC 连接池更换旧客户端时，同步 `Dispose` 可能阻塞整个算子执行线程 10 秒以上。

**建议**:
- 所有 `Dispose()` 改为 `IAsyncDisposable.DisposeAsync()`，并在调用链中彻底使用 async。
- 若必须同步释放，使用 `Task.Run(() => DisposeAsync()).GetAwaiter().GetResult()` 在后台线程完成，避免阻塞 UI。

---

### 🔴 问题 13：PLC 连接池无逐出策略，长期运行泄漏

**位置**: `PlcCommunicationOperatorBase.cs` 静态连接池

**现场影响**:
- 产线调试阶段可能频繁更换 PLC IP 地址测试，旧连接永远留在池中。
- 每个连接持有 TCP Socket 和 HSL 客户端资源，数月后可能耗尽句柄。

**建议**:
- 增加连接池 TTL（如 30 分钟无使用自动释放）。
- 增加池大小上限（如 16 个连接），超出时 LRU 逐出。
- 心跳检测发现离线超过 5 分钟的连接，主动 Dispose 并从池移除。

---

### 🟡 问题 14：Dead Code 污染严重，增加维护成本

**位置**: `ClearVision.PlcComm/Core/PlcBaseClient.cs` 及 `FrameBuilder` 系列

**现场影响**:
- 新入职的视觉工程师看到两套 PLC 协议栈（自定义 TCP + HSL 包装），无法判断该改哪边。
- `PlcBaseClient` 的地址解析、重连逻辑与 `HaoPlcClientBase` 差异很大，容易改错。

**建议**:
- 彻底移除 `PlcBaseClient`、`IPlcProtocol`、`S7FrameBuilder`、`McFrameBuilder`、`FinsFrameBuilder` 等死代码。
- 如果保留作"未来扩展"，应放到单独的 `archive/` 目录或标记 `[Obsolete("Dead code, do not use")]`。

---

### 🟡 问题 15：Station 端 OpenTelemetry 只有 ConsoleExporter

**位置**: `Desktop/Program.cs` 第 141–153 行

```csharp
.WithMetrics(metrics => metrics
    .AddMeter(InspectionMetrics.MeterName)
    .AddConsoleExporter());
```

**现场影响**:
- 控制台输出在 WinForms 应用中几乎不可见（没有控制台窗口）。
- 产线需要对接 Prometheus / Grafana 做监控，ConsoleExporter 无法提供。

**建议**:
- 增加 `PrometheusHttpListener` 或 OTLP exporter，让运维能拉取 metrics。
- 至少暴露关键指标：检测吞吐量（件/分钟）、OK/NG/Error 比例、相机帧率、算子平均耗时。

---

## 五、总结：优先级建议矩阵

| 优先级 | 问题 | 影响人群 | 预估工作量 |
|--------|------|---------|-----------|
| P0 | 未处理异常弹窗阻塞产线 | 操作员 | 小 |
| P0 | 同步 Dispose 死锁 | 全体 | 中 |
| P0 | 错误响应码滥用 | 工程师 | 中 |
| P1 | 相机状态永远 Unknown | 管理员 | 小 |
| P1 | PLC 连接池泄漏 | 运维 | 中 |
| P1 | 磁盘满无保护 | 操作员/运维 | 小 |
| P1 | 算子超时与 PLC 轮询冲突 | 工程师 | 中 |
| P2 | WebView2 依赖风险 | 运维 | 大 |
| P2 | Mono8 强制转色 | 工程师 | 小 |
| P2 | 流程双轨制无提示 | 工程师 | 小 |
| P2 | 死代码清理 | 工程师 | 中 |
| P2 | OpenTelemetry 无实用导出 | 运维 | 小 |

---

## 附录：架构亮点（值得保持）

在指出问题的同时，以下设计在现场视觉软件中属于较高水平，应继续坚持：

1. **ImageWrapper 引用计数 + Copy-on-Write**: 有效减少高分辨率图像在算子 pipeline 中的内存拷贝。
2. **Channel-based 异步 I/O 解耦**: `RuntimeResultRecordWriter` 和 `RuntimeImageWriter` 使用 BoundedChannel，避免磁盘 I/O 阻塞检测节拍。
3. **状态机 + Semaphore 序列化**: `RuntimeHost` 的 `_stateGate` 和 `InspectionRuntimeCoordinator` 的 `_stateLock` 防止了并发状态竞态。
4. **相机流协调器租赁模型**: `CameraFrameStreamCoordinator` 的 Producer/Lease 设计允许多个消费者（检测 + 预览）安全共享同一相机。
5. **Station 离线 Spool + 重连**: `StationSyncHostedService` 的本地队列和断网续传机制，在工厂网络不稳定场景下非常实用。
6. **算子并行执行层**: `FlowExecutionService` 的拓扑排序 + 执行层并行，在纯图像处理流程中能显著提升吞吐量。
