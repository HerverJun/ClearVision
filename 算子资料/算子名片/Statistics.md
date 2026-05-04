# 统计 / Statistics

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `StatisticsOperator` |
| 枚举值 (Enum) | `OperatorType.Statistics` |
| 分类 (Category) | General |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `stats` |

## 算法原理 / Algorithm Principle
> **中文：** 对滚动窗口内的数值历史计算统计指标：均值(Mean)、标准差(StdDev)、最小值(Min)、最大值(Max)、
> 范围(Range)、样本数(Count)。当同时提供 USL（上规格限）和 LSL（下规格限）时，额外计算过程能力指数：
> - **Cp** = (USL - LSL) / (6 * StdDev)
> - **Cpu** = (USL - Mean) / (3 * StdDev)
> - **Cpl** = (Mean - LSL) / (3 * StdDev)
> - **Cpk** = min(Cpu, Cpl)
>
> 标准差使用无偏估计（Bessel 校正，除以 n-1）。`IsCapable = Cpk >= 1.33`。
>
> 状态通过 `ConcurrentDictionary<Guid, RollingHistoryState>` 按算子实例隔离，
> 支持 TTL 自动清理和手动 Reset。
>
> **English:** Computes rolling-window statistics: Mean, StdDev, Min, Max, Range, Count.
> When both USL and LSL are provided, also computes process capability indices:
> Cp, Cpu, Cpl, Cpk. StdDev uses Bessel's correction (n-1). `IsCapable = Cpk >= 1.33`.
>
> State is isolated per operator instance via `ConcurrentDictionary<Guid, RollingHistoryState>`,
> with TTL-based auto-cleanup and manual Reset support.

## 实现策略 / Implementation Strategy
- 使用 `ConcurrentDictionary<Guid, RollingHistoryState>` 按算子 ID 隔离状态。
- `RollingHistoryState` 内部使用 `Queue<double>` 实现滑动窗口，`lock(state.SyncRoot)` 保证线程安全。
- 窗口满时自动 Dequeue 最旧值，保持窗口大小 <= `WindowSize`。
- TTL 清理：每 5 分钟检查一次，移除超过 `StateTtlMinutes` 未访问的状态。
- USL/LSL 为可选参数（通过 `GetOptionalDoubleParam` 读取），仅两者都提供时计算 Cpk。
- 方差计算：`variance = sum((v - mean)^2) / (n - 1)`，n=1 时 variance=0。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputValue<double>(inputs, "Value", out value)` -> 读取输入值
2. `GetOptionalDoubleParam(@operator, "USL")` + `GetOptionalDoubleParam("LSL")` -> 可选规格限
3. `GetIntParam(@operator, "WindowSize", 1000, 2, 50_000)` + `StateTtlMinutes` + `Reset`
4. `HistoryByOperator.GetOrAdd(@operator.Id, _ => new RollingHistoryState())` -> 获取/创建状态
5. `lock(state.SyncRoot)` -> Enqueue + Dequeue -> `state.Values.ToArray()` 快照
6. `TryCleanupStaleStates(nowUtc)` -> 定期清理过期状态
7. 统计计算：`snapshot.Average()` / `Min()` / `Max()` / 方差 / StdDev
8. 条件：`usl.HasValue && lsl.HasValue && count >= 2 && stdDev > 0` -> 计算 Cp/Cpk/Cpu/Cpl
9. `OperatorExecutionOutput.Success(...)` -> Mean, StdDev, Count, Min, Max, Range, [Cpk, IsCapable, ...]

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `USL` | `double` | `""` (空) | - | 上规格限（Upper Specification Limit）。可选，与 LSL 同时提供时启用 Cpk 计算。 |
| `LSL` | `double` | `""` (空) | - | 下规格限（Lower Specification Limit）。可选，与 USL 同时提供时启用 Cpk 计算。 |
| `WindowSize` | `int` | `1000` | [2, 50000] | 滑动窗口大小（最大样本数）。 |
| `StateTtlMinutes` | `int` | `120` | [1, 10080] | 状态生存时间（分钟），超时未访问自动清理。10080 = 7 天。 |
| `Reset` | `bool` | `false` | - | 设为 true 时清空当前算子的历史窗口。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | Input Value | `Float` | Yes | 待统计的数值输入。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Mean` | Mean | `Float` | 滚动窗口内样本的算术平均值。 |
| `StdDev` | StdDev | `Float` | 样本标准差（无偏估计，除以 n-1）。 |
| `Count` | Count | `Integer` | 当前窗口内的样本数。 |
| `Min` | Min | `Float` | 窗口内最小值。 |
| `Max` | Max | `Float` | 窗口内最大值。 |
| `Cpk` | Cpk | `Float` | 过程能力指数（仅在 USL+LSL 均提供且 n>=2, StdDev>0 时输出）。 |
| `IsCapable` | Is Capable | `Boolean` | Cpk >= 1.33 时为 true。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Range` | `Float` | Max - Min。 |
| `Cp` | `Float` | 过程能力指数 Cp = (USL-LSL)/(6*StdDev)。 |
| `CPU` | `Float` | 上侧过程能力指数 Cpu = (USL-Mean)/(3*StdDev)。 |
| `CPL` | `Float` | 下侧过程能力指数 Cpl = (Mean-LSL)/(3*StdDev)。 |
| `USL` | `Float` | 实际使用的上规格限。 |
| `LSL` | `Float` | 实际使用的下规格限。 |
| `WindowSize` | `Integer` | 实际使用的窗口大小。 |
| `StateTtlMinutes` | `Integer` | 实际使用的 TTL 分钟数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n) 每次调用（n 为窗口大小，需遍历计算方差） |
| 典型耗时 (Typical Latency) | < 1ms（窗口 1000）；< 10ms（窗口 50000） |
| 内存特征 (Memory Profile) | O(W) 每个算子实例（W = WindowSize，存储 Queue<double>） |

## 适用场景 / Use Cases
- 适合 (Suitable)：在线质量监控中的实时统计（SPC 控制图）
- 适合 (Suitable)：过程能力分析（Cpk 计算）
- 适合 (Suitable)：需要滑动窗口统计而非全局统计的场景
- 不适合 (Not Suitable)：需要中位数、百分位数等高级统计量
- 不适合 (Not Suitable)：多维数据的协方差或相关性分析
- 不适合 (Not Suitable)：需要持久化统计结果的场景（状态仅在进程内存中）

## 已知限制 / Known Limitations
1. 状态存储在进程内存中，进程重启后历史数据丢失。
2. 每次调用需遍历整个窗口计算方差，大窗口（50000）时性能下降。
3. USL 必须大于 LSL，否则参数校验失败。
4. Cpk 计算要求 n>=2 且 StdDev>0，单样本时不输出 Cpk。
5. TTL 清理每 5 分钟执行一次，极端情况下状态可能延迟清理。
6. `Reset=true` 只清空当前算子的状态，不影响其他实例。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 Cpk 公式、滑动窗口机制、TTL 清理、运行时附加输出 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 |
