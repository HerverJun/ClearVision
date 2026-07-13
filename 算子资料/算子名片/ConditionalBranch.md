# 条件分支 / ConditionalBranch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ConditionalBranchOperator` |
| 枚举值 (Enum) | `OperatorType.ConditionalBranch` |
| 分类 (Category) | 流程控制 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:流程`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于根据数值/字符串/布尔条件执行 True/False 两路分支，常用于 OK/NG 判定路由。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
该类算子主要对上游值、集合或流程状态做判断、转换、聚合或路由，不直接改写图像像素。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Value`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Compare`。
- 参数解析覆盖 12 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `JsonDocument.Parse`
- `Math.Abs`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Condition` | 条件 | `enum` | GreaterThan | GreaterThan/大于；GreaterThanOrEqual/大于等于；LessThan/小于；LessThanOrEqual/小于等于；Equal/等于；NotEqual/不等于；InRange/范围内；Between/介于；NotInRange/范围外；InList/列表内；NotInList/列表外；Contains/包含；StartsWith/开头是；EndsWith/结尾是；Matches/正则匹配；IsTrue/为真/OK；IsFalse/为假/NG；IsEmpty/为空；IsNotEmpty/非空 | Yes | - |
| `CompareValue` | 比较值 | `string` | 0 | - | Yes | - |
| `CompareListDelimiter` | Compare List Delimiter | `string` | , | - | Yes | 列表条件使用的主分隔符。 |
| `CompareListDelimiters` | Additional Compare List Delimiters | `string` | "" | - | Yes | 列表条件使用的附加分隔符，多个值用 \| 分隔。 |
| `FieldName` | 字段名 | `string` | "" | - | Yes | - |
| `CompareFieldName` | Compare Field Name | `string` | "" | - | Yes | 从 Compare 输入或当前 Value 中读取比较值的字段路径。 |
| `FailOnMissingField` | Fail On Missing Field | `bool` | false | - | Yes | 启用后，FieldName 字段缺失时执行失败。 |
| `FailOnEvaluationError` | Fail On Evaluation Error | `bool` | false | - | Yes | 启用后，条件计算错误时执行失败。 |
| `NumericTolerance` | Numeric Tolerance | `double` | 0 | >= 0 | Yes | 数值比较允许的绝对误差。 |
| `IgnoreCase` | Ignore Case | `bool` | false | - | Yes | 启用后，字符串比较和正则匹配忽略大小写。 |
| `RangeMin` | Range Min | `double` | 0 | - | Yes | 范围判断的默认下限。 |
| `RangeMax` | Range Max | `double` | 1 | - | Yes | 范围判断的默认上限。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | 判断值 | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Compare` | Compare Value | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `True` | True分支 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `False` | False分支 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `EvaluationSuccess` | Evaluation Success | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `EvaluationError` | Evaluation Error | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ActualSource` | Actual Source | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `ActualValue` | `Any` | 源码输出字典初始化中可见字段。 |
| `CompareSource` | `String` | 源码输出字典初始化中可见字段。 |
| `RangeSource` | `String` | 源码输出字典初始化中可见字段。 |
| `Result` | `Any` | 源码输出字典初始化中可见字段。 |

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
- 执行失败契约：源码中发现 6 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：需要对上游结果做判断、转换、聚合、计数、延时或流程路由的场景。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
