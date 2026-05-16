# Min Enclosing Geometry / MinEnclosingGeometry

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MinEnclosingGeometryOperator` |
| 枚举值 (Enum) | `OperatorType.MinEnclosingGeometry` |
| 分类 (Category) | 检测 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Computes minimum enclosing geometry (circle, rectangle, triangle) and robust arc fitting with RANSAC。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Contour-derived enclosing geometry and robust fitting` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 10 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `Threshold -> FindContours -> contour selection -> MinEnclosingCircle/MinAreaRect/ConvexHull/RANSAC fit`
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Threshold`
- `Cv2.FindContours`
- `Cv2.ContourArea`
- `Cv2.DrawContours`
- `Cv2.MinEnclosingCircle`
- `Cv2.Circle`
- `Cv2.PutText`
- `Cv2.MinAreaRect`
- `Cv2.Line`
- `Cv2.ConvexHull`
- `Cv2.ArcLength`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Operation` | Operation | `enum` | SmallestCircle | SmallestCircle/Smallest Enclosing Circle；MinAreaRect/Minimum Area Rectangle；MinAreaTriangle/Minimum Area Triangle；ConvexHull/Convex Hull；FitArc/Fit Arc (RANSAC)；FitCircleRobust/Fit Circle (Robust)；FitEllipseDirect/Fit Ellipse (Direct) | Yes | - |
| `Threshold` | Binary Threshold | `double` | 127 | [0, 255] | Yes | - |
| `MinArea` | Min Contour Area | `int` | 100 | >= 0 | Yes | - |
| `ContourSelection` | Contour Selection | `enum` | LargestContour | LargestContour/Largest Contour；AllContours/All Contours；FirstContour/First Contour | Yes | - |
| `RansacIterations` | RANSAC Iterations | `int` | 500 | [10, 5000] | Yes | - |
| `RansacInlierThreshold` | RANSAC Inlier Threshold (px) | `double` | 2 | [0.1, 50] | Yes | - |
| `MinArcAngle` | Min Arc Angle (degrees) | `double` | 30 | [5, 350] | Yes | - |
| `MaxArcAngle` | Max Arc Angle (degrees) | `double` | 330 | [10, 360] | Yes | - |
| `OutlierRatio` | Expected Outlier Ratio | `double` | 0.3 | [0, 0.9] | Yes | - |
| `CheckConditionNumber` | Check Condition Number | `bool` | true | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `GeometryResult` | Geometry Result | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Angle` | `Float` | 源码通过输出字典索引赋值写入。 |
| `ArcAngle` | `Float` | 源码通过输出字典索引赋值写入。 |
| `Area` | `Any` | 源码通过输出字典索引赋值写入。 |
| `AspectRatio` | `Float` | 源码通过输出字典索引赋值写入。 |
| `Center` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ConditionNumber` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ContourCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Convexity` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Eccentricity` | `Any` | 源码通过输出字典索引赋值写入。 |
| `EnclosedPoints` | `Any` | 源码通过输出字典索引赋值写入。 |
| `EnclosureRatio` | `Float` | 源码通过输出字典索引赋值写入。 |
| `EndAngle` | `Float` | 源码通过输出字典索引赋值写入。 |
| `EndPoint` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Error` | `Any` | 源码通过输出字典索引赋值写入。 |
| `FitQuality` | `Any` | 源码通过输出字典索引赋值写入。 |
| `GeometryType` | `String` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `HullArea` | `Any` | 源码通过输出字典索引赋值写入。 |
| `HullPerimeter` | `Any` | 源码通过输出字典索引赋值写入。 |
| `HullVertices` | `Any` | 源码通过输出字典索引赋值写入。 |
| `InlierCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `InlierRatio` | `Float` | 源码通过输出字典索引赋值写入。 |
| `IsValid` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `MajorAxis` | `Any` | 源码通过输出字典索引赋值写入。 |
| `MaxResidual` | `Float` | 源码通过输出字典索引赋值写入。 |
| `MeanResidual` | `Float` | 源码通过输出字典索引赋值写入。 |
| `MinorAxis` | `Float` | 源码通过输出字典索引赋值写入。 |
| `OutlierCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `PointCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Quality` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Radius` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Size` | `Any` | 源码通过输出字典索引赋值写入。 |
| `StartAngle` | `Float` | 源码通过输出字典索引赋值写入。 |
| `StartPoint` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Success` | `Any` | 源码通过输出字典索引赋值写入。 |
| `VertexCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `Vertices` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H + P log P + I*P) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by geometry operator tests |
| 内存特征 (Memory Profile) | O(W*H + P) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 2 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Measuring minimum enclosing circle, rotated rectangle, triangle, or convex hull for segmented parts.
- 适合 (Suitable)：Fitting circles, arcs, or ellipses when contour points are available and outliers are expected.
- 不适合 (Not Suitable)：Low-contrast scenes where threshold segmentation does not isolate the target contour.
- 不适合 (Not Suitable)：Metrology that requires calibrated subpixel edge extraction before geometry fitting.

## 已知限制 / Known Limitations
1. Contour extraction is threshold-based and uses external contours only.
2. Robust arc and circle fitting depend on RANSAC iteration and inlier-threshold parameters.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
5. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
