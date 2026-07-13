# TCP通信 / TcpCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TcpCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.TcpCommunication` |
| 分类 (Category) | 通信 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:通信`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于TCP/IP网络通信。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Data`。
- 参数解析覆盖 39 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `JsonDocument.Parse`
- `JsonSerializer.Serialize`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ProfileId` | 全局Profile | `string` | "" | - | Yes | - |
| `UseGlobalProfile` | 使用全局Profile | `bool` | false | - | Yes | - |
| `Mode` | 模式 | `enum` | Client | Client/客户端；Server/服务器 | Yes | - |
| `IpAddress` | IP地址 | `string` | 127.0.0.1 | - | Yes | - |
| `Port` | 端口 | `int` | 8080 | [1, 65535] | Yes | - |
| `SendData` | 发送数据 | `string` | "" | - | Yes | - |
| `UseFixedSendData` | 固定发送数据 | `bool` | false | - | Yes | - |
| `PayloadTemplate` | 报文模板 | `string` | "" | - | Yes | - |
| `DecodeEscapeSequences` | Decode Escape Sequences | `bool` | false | - | Yes | 启用后解析发送报文、分隔符和匹配条件中的 \r、\n、\xHH 等转义序列。 |
| `WaitResponse` | 等待响应 | `bool` | true | - | Yes | - |
| `ResponseTimeoutMs` | 响应超时(ms) | `int` | 5000 | [100, 600000] | Yes | - |
| `Timeout` | 超时(ms) | `int` | 5000 | [100, 600000] | Yes | - |
| `Encoding` | 编码 | `enum` | UTF8 | UTF8/UTF-8；ASCII/ASCII；GBK/GBK；HEX/HEX | Yes | - |
| `FailOnUnresolvedPayloadPlaceholder` | Fail On Unresolved Payload Placeholder | `bool` | true | - | Yes | 启用后，请求报文模板中存在未解析占位符时执行失败。 |
| `FailOnParseError` | Fail On Parse Error | `bool` | false | - | Yes | 启用后，响应解析失败时执行失败。 |
| `FailOnUnexpectedResponse` | Fail On Unexpected Response | `bool` | false | - | Yes | 启用后，响应未满足期望或命中拒绝条件时执行失败。 |
| `ResponseParseMode` | Response Parse Mode | `enum` | None | None/None；JsonPath/JSON path；KeyValue/Key-value；Regex/Regex；Delimited/Delimited；FixedWidth/Fixed width | Yes | 选择响应解析方式：不解析、JSON路径、键值对、正则、分隔符或固定宽度。 |
| `ResponseFieldName` | Response Field Name | `string` | "" | - | Yes | 单字段解析目标，例如 JSONPath 或解析字段名。 |
| `ResponseFieldNames` | Response Field Names | `string` | "" | - | Yes | 多字段解析名称列表，通常用逗号分隔。 |
| `RequiredResponseFields` | Required Response Fields | `string` | "" | - | Yes | 必需响应字段列表，缺失时记录 MissingResponseFields。 |
| `ResponseFieldWidths` | Response Field Widths | `string` | "" | - | Yes | 固定宽度解析时每个字段的字符宽度列表。 |
| `ResponseRegexPattern` | Response Regex Pattern | `string` | "" | - | Yes | 正则解析或正则匹配使用的表达式。 |
| `ResponseRegexIgnoreCase` | Response Regex Ignore Case | `bool` | false | - | Yes | 启用后，响应正则解析忽略大小写。 |
| `ResponseKeyValuePairDelimiter` | Response Key-Value Pair Delimiter | `string` | ; | - | Yes | 键值对响应中不同键值对之间的主分隔符。 |
| `ResponseKeyValuePairDelimiters` | Additional Key-Value Pair Delimiters | `string` | "" | - | Yes | 键值对响应的附加分隔符，多个值用 \| 分隔。 |
| `ResponseKeyValueSeparator` | Response Key-Value Separator | `string` | = | - | Yes | 键和值之间的主分隔符。 |
| `ResponseKeyValueSeparators` | Additional Key-Value Separators | `string` | "" | - | Yes | 键和值之间的附加分隔符，多个值用 \| 分隔。 |
| `ResponseDelimiter` | Response Delimiter | `string` | , | - | Yes | 分隔符解析时使用的主分隔符。 |
| `ResponseDelimiters` | Additional Response Delimiters | `string` | "" | - | Yes | 分隔符解析时使用的附加分隔符，多个值用 \| 分隔。 |
| `ResponseIndex` | Response Index | `int` | 0 | [0, 4096] | Yes | 分隔符解析时选取的字段索引，从 0 开始。 |
| `TrimResponseBeforeParse` | Trim Response Before Parse | `bool` | false | - | Yes | 解析前先裁剪响应两端空白字符。 |
| `ResponseStartMarker` | Response Start Marker | `string` | "" | - | Yes | 响应帧起始标记，配置后仅截取标记后的内容。 |
| `ResponseEndMarker` | Response End Marker | `string` | "" | - | Yes | 响应帧结束标记，配置后仅截取标记前的内容。 |
| `FailOnMissingResponseFrame` | Fail On Missing Response Frame | `bool` | false | - | Yes | 启用后，响应未找到配置的起止标记时执行失败。 |
| `ExpectedResponse` | Expected Response | `string` | "" | - | Yes | 期望响应内容；配置后用于判断响应是否通过。 |
| `RejectedResponse` | Rejected Response | `string` | "" | - | Yes | 拒绝响应内容；命中后 ResponseAccepted 为 false。 |
| `ResponseMatchMode` | Response Match Mode | `enum` | Contains | Contains/Contains；Equals/Equals；StartsWith/Starts with；EndsWith/Ends with；Regex/Regex | Yes | 响应判断方式：包含、等于、开头、结尾或正则。 |
| `ResponseMatchIgnoreCase` | Response Match Ignore Case | `bool` | false | - | Yes | 启用后，期望/拒绝响应匹配忽略大小写。 |
| `ResponseMatchSource` | Response Match Source | `enum` | Response | Response/Raw response；NormalizedResponse/Normalized response；ParsedValue/Parsed value | Yes | 选择响应判断的数据来源：原始响应、归一化响应或解析值。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 数据 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Response` | 响应 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Status` | 状态 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `NormalizedResponse` | Normalized Response | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `RequestPayload` | Request Payload | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ParseSuccess` | Parse Success | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ParsedValue` | Parsed Value | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ParsedFields` | Parsed Fields | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ParseError` | Parse Error | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `MissingResponseFields` | Missing Response Fields | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ResponseAccepted` | Response Accepted | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ResponseMatchError` | Response Match Error | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResponseMatchValue` | Response Match Value | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `ResponseFrameError` | `Any` | 源码输出字典初始化中可见字段。 |
| `ResponseFrameFound` | `Any` | 源码输出字典初始化中可见字段。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 通常随输入集合、字符串长度或字段数量线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；一般由输入数据规模和运行时调度开销决定。 |
| 内存特征 (Memory Profile) | 主要由输出字典、集合和少量中间对象决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：检测结果需要与现场设备、上位系统或网络服务进行读写交互的场景。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
