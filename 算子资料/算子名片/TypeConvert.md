# 类型转换 / TypeConvert

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TypeConvertOperator` |
| 枚举值 (Enum) | `OperatorType.TypeConvert` |
| 分类 (Category) | General |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `convert` |

## 算法原理 / Algorithm Principle
> **中文：** 将输入值安全转换为 4 种目标类型：String、Float、Integer、Boolean。
> 同时输出所有 4 种类型的转换结果，方便下游按需取用。
>
> 转换规则：
> - **String**：支持 `IFormattable` 的 `Format` 参数（如 `"F2"`、`"0.00"`）
> - **Float**：直接类型匹配优先，否则 `float.TryParse`（InvariantCulture）
> - **Integer**：直接类型匹配优先，否则 `int.TryParse`（截断 float/double）
> - **Boolean**：`bool` 直传；数值类型非零为 true；字符串先试 `bool.TryParse`，再试数值解析，非空非零为 true
>
> **English:** Safely converts input values to 4 target types: String, Float, Integer, Boolean.
> All 4 conversion results are output simultaneously for downstream convenience.
>
> Conversion rules:
> - **String**: supports `IFormattable.Format` parameter
> - **Float**: direct type match first, then `float.TryParse`
> - **Integer**: direct type match first, then `int.TryParse` (truncates float/double)
> - **Boolean**: `bool` direct; numeric non-zero = true; string tries `bool.TryParse` then numeric parse

## 实现策略 / Implementation Strategy
- 输入通过 `TryReadInputValue` 读取，仅支持 `Input` 键。
- 4 种转换并行执行，结果全部输出，`TargetType` 参数仅决定 `Output` 端口的值。
- String 转换时若 `Format` 非空且值实现 `IFormattable`，使用 `ToString(format, InvariantCulture)`。
- Float/Integer 转换使用模式匹配优先处理已知类型（float/double/int/bool），避免不必要的字符串解析。
- Boolean 转换的 `TryParseBoolean` 回退逻辑：空值=false，`bool.TryParse` 失败后试 `double.TryParse`，再失败则非空=true。

## 核心 API 调用链 / Core API Call Chain
1. `TryReadInputValue(inputs, out value)` -> 读取 `Input` 端口
2. `GetStringParam(@operator, "Format", "")` + `GetStringParam(@operator, "TargetType", "String")`
3. `ConvertToString(value, format)` -> `IFormattable.ToString(format, InvariantCulture)` 或 `.ToString()`
4. `ConvertToFloat(value)` -> 模式匹配 -> `float.TryParse`
5. `ConvertToInteger(value)` -> 模式匹配 -> `int.TryParse`
6. `ConvertToBoolean(value)` -> 模式匹配 -> `TryParseBoolean`
7. `outputValue = targetType switch { "string" => asString, "float" => asFloat, ... }`
8. `OperatorExecutionOutput.Success(...)` -> Output, AsString, AsFloat, AsInteger, AsBoolean, OriginalType

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `TargetType` | `enum` | `"String"` | `String` / `Float` / `Integer` / `Boolean` | Output 端口的目标类型。 |
| `Format` | `string` | `""` | - | String 转换时的格式化字符串（如 `"F2"`、`"0.00"`），仅对 `IFormattable` 有效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Input` | Input | `Any` | Yes | 待转换的输入值。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Output` | Output | `Any` | 按 TargetType 转换后的主输出。 |
| `AsString` | As String | `String` | 输入值的字符串表示。 |
| `AsFloat` | As Float | `Float` | 输入值的 float 表示（转换失败时为 0f）。 |
| `AsInteger` | As Integer | `Integer` | 输入值的 int 表示（转换失败时为 0）。 |
| `AsBoolean` | As Boolean | `Boolean` | 输入值的布尔表示。 |
| `OriginalType` | Original Type | `String` | 输入值的原始 CLR 类型名。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)（常数时间类型转换） |
| 典型耗时 (Typical Latency) | < 0.1ms |
| 内存特征 (Memory Profile) | O(1)（仅存储输入输出标量） |

## 适用场景 / Use Cases
- 适合 (Suitable)：上下游端口类型不匹配时的安全桥接
- 适合 (Suitable)：需要同时获取多种类型表示的场景（如日志记录）
- 适合 (Suitable)：数值到字符串的格式化输出（配合 Format 参数）
- 不适合 (Not Suitable)：复杂对象的序列化/反序列化
- 不适合 (Not Suitable)：需要自定义转换逻辑的场景（固定 4 种类型）
- 不适合 (Not Suitable)：批量数据的类型转换（每次处理单个值）

## 已知限制 / Known Limitations
1. Float 转换失败时静默返回 0f，不报错。
2. Integer 转换对 float/double 使用截断（`(int)f`），非四舍五入。
3. Boolean 转换的字符串解析：空字符串=false，非空非 "false"/"0"=true（可能不符合预期）。
4. `Format` 参数仅对实现 `IFormattable` 的类型有效，其他类型忽略此参数。
5. `Output` 端口的类型是 `object`，下游需自行处理装箱/拆箱。
6. 不支持枚举类型、DateTime 等特殊类型的转换。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 4 种类型转换规则、Format 参数、OriginalType 输出 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 |
