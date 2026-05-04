# 循环计数器 / CycleCounter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CycleCounterOperator` |
| 枚举值 (Enum) | `OperatorType.CycleCounter` |
| 分类 (Category) | 变量 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | cycle |

## 算法原理 / Algorithm Principle

**中文：**
循环计数器算子通过 `IVariableContext` 读取、递增或重置全局循环计数器，并输出计数统计信息。核心算法：

1. **操作分派**：`Action` 参数支持三种操作模式 —— Read（读取当前计数）、Reset（重置为 0）、Increment（递增 1）。
2. **计数器状态管理**：计数器存储在 `IVariableContext.CycleCount` 中（long 类型），为流程级全局状态。Read 仅读取；Reset 调用 `ResetCycleCount()` 清零；Increment 调用 `IncrementCycleCount()` 加 1。
3. **上限检查**：`MaxCycles` 参数设置最大循环次数（0 表示无限制）。Increment 操作前检查 `currentCount >= maxCycles`，达到上限时不再递增，设置 `IsLimitReached = true`。
4. **溢出保护**：Increment 操作检查 `currentCount == long.MaxValue` 和溢出后负值，防止 long 溢出。
5. **派生输出**：
   - `RemainingCycles` = maxCycles > 0 ? max(0, maxCycles - currentCount) : -1（-1 表示无限制）
   - `Progress` = maxCycles > 0 ? min(100, currentCount / maxCycles * 100) : 0（百分比进度）

**English:**
A cycle counter operator that reads, increments, or resets a global cycle counter via `IVariableContext`, and outputs counting statistics. Core algorithm:

1. **Action dispatch**: The `Action` parameter supports three modes — Read (read current count), Reset (reset to 0), Increment (add 1).
2. **Counter state management**: The counter is stored in `IVariableContext.CycleCount` (long type) as a process-level global state. Read only reads; Reset calls `ResetCycleCount()` to clear; Increment calls `IncrementCycleCount()` to add 1.
3. **Limit check**: The `MaxCycles` parameter sets the maximum cycle count (0 = unlimited). Increment checks `currentCount >= maxCycles` before incrementing; when the limit is reached, no further increment occurs and `IsLimitReached = true`.
4. **Overflow protection**: Increment checks for `currentCount == long.MaxValue` and negative values after overflow to prevent long overflow.
5. **Derived outputs**:
   - `RemainingCycles` = maxCycles > 0 ? max(0, maxCycles - currentCount) : -1 (-1 = unlimited)
   - `Progress` = maxCycles > 0 ? min(100, currentCount / maxCycles * 100) : 0 (percentage)

## 实现策略 / Implementation Strategy

- **IVariableContext 依赖注入**：计数器状态通过 DI 容器注入的 `IVariableContext` 管理，保证跨算子共享同一计数器实例。
- **操作归一化**：`NormalizeAction` 将 Action 参数 trim + toLower，使 `"Read"`、`"read"`、`" READ "` 等写法均等效。
- **防御性编程**：多次校验 maxCycles >= 0、long.MaxValue 溢出、溢出后负值检测。
- **同步执行**：纯内存操作，无 I/O，`ExecuteCoreAsync` 通过 `Task.FromResult` 同步返回。
- **诊断日志**：Reset/Increment 操作通过 `Logger.LogInformation` 记录，Read 操作通过 `Logger.LogDebug` 记录。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync
  ├── GetStringParam("Action") → NormalizeAction()    // 获取并归一化操作
  │   └── action.Trim().ToLowerInvariant()
  ├── IsSupportedAction(action) ?                      // 校验操作合法性
  ├── GetParam("MaxCycles", 0)                         // 获取最大循环次数
  ├── maxCycles < 0 ? → Failure                        // 校验非负
  ├── currentCount = _variableContext.CycleCount        // 读取当前计数
  ├── isLimitReached = maxCycles > 0 && currentCount >= maxCycles
  └── action switch
      ├── "reset" →
      │   ├── _variableContext.ResetCycleCount()
      │   └── currentCount = 0
      ├── "increment" →
      │   ├── maxCycles > 0 && currentCount >= maxCycles ? → skip (IsLimitReached=true)
      │   ├── currentCount == long.MaxValue ? → Failure (overflow)
      │   ├── _variableContext.IncrementCycleCount()
      │   ├── currentCount = _variableContext.CycleCount
      │   ├── currentCount < 0 ? → Failure (overflow detected)
      │   └── isLimitReached = maxCycles > 0 && currentCount >= maxCycles
      └── "read" → (no-op, just read)
  └── output = {CycleCount, MaxCycles, IsLimitReached, RemainingCycles, Progress}
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Action` | `enum` | `"Read"` | Read / Reset / Increment | 操作模式。Read 仅读取当前计数；Reset 将计数器清零；Increment 将计数器加 1（达到上限时不再递增） |
| `MaxCycles` | `int` | `0` | [0, +inf) | 最大循环次数限制。0 表示无限制（永不触发 IsLimitReached）。达到上限后 Increment 操作不再递增 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| （无输入端口） | - | - | - | 本算子无输入端口，计数器状态通过 IVariableContext 全局管理 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `CycleCount` | 当前次数 | `Integer` | 当前循环计数值（long 类型），Reset 后为 0，Increment 后加 1 |
| `MaxCycles` | 最大次数 | `Integer` | 配置的最大循环次数限制，0 表示无限制 |
| `IsLimitReached` | 是否达到限制 | `Boolean` | 是否已达到 MaxCycles 限制。MaxCycles=0 时始终为 false |
| `RemainingCycles` | 剩余次数 | `Integer` | 剩余可循环次数。MaxCycles=0 时输出 -1（表示无限制） |
| `Progress` | 进度(%) | `Float` | 循环进度百分比（0-100）。MaxCycles=0 时输出 0 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 单次读取/递增/重置操作 |
| 典型耗时 (Typical Latency) | < 0.1ms，同步执行无 I/O |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典（5 个键值对）。计数器本身存储在 IVariableContext 中 |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- 循环批次控制：配合 ForEach 或循环流程，跟踪当前执行到第几个循环
- 生产计数：记录产品经过检测站的次数，达到 MaxCycles 后触发维护提醒
- 进度监控：通过 Progress 输出实时显示循环进度（如 PLC HMI 显示）
- 循环终止条件：配合 ConditionalBranch 使用 IsLimitReached 作为循环退出条件
- 设备寿命管理：记录设备动作次数，达到上限时触发保养流程

**不适合 (Not Suitable)：**
- 嵌套循环计数：当前仅支持单一全局计数器，不支持多级嵌套循环独立计数
- 并发安全的计数：多线程同时 Increment 可能存在竞态条件（取决于 IVariableContext 实现）
- 浮点计数或步长不为 1 的计数：仅支持整数 +1 递增

## 已知限制 / Known Limitations

1. **单例全局计数器**：`IVariableContext.CycleCount` 是流程级全局状态，多个 CycleCounter 算子共享同一计数器，无法独立计数。
2. **无输入端口**：算子没有输入端口，无法接收外部信号来触发 Reset/Increment，必须通过 Action 参数静态配置操作类型。
3. **MaxCycles 为 int 类型**：参数声明为 `int`，但 CycleCount 内部为 `long`，当 MaxCycles 需要超过 int.MaxValue 时无法配置。
4. **RemainingCycles 类型混合**：MaxCycles > 0 时输出 `long`（Math.Max 返回 long），MaxCycles = 0 时输出 `int` (-1)，下游类型处理需注意。
5. **Increment 并发安全未保证**：代码中 `_variableContext.IncrementCycleCount()` 和 `_variableContext.CycleCount` 是两步操作，非原子性，多线程场景可能有竞态。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充 IVariableContext 依赖注入机制、操作归一化、溢出保护、RemainingCycles/Progress 派生计算公式；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
