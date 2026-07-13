# 距离变换 / DistanceTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DistanceTransformOperator` |
| 枚举值 (Enum) | `OperatorType.DistanceTransform` |
| 分类 (Category) | 图像处理 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于计算每个像素到最近零像素的距离，支持多种距离度量和有符号距离。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `OpenCV binary distance transform` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 7 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `EnsureSingleChannelGray -> Threshold -> Cv2.DistanceTransform -> MinMaxLoc/Normalize/ApplyColorMap`
- `OperatorBase.Get*Param(...)`
- `Cv2.DistanceTransform`
- `Cv2.Threshold`
- `Cv2.BitwiseNot`
- `Cv2.MinMaxLoc`
- `Cv2.Normalize`
- `Cv2.ApplyColorMap`
- `Cv2.Circle`
- `Cv2.PutText`
- `Cv2.Mean`
- `Cv2.ConnectedComponentsWithStats`
- `Math.Abs`
- `Math.Min`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `DistanceType` | Distance Type | `enum` | Euclidean | Euclidean/Euclidean；Manhattan/Manhattan (L1)；Chessboard/Chessboard；C/3x3 C；L12/3x3 L12 | Yes | - |
| `MaskSize` | Mask Size | `int` | 5 | [3, 7] | Yes | - |
| `Signed` | Compute Signed Distance | `bool` | false | - | Yes | - |
| `Threshold` | Binary Threshold | `double` | 127 | [0, 255] | Yes | - |
| `Invert` | Invert Input | `bool` | false | - | Yes | - |
| `Normalize` | Normalize Output | `bool` | false | - | Yes | - |
| `MaxDistanceLimit` | Max Distance Limit (0=unlimited) | `double` | 0 | [0, 10000] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image (Binary or Grayscale) | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Distance Transform Result | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `DistanceMap` | Distance Map (Float) | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `MaxDistance` | Maximum Distance | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MaxLocation` | Maximum Distance Location | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `AccuracyReport` | `Any` | 源码输出字典初始化中可见字段。 |
| `Area` | `Any` | 源码输出字典初始化中可见字段。 |
| `AspectRatio` | `Float` | 源码输出字典初始化中可见字段。 |
| `AverageError` | `Any` | 源码通过输出字典索引赋值写入。 |
| `BoundingBox` | `Any` | 源码输出字典初始化中可见字段。 |
| `ComponentId` | `String` | 源码输出字典初始化中可见字段。 |
| `ComponentsWithinTolerance` | `Float` | 源码通过输出字典索引赋值写入。 |
| `Error` | `Any` | 源码输出字典初始化中可见字段。 |
| `ErrorRatio` | `Float` | 源码输出字典初始化中可见字段。 |
| `ExpectedMaxDistance` | `Float` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `ImageHeight` | `Integer` | 源码输出字典初始化中可见字段。 |
| `ImageWidth` | `Integer` | 源码输出字典初始化中可见字段。 |
| `InputBitDepth` | `Any` | 源码输出字典初始化中可见字段。 |
| `IsSigned` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `IsWithinTolerance` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `MaxError` | `Float` | 源码通过输出字典索引赋值写入。 |
| `MeanDistance` | `Float` | 源码输出字典初始化中可见字段。 |
| `MinDistance` | `Float` | 源码输出字典初始化中可见字段。 |
| `ProcessingTimeMs` | `Any` | 源码输出字典初始化中可见字段。 |
| `ShapeAnalyses` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ThresholdUsed` | `Any` | 源码输出字典初始化中可见字段。 |
| `Timestamp` | `Any` | 源码输出字典初始化中可见字段。 |
| `TotalComponents` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ValidationError` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `ValidationType` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by distance-transform unit tests |
| 内存特征 (Memory Profile) | O(W*H) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Binary-mask analysis that needs maximum inscribed distance, center candidates, or distance-map visualization.
- 适合 (Suitable)：Foreground/background signed-distance measurements after a stable threshold has isolated the target.
- 不适合 (Not Suitable)：Gray-scale distance analysis without first binarizing the image.
- 不适合 (Not Suitable)：High-throughput signed-distance workloads where the extra foreground/background transform and pixel loop dominate latency.

## 已知限制 / Known Limitations
1. Input is thresholded before distance computation, so result quality depends on Threshold and Invert parameters.
2. Parameter validation currently accepts standard mask sizes 3 and 5; precise-mask execution is not exposed through validation.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
