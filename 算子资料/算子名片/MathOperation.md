# 数值计算 / MathOperation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MathOperationOperator` |
| 枚举值 (Enum) | `OperatorType.MathOperation` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `calc` |
| 关键词 (Keywords) | 计算, 数学, 加减乘除, 数值, 判断大小, 运算, Math, Calculate, Add, Subtract, Multiply, Divide |

## 算法原理 / Algorithm Principle
> **中文：** 对 1~2 个数值输入执行 11 种数学运算：
> 加(Add)、减(Subtract)、乘(Multiply)、除(Divide)、绝对值(Abs)、取小(Min)、取大(Max)、
> 幂运算(Power)、平方根(Sqrt)、取整(Round)、取余(Modulo)。
>
> 单操作数运算（Abs/Sqrt/Round）仅使用 ValueA；双操作数运算需要 ValueA 和 ValueB。
> 所有输入通过 `TryConvertToFiniteDouble` 统一转换，拒绝 NaN/Infinity。
> 运算结果若为非有限数（如除零溢出），直接返回失败。
>
> **English:** Performs 11 math operations on 1-2 numeric inputs:
> Add, Subtract, Multiply, Divide, Abs, Min, Max, Power, Sqrt, Round, Modulo.
>
> Single-operand operations (Abs/Sqrt/Round) use only ValueA; dual-operand operations require both.
> All inputs are normalized via `TryConvertToFiniteDouble`, rejecting NaN/Infinity.
> Non-finite results (e.g., division overflow) cause immediate failure.

## 实现策略 / Implementation Strategy
- `RequiresSecondOperand` 方法决定是否需要 ValueB 输入。
- 输入类型支持 double/float/byte/sbyte/short/ushort/int/uint/long/ulong/decimal/string/IFormattable。
- 除法和取余除数为 0 时抛出 `DivideByZeroException`；Sqrt 输入为负时抛出 `ArgumentException`。
- 结果校验 `double.IsFinite(result)`，确保不会输出 NaN/Infinity。
- 输出包含多种格式：`Result`(double)、`ResultFloat`(float)、`ResultInt`(int)、`IsPositive/IsZero/IsNegative`。

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "Operation", "Add")` -> `RequiresSecondOperand(operation)` 判断
2. `TryGetRequiredFiniteInputDouble(inputs, "ValueA", out valueA, ...)` -> 类型转换 + 有限数校验
3. 条件：`TryGetRequiredFiniteInputDouble(inputs, "ValueB", out valueB, ...)`
4. operation switch -> 执行数学运算 -> `double.IsFinite(result)` 校验
5. `OperatorExecutionOutput.Success(...)` -> Result, ResultFloat, ResultInt, IsPositive, IsZero, IsNegative, InputA, InputB, Operation

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `enum` | `"Add"` | `Add` / `Subtract` / `Multiply` / `Divide` / `Abs` / `Min` / `Max` / `Power` / `Sqrt` / `Round` / `Modulo` | 运算类型。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `ValueA` | 数值 A | `Float` | Yes | 第一操作数（所有运算均需要）。 |
| `ValueB` | 数值 B | `Float` | No | 第二操作数（Add/Subtract/Multiply/Divide/Min/Max/Power/Modulo 需要）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | 结果 | `Float` | 运算结果（double 精度）。 |
| `IsPositive` | 大于零 | `Boolean` | 结果是否大于 0。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `ResultFloat` | `Float` | 结果的 float 精度版本。 |
| `ResultInt` | `Integer` | 结果的 int 截断版本。 |
| `IsZero` | `Boolean` | 结果是否等于 0。 |
| `IsNegative` | `Boolean` | 结果是否小于 0。 |
| `InputA` | `Float` | 实际使用的 ValueA 值。 |
| `InputB` | `Float` | 实际使用的 ValueB 值。 |
| `Operation` | `String` | 实际执行的运算名称。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)（常数时间算术运算） |
| 典型耗时 (Typical Latency) | < 0.1ms |
| 内存特征 (Memory Profile) | O(1)（仅存储输入输出标量） |

## 适用场景 / Use Cases
- 适合 (Suitable)：流水线中的加减乘除基础运算
- 适合 (Suitable)：判断数值正负、取绝对值、开方等常用数学操作
- 适合 (Suitable)：配合 Comparator 算子实现阈值判断
- 不适合 (Not Suitable)：矩阵运算或向量运算（请使用专用线性代数算子）
- 不适合 (Not Suitable)：需要高精度十进制运算的金融场景（内部使用 double）
- 不适合 (Not Suitable)：需要三角函数、对数等高级数学函数的场景

## 已知限制 / Known Limitations
1. 除法和取余运算除数为 0 时返回失败而非返回 Infinity/NaN。
2. Sqrt 输入为负数时返回失败而非返回 NaN。
3. `Round` 使用 `Math.Round`（银行家舍入法），非四舍五入。
4. 内部使用 double 精度，大整数运算可能有精度损失。
5. 单操作数运算（Abs/Sqrt/Round）忽略 ValueB 输入，不产生警告。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 11 种运算详细行为、输入类型转换、运行时附加输出 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 |
