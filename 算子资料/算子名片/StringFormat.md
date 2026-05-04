# 字符串格式化 / StringFormat

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `StringFormatOperator` |
| 枚举值 (Enum) | `OperatorType.StringFormat` |
| 分类 (Category) | 通用 |
| 图标 (Icon) | `text` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子提供三种字符串生成模式。**模板模式 (Template)**：使用正则表达式 `\{(\w+)\}` 替换模板中的 `{KeyName}` 占位符为对应输入值，再按顺序替换 `{0}`、`{1}` 等索引占位符。**拼接模式 (Join)**：将所有输入值用指定分隔符连接。**日期模式 (Date)**：使用 `DateTime.Now.ToString(format)` 生成当前时间字符串。

> English: This operator provides three string generation modes. **Template mode**: uses regex `\{(\w+)\}` to replace `{KeyName}` placeholders with corresponding input values, then sequentially replaces `{0}`, `{1}` index-based placeholders. **Join mode**: concatenates all input values with a specified separator. **Date mode**: generates the current timestamp string using `DateTime.Now.ToString(format)`.

## 实现策略 / Implementation Strategy
> 中文：算子有两个可选输入端口 `Arg1` 和 `Arg2`，但实际执行时从 `inputs` 字典中读取所有键值对。模板替换分两轮进行：第一轮用正则匹配 `{KeyName}` 并按输入字典的键查找替换；第二轮遍历输入字典的 Values 按顺序替换 `{0}`、`{1}` 索引占位符。对于无法匹配的占位符保留原样不做替换。Join 模式直接对 `inputs.Values` 调用 `string.Join`。

> English: The operator has two optional input ports `Arg1` and `Arg2`, but at runtime reads all key-value pairs from the `inputs` dictionary. Template replacement runs in two passes: first, regex matches `{KeyName}` and looks up the input dictionary by key; second, iterates input Values in order to replace `{0}`, `{1}` index placeholders. Unmatched placeholders are preserved as-is. Join mode directly calls `string.Join` on `inputs.Values`.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "Template")` - 读取模板字符串
2. `GetStringParam(@operator, "Separator")` - 读取拼接分隔符
3. `GetStringParam(@operator, "Mode")` - 读取模式（Template/Join/Date）
4. 模板模式: `Regex.Replace(result, @"\{(\w+)\}", match => ...)` - 键名占位符替换
5. 模板模式: `result.Replace($"{{{index}}}", value)` - 索引占位符替换
6. 拼接模式: `string.Join(separator, inputs.Values.Select(...))`
7. 日期模式: `DateTime.Now.ToString(format)`
8. 返回 `OperatorExecutionOutput.Success(...)` 包含 Result, Length, IsEmpty

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Template` | `string` | `"Result is {0} and {1}"` | 任意字符串 | 模板模式下的格式化模板；支持 `{0}` 索引和 `{Name}` 键名占位符 |
| `Separator` | `string` | `""` | 任意字符串 | 拼接模式下的分隔符（代码中从参数读取但未声明为 `[OperatorParam]`） |
| `Mode` | `string` | `"Template"` | `Template` / `Join` / `Date` | 字符串生成模式（代码中从参数读取但未声明为 `[OperatorParam]`） |
| `DateFormat` | `string` | `"yyyy-MM-dd HH:mm:ss"` | .NET 日期格式字符串 | 日期模式下的格式化字符串（代码中从参数读取但未声明为 `[OperatorParam]`） |

> 注：`Separator`、`Mode`、`DateFormat` 三个参数在 `ExecuteCoreAsync` 中通过 `GetStringParam` 读取，但源码中未声明 `[OperatorParam]` 特性，可能需要运行时配置或后续补全声明。

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Arg1` | 参数 1 | `Any` | No | 第一个格式化参数，对应模板中的 `{0}` |
| `Arg2` | 参数 2 | `Any` | No | 第二个格式化参数，对应模板中的 `{1}` |

> 注：虽然仅声明了两个输入端口，但模板模式可访问 `inputs` 字典中的所有键值对。

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | 结果 | `String` | 格式化后的字符串结果 |
| `Length` | - | `Integer` | 结果字符串的字符长度 |
| `IsEmpty` | - | `Boolean` | 结果字符串是否为空或 null |

> 注：`Length` 和 `IsEmpty` 出现在运行时输出中但未声明为 `[OutputPort]`。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n*m) - n 为输入数量，m 为模板长度（正则替换遍历） |
| 典型耗时 (Typical Latency) | < 1 ms（小模板和少量输入） |
| 内存特征 (Memory Profile) | O(m) - 模板字符串的中间副本 |

## 适用场景 / Use Cases
- 适合 (Suitable)：报告生成，将检测结果插入预定义模板
- 适合 (Suitable)：日志拼装，将多个算子输出合并为可读日志行
- 适合 (Suitable)：文件名生成，配合日期模式生成带时间戳的文件名
- 适合 (Suitable)：简单字符串拼接，替代多个 Concat 算子
- 不适合 (Not Suitable)：复杂的条件格式化（不支持 if/else 逻辑）
- 不适合 (Not Suitable)：大文本的高性能拼接（正则替换有开销）

## 已知限制 / Known Limitations
1. `Separator`、`Mode`、`DateFormat` 三个参数在代码中通过 `GetStringParam` 读取但未声明 `[OperatorParam]`，在参数面板中可能不可见或需手动配置。
2. 模板替换中的 `{KeyName}` 匹配使用 `\{(\w+)\}` 正则，仅支持字母数字下划线组成的键名，不支持含空格或特殊字符的键。
3. 未匹配的占位符保留原样（`return match.Value`），不会报错或警告，可能导致输出中残留未替换的 `{xxx}`。
4. Join 模式将所有 `inputs.Values` 转为字符串拼接，包括 Arg1 和 Arg2 之外的其他字典条目。
5. `inputs == null` 时直接返回 Failure，但未区分"无输入"和"输入为空字典"两种情况。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确描述双轮模板替换机制（正则键名 + 索引顺序）、发现 Separator/Mode/DateFormat 未声明 OperatorParam 的问题、明确输入端口与 inputs 字典的差异、补充 Length/IsEmpty 未声明输出端口说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
