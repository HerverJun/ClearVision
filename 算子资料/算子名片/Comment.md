# 注释 / Comment

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CommentOperator` |
| 枚举值 (Enum) | `OperatorType.Comment` |
| 分类 (Category) | 辅助 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:流程`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于在工作流中添加说明文本，不影响数据流，仅用于标注设计意图。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Workflow annotation passthrough` 为主；元数据未声明更多细分时，以当前源码实现为准。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Input`。
- 参数解析覆盖 1 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `optional Input -> preserve value/ImageWrapper AddRef -> Output + Message`
- `OperatorBase.Get*Param(...)`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Text` | 注释文本 | `string` | "" | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Input` | 透传输入 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Output` | 透传输出 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Message` | 注释内容 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) |
| 典型耗时 (Typical Latency) | Avg 1.176 ms, max 21.034 ms over 22 contract golden cases |
| 内存特征 (Memory Profile) | O(1) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 1 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Annotating workflow intent without changing data flow.
- 适合 (Suitable)：Passing scalar or image payloads through a readable checkpoint node.
- 不适合 (Not Suitable)：Transforming payloads or enforcing branching logic; use dedicated flow-control operators instead.
- 不适合 (Not Suitable)：Storing long operator documentation; keep notes concise and externalize large text.

## 已知限制 / Known Limitations
1. The note text is limited to 4096 characters to keep serialized flows bounded.
2. The operator intentionally exposes only Output and Message and does not mutate upstream data.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
