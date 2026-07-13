# 变量写入 / VariableWrite

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VariableWriteOperator` |
| 枚举值 (Enum) | `OperatorType.VariableWrite` |
| 分类 (Category) | 变量 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:流程`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于写入单次运行变量或项目全局变量。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Value`。
- 参数解析覆盖 12 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `JsonDocument.Parse`
- `Math.Abs`
- `Convert.ToDouble`
- `Convert.ToBoolean`
- `Convert.ToDecimal`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Scope` | 作用域 | `enum` | Run | Run/单次运行；Project/项目全局 | Yes | - |
| `VariableId` | 变量ID | `string` | "" | - | Yes | Project 作用域变量的稳定 ID |
| `VariableName` | 变量名 | `string` | "" | - | Yes | 要写入的变量名称 |
| `DataType` | 数据类型 | `enum` | String | String/字符串；Int/整数；Double/浮点数；Bool/布尔值；Object/对象 | Yes | - |
| `UseInputValue` | 使用输入值 | `bool` | true | - | Yes | 优先使用上游输入值，否则使用静态值 |
| `StaticValue` | 静态值 | `string` | 0 | - | Yes | 没有上游输入时使用的值 |
| `ConversionMode` | Conversion Mode | `enum` | Exact | Exact/Exact；Round/Round；Floor/Floor；Ceiling/Ceiling；Truncate/Truncate | Yes | - |
| `Expression` | Expression | `string` | "" | - | Yes | Optional controlled expression evaluated before Project variable write. Use value for the raw input. |
| `InputFieldName` | Input Field Name | `string` | "" | - | Yes | Optional upstream field path such as ParsedFields.score. |
| `RequireInputStatus` | Require Input Status | `bool` | false | - | Yes | When enabled, write only if the configured upstream status field is true/OK/PASS/1. |
| `InputStatusFieldName` | Input Status Field Name | `string` | Status | - | Yes | Optional upstream status field path such as Status or ResponseAccepted. |
| `FailOnInputStatusFalse` | Fail On Input Status False | `bool` | false | - | Yes | Return failure instead of a skipped write when the upstream status is false or missing. |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | 值 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `VariableName` | 变量名 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Value` | 写入的值 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `CycleCount` | 循环计数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `VariableId` | Variable Id | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ValueType` | Value Type | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Version` | Version | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `UpdatedAtUtc` | Updated At UTC | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `UpdatedBy` | Updated By | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `WriteSkipped` | Write Skipped | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `SkipReason` | Skip Reason | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `InputStatusValue` | Input Status Value | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 通常随输入集合、字符串长度或字段数量线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；一般由输入数据规模和运行时调度开销决定。 |
| 内存特征 (Memory Profile) | 主要由输出字典、集合和少量中间对象决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 8 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入数据结构稳定、下游明确消费当前输出字段的常规流程节点。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
