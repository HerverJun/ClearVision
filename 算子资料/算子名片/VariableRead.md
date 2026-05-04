# 变量读取 / VariableRead

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VariableReadOperator` |
| 枚举值 (Enum) | `OperatorType.VariableRead` |
| 分类 (Category) | 变量 |
| 图标 (Icon) | `variable-read` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子从全局变量上下文 (`IVariableContext`) 中按名称读取变量值。支持四种数据类型（String、Int、Double、Bool），读取时自动将原始值转换为指定类型。若变量不存在，返回用户配置的默认值。同时通过 `IVariableContext.Contains` 检测变量是否存在，并将存在性作为独立输出端口暴露。

> English: This operator reads a named variable from the global variable context (`IVariableContext`). It supports four data types (String, Int, Double, Bool) and automatically converts the raw value to the specified type during retrieval. If the variable does not exist, the user-configured default value is returned. Existence is checked via `IVariableContext.Contains` and exposed as a dedicated output port.

## 实现策略 / Implementation Strategy
> 中文：算子无输入端口，纯参数驱动。通过 `DataType` 参数在 switch-case 中选择不同的泛型读取路径 (`GetValue<long>` / `GetValue<double>` / `GetValue<bool>` / `GetValue<string>`)。每条路径都尝试将 `DefaultValue` 参数解析为目标类型，解析失败则使用类型硬编码的零值（0L / 0.0 / false）。变量存在性独立于值读取，确保即使变量不存在也能返回有意义的默认值。

> English: The operator is purely parameter-driven with no input ports. A switch-case on the `DataType` parameter selects the appropriate generic retrieval path (`GetValue<long>` / `GetValue<double>` / `GetValue<bool>` / `GetValue<string>`). Each path attempts to parse the `DefaultValue` parameter into the target type, falling back to a type-specific zero value (0L / 0.0 / false) on failure. Existence checking is independent of value retrieval, ensuring meaningful defaults even for missing variables.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "VariableName")` - 读取目标变量名
2. `GetStringParam(@operator, "DefaultValue")` - 读取默认值字符串
3. `GetStringParam(@operator, "DataType")` - 读取目标数据类型
4. `_variableContext.Contains(variableName)` - 检测变量是否存在
5. `_variableContext.GetValue<T>(variableName, parsedDefault)` - 按指定类型读取值（T = long / double / bool / string）
6. 返回 `OperatorExecutionOutput.Success(...)` 包含 Value, VariableName, Exists, CycleCount

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `VariableName` | `string` | `""` | 非空字符串 | 要读取的变量名称，对应 `IVariableContext` 中的键 |
| `DefaultValue` | `string` | `"0"` | 任意字符串 | 变量不存在时返回的默认值（会按 DataType 转换） |
| `DataType` | `enum` | `"String"` | `String` / `Int` / `Double` / `Bool` | 读取时的目标数据类型；别名 `Integer`/`Float`/`Boolean` 亦可 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
无输入端口。所有配置通过参数面板完成。

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Value` | 值 | `Any` | 读取到的变量值（已转换为 DataType 指定的类型） |
| `Exists` | 是否存在 | `Boolean` | 变量是否存在于全局上下文中 |
| `CycleCount` | 循环计数 | `Integer` | 当前全局上下文的循环计数 |

> 注：运行时输出字典还包含 `VariableName`（字符串），未声明为独立输出端口。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) - 单次字典查找 + 类型转换 |
| 典型耗时 (Typical Latency) | < 1 ms（纯内存操作） |
| 内存特征 (Memory Profile) | O(1) - 仅分配输出字典 |

## 适用场景 / Use Cases
- 适合 (Suitable)：从全局变量表读取共享状态，如产品计数、批次号、累计结果
- 适合 (Suitable)：配合 `VariableWriteOperator` 实现跨算子数据传递
- 适合 (Suitable)：读取配置参数化的运行时变量，支持不同类型自动转换
- 适合 (Suitable)：检查变量是否存在后再做条件分支（利用 `Exists` 输出）
- 不适合 (Not Suitable)：读取复杂的结构化对象（仅支持基础四类型）
- 不适合 (Not Suitable)：高频批量读取大量变量（应考虑缓存策略）

## 已知限制 / Known Limitations
1. `DefaultValue` 参数类型为 `string`，需在运行时解析为目标类型；若解析失败使用零值而非报错，可能导致静默数据错误。
2. `Int` 类型实际存储为 `long`（`GetValue<long>`），输出端口声明为 `PortDataType.Any`，下游需自行处理类型兼容。
3. `DataType` 的 switch-case 同时接受 `int`/`integer`、`double`/`float`、`bool`/`boolean`，但枚举声明中仅列出 `String`/`Int`/`Double`/`Bool`，用户可能不了解别名支持。
4. `VariableName` 为空或纯空白时直接返回 Failure，但不会 trim 后再判断，首尾空格可能导致意外的空名称错误。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确定位算法原理（IVariableContext 泛型读取、四类型 switch-case 路径）、明确 long 存储与 Any 端口的类型差异、补充别名支持说明、分析 DefaultValue 解析失败的静默降级行为 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
