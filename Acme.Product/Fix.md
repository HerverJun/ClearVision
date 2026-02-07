# Acme.Product 项目问题修复清单

> **生成日期**: 2026年2月5日  
> **状态**: 正在进行中  
> **维护者**: Agent (Refined)

---

## 🔴 P0 - 高危问题（立即修复）

### 1. 线程安全 - FlowExecutionService 字典并发崩溃
**文件**: `src/Acme.Product.Infrastructure/Services/FlowExecutionService.cs`  
**描述**: `FlowExecutionService` 被注册为 **Singleton** (见 `DependencyInjection.cs:52`)，但其成员 `_executionStatuses` 使用了非线程安全的 `Dictionary`。当多个请求并发触发流程执行时，对该字典的读写会导致 `InvalidOperationException` 或数据损坏。

**位置**:
```csharp
// Line 16
private readonly Dictionary<Guid, FlowExecutionStatus> _executionStatuses = new();
```

**修复方案**:
将 `_executionStatuses` 更改为 `ConcurrentDictionary`。

```csharp
// [MODIFY] src/Acme.Product.Infrastructure/Services/FlowExecutionService.cs

// 1. 引用命名空间
using System.Collections.Concurrent;

// 2. 修改字段定义 (Line 16)
private readonly ConcurrentDictionary<Guid, FlowExecutionStatus> _executionStatuses = new();

// 3. 确保所有访问点都兼容 ConcurrentDictionary (ConcurrentDictionary 也实现了 IDictionary, 大部分代码无需修改，但要注意 TryAdd/TryUpdate 的使用)
```

**验证**:
- 编写并发测试：启动 10 个 Task 同时调用 `ExecuteFlowAsync`，确保没有抛出集合修改异常。

---

### 2. 业务逻辑 - OperatorFlow 循环检测算法反转
**文件**: `src/Acme.Product.Core/Entities/OperatorFlow.cs`  
**描述**: `HasCycle` 方法中的递归检测逻辑有误。`HashSet.Add` 在元素已存在时返回 `false`。当前的逻辑是 `if (!visited.Add(current)) return false;`，这意味着如果节点**已经被访问过**（即发现了环/重复路径），它反而返回 `false`（表示无环），导致循环依赖检测失效，最终导致堆栈溢出。

**位置**:
```csharp
// Line 178
if (!visited.Add(current))
    return false;
```

**修复方案**:
如果 `visited.Add(current)` 返回 `false`，说明当前路径中已经包含该节点，应返回 `true` (存在环)。

```csharp
// [MODIFY] src/Acme.Product.Core/Entities/OperatorFlow.cs : Line 178

// 修复前:
if (!visited.Add(current))
    return false;

// 修复后:
if (!visited.Add(current))
    return true; // 已访问过，说明有环
```

**验证**:
- 单元测试：创建一个 A -> B -> A 的连接，断言 `ValidateConnection` 抛出异常。

---

### 3. 资源管理 - LruImageCacheRepository 大对象逻辑缺陷
**文件**: `src/Acme.Product.Infrastructure/Repositories/LruImageCacheRepository.cs`  
**描述**: `AddAsync` 方法中，如果传入的单张图片大小 (`size`) 超过了 `_maxSizeInBytes`，`while` 循环虽然会清空整个 `_accessOrder`，但循环结束后代码仍会强行将该超大图片加入缓存。这破坏了缓存的最大容量限制。

**位置**:
```csharp
// Line 36-39
while (_currentSizeInBytes + size > _maxSizeInBytes && _accessOrder.Count > 0)
{
    EvictLeastRecentlyUsed();
}
// Line 51: 即使 size > max，依然被添加
_cache[id] = entry; 
```

**修复方案**:
在添加前检查单体大小限制。

```csharp
// [MODIFY] src/Acme.Product.Infrastructure/Repositories/LruImageCacheRepository.cs : Line 31 后添加

// 在 var size = imageData.Length; 之后添加:
if (size > _maxSizeInBytes)
{
    throw new ArgumentException($"图像大小 {size} 超过最大缓存限制 {_maxSizeInBytes}");
}
```

**验证**:
- 单元测试：初始化 10MB 缓存，尝试添加 11MB 图片，断言抛出异常且缓存为空。

---

### 4. 异常处理 - WebMessageHandler 空引用崩溃
**文件**: `src/Acme.Product.Desktop/Handlers/WebMessageHandler.cs`  
**描述**: `Initialize` 方法没有检查 `webViewControl.CoreWebView2` 是否为 null。虽然通常在 `EnsureCoreWebView2Async` 后调用，但如果初始化失败或时序错误，这里会抛出空引用异常导致程序直接崩溃。

**位置**:
```csharp
// Line 41
_webView = webViewControl.CoreWebView2;
_webView.WebMessageReceived += OnWebMessageReceived; // 如果 CoreWebView2 为 null，这里崩溃
```

**修复方案**:
添加防御性检查。

```csharp
// [MODIFY] src/Acme.Product.Desktop/Handlers/WebMessageHandler.cs : Line 38

public void Initialize(WebView2 webViewControl)
{
    if (webViewControl?.CoreWebView2 == null)
        throw new InvalidOperationException("WebView2 content is not initialized.");
        
    _webViewControl = webViewControl;
    _webView = webViewControl.CoreWebView2;
    _webView.WebMessageReceived += OnWebMessageReceived;
}
```

---

### 5. 业务逻辑 - OperatorService 参数丢失
**文件**: `src/Acme.Product.Application/Services/OperatorService.cs`  
**描述**: 在 `CreateAsync` 方法中，用于从请求中复制参数到实体的代码块是空的，这导致创建的算子丢失所有初始参数配置。

**位置**:
```csharp
// Line 336-339
foreach (var param in request.Parameters)
{
    // 添加参数到算子 (此处为空!)
}
```

**修复方案**:
实现参数赋值逻辑。

```csharp
// [MODIFY] src/Acme.Product.Application/Services/OperatorService.cs : Line 336

foreach (var param in request.Parameters)
{
    if (!string.IsNullOrEmpty(param.Name) && param.Value != null)
    {
        // 假设 Operator 实体有 UpdateParameter 方法，或者需要在构造时传入
        // 查看 Operator.cs (Entity) 可知有 UpdateParameter
        try 
        {
            operatorEntity.UpdateParameter(param.Name, param.Value);
        }
        catch (Exception ex)
        {
            // 记录日志或忽略无效参数
        }
    }
}
```

---

### 6. 资源管理 - MatPool 缺少终结器
**文件**: `src/Acme.Product.Infrastructure/ImageProcessing/MatPool.cs`  
**描述**: `MatPool` 管理非托管内存 (OpenCV Mat)，但只实现了 `IDisposable` 而没有终结器 (`~MatPool`)。如果用户忘记调用 `Dispose()`，GC 回收 `MatPool` 时不会释放内部持有的 Mat 对象，导致非托管内存泄漏。

**位置**:
```csharp
// Line 10
public class MatPool : IDisposable
```

**修复方案**:
实现标准的 Dispose 模式。

```csharp
// [MODIFY] src/Acme.Product.Infrastructure/ImageProcessing/MatPool.cs

private bool _disposed;

~MatPool()
{
    Dispose(false);
}

public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

protected virtual void Dispose(bool disposing)
{
    if (!_disposed)
    {
        if (disposing)
        {
            // 释放托管资源
            // _pools 是 ConcurrentDictionary，本身不需要 Dispose，但我们需要清理里面的内容
            Clear(); 
        }
        _disposed = true;
    }
}
```

---

### 7. 依赖注入 - DbContext 生命周期错误
**文件**: `src/Acme.Product.Desktop/DependencyInjection.cs`  
**描述**: `VisionDbContext` 被注册为 **Singleton**。EF Core `DbContext` 不是线程安全的。在 Singleton 模式下，如果多个后台任务（如 Web API 请求）同时使用同一个 DbContext 实例，会发生严重的并发冲突。

**位置**:
```csharp
// Line 35-38
services.AddDbContext<VisionDbContext>(options =>
{
    options.UseSqlite("Data Source=vision.db");
}, ServiceLifetime.Singleton, ServiceLifetime.Singleton);
```

**修复方案**:
除非有非常特殊的理由（如其内部只读且加锁），否则应改为 **Scoped**（对于 Web API）或 **Transient**。对于 Desktop 应用，通常建议使用 Factory 模式或在每个操作单元中创建 Scope。

鉴于项目中已有 Web API (`Program.cs`)，应改为 Scoped，但注意 Desktop 主线程使用时可能需要 `IServiceScopeFactory`。如果必须保持简单且确认单线程访问，Singleton 勉强可行，但 `Program.cs` 里开启了 WebServer，这使得 Singleton 绝对不可行。

```csharp
// [MODIFY] src/Acme.Product.Desktop/DependencyInjection.cs

// 移除 ServiceLifetime 参数，默认使用 Scoped
services.AddDbContext<VisionDbContext>(options =>
{
    // 另外建议修复硬编码连接字符串
    options.UseSqlite("Data Source=vision.db");
});
```
*注意：修改为 Scoped 后，依赖 DbContext 的 Repository 也必须是 Scoped 或 Transient，不能是 Singleton。需同步修改 Line 41-45 的 Repository 注册。*

```csharp
// [MODIFY] src/Acme.Product.Desktop/DependencyInjection.cs : Line 41-45
services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
services.AddScoped<IProjectRepository, ProjectRepository>();
services.AddScoped<IOperatorRepository, OperatorRepository>();
services.AddScoped<IInspectionResultRepository, InspectionResultRepository>();
services.AddSingleton<IImageCacheRepository, ImageCacheRepository>(); // Cache 可以是 Singleton
```

---

### 8. 资源管理 - WebView2Host 事件泄漏
**文件**: `src/Acme.Product.Desktop/WebView2Host.cs`  
**描述**: `DisposeAsync` 中尝试取消订阅事件，但逻辑可能存在问题，或者由于 `_webView` 先被 Dispose 导致访问异常。

**位置**:
```csharp
// Line 523
if (_webView.CoreWebView2 is not null)
{
    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
}
_webView.Dispose();
```

**修复方案**:
在 Dispose 控件之前清理事件。确保 `CoreWebView2` 对象在访问时有效。当前的顺序看起来是正确的（先取消订阅再 Dispose），但需要确认 `_webView.Dispose()` 是否会立即使 `CoreWebView2` 变为 null 或抛出异常。更安全的做法是包含在 try-catch 中，或者依赖 weak events（如果不方便）。
对于此文件，更重要的问题可能是 **Line 140** 的 lambda 订阅：
`core.WebResourceRequested += (sender, e) => ...`
这个 lambda 捕获了上下文，且从未取消订阅。这会导致泄漏。

**修复**: 将 lambda 提取为命名方法，并在 `DisposeAsync` 中取消订阅。

---

## 🟡 P1 - 中危问题（部分列举）

### 9. 异常处理 - ImageAcquisitionService 后台异常吞没
**文件**: `src/Acme.Product.Infrastructure/Services/ImageAcquisitionService.cs`  
**描述**: `StartContinuousAcquisitionAsync` 启动了一个 `Task.Run` (Line 176)，其中的异常虽然有 `catch` 块 (Line 227)，但只是 `Console.WriteLine`，这在生产环境中会导致问题被忽略。

**修复**: 使用 `ILogger` 记录错误。

### 10. 异步 - async void 使用
**文件**: `src/Acme.Product.Desktop/Program.cs`  
**描述**: `StopWebServer` 是 `async void` (Line 307)。这是 C# 异步编程的大忌（仅允许用于事件处理程序）。这会导致异常无法被调用方捕获，且无法等待其完成。

**修复**: 改为 `async Task StopWebServer()`，并在调用处 `Wait()` 或 `await`。

---

## ✅ 验证计划

修复完成后，请运行以下检查：
1. **构建检查**: 确保项目编译无误。
2. **Web API 测试**: 启动应用，访问 `http://localhost:<port>/health` 确保服务运行。
3. **并发压力测试**: 编写脚本并发调用 `POST /api/execution/flow/{id}`，验证 Singleton 服务是否稳定。
4. **内存泄漏检查**: 使用 Demo 模式连续运行检测 5 分钟，观察内存占用曲线。

