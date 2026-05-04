# 结果输出 / ResultOutput

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ResultOutputOperator` |
| 枚举值 (Enum) | `OperatorType.ResultOutput` |
| 分类 (Category) | 输出 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子是一个**结果汇聚与格式化输出**节点，负责将流程中多个上游算子的检测结果汇总为统一格式并透传给下游。核心思想是：收集所有输入端口（`Image`、`Result`、`Text`、`Data`）的数据，合并为一个输出字典，然后根据 `Format` 参数（JSON/CSV/Text）将非图像数据序列化为文本。当 `SaveToFile=true` 时，序列化结果会写入临时目录并输出文件路径。图像数据在序列化时被降维为 `{Width, Height, Channels}` 结构体，`DetectionList` 和 `DetectionResult` 等领域对象也会被递归归一化为纯数值字典。

> English: This operator is a **result aggregation and formatted output** node that collects detection results from multiple upstream operators, merges them into a unified dictionary, and serializes non-image data to JSON, CSV, or Text format. When `SaveToFile=true`, the formatted output is written to a temp directory. Image data is normalized to `{Width, Height, Channels}` during serialization; domain objects like `DetectionList` and `DetectionResult` are recursively flattened to plain dictionaries.

## 实现策略 / Implementation Strategy
- **输入透传优先**：所有输入端口的数据先按 key 存入输出字典，再做格式化。`ImageWrapper` 类型通过 `AddRef()` 增加引用计数，确保算子生命周期结束后图像仍可用。
- **格式化排除图像**：`BuildFormattedOutput` 在序列化时跳过 `Image`、`Output`、`FilePath` 三个 key，避免将大图像数据嵌入 JSON/CSV。
- **领域对象归一化**：`NormalizeForExport` 递归处理 `ImageWrapper`、`DetectionList`、`DetectionResult`、`Position`、`IDictionary`、`IEnumerable` 等类型，统一转为纯数值结构。
- **CSV 注入防护**：`EscapeCsv` 对以 `=`、`+`、`-`、`@` 开头的值加 `'` 前缀，防止 Excel 公式注入。
- **文件保存容错**：`SaveFormattedOutput` 失败时不抛异常，而是将错误信息写入 `SaveError` 字段，确保结果输出算子不会阻断流程。
- **Output 兜底**：若格式化结果为空，依次尝试 `Result` -> `Data` -> `Text` -> `Image` 作为 `Output` 端口值，最终兜底为空字符串。

> English: The implementation prioritizes input passthrough with reference counting, excludes images from serialization, normalizes domain objects recursively, protects against CSV injection, tolerates file-save failures gracefully, and falls back through multiple keys when the formatted output is empty.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "Format", "JSON")` -- 获取输出格式
2. `GetBoolParam(@operator, "SaveToFile", false)` -- 获取是否落盘
3. `inputs.TryGetValue("Image"/"Result"/"Text"/"Data", ...)` -- 收集各端口输入
4. `PreserveOutputValue(image)` -- `ImageWrapper.AddRef()` 增加引用计数
5. `BuildFormattedOutput(output, format)` -- 格式化主逻辑
   - `NormalizeForExport(value)` -- 递归归一化领域对象
   - `BuildCsv(exportPayload)` -- CSV 构建（含 `EscapeCsv` 注入防护）
   - `JsonSerializer.Serialize(...)` -- JSON 序列化
6. `SaveFormattedOutput(formattedText, format)` -- 可选落盘
7. `File.WriteAllText(filePath, formattedText, Encoding.UTF8)` -- 写入 UTF-8 文件

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Format` | `enum` | `"JSON"` | JSON / CSV / Text | 输出格式。JSON 为带缩进的 JSON 字符串；CSV 为 `Key,Value` 两列表格；Text 为 `Key: Value` 逐行文本。 |
| `SaveToFile` | `bool` | `false` | - | 是否将格式化结果保存到临时文件。文件路径输出到 `FilePath` 端口。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | No | 上游图像。透传到输出端口，不参与格式化序列化。 |
| `Result` | 结果 | `Any` | No | 上游检测结果。可为 `DetectionList`、`DetectionResult` 等。 |
| `Text` | 文本 | `String` | No | 上游文本数据。 |
| `Data` | 数据 | `Any` | No | 上游任意数据。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Output` | 输出 | `Any` | 汇总后的格式化文本（JSON/CSV/Text），或当无格式化结果时的兜底值。 |
| `Image` | 图像 | `Image` | 透传的输入图像（引用计数已增加）。 |
| `Result` | 结果 | `Any` | 透传的输入结果。 |
| `Text` | 文本 | `String` | 透传的输入文本，或格式化后的文本。 |
| `Data` | 数据 | `Any` | 透传的输入数据。 |
| `FilePath` | 文件路径 | `String` | `SaveToFile=true` 时的临时文件路径。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `SaveError` | `String` | 文件保存失败时的错误信息。仅在 `SaveToFile=true` 且写盘失败时出现。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(N)`，其中 `N` 为输入字典的键值对数量。JSON/CSV 序列化时间与数据量线性相关。 |
| 典型耗时 (Typical Latency) | 通常 < 1ms（纯透传 + 格式化），文件保存时额外增加 1-5ms（取决于磁盘 I/O）。 |
| 内存特征 (Memory Profile) | 峰值内存为输入数据总大小 + 序列化文本缓冲区。图像数据通过引用计数共享，不复制像素。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：流程末端的结果汇总节点，将多个检测算子的输出统一格式化后记录。
- **适合 (Suitable)**：需要将检测结果保存为 JSON/CSV 文件供外部系统消费。
- **适合 (Suitable)**：调试阶段快速查看流程各节点的输出数据。
- **适合 (Suitable)**：作为流程的"终点"节点，透传所有数据给下游的同时生成格式化报告。
- **不适合 (Not Suitable)**：需要实时流式输出的场景（如 WebSocket 推送）。
- **不适合 (Not Suitable)**：大规模数据的持久化存储（当前仅写入临时目录）。

## 已知限制 / Known Limitations
1. 文件保存路径为系统临时目录（`%TEMP%/Acme.Product/result-output/`），不支持自定义保存路径。
2. CSV 格式为简单 `Key,Value` 两列，不支持嵌套对象的展平为多行。
3. 图像数据在格式化输出中被降维为 `{Width, Height, Channels}`，不包含像素数据。
4. `SaveToFile` 失败时静默降级（仅记录 `SaveError`），不会中断流程。
5. `DetectionList` 序列化时会递归调用 `NormalizeForExport`，对非常大的检测列表可能产生较多临时对象。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充格式化逻辑、领域对象归一化、CSV 注入防护、文件保存容错、Output 兜底策略等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
