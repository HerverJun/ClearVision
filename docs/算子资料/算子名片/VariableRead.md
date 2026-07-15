# 变量读取 / VariableRead

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VariableReadOperator` |
| 枚举值 (Enum) | `OperatorType.VariableRead` |
| 分类 ID (CategoryId) | `DataProcessing` |
| 分类 (Category) | 数据处理 |
| 分类顺序 (CategoryOrder) | 11 |
| 版本 (Version) | `1.0.0` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:DataProcessing`, `分类显示:数据处理`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于从单次运行变量或项目全局变量读取值。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 参数解析覆盖 10 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `JsonDocument.Parse`
- `JsonSerializer.Serialize`
- `Convert.ToInt64`
- `Convert.ToDecimal`
- `Math.Abs`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Scope` | 作用域 | `enum` | Run | Run/单次运行；Project/项目全局 | Yes | - |
| `VariableId` | 变量ID | `string` | "" | - | Yes | Project 作用域变量的稳定 ID |
| `VariableName` | 变量名 | `string` | "" | - | Yes | 要读取的变量名称 |
| `DefaultValue` | 默认值 | `string` | 0 | - | Yes | 变量不存在时的默认值 |
| `DataType` | 数据类型 | `enum` | String | String/字符串；Int/整数；Double/浮点数；Bool/布尔值；Object/对象 | Yes | - |
| `OutputFieldName` | 输出字段名 | `string` | "" | - | Yes | 可选字段路径，用于从变量原始值中读取子字段，例如 ParsedFields.Score。 |
| `FailOnMissingOutputField` | 输出字段缺失时失败 | `bool` | true | - | Yes | 启用后，OutputFieldName 路径缺失时直接失败，而不是回退为完整变量值。 |
| `ConversionMode` | 转换模式 | `enum` | Exact | Exact/精确转换；Round/四舍五入；Floor/向下取整；Ceiling/向上取整；Truncate/截断 | Yes | - |
| `Expression` | 表达式 | `string` | "" | - | Yes | 项目变量读取后可选执行的受控表达式；使用 value 表示当前变量值。 |
| `FailOnMissingVariable` | 变量缺失时失败 | `bool` | false | - | Yes | 启用后，运行变量或项目变量缺失时直接失败，而不是返回 DefaultValue。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| - | - | - | - | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Value` | 值 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `RawValue` | 原始值 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Exists` | 是否存在 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `CycleCount` | 循环计数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `VariableId` | 变量ID | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ValueType` | 值类型 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Version` | 版本 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `UpdatedAtUtc` | 更新时间(UTC) | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `UpdatedBy` | 更新来源 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ReadSource` | 读取来源 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `OutputFieldName` | 输出字段名 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `OutputFieldFound` | 输出字段已找到 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`32855968C1EF71D2B1ADCE8D2C365358C9FEAB3280F308B1E227FADC4780C7E1`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

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
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
