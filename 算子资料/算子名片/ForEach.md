# ForEach 循环 / ForEach

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ForEachOperator` |
| 枚举值 (Enum) | `OperatorType.ForEach` |
| 分类 (Category) | 流程控制 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | loop |

## 算法原理 / Algorithm Principle

**中文：**
ForEach 循环算子对输入集合中的每个元素执行子图（SubGraph），支持并行和串行两种执行模式。核心算法：

1. **集合解析**：从 `Items` 端口获取输入，通过 `ParseItems` 将任意 `IEnumerable`（非 string）转换为 `List<object>`。
2. **子图加载**：优先使用运行时注入的 `SubGraph` 属性，其次从算子参数中反序列化 JSON 子图定义。
3. **双模式执行**：
   - **Parallel（并行模式）**：使用 `Parallel.ForEachAsync` 并行执行子图，`MaxDegreeOfParallelism` 控制并发度。适用于纯计算子图（图像处理、AI 推理），各迭代项相互独立。
   - **Sequential（串行模式）**：`foreach` 顺序执行子图。适用于含 I/O 的子图（HTTP 校验、MES 上报），保证对外部设备的访问严格串行。
4. **子图输入构建**：每次迭代向子图注入 `CurrentItem`（当前元素）、`CurrentIndex`（索引）、`TotalCount`（总数）三个上下文变量。
5. **结果聚合**：收集所有迭代结果，计算 `Results`（结果列表）、`Count`（总数）、`PassCount`（通过数）、`AllPass`（全部通过）、`SuccessCount`（成功数）、`FailureCount`（失败数）、`AllSucceeded`（全部成功）。
6. **FailFast 机制**：当 `FailFast=true` 时，任一子图失败即取消后续执行（并行模式通过 CancellationToken，串行模式通过 break）。

**English:**
A ForEach loop operator that executes a sub-graph (SubGraph) for each element in the input collection, supporting both parallel and sequential execution modes. Core algorithm:

1. **Collection parsing**: Retrieves input from the `Items` port, converts any `IEnumerable` (non-string) to `List<object>` via `ParseItems`.
2. **SubGraph loading**: Prefers the runtime-injected `SubGraph` property, falls back to JSON deserialization from operator parameters.
3. **Dual-mode execution**:
   - **Parallel mode**: Uses `Parallel.ForEachAsync` for concurrent sub-graph execution, controlled by `MaxDegreeOfParallelism`. Suitable for pure-compute sub-graphs (image processing, AI inference) where iterations are independent.
   - **Sequential mode**: `foreach` sequential execution. Suitable for I/O-containing sub-graphs (HTTP validation, MES reporting) to ensure serial external device access.
4. **Sub-graph input construction**: Each iteration injects `CurrentItem` (current element), `CurrentIndex` (index), `TotalCount` (total count) as context variables.
5. **Result aggregation**: Collects all iteration results, computing `Results`, `Count`, `PassCount`, `AllPass`, `SuccessCount`, `FailureCount`, `AllSucceeded`.
6. **FailFast mechanism**: When `FailFast=true`, any sub-graph failure cancels subsequent execution (parallel via CancellationToken, sequential via break).

## 实现策略 / Implementation Strategy

- **Parallel.ForEachAsync**：使用 .NET 6+ 的 `Parallel.ForEachAsync` 而非手动 Task.WhenAll，自动处理异常聚合和取消传播。
- **ConcurrentBag 收集**：并行模式使用 `ConcurrentBag` 线程安全收集结果，避免锁竞争。
- **子图隔离**：每次子图执行通过 `IFlowExecutionService.ExecuteFlowAsync` 启动独立的流程执行，`enableParallel: false` 防止子图内部再嵌套并行。
- **超时机制**：通过 `CancellationTokenSource.CancelAfter` 实现超时控制。并行模式总超时 = timeoutMs * items.Count，串行模式每项独立超时。
- **ImageWrapper 引用安全**：子图输入中的对象直接传递引用，不做深拷贝，需注意并发访问安全。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync (async)
  ├── inputs.TryGetValue("Items", out itemsObj)     // 获取集合输入
  ├── ParseItems(itemsObj)                          // IEnumerable → List<object>
  │   └── enumerable.Cast<object>().ToList()
  ├── GetStringParam("IoMode")                      // 获取执行模式
  ├── GetIntParam("MaxParallelism")                 // 获取并行度
  ├── GetBoolParam("FailFast")                      // 获取快速失败标志
  ├── GetTimeoutMs(@operator)                       // 获取超时时间
  ├── GetSubGraph(@operator)                        // 获取子图定义
  │   ├── SubGraph 属性（优先）
  │   └── JsonSerializer.Deserialize<OperatorFlow>() // 从参数反序列化
  ├── IoMode == "Sequential" ?
  │   └── ExecuteSequentialAsync(items, subGraph, timeoutMs, failFast, ct)
  │       └── foreach (item, index) in items
  │           ├── BuildSubInputs(item, index, total) // {CurrentItem, CurrentIndex, TotalCount}
  │           ├── ExecuteSubGraphAsync(subGraph, inputs, timeoutMs, ct)
  │           │   └── IFlowExecutionService.ExecuteFlowAsync(subGraph, inputs, enableParallel: false)
  │           └── failFast && !success ? break
  └── IoMode == "Parallel" ?
      └── ExecuteParallelAsync(items, subGraph, maxParallelism, timeoutMs, failFast, orderResults, ct)
          └── Parallel.ForEachAsync(items, maxDegreeOfParallelism, ct)
              ├── BuildSubInputs(item, index, total)
              ├── ExecuteSubGraphAsync(subGraph, inputs, timeoutMs, ct)
              └── failFast && !success ? cts.Cancel()
  └── BuildAggregateResult(results, orderResults)
      └── {Results, Count, PassCount, AllPass, SuccessCount, FailureCount, AllSucceeded}
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `IoMode` | `enum` | `"Parallel"` | Parallel / Sequential | 执行模式。Parallel 使用 Parallel.ForEachAsync 并行执行（适合纯计算子图）；Sequential 使用 foreach 串行执行（适合含 I/O 通信的子图） |
| `MaxParallelism` | `int` | `8` | [1, 64] | 并行模式下的最大并发度。默认值为 8，实际运行时默认为 `Environment.ProcessorCount` |
| `Timeout` | `int` | `30000` | [1000, 300000] | 单次子图执行超时毫秒数。并行模式总超时 = Timeout * Count；串行模式每项独立超时 |
| `FailFast` | `bool` | `true` | - | 遇错即停。true 时任一子图失败立即取消后续执行；false 时继续执行剩余项 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Items` | 集合 | `Any` | Yes | 可枚举的输入集合（List、Array 等 IEnumerable 类型，排除 string）。空集合直接返回空结果 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Results` | 结果列表 | `Any` | 所有迭代结果的列表，每项为子图输出中的 `Result` 字段值 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n) — n 为集合大小，每个元素执行一次子图 |
| 典型耗时 (Typical Latency) | 串行：n * 子图耗时；并行：n * 子图耗时 / MaxParallelism（受子图性质和系统资源限制） |
| 内存特征 (Memory Profile) | 中等。并行模式下同时持有 MaxParallelism 个子图的执行上下文；ConcurrentBag 线程安全收集结果 |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- 批量检测结果处理：对多目标检测的每个缺陷逐条上报 MES
- 并行图像处理：批量滤波、裁剪、AI 推理（Parallel 模式）
- 串行通信：逐条 HTTP 校验、逐个设备指令发送（Sequential 模式）
- 数据转换管道：对集合中每个元素执行相同的转换子图
- 循环判定配合：与 CycleCounter、ResultJudgment 组合实现批量质量统计

**不适合 (Not Suitable)：**
- 条件循环（while 语义）：ForEach 总是遍历完整集合，不支持中途条件终止（除 FailFast）
- 递归子图：子图内不应再嵌套 ForEach，避免不可控的并发和资源消耗
- 实时流处理：ForEach 设计为批量处理，不适合逐帧实时处理场景
- 超大集合（>10 万项）：内存和调度开销可能成为瓶颈

## 已知限制 / Known Limitations

1. **子图加载依赖 IFlowExecutionService**：子图通过 DI 容器的 `IFlowExecutionService` 执行，运行时必须注册该服务。
2. **SubGraph 参数序列化限制**：从算子参数反序列化子图定义时，依赖 `System.Text.Json`，复杂嵌套对象可能序列化失败。
3. **并行模式总超时计算**：并行模式总超时 = `timeoutMs * items.Count`，对于大集合可能导致非常长的超时时间。
4. **orderResults 参数未在元数据声明**：`OrderResults` 参数在代码中使用（默认 true），但未在 `[OperatorParam]` 属性中声明，UI 中不可配置。
5. **空集合返回 AllPass=true**：空集合直接返回 `AllPass: true`，语义上可能有歧义（是否应视为"无通过项"而非"全部通过"）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充 Parallel.ForEachAsync 并行机制、ConcurrentBag 线程安全收集、子图输入构建（CurrentItem/CurrentIndex/TotalCount）、FailFast 取消传播、结果聚合字段详情；修正参数表（Timeout 实际范围 [1000, 300000]）；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
