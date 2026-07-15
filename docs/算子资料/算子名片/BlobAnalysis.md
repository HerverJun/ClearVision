# Blob分析 / BlobDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `BlobDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.BlobAnalysis` |
| 分类 ID (CategoryId) | `SegmentationAndRegion` |
| 分类 (Category) | 分割与区域 |
| 分类顺序 (CategoryOrder) | 3 |
| 版本 (Version) | `1.2.1` |
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
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:SegmentationAndRegion`, `分类显示:分割与区域`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于连通区域分析。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`SourceImage`。
- 参数解析覆盖 17 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.BitwiseAnd`
- `Cv2.CvtColor`
- `Cv2.Circle`
- `Cv2.BitwiseNot`
- `Cv2.ConnectedComponentsWithStats`
- `Cv2.Compare`
- `Cv2.FindContours`
- `Cv2.ArcLength`
- `Cv2.ContourArea`
- `Cv2.ConvexHull`
- `Cv2.Moments`
- `Cv2.MeanStdDev`
- `Cv2.DrawContours`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `MinArea` | 最小面积 | `int` | 100 | >= 0 | Yes | - |
| `MaxArea` | 最大面积 | `int` | 100000 | >= 0 | Yes | - |
| `Color` | 目标颜色 | `enum` | White | White/白色；Black/黑色 | Yes | - |
| `MinCircularity` | 最小圆度 | `double` | 0 | [0, 1] | Yes | - |
| `MinConvexity` | 最小凸度 | `double` | 0 | [0, 1] | Yes | - |
| `MinInertiaRatio` | 最小惯性比 | `double` | 0 | [0, 1] | Yes | - |
| `MinRectangularity` | 最小矩形度 | `double` | 0 | [0, 1] | Yes | - |
| `MinEccentricity` | 最小离心率 | `double` | 0 | [0, 1] | Yes | - |
| `OutputDetailedFeatures` | 输出详细特征 | `bool` | false | - | Yes | - |
| `FeatureFilter` | 特征过滤表达式 | `string` | "" | - | No | 可选。支持 Area、ContourArea、Perimeter、Circularity、Convexity、Rectangularity、Eccentricity、EulerNumber、MeanGray、GrayDeviation、Width、Height、X、Y、CenterX、CenterY、InertiaRatio、ConvexHullArea、HoleCount；示例：Area >= 100 && Circularity >= 0.8。留空不过滤。 |
| `EnableColorFilter` | 启用颜色过滤 | `bool` | false | - | Yes | 启用HSV颜色范围预过滤 |
| `HueLow` | 色相下限 | `int` | 0 | [0, 180] | Yes | - |
| `HueHigh` | 色相上限 | `int` | 180 | [0, 180] | Yes | - |
| `SatLow` | 饱和度下限 | `int` | 50 | [0, 255] | Yes | - |
| `SatHigh` | 饱和度上限 | `int` | 255 | [0, 255] | Yes | - |
| `ValLow` | 明度下限 | `int` | 50 | [0, 255] | Yes | - |
| `ValHigh` | 明度上限 | `int` | 255 | [0, 255] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 二值图像 | `Image` | Yes | 用于连通域分析的二值图或可自动阈值化的灰度图。 |
| `SourceImage` | 参考图像 | `Image` | No | 可选，仅作为标注结果的参考底图，不替代主 Image 输入。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 标记图像 | `Image` | 绘制 Blob 边界与中心点的可视化图像。 |
| `Blobs` | Blob结果列表 | `BlobList` | 兼容旧流程端口名的 Blob 结果字典列表，包含边界框、中心、面积及常用度量；不是 Contour 或 Region。 |
| `BlobFeatures` | Blob详细特征 | `BlobFeatureList` | 稳定输出的 Blob 详细特征列表：关闭详细特征时为空列表；开启时 Area、Circularity、CenterX 等旧字段保留在条目顶层，并提供 Features 嵌套别名。不是轮廓或像素区域。 |
| `BlobCount` | Blob数量 | `Integer` | 通过面积、形状与可选特征过滤后的 Blob 数量。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `FeatureFilter` | optional; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `BLOB_FEATURE_FILTER_OPTIONAL` |
| `HueHigh` | metadata; - | visible: -; hidden: ALL(EnableColorFilter == false) | enabled: -; disabled: ALL(EnableColorFilter == false) | ALL(EnableColorFilter == false) | - | - | `BLOB_HSV_ONLY_WITH_COLOR_FILTER` |
| `HueLow` | metadata; - | visible: -; hidden: ALL(EnableColorFilter == false) | enabled: -; disabled: ALL(EnableColorFilter == false) | ALL(EnableColorFilter == false) | - | - | `BLOB_HSV_ONLY_WITH_COLOR_FILTER` |
| `SatHigh` | metadata; - | visible: -; hidden: ALL(EnableColorFilter == false) | enabled: -; disabled: ALL(EnableColorFilter == false) | ALL(EnableColorFilter == false) | - | - | `BLOB_HSV_ONLY_WITH_COLOR_FILTER` |
| `SatLow` | metadata; - | visible: -; hidden: ALL(EnableColorFilter == false) | enabled: -; disabled: ALL(EnableColorFilter == false) | ALL(EnableColorFilter == false) | - | - | `BLOB_HSV_ONLY_WITH_COLOR_FILTER` |
| `ValHigh` | metadata; - | visible: -; hidden: ALL(EnableColorFilter == false) | enabled: -; disabled: ALL(EnableColorFilter == false) | ALL(EnableColorFilter == false) | - | - | `BLOB_HSV_ONLY_WITH_COLOR_FILTER` |
| `ValLow` | metadata; - | visible: -; hidden: ALL(EnableColorFilter == false) | enabled: -; disabled: ALL(EnableColorFilter == false) | ALL(EnableColorFilter == false) | - | - | `BLOB_HSV_ONLY_WITH_COLOR_FILTER` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |
| `SourceImage` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `Image` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |
| `SourceImage` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `SourceImage` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`896918CF9B07260F53FF2B19704EB5445C688CD7682AA9F2BADAA9AD1010962D`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Area` | `Any` | 源码通过输出字典索引赋值写入。 |
| `BlobId` | `String` | 源码通过输出字典索引赋值写入。 |
| `CenterX` | `Any` | 源码通过输出字典索引赋值写入。 |
| `CenterY` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Circularity` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ContourArea` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ConvexHullArea` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Convexity` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Eccentricity` | `Any` | 源码通过输出字典索引赋值写入。 |
| `EulerNumber` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Features` | `Any` | 源码通过输出字典索引赋值写入。 |
| `GrayDeviation` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `HoleCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Id` | `String` | 源码输出字典初始化中可见字段。 |
| `InertiaRatio` | `Float` | 源码通过输出字典索引赋值写入。 |
| `MeanGray` | `Float` | 源码通过输出字典索引赋值写入。 |
| `Perimeter` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Rectangularity` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Size` | `Any` | 源码输出字典初始化中可见字段。 |
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
- 执行失败契约：源码中发现 6 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
| 1.2.1 | 2026-07-16 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
