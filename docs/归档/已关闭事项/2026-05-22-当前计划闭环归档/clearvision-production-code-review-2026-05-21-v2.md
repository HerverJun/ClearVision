# ClearVision 深度代码审查报告（第二版 · 逐行核实）

> **审查日期**: 2026-05-21
> **审查范围**: ClearVision.Product（530+ C# 文件）、ClearVision.PlcComm、Runtime、Desktop、Station
> **审查视角**: 产线操作员、视觉工程师、产线管理员、运维人员
> **核实方法**: 对第一版报告的 15 个问题逐一读取原始代码行验证；对关键路径进行深度逐行排查
> **核实结论**: 第一版 15 个问题全部属实，零误判；本版补充 6 个新发现的深层缺陷，并修正 1 处分类。

---

## 核实声明

| 第一版问题 | 核实状态 | 关键证据 |
|-----------|---------|---------|
| 1. MessageBox 弹窗阻塞产线 | ✅ 属实 | `Desktop/Program.cs:74-90` |
| 2. WebView2 + Kestrel 工控机风险 | ✅ 属实 | `Desktop/Program.cs:92-261` |
| 3. 缺少一键急停 | ⚠️ 功能建议（非缺陷） | 代码中无此功能，但现有机制未出错 |
| 4. API 错误码滥用 400 | ✅ 属实 | `ApiEndpoints.cs` 9 个端点 `catch (Exception)→BadRequest` |
| 5. 流程双轨制无提示 | ✅ 属实 | `InspectionService.cs:578-609` |
| 6. 算子超时 30s vs PLC 轮询 300s | ✅ 属实 | `FlowExecutionService.cs:612` hardcoded `30000`；PLC 算子 `PollingTimeout=300000` |
| 7. Mono8 强制转 BGR | ✅ 属实 | `ImageAcquisitionOperator.cs:200-203` `ImreadModes.Color` |
| 8. CameraStatusSummary="Unknown" | ✅ 属实 | `StationSyncHostedService.cs:806` 硬编码 |
| 9. 日志分级字符串匹配 | ✅ 属实 | `StationSyncHostedService.cs:710-731` `Contains` 匹配 |
| 10. 图像保存用 `DateTime.Now` | ✅ 属实 | `InspectionService.cs:678,690` |
| 11. 磁盘满无检测 | ✅ 属实 | `InspectionService.cs:693` 直接写入 |
| 12. Dispose 同步等待死锁 | ✅ 属实 | `PlcBaseClient.cs:595`、`HaoPlcClientBase.cs:258`、`Station/Program.cs:57-61` |
| 13. PLC 连接池无逐出 | ✅ 属实 | `PlcCommunicationOperatorBase.cs:23`，无 TTL/上限/清理 |
| 14. Dead Code 污染 | ✅ 属实 | `PlcBaseClient` 无继承者；`McFrameBuilder`/`FinsFrameBuilder`/`IPlcProtocol` 无实例化 |
| 15. OpenTelemetry 仅 ConsoleExporter | ✅ 属实 | `Desktop/Program.cs:141-153` |

---

## 新增缺陷（经逐行代码确认）

### 🔴 新增 1：ImageWrapper 引用计数存在真实竞态，可导致已释放图像被复用

**位置**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/ImageWrapper.cs`

```csharp
public ImageWrapper AddRef()
{
    lock (_lock)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _refCount);   // 在锁内
    }
    return this;
}

public void Release()
{
    int remaining = Interlocked.Decrement(ref _refCount);  // 在锁外！
    if (remaining == 0) { Dispose(); }
    else if (remaining < 0) { /* 抛异常 */ }
}
```

**问题**: `AddRef` 的 `_disposed` 检查和 `Interlocked.Increment` 被 `_lock` 保护，但 `Release` 的 `Interlocked.Decrement` **在锁外**。存在以下竞态窗口：

| 线程 A (Release) | 线程 B (AddRef) |
|---|---|
| `Decrement` 1→0 | |
| | 获取 `_lock`，`_disposed` 仍为 **false** |
| | `Increment` 0→1 |
| `remaining==0` → `Dispose()` 释放 Mat 回 Pool | |
| | 返回一个已 Dispose 的 wrapper，其 Mat 可能已被 Pool 重新分配给其他算子 |

**现场影响**: 高并发并行执行模式下，罕见但致命的图像数据污染。一张产品的图像数据可能被另一张产品的处理结果覆盖，导致 **OK 判为 NG 或 NG 漏判为 OK**。

**建议**: `Release` 的 decrement 也应纳入 `_lock` 保护，或改用更严格的原子状态机（如 `Interlocked.CompareExchange` 循环）。

---

### 🔴 新增 2：多根算子共享输入时触发 "double release detected" 异常

**位置**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`

**根因**: 初始输入 `inputData` 以 `operatorOutputs[Guid.Empty]` 存储，但 `AnalyzeFanOutDegrees` / `ApplyFanOutRefCounts` **从不处理 `Guid.Empty`**。初始 ImageWrapper 的 `RefCount` 始终为 1。若流程中有 **多个无上游连接的根算子**（如两个独立分支都从同一输入图像开始），每个根算子的 `ExecuteWithLifecycleAsync` finally 块都会 `Release()` 一次。

**现场影响**: 流程配置稍微复杂一点（如一个图像同时进入两条独立预处理分支），第二次 `Release()` 时 `_refCount` 变为 -1，抛出 `InvalidOperationException: [ImageWrapper] RefCount became negative (double release detected)`，整个检测流程被标记为 Error。

**建议**: 在 `ExecuteFlowAsync` 开始时，若检测到 `inputData` 包含 `ImageWrapper`，应根据根算子数量预先 `AddRef()`。

---

### 🔴 新增 3：RuntimeHost.StopAsync 传入 CancellationToken 被取消后，状态永久卡在 Stopping

**位置**: `ClearVision.Product/src/ClearVision.Product.Runtime/RuntimeHost.cs:202-288`

```csharp
var completedTask = await Task.WhenAny(
    backgroundRunTask,
    Task.Delay(timeout, cancellationToken));   // ← 如果此 token 被取消
```

**问题**: `Task.Delay(timeout, cancellationToken)` 若因传入的 `cancellationToken` 被取消，会抛出 `OperationCanceledException`。该异常从 `StopAsync` 逃逸，**没有任何 catch 处理**。此时 `_state` 已被设为 `RuntimeHostState.Stopping`（line 232），但 `FinalizeRunAsync()` 从未被调用，`_state` **永远不会被重置**。

**现场影响**: `StationSyncHostedService` 收到远程 `StopRuntime` 命令时，若命令自带 CancellationToken（如 Station 正在关机），`StopAsync` 抛异常 → 状态永久 `Stopping` → 后续 `EnsureNotRunning()` 检查失败 → **RuntimeHost 永久不可用，必须重启进程**。

**建议**: `StopAsync` 内部等待逻辑应使用独立的内部 CTS，或将 `Task.WhenAny` 包裹在 `try/catch (OperationCanceledException)` 中，确保 `FinalizeRunAsync` 或状态回滚始终执行。

---

### 🔴 新增 4：RuntimeImageWriter / RuntimeResultRecordWriter DisposeAsync 可能无限挂起

**位置**: `ClearVision.Product/src/ClearVision.Product.Runtime/RuntimeImageWriter.cs:1177-1194` 及 `RuntimeResultRecordWriter.cs:1015-1032`

```csharp
public async ValueTask DisposeAsync()
{
    _channel.Writer.TryComplete();
    try
    {
        await _consumerTask.ConfigureAwait(false);  // ← 无超时！
    }
    finally
    {
        await _disposeCts.CancelAsync();            // ← 在 consumerTask 完成后才执行
        _disposeCts.Dispose();
    }
}
```

**问题**: Consumer task 内部执行磁盘 I/O（`File.AppendAllTextAsync`、`File.WriteAllBytesAsync`）。这些 API **不接受 `_disposeCts` 的 CancellationToken**。如果磁盘响应极慢（如网络映射驱动断开、USB 存储卡顿），consumer task 将阻塞在 I/O 上。`DisposeAsync` 会无限等待 `_consumerTask`，而 `_disposeCts.CancelAsync()` 永远到达不了。

**现场影响**: Station 端收到 `ReloadPackage` 或执行关机时，`RuntimeHost.DisposeAsync()` 在 `await imageWriter.DisposeAsync()` 处 **死锁**。进程无法退出，Windows 服务管理器只能强制杀进程，导致 Spool 数据丢失。

**建议**: `DisposeAsync` 使用 `Task.WhenAny(_consumerTask, Task.Delay(10_000))`，超时后强制放弃等待，并记录警告日志。

---

### 🟡 新增 5：CameraFrameStreamCoordinator 的 "IsRunning=true 但未实际运行" 损坏状态

**位置**: `ClearVision.Product/src/ClearVision.Product.Infrastructure/Cameras/CameraFrameStreamCoordinator.cs:511-608`

```csharp
entry.IsRunning = true;                        // line 524
// ... 其他字段初始化 ...
try
{
    await eventCamera.StartContinuousAcquisitionAsync(...);  // line 578
}
catch (Exception ex)
{
    // 日志记录
    throw;
}
```

**问题**: `entry.IsRunning = true` 设置在 `StartContinuousAcquisitionAsync` **之前**。如果后者抛出异常（如相机刚被拔掉），异常向上传播，`IsRunning` 却仍是 `true`。下一次 `EnsureProducerAsync` 看到 `entry.IsRunning == true`，会认为生产者已就绪，**直接返回而不重启**，导致该相机永久无帧输出。

**现场影响**: 相机热插拔或短暂断线后，检测系统报告"相机正常"但实际收不到任何图像，产线持续报 "ImageAcquisitionOperator 超时"。

**建议**: 将 `entry.IsRunning = true` 移至 `StartContinuousAcquisitionAsync` 成功返回之后。

---

### 🟡 新增 6：空 catch 块真正吞掉异常且未记录日志

**位置**: `ClearVision.Product/src/ClearVision.Product.Application/Services/OperatorService.cs:468-471`

```csharp
catch (Exception)
{
    // 记录日志或忽略无效参数
}
```

**核实结果**: 注释说"记录日志"，但 catch 块内 **没有任何日志代码**。参数更新异常被完全静默吞掉。

**位置**: `ClearVision.Product/src/ClearVision.Product.Application/Services/ProjectService.cs:81-84`

```csharp
catch
{
    // 忽略反序列化错误，回退到 DB 数据
}
```

**核实结果**: 同样无任何日志。如果文件存储的流程 JSON 损坏，系统静默回退到 DB 数据，工程师完全不知道文件已损坏。

**建议**: 所有空 catch 块至少写入 `_logger.LogWarning(...)`，保留异常信息以便排查。

---

## 修正说明

### 问题 3 重新分类：缺少急停机制

第一版将其列为 P0 缺陷。经逐代码核实，现有代码中 **没有任何急停相关逻辑**，因此这不是"代码出错"，而是**功能缺失**。对于产线现场而言，这是严重的用户体验缺口，但在代码层面它属于"未实现"而非"实现错误"。

**修正后分类**: P2 功能建议。

---

## 完整优先级矩阵（核实后）

| 优先级 | 问题 | 类型 | 影响人群 | 代码依据 |
|--------|------|------|---------|---------|
| **P0** | 未处理异常弹 MessageBox 阻塞产线 | 缺陷 | 操作员 | `Desktop/Program.cs:74-90` |
| **P0** | ImageWrapper 引用计数竞态（ disposed 后复活） | 缺陷 | 全体 | `ImageWrapper.cs` AddRef/Release 不对称 |
| **P0** | 多根算子共享输入触发 double release | 缺陷 | 工程师 | `FlowExecutionService.cs` 跳过 Guid.Empty |
| **P0** | RuntimeHost.StopAsync 永久卡 Stopping | 缺陷 | 运维 | `RuntimeHost.cs:202-288` |
| **P0** | Writer DisposeAsync 无限挂起 | 缺陷 | 运维 | `RuntimeImageWriter.cs:1177-1194` |
| **P0** | Dispose 同步等待死锁 | 缺陷 | 全体 | `PlcBaseClient.cs:595`、`Station/Program.cs:57-61` |
| **P1** | API 错误码滥用 400 | 缺陷 | 工程师 | `ApiEndpoints.cs` 9 处 catch-all |
| **P1** | 算子超时 30s vs PLC 轮询 300s | 缺陷 | 工程师 | `FlowExecutionService.cs:612` vs PLC 算子参数 |
| **P1** | 相机流协调器损坏状态（IsRunning 提前设置） | 缺陷 | 操作员 | `CameraFrameStreamCoordinator.cs:524` |
| **P1** | PLC 连接池无逐出 | 缺陷 | 运维 | `PlcCommunicationOperatorBase.cs:23` |
| **P1** | 磁盘满无检测 | 缺陷 | 操作员/运维 | `InspectionService.cs:693` |
| **P1** | 空 catch 块未记录日志 | 缺陷 | 工程师 | `OperatorService.cs:468`、`ProjectService.cs:81` |
| **P1** | CameraStatusSummary="Unknown" | 缺陷 | 管理员 | `StationSyncHostedService.cs:806` |
| **P1** | 日志分级字符串匹配 | 缺陷 | 运维 | `StationSyncHostedService.cs:710-731` |
| **P2** | 图像保存路径用 `DateTime.Now` | 缺陷 | 管理员 | `InspectionService.cs:678` |
| **P2** | Mono8 强制转 BGR | 缺陷 | 工程师 | `ImageAcquisitionOperator.cs:200-203` |
| **P2** | 流程双轨制无提示 | 缺陷 | 工程师 | `InspectionService.cs:578-609` |
| **P2** | Dead Code 污染 | 技术债 | 工程师 | `PlcBaseClient` 等无引用 |
| **P2** | OpenTelemetry 仅 ConsoleExporter | 缺陷 | 运维 | `Desktop/Program.cs:141-153` |
| **P2** | WebView2 + Kestrel 工控机依赖风险 | 架构债 | 运维 | `Desktop/Program.cs` 整体启动流程 |
| **P2** | 缺少一键急停 / 物理安全快捷键 | 功能建议 | 操作员 | 无对应代码 |

---

## 架构亮点（继续保持）

以下设计在现场视觉软件中属于较高水平，应继续坚持：

1. **ImageWrapper 引用计数 + Copy-on-Write**（概念正确，只需修复上述竞态窗口）
2. **Channel-based 异步 I/O 解耦**
3. **状态机 + Semaphore 序列化**
4. **相机流协调器租赁模型**
5. **Station 离线 Spool + 重连**
6. **算子并行执行层**
7. **FlowExecutionService 的 finally 块 reliably 释放 ImageWrapper**（`ReleaseRemainingImageWrappers` 设计正确，有兜底）
