# 变量写入 / VariableWrite

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VariableWriteOperator` |
| 枚举值 (Enum) | `OperatorType.VariableWrite` |
| 分类 (Category) | 变量 |
| 图标 (Icon) | `variable-write` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子向全局变量上下文 (`IVariableContext`) 写入一个命名变量。值的来源支持两种模式：优先使用上游输入端口的值（`UseInputValue = true`），或使用参数面板中配置的静态值。写入前根据 `DataType` 参数将值转换为对应的基础类型（String/Int/Double/Bool），然后通过 `IVariableContext.SetValue` 持久化到全局状态中。

> English: This operator writes a named variable into the global variable context (`IVariableContext`). The value source supports two modes: prefer upstream input port value (`UseInputValue = true`), or use the static value from the parameter panel. Before writing, the value is converted to the target primitive type (String/Int/Double/Bool) based on the `DataType` parameter, then persisted via `IVariableContext.SetValue`.

## 实现策略 / Implementation Strategy
> 中文：算子有一个可选输入端口 `Value`。执行时先读取 `UseInputValue` 标志：若为 true 且 inputs 非空，依次尝试从 `inputs["Value"]` 和 `inputs[variableName]` 获取上游值；若均未获取到则降级为静态值。静态值通过私有方法 `GetStaticValue` 按 DataType 进行类型解析（`long.TryParse` / `double.TryParse` / `bool.TryParse`），解析失败使用零值。最终通过 `IVariableContext.SetValue` 写入，支持 long / double / bool / string 四种存储类型。

> English: The operator has one optional input port `Value`. During execution, it reads the `UseInputValue` flag: if true and inputs is non-null, it tries `inputs["Value"]` then `inputs[variableName]` in order; falls back to the static value if neither is found. The static value is parsed by the private `GetStaticValue` method according to DataType (`long.TryParse` / `double.TryParse` / `bool.TryParse`), defaulting to zero on failure. Writing is done via `IVariableContext.SetValue`, supporting long / double / bool / string storage types.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "VariableName")` - 读取目标变量名
2. `GetStringParam(@operator, "DataType")` - 读取目标数据类型
3. `GetBoolParam(@operator, "UseInputValue")` - 判断值来源模式
4. `inputs.TryGetValue("Value", ...)` / `inputs.TryGetValue(variableName, ...)` - 尝试获取上游输入
5. `GetStaticValue(@operator, dataType)` - 私有方法，按类型解析静态值
6. `_variableContext.SetValue(variableName, convertedValue)` - 写入全局变量
7. 返回 `OperatorExecutionOutput.Success(...)` 包含 VariableName, Value, CycleCount

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `VariableName` | `string` | `""` | 非空字符串 | 要写入的变量名称 |
| `DataType` | `enum` | `"String"` | `String` / `Int` / `Double` / `Bool` | 写入时的数据类型；别名 `Integer`/`Float`/`Boolean` 亦可 |
| `UseInputValue` | `bool` | `true` | true / false | 为 true 时优先从上游输入端口读取值；为 false 时使用下方静态值 |
| `StaticValue` | `string` | `"0"` | 任意字符串 | 当没有上游输入或 UseInputValue 为 false 时使用的值（按 DataType 转换） |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | 值 | `Any` | No | 上游传入的待写入值；当 UseInputValue=true 时优先使用 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `VariableName` | 变量名 | `String` | 本次写入的变量名称 |
| `Value` | 写入的值 | `Any` | 实际写入变量表的值（已转换类型） |
| `CycleCount` | 循环计数 | `Integer` | 当前全局上下文的循环计数 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) - 单次输入查找 + 类型转换 + 字典写入 |
| 典型耗时 (Typical Latency) | < 1 ms（纯内存操作） |
| 内存特征 (Memory Profile) | O(1) - 仅分配输出字典 |

## 适用场景 / Use Cases
- 适合 (Suitable)：将上游算子的检测结果写入全局变量，供其他分支读取
- 适合 (Suitable)：初始化全局变量（配合 `UseInputValue=false` 和 `StaticValue`）
- 适合 (Suitable)：实现跨流程的累计/汇总逻辑（如总不良数、总检测时间）
- 适合 (Suitable)：配合 `VariableReadOperator` 实现算子间的状态传递
- 不适合 (Not Suitable)：写入复杂结构化对象（仅支持基础四类型）
- 不适合 (Not Suitable)：需要事务性写入多个变量的场景（每次只能写一个变量）

## 已知限制 / Known Limitations
1. 值来源的查找顺序固定为 `inputs["Value"]` -> `inputs[variableName]` -> 静态值，无法自定义优先级。
2. `StaticValue` 为 string 类型，需在运行时解析；解析失败时静默使用零值（0L / 0.0 / false），不报错。
3. Int 类型实际存储为 `long`（`Convert.ToInt64`），Double 使用 `Convert.ToDouble`，可能在极端值时溢出。
4. 当 `UseInputValue=true` 但上游未连接时，会静默降级到静态值，可能导致非预期行为。
5. 每次执行仅写入一个变量，无法在单次调用中原子地写入多个相关变量。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确描述值来源三级降级逻辑（inputs["Value"] -> inputs[variableName] -> StaticValue）、明确 GetStaticValue 私有方法的类型解析行为、补充 UseInputValue 静默降级风险说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
