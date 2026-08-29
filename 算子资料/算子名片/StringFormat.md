# 字符串格式化 / StringFormat

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `StringFormatOperator` |
| 枚举值 (Enum) | `OperatorType.StringFormat` |
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
该算子用于按模板生成字符串。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Arg1`、`Arg2`。
- 参数解析覆盖 4 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Mode` | 格式模式 | `enum` | Template | `Template` (模板)<br>`Join` (拼接)<br>`Date` (日期时间) | Yes | - |
| `Template` | 模板 | `string` | Result is {0} and {1} | - | Yes | - |
| `Separator` | 分隔符 | `string` |  | - | Yes | - |
| `DateFormat` | 日期格式 | `string` | yyyy-MM-dd HH:mm:ss | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Arg1` | 参数 1 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Arg2` | 参数 2 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | 结果 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Length` | 结果长度 | `Integer` | 整数结果。 |
| `IsEmpty` | 结果为空 | `Boolean` | 布尔结果，表示当前判定或状态。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `Template` | Metadata | - | Disabled when `Mode!=Template` | Ignored when `Mode!=Template` | - | - | `STRING_FORMAT_TEMPLATE_MODE_ONLY` |
| `Separator` | Metadata | - | Disabled when `Mode!=Join` | Ignored when `Mode!=Join` | - | - | `STRING_FORMAT_JOIN_MODE_ONLY` |
| `DateFormat` | Metadata | - | Disabled when `Mode!=Date` | Ignored when `Mode!=Date` | - | - | `STRING_FORMAT_DATE_MODE_ONLY` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`149B3B790FF62C6E7DA8113AED8CA7CA3CCDA31C4A3A68FB1EBC885DBB67F6EA`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 通常随输入集合、字符串长度或字段数量线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；一般由输入数据规模和运行时调度开销决定。 |
| 内存特征 (Memory Profile) | 主要由输出字典、集合和少量中间对象决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：`StringFormatOperatorTests` 覆盖索引/命名占位符、参数隔离、缺口端口、Join、缺失模板和显式空模板；`FlowExecutionServiceTests` 覆盖共享输入准备端到端路径。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 2 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入数据结构稳定、下游明确消费当前输出字段的常规流程节点。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 模板占位符只读取正式输入端口：`{0}`/`{Arg1}` 对应 `Arg1`，`{1}`/`{Arg2}` 对应 `Arg2`；其他字典键不会参与替换。
2. 属性缺失时使用元数据默认模板；显式空模板保留为空模板语义，并按端口顺序直接拼接输入。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
