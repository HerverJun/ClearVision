# 像素统计 / PixelStatistics

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PixelStatisticsOperator` |
| 枚举值 (Enum) | `OperatorType.PixelStatistics` |
| 分类 ID (CategoryId) | `FeatureExtraction` |
| 分类 (Category) | 特征提取 |
| 分类顺序 (CategoryOrder) | 4 |
| 版本 (Version) | `1.0.1` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:FeatureExtraction`, `分类显示:特征提取`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于计算 ROI 或掩码区域内的像素级统计信息。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Mask`。
- 参数解析覆盖 5 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.ExtractChannel`
- `Cv2.CvtColor`
- `Cv2.Threshold`
- `Math.Min`
- `Math.Max`
- `Math.Sqrt`
- `Math.Abs`
- `Math.Round`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `RoiX` | ROIX | `int` | 0 | >= 0 | Yes | - |
| `RoiY` | ROIY | `int` | 0 | >= 0 | Yes | - |
| `RoiW` | ROI宽 | `int` | 0 | >= 0 | Yes | - |
| `RoiH` | ROI高 | `int` | 0 | >= 0 | Yes | - |
| `Channel` | 通道 | `enum` | Gray | Gray/灰度；R；G；B；All/全部 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Mask` | Mask | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Mean` | 均值 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `StdDev` | StdDev | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Min` | Min | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Max` | Max | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Median` | 中值 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Range` | Range | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MedianAbsoluteDeviation` | Median Absolute Deviation | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `StdError` | Standard Error | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `NonZeroCount` | NonZero Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `SampleCount` | 采样数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `SelectedChannel` | Selected Channel | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ChannelsAnalyzed` | Channels Analyzed | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `AggregationMode` | Aggregation Mode | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ChannelStats` | Per-channel Statistics | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:13, Rejected:0, Unknown:0 | Verified production support is present. | CV_8U, CV_8S, CV_16U, CV_16S, CV_32S, CV_32F, CV_64F | CV_8U, CV_8S, CV_16U, CV_16S, CV_32S, CV_32F, CV_64F | 1, 3, 4 | Exact support is declared per depth/channel pair. | No implicit depth normalization; explicit channel selection only. | No image output. | Statistics retain the admitted input numeric domain. | RejectNaNAndInfinityForFloatingVariants | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |
| `Mask` | Allowed:3, Rejected:0, Unknown:0 | Verified production support is present. | CV_8U | CV_8U | 1, 3, 4 | Mask is an 8-bit selection image. | Optional color-to-gray conversion followed by binary thresholding. | No image output. | Non-zero mask domain. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8SC1, CV_16SC1, CV_32SC1 | Single-channel statistics use typed scalar reads without depth conversion. | `Allowed` | `VerifiedSupport` | None. | No image output. | Exact source numeric values are accumulated as double. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Default | CV_64FC1 | Single-channel statistics use typed scalar reads without depth conversion. | `Allowed` | `VerifiedSupport` | None. | No image output. | Exact source numeric values are accumulated as double. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Image` | Default | CV_8UC1, CV_16UC1 | Statistics are computed in the admitted input numeric domain; Channel selects Gray/R/G/B/All at runtime. | `Allowed` | `VerifiedSupport` | Channel split or supported color-to-gray conversion; no depth scaling. | No image output. | Exact source numeric values are accumulated as double. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Default | CV_32FC1 | Statistics are computed in the admitted input numeric domain; Channel selects Gray/R/G/B/All at runtime. | `Allowed` | `VerifiedSupport` | Channel split or supported color-to-gray conversion; no depth scaling. | No image output. | Exact source numeric values are accumulated as double. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Default | CV_8UC3, CV_8UC4, CV_16UC3, CV_16UC4 | Statistics are computed in the admitted input numeric domain; Channel selects Gray/R/G/B/All at runtime. | `Allowed` | `VerifiedConversion` | Channel split or supported color-to-gray conversion; no depth scaling. | No image output. | Exact source numeric values are accumulated as double. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Image` | Default | CV_32FC3, CV_32FC4 | Statistics are computed in the admitted input numeric domain; Channel selects Gray/R/G/B/All at runtime. | `Allowed` | `VerifiedConversion` | Channel split or supported color-to-gray conversion; no depth scaling. | No image output. | Exact source numeric values are accumulated as double. | `RejectNonFinite` | `IMAGE_NONFINITE_INPUT` | `E2_NUMERICAL_ORACLE` |
| `Mask` | Default | CV_8UC1 | Mask is converted to single-channel binary 8U before sampling. | `Allowed` | `VerifiedSupport` | C3/C4 mask -> Gray -> binary 8U; C1 is thresholded directly. | No image output. | Non-zero mask values select samples. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |
| `Mask` | Default | CV_8UC3, CV_8UC4 | Mask is converted to single-channel binary 8U before sampling. | `Allowed` | `VerifiedConversion` | C3/C4 mask -> Gray -> binary 8U; C1 is thresholded directly. | No image output. | Non-zero mask values select samples. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_EXECUTABLE_PROBE` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`7D84BF747A71B517E404E47917B5A51877365C79DA3C86A297784C89A557FBCD`
- `type:ClearVision.Product.Infrastructure.Operators.PixelStatisticsImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Confidence` | `Float` | 源码通过输出字典索引赋值写入。 |
| `StatusCode` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StatusMessage` | `String` | 源码通过输出字典索引赋值写入。 |
| `UncertaintyPx` | `Any` | 源码通过输出字典索引赋值写入。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 多数图像路径近似 `O(W*H)`；涉及轮廓、匹配或排序时会叠加候选数量相关开销。 |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
