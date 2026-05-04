# 变量递增 / VariableIncrement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VariableIncrementOperator` |
| 枚举值 (Enum) | `OperatorType.VariableIncrement` |
| 分类 (Category) | 变量 |
| 图标 (Icon) | `counter` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子维护一个基于全局变量上下文 (`IVariableContext`) 的计数器。每次执行时读取当前变量值，按 `Delta` 参数递增（或递减，当 Delta 为负数时）。在递增前检查可配置的重置条件（大于阈值、小于阈值、等于阈值），若满足则先将变量重置为 `ResetValue` 再加 Delta。重置与递增均通过 `IVarcentext.SetValue` / `IVariableContext.Increment` 原子操作完成，确保并发安全。

> English: This operator maintains a counter backed by the global variable context (`IVariableContext`). On each execution it reads the current value, checks a configurable reset condition (GreaterThan, LessThan, Equal against a threshold), optionally resets the variable to `ResetValue`, and then increments by `Delta` (which may be negative for decrement). Both reset and increment are performed through atomic `IVariableContext.SetValue` / `IVariableContext.Increment` calls, ensuring thread safety.

## 实现策略 / Implementation Strategy
> 中文：算子不依赖输入端口，仅从参数面板读取配置。先读取当前值，再按 switch-case 判断重置条件，最后选择"重置后递增"或"直接递增"两条路径。所有变量操作通过 `IVariableContext` 接口解耦，支持跨算子共享状态。输出包含前值、新值、增量和重置标志，方便下游条件判断。

> English: The operator has no input ports; all configuration comes from the parameter panel. It reads the current variable value, evaluates the reset condition via a switch-case, then either resets-then-increments or directly increments. All variable operations are decoupled through the `IVariableContext` interface, enabling shared state across operators. Outputs include previous value, new value, delta, and reset flag for downstream conditional logic.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "VariableName")` - 读取变量名
2. `GetIntParam(@operator, "Delta")` - 读取增量值
3. `GetStringParam(@operator, "ResetCondition")` - 读取重置条件
4. `GetIntParam(@operator, "ResetThreshold")` / `GetIntParam(@operator, "ResetValue")` - 读取阈值与重置值
5. `_variableContext.GetValue<long>(variableName, 0L)` - 获取当前值
6. 条件判断: `currentValue >/< /== resetThreshold`
7. `_variableContext.SetValue(variableName, resetValue + delta)` 或 `_variableContext.Increment(variableName, delta)` - 执行写入
8. 返回 `OperatorExecutionOutput.Success(...)` 包含 VariableName, PreviousValue, NewValue, Delta, WasReset, CycleCount

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `VariableName` | `string` | `"counter"` | 非空字符串 | 计数器变量名称，对应 `IVariableContext` 中的键 |
| `Delta` | `int` | `1` | 任意整数（可为负） | 每次递增的值；负数实现递减功能 |
| `ResetCondition` | `enum` | `"None"` | `None` / `GreaterThan` / `LessThan` / `Equal` | 满足条件时重置计数器；None 表示永不重置 |
| `ResetThreshold` | `int` | `100` | 任意整数 | 与当前值比较的阈值，仅在 ResetCondition 非 None 时生效 |
| `ResetValue` | `int` | `0` | 任意整数 | 触发重置后变量被设为此值，随后再加 Delta |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
无输入端口。所有配置通过参数面板完成。

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `VariableName` | 变量名 | `String` | 本次操作的变量名称 |
| `PreviousValue` | 前值 | `Integer` | 递增前的变量值（`long` 类型） |
| `NewValue` | 新值 | `Integer` | 递增后的变量值 |
| `Delta` | 增量 | `Integer` | 本次实际应用的增量 |
| `WasReset` | 是否已重置 | `Boolean` | 本次执行是否触发了重置逻辑 |

> 注：运行时输出字典还包含 `CycleCount`（来自 `_variableContext.CycleCount`），未声明为独立输出端口。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) - 单次变量读取 + 单次条件判断 + 单次变量写入 |
| 典型耗时 (Typical Latency) | < 1 ms（纯内存操作，无 I/O） |
| 内存特征 (Memory Profile) | O(1) - 仅分配输出字典，无中间集合 |

## 适用场景 / Use Cases
- 适合 (Suitable)：流程循环计数，如统计检测次数、循环迭代索引
- 适合 (Suitable)：产品计数器，配合重置条件实现批次管理
- 适合 (Suitable)：递减场景（Delta 为负），如倒计时、剩余次数统计
- 适合 (Suitable)：与其他变量算子配合，实现跨算子的状态共享
- 不适合 (Not Suitable)：需要浮点精度的递增场景（当前仅支持 `long` 整数）
- 不适合 (Not Suitable)：高并发写入同一变量名的无锁原子操作（依赖 `IVariableContext` 实现的线程安全性）

## 已知限制 / Known Limitations
1. 变量值以 `long` 类型存储（`GetValue<long>`），不支持浮点递增；若需浮点计数请配合 `MathOperation` 算子。
2. 重置判断使用 `switch (resetCondition.ToLower())`，仅匹配 `greaterthan`/`lessthan`/`equal` 三种小写形式；不支持 `GreaterThanOrEqual` / `LessThanOrEqual`。
3. 重置时先写入 `resetValue + delta`（而非先写 resetValue 再 Increment），意味着重置后的新值 = ResetValue + Delta，而非 ResetValue。
4. 输出中的 `PreviousValue` 为 `long` 类型但端口声明为 `PortDataType.Integer`，下游若期望 `int` 可能需要类型转换。
5. `CycleCount` 出现在运行时输出中但未声明为 `[OutputPort]`，仅能通过输出字典访问。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确定位算法原理（IVariableContext 原子操作、重置-递增双路径）、完善参数范围与语义、补充输出端口 CycleCount 说明、明确 long 类型限制与重置边界行为 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
