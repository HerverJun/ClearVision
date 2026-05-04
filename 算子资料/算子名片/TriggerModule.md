# 触发模块 / TriggerModule

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TriggerModuleOperator` |
| 枚举值 (Enum) | `OperatorType.TriggerModule` |
| 分类 (Category) | Logic Tools（逻辑工具） |
| 图标 (Icon) | `trigger` |
| 关键词 (Keywords) | `trigger`, `start`, `timer`, `external signal` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子管理三种触发模式生成流程启动信号。**Software（软件触发）**：每次执行时无条件产生触发。**Timer（定时触发）**：基于 `DateTime.UtcNow` 时间差判断，当距上次触发时间 >= `Interval` 毫秒时产生触发；首次执行始终触发。**ExternalSignal（外部信号触发）**：从输入端口 `Signal` 读取布尔信号，信号为 true 时触发。触发状态（最后触发时间、累计触发次数）通过实例级 `_syncRoot` 锁保证线程安全。

> English: This operator manages three trigger modes to generate workflow start signals. **Software**: unconditionally triggers on every execution. **Timer**: uses `DateTime.UtcNow` time delta to trigger when the interval since the last trigger >= `Interval` milliseconds; always triggers on first execution. **ExternalSignal**: reads a boolean signal from the `Signal` input port; triggers when signal is true. Trigger state (last trigger time, cumulative count) is thread-safe via an instance-level `_syncRoot` lock.

## 实现策略 / Implementation Strategy
> 中文：算子有一个可选布尔输入端口 `Signal` 和三个输出端口。执行时先检查模式和输入有效性：ExternalSignal 模式下若无有效 Signal 输入则返回 Failure。然后进入 `lock(_syncRoot)` 临界区，按模式判断是否触发。Timer 模式的 `ShouldTriggerByTimer` 方法检查三个条件：首次执行（`_triggerCount == 0`）直接触发；非自动重复（`autoRepeat = false`）时仅首次触发；否则检查时间间隔。触发时更新 `_lastTriggerUtc` 并递增 `_triggerCount`。信号输入的布尔转换 (`TryConvertToBool`) 支持 bool/int/long/double/string 五种类型。

> English: The operator has one optional boolean input port `Signal` and three output ports. Execution first validates mode and input: in ExternalSignal mode, missing valid Signal input returns Failure. Then it enters `lock(_syncRoot)` and evaluates triggering per mode. The Timer mode's `ShouldTriggerByTimer` checks three conditions: first execution (`_triggerCount == 0`) always triggers; non-auto-repeat (`autoRepeat = false`) triggers only once; otherwise checks the time interval. On trigger, `_lastTriggerUtc` is updated and `_triggerCount` incremented. Signal input boolean conversion (`TryConvertToBool`) supports five types: bool/int/long/double/string.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "TriggerMode")` - 读取触发模式
2. `GetIntParam(@operator, "Interval")` - 读取定时间隔（毫秒）
3. `GetBoolParam(@operator, "AutoRepeat")` - 读取自动重复标志
4. ExternalSignal 模式: `TryGetSignalInput(inputs, out signal)` -> 验证输入
5. `lock(_syncRoot)` - 进入线程安全区
6. Software: `triggered = true`
7. Timer: `ShouldTriggerByTimer(now, intervalMs, autoRepeat)` - 时间间隔判断
8. ExternalSignal: `TryGetSignalInput(inputs, out signal) && signal` - 信号判断
9. 若触发: `_lastTriggerUtc = now`, `_triggerCount++`
10. 返回 `OperatorExecutionOutput.Success(...)` 包含 Triggered, Timestamp, TriggerCount

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TriggerMode` | `enum` | `"Software"` | `Software` / `Timer` / `ExternalSignal` | 触发模式 |
| `Interval` | `int` | `1000` | 1 ~ 3600000 | 定时触发的间隔时间（毫秒），仅 Timer 模式生效 |
| `AutoRepeat` | `bool` | `true` | true / false | Timer 模式下是否自动重复触发；false 时仅首次触发 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Signal` | Signal | `Boolean` | No | 外部信号输入；仅 ExternalSignal 模式下必填；支持 bool/int/long/double/string 类型自动转换 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Triggered` | Triggered | `Boolean` | 本次是否产生了触发信号 |
| `Timestamp` | Timestamp | `String` | 触发时间戳（ISO 8601 格式 `"O"`） |
| `TriggerCount` | Trigger Count | `Integer` | 累计触发次数 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) - 时间比较 + 计数器递增 |
| 典型耗时 (Typical Latency) | < 1 ms（纯内存操作，`DateTime.UtcNow` 精度足够） |
| 内存特征 (Memory Profile) | O(1) - 仅三个实例字段（`_lastTriggerUtc`, `_triggerCount`, `_syncRoot`） |
| 线程安全 | `lock(_syncRoot)` 保护所有状态读写 |

## 适用场景 / Use Cases
- 适合 (Suitable)：流程启动信号源，作为工作流的第一个节点
- 适合 (Suitable)：定时采样/检测，配合 Timer 模式实现固定间隔触发
- 适合 (Suitable)：外部硬件信号响应，配合 ExternalSignal 模式接收 PLC 或传感器信号
- 适合 (Suitable)：流程调试，使用 Software 模式手动触发单步执行
- 不适合 (Not Suitable)：需要高精度定时的场景（受算子调度延迟影响）
- 不适合 (Not Suitable)：需要触发脉冲宽度控制的场景（触发信号无持续时间概念）

## 已知限制 / Known Limitations
1. Timer 模式的时间判断基于 `DateTime.UtcNow`，受系统时钟调整（如 NTP 同步）影响，可能产生非预期的触发间隔。
2. 状态存储在实例字段中（非 `static`），每个 `TriggerModuleOperator` 实例独立维护触发状态，不同流程图中的同类型算子互不影响。
3. ExternalSignal 模式下，`TryConvertToBool` 对非布尔类型的转换使用宽松规则（如 int 0 = false），可能产生非预期行为。
4. `_triggerCount` 为 `int` 类型，在极端长时间运行场景下可能溢出（约 21 亿次触发后）。
5. `ShouldTriggerByTimer` 中 `autoRepeat = false` 时仅首次触发后永不触发，但 `_lastTriggerUtc` 仍会更新，再次设为 `autoRepeat = true` 不会立即触发（需等待间隔时间）。
6. `Timestamp` 输出使用 `"O"` 格式（ISO 8601），包含时区信息（UTC），但格式为字符串而非时间戳数值。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确定位三种触发模式的实现差异、发现 autoRepeat=false 后重新启用不会立即触发的行为、明确 TryConvertToBool 五类型转换规则、补充 int 溢出和系统时钟调整风险 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
