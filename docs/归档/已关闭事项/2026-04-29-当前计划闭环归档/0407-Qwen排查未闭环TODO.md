---
title: "0407 Qwen 排查剩余 TODO"
doc_type: "closed-plan"
status: "closed"
topic: "当前计划闭环归档"
created: "2026-04-28"
updated: "2026-04-29"
closed_at: "2026-04-29"
source_path: "docs/进行中/当前计划/0407-Qwen排查未闭环TODO.md"
---

# 0407 Qwen 排查剩余 TODO

> 归档说明：本计划已于 2026-04-29 完成代码闭环与定向验证，归档至 `docs/归档/已关闭事项/2026-04-29-当前计划闭环归档/`。后续如需重新开启相关问题，应新建当前计划并引用本文，不直接改写归档正文。

> 核对日期：2026-04-28  
> 来源：`docs/进行中/未闭环事项/0407-Qwen排查未闭环.md`  
> 结论：原 26 项中多数已有代码闭环或源文件已不在仓库；2026-04-29 已完成当前剩余 2 项闭环。

## 本轮落地记录（2026-04-29）

- `OpenAiConnector` 已改为区分内部/外部 `HttpClient` 所有权；外部注入 client 不再被 connector dispose。
- OpenAI 请求改为逐请求 `HttpRequestMessage`，不再修改共享 client 的 `BaseAddress`、`DefaultRequestHeaders.Authorization` 或 `Timeout`。
- `ImageAcquisitionService` 已删除未使用 `_cacheLock` 与对应 dispose，缓存路径继续使用既有 `lock (_imageCache)` 策略。
- 验证：`LLMConnectorSmokeTests,ImageAcquisitionServiceIntegrationTests` 已并入本轮定向测试批次，最终 97/97 passed。

## 已见闭环证据

- `AutoTuneService` 已通过 `TryCreateOwnedPreviewImage` 克隆/解码为自有 `Mat`，调用处使用 `using var outputImage`。
- `ImageAcquisitionService` resize 路径已先 `Clone()` 到 `nextMat`，再释放旧 `resultMat`，规避原双重释放窗口。
- `CameraManager.Dispose()` 已改为同步 `DisconnectAllCore()`，不再 `.Wait()` 异步方法。
- `ConnectionPoolManager` 已增加 `_gate` / `_connectGates`、`Interlocked.Exchange` dispose guard，并把 TCP 返回值改为 `PooledTcpConnectionLease`。
- `InspectionRuntimeCoordinator` 已用 `ScheduleCleanup`/`CleanupSessionAsync` 捕获并记录清理异常，清理 CTS 和会话。
- `InspectionWorker.ExecuteCycleAsync` 已接收 `sessionId`，`Task = null!` 初始化模式未再出现。
- `FlowExecutionService` 已在并行 layer 失败后触发 `CancelLayerAsync(layerCts)`，并在执行结束清理 `_executionCancellations`。
- `ParameterRecommender.EnsureGray` 已重命名为 `CreateOwnedGrayMat`，调用处继续使用 `using var`。
- `BaselineBenchmark` 模板尺寸已用 `Math.Max(1, ...)` 兜底；`cleanup.bat` / `build_script.bat` 已改为脚本目录相对路径。
- `401.py` / `renameAuthFile` / `RUN_ONCE` 在当前仓库未定位到源码，暂按历史外部脚本处理，不纳入本轮代码修复。

## P1：OpenAiConnector 共享 HttpClient 所有权与配置隔离

### 未闭环证据

- `Acme.Product/src/Acme.Product.Infrastructure/AI/Connectors/OpenAiConnector.cs` 构造函数仍直接修改传入的 `_httpClient.BaseAddress`、`DefaultRequestHeaders`、`Timeout`。
- `Dispose()` 仍无条件 `_httpClient?.Dispose()`，外部注入的共享 `HttpClient` 仍可能被释放。

### TODO

- [x] 增加 HttpClient 所有权标记，仅 dispose 内部创建的 client。
- [x] 避免修改外部注入 client 的全局 `BaseAddress` / `DefaultRequestHeaders` / `Timeout`。
- [x] 将认证头迁移到单次 `HttpRequestMessage`，或引入明确的 connector-owned client 构造路径。
- [x] 补单元测试：外部传入 `HttpClient` 时，connector dispose 后 client 仍可用；多个 connector 不互相污染 headers/base URL。

### 验收标准

- 外部注入的 `HttpClient` 不被 connector dispose。
- 并行创建两个不同 OpenAI 配置的 connector，不会互相覆盖 `Authorization`、`BaseAddress` 或 `Timeout`。
- 现有 OpenAI connector 行为测试通过。

## P3：ImageAcquisitionService 缓存锁清理

### 未闭环证据

- `ImageAcquisitionService` 仍保留 `_cacheLock = new(1, 1)`，但缓存读写实际使用 `lock (_imageCache)`。

### TODO

- [x] 删除未使用的 `_cacheLock`，或统一改用 `_cacheLock.WaitAsync()` 保护缓存路径。
- [x] 如果改用异步锁，补并发缓存读写测试，覆盖 `AddToCache`、`RemoveFromCache`、`GetImageAsync`。（本轮未改用异步锁，沿用既有 `lock (_imageCache)`，无需新增异步锁专项测试。）

### 验收标准

- 无未使用字段警告或死代码。
- 缓存淘汰、释放、读取路径仍保持线程安全。
