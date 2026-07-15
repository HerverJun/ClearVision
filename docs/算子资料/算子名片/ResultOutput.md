# 结果输出 / ResultOutput

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ResultOutputOperator` |
| 枚举值 (Enum) | `OperatorType.ResultOutput` |
| 分类 ID (CategoryId) | `OutputAndAuxiliary` |
| 分类 (Category) | 输出与辅助 |
| 分类顺序 (CategoryOrder) | 14 |
| 版本 (Version) | `1.0.1` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | No |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:OutputAndAuxiliary`, `分类显示:输出与辅助`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于汇总检测结果并输出，支持 JSON/CSV/Text 格式，可选保存到文件。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含外部资源访问逻辑，执行结果会受文件系统、网络、PLC、串口或外部服务状态影响。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Image`、`Result`、`Text`、`Data`。
- 参数解析覆盖 3 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Path.Combine`
- `Path.GetTempPath`
- `Directory.CreateDirectory`
- `File.WriteAllText`
- `JsonSerializer.Serialize`
- `Math.Min`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Format` | 输出格式 | `enum` | JSON | JSON；CSV；Text/文本 | Yes | - |
| `SaveToFile` | 保存到文件 | `bool` | false | - | Yes | - |
| `MaxFormattedCollectionItems` | Max formatted collection items | `int` | 256 | [1, 10000] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Result` | 结果 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Text` | 文本 | `String` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Data` | 数据 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Output` | 输出 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Image` | 图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Result` | 结果 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Text` | 文本 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Data` | 数据 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `FilePath` | 文件路径 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `SaveToFile` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | output_file | - | `RESULT_OUTPUT_OPTIONAL_INTERNAL_FILE_WRITE` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `Image` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`0B158C24D2FFDD41D1EC343609E1F4FF10D6A344C2EC70F464D69E88CC4C9D15`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Items` | `Any` | 源码通过输出字典索引赋值写入。 |
| `OmittedCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `SaveError` | `Any` | 源码通过输出字典索引赋值写入。 |
| `TotalCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Truncated` | `Any` | 源码通过输出字典索引赋值写入。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。 |
| 内存特征 (Memory Profile) | 主要由请求/响应缓冲、序列化数据和外部资源句柄占用决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。

## 适用场景 / Use Cases
- 适合 (Suitable)：需要把视觉流程与文件、HTTP、数据库、PLC、MQTT 或串口等外部系统连接的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
3. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
