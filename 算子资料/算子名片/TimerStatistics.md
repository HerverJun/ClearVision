# 计时统计 / TimerStatistics

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TimerStatisticsOperator` |
| 枚举值 (Enum) | `OperatorType.TimerStatistics` |
| 分类 (Category) | Logic Tools（逻辑工具） |
| 图标 (Icon) | `timer` |
| 关键词 (Keywords) | `timer`, `elapsed`, `cycle time`, `ct`, `statistics` |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子测量流程或算子的耗时并输出统计指标。使用 `System.Diagnostics.Stopwatch` 进行高精度计时。支持两种模式：**SingleShot**（单次模式）——每次执行仅测量自上次执行以来的间隔时间；**Cumulative**（累积模式）——累计多次执行的总时间和次数，计算平均值。状态通过 `ConcurrentDictionary<Guid, TimerState>` 按算子实例 ID 隔离存储，支持 TTL 自动清理过期状态。

> English: This operator measures elapsed time and outputs statistics for workflow or operator performance. It uses `System.Diagnostics.Stopwatch` for high-precision timing. Two modes are supported: **SingleShot** - measures only the interval since the last execution; **Cumulative** - accumulates total time and count across executions, computing averages. State is isolated per operator instance ID via `ConcurrentDictionary<Guid, TimerState>` with TTL-based automatic cleanup of stale states.

## 实现策略 / Implementation Strategy
> 中文：算子有一个可选输入端口 `Trigger`，但不参与计时逻辑。状态以 `TimerState` 内部类存储，包含 `Stopwatch`、累计时间、计数、最后访问时间和 TTL。执行时先清理过期状态（`CleanupStaleStates`），再检查 `Reset` 标志（若为 true 则删除该算子的状态并返回零值）。正常执行时启动/重启 `Stopwatch`，记录 `ElapsedMs`。Cumulative 模式下累加到 `TotalMs` 和 `Count`，当 `Count >= ResetInterval` 时自动重置。状态通过 `lock(state.SyncRoot)` 保证线程安全。

> English: The operator has one optional input port `Trigger` that does not participate in timing logic. State is stored in a `TimerState` inner class containing a `Stopwatch`, accumulated time, count, last access time, and TTL. Execution first cleans up stale states (`CleanupStaleStates`), then checks the `Reset` flag (if true, removes the operator's state and returns zeros). In normal execution, the `Stopwatch` is started/restarted and `ElapsedMs` is recorded. In Cumulative mode, values are accumulated into `TotalMs` and `Count`; when `Count >= ResetInterval`, an automatic reset occurs. Thread safety is ensured via `lock(state.SyncRoot)`.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "Mode")` - 读取计时模式
2. `GetIntParam(@operator, "ResetInterval")` - 读取自动重置间隔
3. `GetIntParam(@operator, "StateTtlMinutes")` - 读取状态 TTL（分钟）
4. `GetBoolParam(@operator, "Reset")` - 读取手动重置标志
5. `CleanupStaleStates(nowUtc, @operator.Id)` - 清理过期状态
6. 若 Reset=true: `_states.TryRemove(@operator.Id, out _)` -> 返回零值
7. `state = _states.GetOrAdd(@operator.Id, ...)` - 获取或创建状态
8. `lock(state.SyncRoot)` - 进入线程安全区
9. `state.IntervalStopwatch.Start()` / `.Restart()` - 启动/重启计时
10. Cumulative 模式: `state.Count++`, `state.TotalMs += elapsedMs`, 检查 ResetInterval
11. 返回 `OperatorExecutionOutput.Success(...)` 包含 ElapsedMs, TotalMs, AverageMs, Count 等

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"SingleShot"` | `SingleShot` / `Cumulative` | 计时模式；SingleShot 仅测单次间隔，Cumulative 累计统计 |
| `ResetInterval` | `int` | `0` | 0 ~ 1000000 | 累计模式下自动重置的次数阈值；0 表示不自动重置 |
| `StateTtlMinutes` | `int` | `120` | 0 ~ 10080 | 状态保留时间（分钟）；过期状态在下次执行时自动清理 |
| `Reset` | `bool` | `false` | true / false | 设为 true 时手动清除该算子的累计状态并返回零值 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Trigger` | Trigger | `Any` | No | 触发信号（不参与计时逻辑，仅透传到输出） |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `ElapsedMs` | Elapsed (ms) | `Float` | 本次执行与上次执行之间的时间间隔（毫秒） |
| `TotalMs` | Total (ms) | `Float` | 累计总时间（毫秒，仅 Cumulative 模式有意义） |
| `AverageMs` | Average (ms) | `Float` | 平均每次耗时（毫秒，仅 Cumulative 模式有意义） |
| `Count` | Count | `Integer` | 累计执行次数（仅 Cumulative 模式有意义） |

> 注：运行时输出还包含 StateScope、StateKey、StateTtlMinutes、ResetApplied、Diagnostics 等诊断字段。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) 计时 + O(s) 过期清理（s 为其他算子实例数） |
| 典型耗时 (Typical Latency) | < 1 ms（Stopwatch 读取 + 字典操作） |
| 内存特征 (Memory Profile) | O(i) - i 为活跃算子实例数（每个实例一个 TimerState） |
| 计时精度 | `Stopwatch` 精度，通常 < 1 微秒 |

## 适用场景 / Use Cases
- 适合 (Suitable)：测量单个算子或流程段的执行耗时
- 适合 (Suitable)：统计循环流程的平均周期时间（CT）
- 适合 (Suitable)：性能瓶颈分析，配合 Cumulative 模式获取累计指标
- 适合 (Suitable)：流程节拍监控，利用 ResetInterval 自动重置统计窗口
- 不适合 (Not Suitable)：需要纳秒级精度的微基准测试
- 不适合 (Not Suitable)：跨进程的分布式计时（状态存储在进程内存中）

## 已知限制 / Known Limitations
1. 状态存储在进程内存的 `static ConcurrentDictionary` 中，进程重启后所有累计数据丢失。
2. `CleanupStaleStates` 在每次执行时遍历所有状态条目，在大量算子实例场景下可能产生性能开销。
3. SingleShot 模式下首次执行 `ElapsedMs` 为 0（`Stopwatch` 刚启动），后续才是实际间隔。
4. Cumulative 模式下 `TotalMs` 和 `AverageMs` 包含 ResetInterval 自动重置前的值，重置后重新从零开始。
5. `StateTtlMinutes` 设为 0 时状态永不过期（`Ttl = TimeSpan.FromMinutes(0)`，`LastTouchedUtc <= nowUtc - Ttl` 始终为 true），但代码中 `CleanupStaleStates` 会立即清理 TTL 为 0 的状态。
6. 输出中的 `Diagnostics` 字段是嵌套字典，某些下游序列化器可能不支持。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确定位 Stopwatch 高精度计时和 ConcurrentDictionary 状态管理机制、发现 TTL=0 时 CleanupStaleStates 的边界行为、明确 ResetInterval 自动重置逻辑、补充 Diagnostics 嵌套输出说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
