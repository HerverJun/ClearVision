# JSON 提取器 / JsonExtractor

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `JsonExtractorOperator` |
| 枚举值 (Enum) | `OperatorType.JsonExtractor` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `json` |

## 算法原理 / Algorithm Principle
> **中文：** 按简化的 JSONPath 表达式从 JSON 字符串中提取字段值。
> 支持 `$.a.b.c` 形式的属性访问和 `$.items[0]` 形式的数组下标访问。
> 提取后可选将值转换为目标类型（String/Float/Double/Integer/Boolean）。
> 未命中路径时根据 `Required` 参数决定返回默认值或直接失败。
>
> **English:** Extracts field values from JSON strings using simplified JSONPath expressions.
> Supports `$.a.b.c` property access and `$.items[0]` array index access.
> Optionally converts extracted values to target types (String/Float/Double/Integer/Boolean).
> When the path is not found, returns default value or fails based on the `Required` parameter.

## 实现策略 / Implementation Strategy
- 使用 `System.Text.Json.Nodes.JsonNode.Parse` 解析 JSON 字符串。
- JSONPath 解析：去除 `$` 前缀 -> 按 `.` 分割 segments -> 逐段遍历。
- 数组下标支持 `segment[index]` 语法，通过 `int.TryParse` 解析索引。
- 类型转换通过 `TryConvertToOutputType` 实现，使用 `CultureInfo.InvariantCulture` 解析。
- `Required=true` 且路径未命中时直接返回失败；`Required=false` 时返回 `DefaultValue`（需可转换为目标类型）。

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("Json", out jsonObj)` -> `jsonObj.ToString()` -> 获取 JSON 字符串
2. `GetStringParam(@operator, "JsonPath", "$.data")` + `OutputType` + `DefaultValue` + `GetBoolParam("Required")`
3. `JsonNode.Parse(jsonString)` -> 解析为 DOM 树
4. `ExtractValue(rootNode, jsonPath)` -> 按 path segments 逐段遍历
5. 路径命中：`TryConvertToOutputType(extractedValue, outputType, out result)` -> 类型转换
6. 路径未命中 + Required=false：`TryConvertToOutputType(defaultValue, outputType, out defaultResult)`
7. `OperatorExecutionOutput.Success(...)` -> Value, IsSuccess

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `JsonPath` | `string` | `"$.data"` | 非空 | JSONPath 表达式，支持 `$.a.b` 属性访问和 `$.items[0]` 数组下标。 |
| `OutputType` | `string` | `"Any"` | `Any` / `String` / `Float` / `Double` / `Integer` / `Int` / `Boolean` / `Bool` | 输出值的目标类型。 |
| `DefaultValue` | `string` | `""` | - | 路径未命中且 `Required=false` 时的默认值（需可转换为目标类型）。 |
| `Required` | `bool` | `false` | - | 路径未命中时是否直接失败。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Json` | JSON字符串 | `String` | Yes | 待提取的 JSON 字符串。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Value` | 提取值 | `Any` | 提取并转换后的值（未命中时为默认值或空字符串）。 |
| `IsSuccess` | 是否命中路径 | `Boolean` | JSONPath 是否成功命中。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(d)，d 为 JSONPath 深度（通常 < 10 层） |
| 典型耗时 (Typical Latency) | < 1ms（常规 JSON 文档） |
| 内存特征 (Memory Profile) | O(n) 存储解析后的 JsonNode DOM 树（n 为 JSON 文档大小） |

## 适用场景 / Use Cases
- 适合 (Suitable)：从外部 API 响应中提取特定字段
- 适合 (Suitable)：从配置 JSON 中读取参数值
- 适合 (Suitable)：需要类型安全转换的 JSON 字段提取
- 不适合 (Not Suitable)：需要复杂 JSONPath 表达式（通配符 `*`、递归 descent `..`、过滤器 `[?(@.x>1)]`）
- 不适合 (Not Suitable)：大型 JSON 文档的批量提取（每次调用重新解析整个文档）
- 不适合 (Not Suitable)：需要 JSON Schema 验证的场景

## 已知限制 / Known Limitations
1. JSONPath 语法仅支持属性访问（`.key`）和数组下标（`[n]`），不支持通配符、过滤器或递归下降。
2. 数组下标越界时返回 null（不报错），配合 `Required=true` 才会失败。
3. `DefaultValue` 为字符串类型，需能被转换为目标 `OutputType`，否则返回失败。
4. 每次执行都重新解析整个 JSON 文档，无缓存机制。
5. `OutputType=Any` 时返回原始 `JsonNode` 对象，下游需自行处理类型。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 JSONPath 解析细节、类型转换规则、运行时行为说明 |
| 1.1.0 | 2026-04-12 | 严格收口到正式契约，删除旧别名依赖，统一输出键为 `Value/IsSuccess` |
