# 表面缺陷检测 / SurfaceDefectDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SurfaceDefectDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.SurfaceDefectDetection` |
| 分类 (Category) | AI检测 |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `surface-defect`, `功能域:AI`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于使用梯度、配准后的参考差分或局部对比度检测表面缺陷。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Reference`。
- 参数解析覆盖 24 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CountNonZero`
- `Cv2.GetStructuringElement`
- `Cv2.FindContours`
- `Cv2.MeanStdDev`
- `Cv2.ContourArea`
- `Cv2.DrawContours`
- `Cv2.BoundingRect`
- `Cv2.Rectangle`
- `Cv2.PutText`
- `Cv2.Sobel`
- `Cv2.Magnitude`
- `Cv2.Absdiff`
- `Cv2.Resize`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Method` | Method | `enum` | GradientMagnitude | GradientMagnitude/GradientMagnitude；ReferenceDiff/ReferenceDiff；LocalContrast/LocalContrast | Yes | - |
| `Threshold` | Threshold | `double` | 35 | [0, 255] | Yes | - |
| `MinArea` | Min Area | `int` | 20 | [0, 10000000] | Yes | - |
| `MaxArea` | Max Area | `int` | 1000000 | [0, 10000000] | Yes | - |
| `MorphCleanSize` | Morph Clean Size | `int` | 3 | [1, 301] | Yes | - |
| `MorphMode` | Morph Mode | `enum` | OpenClose | None/None；OpenClose/Open then close；CloseOpen/Close then open；CloseOnly/Close only | Yes | - |
| `AlignmentMode` | Alignment Mode | `enum` | PhaseCorrelation | None/None；PhaseCorrelation/PhaseCorrelation | Yes | - |
| `NormalizationMode` | Normalization Mode | `enum` | LocalMean | None/None；LocalMean/LocalMean；ClaheLocalMean/CLAHE + LocalMean | Yes | - |
| `ThresholdMode` | Threshold Mode | `enum` | Auto | Auto/Auto；Manual/Manual；Otsu/Otsu；ReferenceStats/ReferenceStats | Yes | - |
| `BackgroundKernelSize` | Background Kernel Size | `int` | 31 | [3, 301] | Yes | - |
| `ClaheClipLimit` | CLAHE Clip Limit | `double` | 2 | [0.1, 40] | Yes | - |
| `ClaheTileGridSize` | CLAHE Tile Grid Size | `int` | 8 | [2, 64] | Yes | - |
| `ReferenceStatsSigma` | Reference Stats Sigma | `double` | 2.5 | [0.1, 10] | Yes | - |
| `RobustReferenceStats` | Robust Reference Stats | `bool` | false | - | Yes | - |
| `ResponseNormalizeMode` | Response Normalize Mode | `enum` | RawClamp | RawClamp/Raw clamp；MinMax/Min/max；PercentileClip/Percentile clip | Yes | - |
| `ComponentFilterMode` | Component Filter Mode | `enum` | AreaOnly | AreaOnly/Area only；ResponseStats/Response statistics；ShapeAndResponseStats/Shape and response statistics | Yes | - |
| `SmallNoiseAreaMax` | Small Noise Area Max | `int` | 0 | [0, 10000000] | Yes | - |
| `MinElongationForSmallComponent` | Min Elongation For Small Component | `double` | 0 | [0, 50] | Yes | - |
| `CompactNoiseAreaMax` | Compact Noise Area Max | `int` | 0 | [0, 10000000] | Yes | - |
| `CompactNoiseCircularityMin` | Compact Noise Circularity Min | `double` | 0 | [0, 1] | Yes | - |
| `CompactNoiseFillRatioMin` | Compact Noise Fill Ratio Min | `double` | 0 | [0, 1] | Yes | - |
| `MinLocalResponseProminence` | Min Local Response Prominence | `double` | 0 | [0, 255] | Yes | - |
| `EnableCandidateProfile` | Enable Candidate Profile | `bool` | false | - | Yes | - |
| `CandidateProfile` | Candidate Profile | `enum` | default | default/Default；taxonomy_v2/Surface taxonomy v2 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Reference` | Reference | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `DefectMask` | Defect Mask | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ResponseImage` | Response Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `DefectCount` | Defect Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `DefectArea` | Defect Area | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `AlignmentScore` | Alignment Score | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `RejectedReason` | Rejected Reason | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `Diagnostics` | Diagnostics | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `AcceptedCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `AlignmentShiftX` | `Any` | 源码输出字典初始化中可见字段。 |
| `AlignmentShiftY` | `Any` | 源码输出字典初始化中可见字段。 |
| `AppliedThreshold` | `Any` | 源码输出字典初始化中可见字段。 |
| `CandidateAreaAfterMorph` | `Any` | 源码输出字典初始化中可见字段。 |
| `CandidateAreaBeforeMorph` | `Any` | 源码输出字典初始化中可见字段。 |
| `CandidateCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `CandidateProfileApplied` | `Any` | 源码输出字典初始化中可见字段。 |
| `CandidateProfileEnabled` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `ComponentCompactNoiseRejectedCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `ComponentLocalProminenceRejectedCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `ComponentMeanGate` | `Float` | 源码输出字典初始化中可见字段。 |
| `ComponentPeakGate` | `Any` | 源码输出字典初始化中可见字段。 |
| `ComponentRejectedCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `ComponentResponseRejectedCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `ComponentShapeRejectedCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `ResponseMean` | `Float` | 源码输出字典初始化中可见字段。 |
| `ResponseStdDev` | `Float` | 源码输出字典初始化中可见字段。 |
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
- 执行失败契约：源码中发现 5 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
4. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
