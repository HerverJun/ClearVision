# 延时 / Delay

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DelayOperator` |
| 枚举值 (Enum) | `OperatorType.Delay` |
| 分类 (Category) | 流程控制 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | timer |
| 关键词 (Keywords) | 延时, 等待, 暂停, 定时, 休眠, Delay, Wait, Sleep, Timer |

## 算法原理 / Algorithm Principle

**中文：**
延时算子在流程执行中插入指定时长的异步等待，然后透传输入数据并输出实际耗时。核心逻辑：

1. **参数校验**：从参数 `Milliseconds` 读取延时时长（默认 200ms），校验范围 [0, 60000]。
2. **异步等待**：使用 `Task.Delay(ms, cancellationToken)` 实现可取消的异步等待。等待期间不阻塞线程，释放线程池资源。
3. **耗时计量**：通过 `DateTime.UtcNow` 在等待前后取差值计算实际耗时（毫秒），输出到 `ElapsedMs` 端口。
4. **数据透传**：从 `Input` 端口读取输入值，原样传递到 `Output` 端口，实现"延时后继续"的语义。

**English:**
A delay operator that inserts a specified asynchronous wait into the workflow, then passes through input data and outputs the actual elapsed time. Core logic:

1. **Parameter validation**: Reads delay duration from the `Milliseconds` parameter (default 200ms), validates range [0, 60000].
2. **Async wait**: Uses `Task.Delay(ms, cancellationToken)` for cancellable asynchronous waiting. Does not block the thread during the wait, releasing thread pool resources.
3. **Elapsed measurement**: Takes `DateTime.UtcNow` before and after the wait to compute actual elapsed time (milliseconds), output to the `ElapsedMs` port.
4. **Data passthrough**: Reads the value from the `Input` port and passes it unchanged to the `Output` port, implementing "delay then continue" semantics.

## 实现策略 / Implementation Strategy

- **异步非阻塞**：使用 `Task.Delay` 而非 `Thread.Sleep`，在等待期间释放线程池线程，适合高并发流程引擎。
- **CancellationToken 支持**：等待可被外部取消（如流程超时、用户中断），取消时抛出 `OperationCanceledException`。
- **硬上限 60 秒**：MaxDelayMs 常量限制为 60000ms，防止误配置导致流程长时间挂起。
- **透传设计**：Input/Output 端口设计为任意类型透传，不修改数据内容，仅插入时间间隔。
- **实际耗时输出**：ElapsedMs 输出实际等待时间（可能因系统调度略有偏差），而非配置值。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync (async)
  ├── GetParam("Milliseconds", DefaultDelayMs=200)  // 获取延时毫秒数
  ├── ms < 0 ? → Failure                             // 下限校验
  ├── ms > 60000 ? → Failure                         // 上限校验
  ├── start = DateTime.UtcNow                        // 记录开始时间
  ├── await Task.Delay(ms, cancellationToken)         // 异步等待（可取消）
  ├── elapsed = (DateTime.UtcNow - start).TotalMs    // 计算实际耗时
  ├── inputs.TryGetValue("Input", out v)              // 获取透传输入
  └── output = {Output: input, ElapsedMs: elapsed}    // 构建输出
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Milliseconds` | `int` | `200` | [0, 60000] | 延时毫秒数。0 表示不等待直接透传，最大 60000ms（60秒）。超过范围将返回失败 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Input` | 透传输入 | `Any` | No | 需要延时后传递的数据，可为任意类型或不连接 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Output` | 透传输出 | `Any` | 输入数据的原样透传，未连接输入时为空字符串 |
| `ElapsedMs` | 实际耗时(ms) | `Integer` | 实际等待的毫秒数（因系统调度可能与配置值有微小偏差） |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 无计算逻辑，仅等待 |
| 典型耗时 (Typical Latency) | 等于 Milliseconds 参数值（200ms 默认），实际可能有 +/- 几毫秒的系统调度偏差 |
| 内存特征 (Memory Profile) | 极低，等待期间无内存分配，仅输出字典 2 个键值对 |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- 通信前等待：发送指令给下位机后等待设备就绪（如相机触发后等待曝光完成）
- 流程节拍控制：在两个操作之间插入固定间隔，匹配产线节拍
- 重试间隔：配合循环和异常捕获实现带间隔的重试逻辑
- 调试用途：减慢流程执行速度以便观察中间状态

**不适合 (Not Suitable)：**
- 精确定时/定时触发（应使用 TimerStatistics 算子或系统定时器）
- 高频短延时（<1ms）：Task.Delay 的精度受系统调度限制
- 需要在延时期间执行其他逻辑的场景（应使用异步子图）

## 已知限制 / Known Limitations

1. **延时精度受系统调度影响**：`Task.Delay` 的实际等待时间取决于系统定时器分辨率和线程池负载，通常有 1-15ms 的偏差。
2. **硬上限 60 秒**：MaxDelayMs 常量固定为 60000，无法通过参数配置突破。需要更长等待应使用循环+延时组合。
3. **DateTime.UtcNow 精度**：Windows 上 `DateTime.UtcNow` 精度约 15ms，ElapsedMs 可能不精确反映实际等待时间。
4. **CancellationToken 外部取消**：如果外部取消了 token，等待会立即中断并抛出 `OperationCanceledException`，此时 Output 端口不会有输出。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充异步非阻塞机制、CancellationToken 取消支持、DateTime.UtcNow 精度限制说明；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
