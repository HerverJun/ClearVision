# 文本保存 / TextSave

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TextSaveOperator` |
| 枚举值 (Enum) | `OperatorType.TextSave` |
| 分类 (Category) | Logic Tools（逻辑工具） |
| 图标 (Icon) | `save-text` |
| 关键词 (Keywords) | `save text`, `export csv`, `log`, `json export` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子将文本或结构化数据写入文件系统，支持三种输出格式（Text/CSV/JSON）。执行时解析文件路径模板中的 `{date}` 和 `{time}` 占位符为当前时间戳，根据格式选择序列化策略：Text 模式直接输出文本；CSV 模式对集合元素逐个进行 CSV 单元格转义（处理逗号、引号、换行符）；JSON 模式使用 `System.Text.Json.JsonSerializer` 序列化。文件写入通过 `ConcurrentDictionary<string, object>` 实现按路径粒度的线程安全锁。

> English: This operator writes text or structured data to the file system in three formats (Text/CSV/JSON). It resolves `{date}` and `{time}` placeholders in the file path template to the current timestamp. Serialization strategy is format-dependent: Text mode outputs raw text; CSV mode applies per-cell escaping (handling commas, quotes, newlines); JSON mode uses `System.Text.Json.JsonSerializer`. Thread-safe file writing is achieved via a `ConcurrentDictionary<string, object>` providing per-path locking.

## 实现策略 / Implementation Strategy
> 中文：算子有两个可选输入端口 `Data` 和 `Text`。路径解析使用简单的字符串替换（`{date}` -> `yyyyMMdd`，`{time}` -> `HHmmss`）后调用 `Path.GetFullPath`。内容构建时优先从 `inputs["Text"]` 获取文本，从 `inputs["Data"]` 获取结构化数据。可选地在内容前添加 `[yyyy-MM-dd HH:mm:ss]` 时间戳前缀。写入前自动创建目录（`Directory.CreateDirectory`）。编码支持 UTF-8 和 GBK（代码页 936）。线程安全通过按文件路径哈希获取锁对象实现。

> English: The operator has two optional input ports `Data` and `Text`. Path resolution uses simple string replacement (`{date}` -> `yyyyMMdd`, `{time}` -> `HHmmss`) followed by `Path.GetFullPath`. Content building prioritizes `inputs["Text"]` for text and `inputs["Data"]` for structured data. An optional `[yyyy-MM-dd HH:mm:ss]` timestamp prefix can be prepended. Directories are auto-created before writing (`Directory.CreateDirectory`). Encoding supports UTF-8 and GBK (code page 936). Thread safety is achieved by locking on a per-file-path hash object.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "FilePath")` - 读取文件路径模板
2. `GetStringParam(@operator, "Format")` - 读取输出格式
3. `GetBoolParam(@operator, "AppendMode")` / `GetBoolParam(@operator, "AddTimestamp")` - 读取追加模式和时间戳标志
4. `GetStringParam(@operator, "Encoding")` - 读取编码
5. `ResolvePath(filePathTemplate)` - 解析 `{date}`/`{time}` 占位符，返回绝对路径
6. `BuildContent(format, inputs)` - 按格式构建内容字符串
7. `ResolveEncoding(encodingName)` - 返回 UTF-8 或 GBK 编码
8. `WriteContentThreadSafe(filePath, content, appendMode, encoding)` - 线程安全写入
9. 返回 `OperatorExecutionOutput.Success(...)` 包含 FilePath, Success

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FilePath` | `file` | `"output_{date}_{time}.txt"` | 有效文件路径 | 输出文件路径模板；`{date}` 展开为 `yyyyMMdd`，`{time}` 展开为 `HHmmss` |
| `Format` | `enum` | `"Text"` | `Text` / `CSV` / `JSON` | 输出文件格式 |
| `AppendMode` | `bool` | `true` | true / false | true 追加写入；false 覆盖写入 |
| `AddTimestamp` | `bool` | `true` | true / false | 是否在每行内容前添加 `[yyyy-MM-dd HH:mm:ss]` 时间戳 |
| `Encoding` | `enum` | `"UTF8"` | `UTF8` / `GBK` | 文件编码；GBK 使用代码页 936 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | Data | `Any` | No | 结构化数据输入（JSON/CSV 序列化源） |
| `Text` | Text | `String` | No | 纯文本输入；Text 格式下优先使用 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `FilePath` | File Path | `String` | 实际写入的文件绝对路径 |
| `Success` | Success | `Boolean` | 写入是否成功 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n) - n 为内容长度（序列化 + 写入） |
| 典型耗时 (Typical Latency) | 取决于磁盘 I/O 和内容大小；小文件 < 10 ms |
| 内存特征 (Memory Profile) | O(n) - 内容字符串的完整副本；JSON 序列化可能产生额外中间对象 |
| 线程安全 | 按文件路径粒度加锁（`ConcurrentDictionary` + `lock`） |

## 适用场景 / Use Cases
- 适合 (Suitable)：将检测结果保存为 CSV 报告文件
- 适合 (Suitable)：将流程日志以文本追加模式写入日志文件
- 适合 (Suitable)：将结构化数据导出为 JSON 格式
- 适合 (Suitable)：按日期自动命名的输出文件（利用 `{date}`/`{time}` 模板）
- 不适合 (Not Suitable)：二进制文件写入（仅支持文本编码）
- 不适合 (Not Suitable)：需要行级锁的高并发写入（当前为文件级锁）

## 已知限制 / Known Limitations
1. GBK 编码通过 `Encoding.GetEncoding(936)` 获取，若运行环境不支持代码页 936 会静默降级为 UTF-8。
2. CSV 序列化仅支持一维集合（`IEnumerable<object>`），不支持嵌套对象的自动展平。
3. JSON 序列化使用默认 `JsonSerializerOptions { WriteIndented = true }`，不支持自定义序列化选项。
4. 文件路径模板仅支持 `{date}` 和 `{time}` 两个占位符，不支持自定义变量。
5. 追加模式下的时间戳前缀添加在 `BuildContent` 返回值之前，意味着 JSON 格式追加时每行都有时间戳前缀，会破坏 JSON 结构。
6. 线程安全锁使用 `ConcurrentDictionary.GetOrAdd`，在极端并发下可能出现同一路径的锁对象重复创建（`GetOrAdd` 的 valueFactory 不保证只执行一次）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确描述线程安全机制（ConcurrentDictionary 按路径锁）、CSV 单元格转义逻辑、GBK 代码页 936 降级行为、发现 JSON 追加模式下时间戳破坏结构的问题 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
