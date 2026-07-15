# 颜色分析 / ColorDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ColorDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.ColorDetection` |
| 分类 ID (CategoryId) | `FeatureExtraction` |
| 分类 (Category) | 特征提取 |
| 分类顺序 (CategoryOrder) | 4 |
| 版本 (Version) | `2.0.1` |
| 生命周期 (Lifecycle) | 实验 `Experimental` |
| 生命周期说明 (Lifecycle Note) | 颜色检查多模式仍处于工业化验证阶段，阈值和白平衡策略需按现场样本确认。 |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Experimental` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Experimental`, `color-inspection`, `experimental`, `industrial-remediation`, `分类:FeatureExtraction`, `分类显示:特征提取`, `生命周期:Experimental`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于对图像执行平均色、主色和范围分析，并支持 HSV 区间检查与 Lab DeltaE 色差分析。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`ReferenceColor`。
- 参数解析覆盖 18 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Mean`
- `Cv2.Rectangle`
- `Cv2.PutText`
- `Cv2.Resize`
- `Cv2.Kmeans`
- `Cv2.CountNonZero`
- `Cv2.BitwiseAnd`
- `Cv2.AddWeighted`
- `Cv2.InRange`
- `Cv2.BitwiseOr`
- `Math.Max`
- `Math.Min`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ColorSpace` | 颜色空间 | `enum` | HSV | HSV；Lab | Yes | - |
| `AnalysisMode` | 分析模式 | `enum` | Average | Average/Legacy Average；Dominant/Legacy Dominant；Range/Legacy Range；HsvInspection/HSV Inspection；LabDeltaE/Lab DeltaE | Yes | - |
| `HueLow` | H下限 | `int` | 0 | [0, 180] | Yes | - |
| `HueHigh` | H上限 | `int` | 180 | [0, 180] | Yes | - |
| `SatLow` | S下限 | `int` | 50 | [0, 255] | Yes | - |
| `SatHigh` | S上限 | `int` | 255 | [0, 255] | Yes | - |
| `ValLow` | V下限 | `int` | 50 | [0, 255] | Yes | - |
| `ValHigh` | V上限 | `int` | 255 | [0, 255] | Yes | - |
| `DominantK` | 主色数量K | `int` | 3 | [1, 10] | Yes | - |
| `DeltaEMethod` | DeltaE方法 | `enum` | CIEDE2000 | CIE76；CIEDE2000 | Yes | - |
| `RefL` | 参考L | `double` | 0 | - | Yes | - |
| `RefA` | 参考A | `double` | 0 | - | Yes | - |
| `RefB` | 参考B | `double` | 0 | - | Yes | - |
| `RoiX` | ROIX | `int` | 0 | - | Yes | - |
| `RoiY` | ROIY | `int` | 0 | - | Yes | - |
| `RoiW` | ROI宽 | `int` | 0 | - | Yes | - |
| `RoiH` | ROI高 | `int` | 0 | - | Yes | - |
| `WhiteBalanceTolerance` | 白平衡容差 | `double` | 12 | [0, 255] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `ReferenceColor` | 参考颜色 | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ColorInfo` | 颜色信息 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `AnalysisMode` | 分析模式 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ColorSpace` | 颜色空间 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `DeltaE` | DeltaE | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Coverage` | 覆盖率 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `WhiteBalanceStatus` | 白平衡状态 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `MeanColor` | 平均颜色 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `DominantColors` | 主颜色 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Diagnostics` | 诊断信息 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:3, Rejected:0, Unknown:0 | Verified production support is present. | CV_8U | CV_8U | 1, 3, 4 | ColorDetection executes only on verified 8-bit C1/C3/C4 inputs. | C1/C4 are converted to BGR without depth scaling. | Image output is CV_8UC3; coverage and color metrics are scalar outputs. | HSV/Lab inspection thresholds are defined in the 8-bit intensity domain. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC3 | Verified 8-bit grayscale, BGR, and BGRA color-analysis inputs. | `Allowed` | `VerifiedSupport` | C1 -> BGR and C4 -> BGR before HSV/Lab analysis; no depth scaling. | Image output is CV_8UC3; scalar color metrics are double precision. | HSV thresholds and rendered diagnostics use the legacy 0..255 intensity domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_OPERATOR_AND_PACKAGE_TESTS` |
| `Image` | Default | CV_8UC1, CV_8UC4 | Verified 8-bit grayscale, BGR, and BGRA color-analysis inputs. | `Allowed` | `VerifiedConversion` | C1 -> BGR and C4 -> BGR before HSV/Lab analysis; no depth scaling. | Image output is CV_8UC3; scalar color metrics are double precision. | HSV thresholds and rendered diagnostics use the legacy 0..255 intensity domain. | `Any` | `IMAGE_NONFINITE_INPUT` | `E2_OPERATOR_AND_PACKAGE_TESTS` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`7FC15AEA6072ED2E8E4B972492A39FA48688B2F8FC45DD2FF6A941FBBBC54EC7`
- `type:ClearVision.Product.Infrastructure.Operators.ColorDetectionImageContractProvider`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Channel1` | `Any` | 源码输出字典初始化中可见字段。 |
| `Channel2` | `Any` | 源码输出字典初始化中可见字段。 |
| `Channel3` | `Any` | 源码输出字典初始化中可见字段。 |
| `GrayWorldDeviation` | `Any` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Hex` | `Any` | 源码输出字典初始化中可见字段。 |
| `Hue` | `Any` | 源码输出字典初始化中可见字段。 |
| `MatchedPixels` | `Integer` | 源码输出字典初始化中可见字段。 |
| `MeanB` | `Float` | 源码输出字典初始化中可见字段。 |
| `MeanG` | `Float` | 源码输出字典初始化中可见字段。 |
| `MeanR` | `Float` | 源码输出字典初始化中可见字段。 |
| `Mode` | `String` | 源码输出字典初始化中可见字段。 |
| `Percentage` | `Any` | 源码输出字典初始化中可见字段。 |
| `PrimaryData` | `Any` | 源码输出字典初始化中可见字段。 |
| `Rank` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ReferenceA` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ReferenceB` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ReferenceL` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ReferenceProvided` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Saturation` | `Float` | 源码输出字典初始化中可见字段。 |
| `Summary` | `Any` | 源码输出字典初始化中可见字段。 |
| `TotalPixels` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

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
| 2.0.1 | 2026-07-16 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
