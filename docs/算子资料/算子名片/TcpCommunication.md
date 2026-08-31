# TCP通信 / TcpCommunication

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TcpCommunicationOperator` |
| 枚举值 (Enum) | `OperatorType.TcpCommunication` |
| 分类 ID (CategoryId) | `Communication` |
| 分类 (Category) | 通信 |
| 分类顺序 (CategoryOrder) | 13 |
| 版本 (Version) | `1.1.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:Communication`, `分类显示:通信`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于TCP/IP网络通信。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Data`。
- 参数解析覆盖 33 个当前元数据字段，默认值、范围和枚举项以参数表为准。
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
| `SendData` | 发送数据 | `string` | "" | - | Yes | - |
| `UseFixedSendData` | 固定发送数据 | `bool` | false | - | Yes | - |
| `PayloadTemplate` | 报文模板 | `string` | "" | - | Yes | - |
| `DecodeEscapeSequences` | 解码转义序列 | `bool` | false | - | Yes | 启用后解析发送报文、分隔符和匹配条件中的 \r、\n、\xHH 等转义序列。 |
| `WaitResponse` | 等待响应 | `bool` | true | - | Yes | - |
| `ResponseTimeoutMs` | 响应超时(ms) | `int` | 5000 | [100, 600000] | Yes | - |
| `FailOnUnresolvedPayloadPlaceholder` | 报文占位符未解析时失败 | `bool` | true | - | Yes | 启用后，请求报文模板中存在未解析占位符时执行失败。 |
| `FailOnParseError` | 解析错误时失败 | `bool` | false | - | Yes | 启用后，响应解析失败时执行失败。 |
| `FailOnUnexpectedResponse` | 非预期响应时失败 | `bool` | false | - | Yes | 启用后，响应未满足期望或命中拒绝条件时执行失败。 |
| `ResponseParseMode` | 响应解析模式 | `enum` | None | None/无；JsonPath/JSON路径；KeyValue/键值对解析；Regex/正则；Delimited/分隔符解析；FixedWidth/固定宽度解析 | Yes | 选择响应解析方式：不解析、JSON路径、键值对、正则、分隔符或固定宽度。 |
| `ResponseFieldName` | 响应字段名 | `string` | "" | - | Yes | 单字段解析目标，例如 JSONPath 或解析字段名。 |
| `ResponseFieldNames` | 响应字段名列表 | `string` | "" | - | Yes | 多字段解析名称列表，通常用逗号分隔。 |
| `RequiredResponseFields` | 必需响应字段 | `string` | "" | - | Yes | 必需响应字段列表，缺失时记录 MissingResponseFields。 |
| `ResponseFieldWidths` | 响应字段宽度 | `string` | "" | - | Yes | 固定宽度解析时每个字段的字符宽度列表。 |
| `ResponseRegexPattern` | 响应正则表达式 | `string` | "" | - | Yes | 正则解析或正则匹配使用的表达式。 |
| `ResponseRegexIgnoreCase` | 响应正则忽略大小写 | `bool` | false | - | Yes | 启用后，响应正则解析忽略大小写。 |
| `ResponseKeyValuePairDelimiter` | 响应键值对分隔符 | `string` | ; | - | Yes | 键值对响应中不同键值对之间的主分隔符。 |
| `ResponseKeyValuePairDelimiters` | 附加键值对分隔符 | `string` | "" | - | Yes | 键值对响应的附加分隔符，多个值用 \| 分隔。 |
| `ResponseKeyValueSeparator` | 响应键值分隔符 | `string` | = | - | Yes | 键和值之间的主分隔符。 |
| `ResponseKeyValueSeparators` | 附加键值分隔符 | `string` | "" | - | Yes | 键和值之间的附加分隔符，多个值用 \| 分隔。 |
| `ResponseDelimiter` | 响应分隔符 | `string` | , | - | Yes | 分隔符解析时使用的主分隔符。 |
| `ResponseDelimiters` | 附加响应分隔符 | `string` | "" | - | Yes | 分隔符解析时使用的附加分隔符，多个值用 \| 分隔。 |
| `ResponseIndex` | 响应字段索引 | `int` | 0 | [0, 4096] | Yes | 分隔符解析时选取的字段索引，从 0 开始。 |
| `TrimResponseBeforeParse` | 解析前裁剪响应 | `bool` | false | - | Yes | 解析前先裁剪响应两端空白字符。 |
| `ResponseStartMarker` | 响应起始标记 | `string` | "" | - | Yes | 响应帧起始标记，配置后仅截取标记后的内容。 |
| `ResponseEndMarker` | 响应结束标记 | `string` | "" | - | Yes | 响应帧结束标记，配置后仅截取标记前的内容。 |
| `FailOnMissingResponseFrame` | 缺失响应帧时失败 | `bool` | false | - | Yes | 启用后，响应未找到配置的起止标记时执行失败。 |
| `ExpectedResponse` | 期望响应 | `string` | "" | - | Yes | 期望响应内容；配置后用于判断响应是否通过。 |
| `RejectedResponse` | 拒绝响应 | `string` | "" | - | Yes | 拒绝响应内容；命中后 ResponseAccepted 为 false。 |
| `ResponseMatchMode` | 响应匹配模式 | `enum` | Contains | Contains/包含；Equals/等于；StartsWith/开头匹配；EndsWith/结尾匹配；Regex/正则 | Yes | 响应判断方式：包含、等于、开头、结尾或正则。 |
| `ResponseMatchIgnoreCase` | 响应匹配忽略大小写 | `bool` | false | - | Yes | 启用后，期望/拒绝响应匹配忽略大小写。 |
| `ResponseMatchSource` | 响应匹配来源 | `enum` | Response | Response/原始响应；NormalizedResponse/归一化响应；ParsedValue/解析值 | Yes | 选择响应判断的数据来源：原始响应、归一化响应或解析值。 |

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
| `NormalizedResponse` | 归一化响应 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `RequestPayload` | 请求报文 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ParseSuccess` | 解析成功 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ParsedValue` | 解析值 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ParsedFields` | 解析字段 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ParseError` | 解析错误 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `MissingResponseFields` | 缺失响应字段 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ResponseAccepted` | 响应通过 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `ResponseMatchError` | 响应匹配错误 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResponseMatchValue` | 响应匹配值 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `ExpectedResponse` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_EXPECTED_RESPONSE_ONLY_WHEN_WAITING` |
| `FailOnMissingResponseFrame` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_FRAME_POLICY_ONLY_WHEN_WAITING` |
| `FailOnParseError` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_PARSE_FAILURE_POLICY_ONLY_WHEN_WAITING` |
| `FailOnUnexpectedResponse` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_FAILURE_POLICY_ONLY_WHEN_WAITING` |
| `ProfileId` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | tcp_profile | - | `TCP_PROFILE_REQUIRED` |
| `RejectedResponse` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_REJECTED_RESPONSE_ONLY_WHEN_WAITING` |
| `RequiredResponseFields` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_REQUIRED_RESPONSE_FIELDS_ONLY_WHEN_WAITING` |
| `ResponseDelimiter` | metadata; ALL(WaitResponse == true && ResponseParseMode == Delimited) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == Delimited); disabled: - | - | - | - | `TCP_DELIMITER_ONLY_FOR_DELIMITED_PARSE` |
| `ResponseDelimiters` | metadata; ALL(WaitResponse == true && ResponseParseMode == Delimited) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == Delimited); disabled: - | - | - | - | `TCP_DELIMITERS_ONLY_FOR_DELIMITED_PARSE` |
| `ResponseEndMarker` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_FRAME_ONLY_WHEN_WAITING` |
| `ResponseFieldName` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode != None); disabled: - | - | - | - | `TCP_RESPONSE_FIELD_ONLY_WHEN_PARSING` |
| `ResponseFieldNames` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); ANY(ResponseParseMode == Delimited \|\| ResponseParseMode == FixedWidth); disabled: - | - | - | - | `TCP_RESPONSE_FIELD_NAMES_ONLY_FOR_POSITIONAL_PARSE` |
| `ResponseFieldWidths` | metadata; ALL(WaitResponse == true && ResponseParseMode == FixedWidth) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == FixedWidth); disabled: - | - | - | - | `TCP_FIXED_WIDTHS_REQUIRED_FOR_FIXED_WIDTH_PARSE` |
| `ResponseIndex` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); ANY(ResponseParseMode == Delimited \|\| ResponseParseMode == FixedWidth); disabled: - | - | - | - | `TCP_RESPONSE_INDEX_ONLY_FOR_POSITIONAL_PARSE` |
| `ResponseKeyValuePairDelimiter` | metadata; ALL(WaitResponse == true && ResponseParseMode == KeyValue) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == KeyValue); disabled: - | - | - | - | `TCP_KEY_VALUE_PAIR_DELIMITER_ONLY_FOR_KEY_VALUE_PARSE` |
| `ResponseKeyValuePairDelimiters` | metadata; ALL(WaitResponse == true && ResponseParseMode == KeyValue) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == KeyValue); disabled: - | - | - | - | `TCP_KEY_VALUE_PAIR_DELIMITERS_ONLY_FOR_KEY_VALUE_PARSE` |
| `ResponseKeyValueSeparator` | metadata; ALL(WaitResponse == true && ResponseParseMode == KeyValue) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == KeyValue); disabled: - | - | - | - | `TCP_KEY_VALUE_SEPARATOR_ONLY_FOR_KEY_VALUE_PARSE` |
| `ResponseKeyValueSeparators` | metadata; ALL(WaitResponse == true && ResponseParseMode == KeyValue) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == KeyValue); disabled: - | - | - | - | `TCP_KEY_VALUE_SEPARATORS_ONLY_FOR_KEY_VALUE_PARSE` |
| `ResponseMatchIgnoreCase` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_MATCH_ONLY_WHEN_WAITING` |
| `ResponseMatchMode` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_MATCH_ONLY_WHEN_WAITING` |
| `ResponseMatchSource` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_MATCH_ONLY_WHEN_WAITING` |
| `ResponseParseMode` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_PARSE_ONLY_WHEN_WAITING` |
| `ResponseRegexIgnoreCase` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == Regex); disabled: - | - | - | - | `TCP_REGEX_OPTIONS_ONLY_FOR_REGEX_PARSE` |
| `ResponseRegexPattern` | metadata; ALL(WaitResponse == true && ResponseParseMode == Regex) | visible: -; hidden: - | enabled: ALL(WaitResponse == true && ResponseParseMode == Regex); disabled: - | - | - | - | `TCP_REGEX_PATTERN_REQUIRED_FOR_REGEX_PARSE` |
| `ResponseStartMarker` | optional; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_FRAME_ONLY_WHEN_WAITING` |
| `ResponseTimeoutMs` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_TIMEOUT_ONLY_WHEN_WAITING` |
| `TrimResponseBeforeParse` | metadata; - | visible: -; hidden: - | enabled: ALL(WaitResponse == true); disabled: - | - | - | - | `TCP_RESPONSE_TRIM_ONLY_WHEN_WAITING` |
| `UseFixedSendData` | metadata; - | visible: -; hidden: - | enabled: -; disabled: ALL(PayloadTemplate is not empty) | - | - | - | `TCP_PAYLOAD_TEMPLATE_OWNS_PAYLOAD_SELECTION` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`CE7463C76C444B6CE795423B70363D7F28892193998630221049D89F8217C698`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Mode` | `String` | 源码输出字典初始化中可见字段。 |
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
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：检测结果需要与现场设备、上位系统或网络服务进行读写交互的场景。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.0 | 2026-08-31 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
