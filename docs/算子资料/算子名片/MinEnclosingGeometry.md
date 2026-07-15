# 最小外接几何体 / MinEnclosingGeometry

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MinEnclosingGeometryOperator` |
| 枚举值 (Enum) | `OperatorType.MinEnclosingGeometry` |
| 分类 ID (CategoryId) | `Measurement` |
| 分类 (Category) | 测量 |
| 分类顺序 (CategoryOrder) | 7 |
| 版本 (Version) | `1.0.1` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | No |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| 标签 (Tags) | `分类:Measurement`, `分类显示:测量`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于计算最小包围几何（圆、矩形、三角形），并支持基于 RANSAC 的鲁棒圆弧拟合。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
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
| `Operation` | 操作 | `enum` | SmallestCircle | SmallestCircle/最小包围圆；MinAreaRect/最小面积矩形；MinAreaTriangle/最小面积三角形；ConvexHull/凸包；FitArc/圆弧拟合（RANSAC）；FitCircleRobust/圆拟合（稳健）；FitEllipseDirect/椭圆拟合（直接法） | Yes | - |
| `Threshold` | 二值阈值 | `double` | 127 | [0, 255] | Yes | - |
| `MinArea` | 最小轮廓面积 | `int` | 100 | >= 0 | Yes | - |
| `ContourSelection` | 轮廓选择 | `enum` | LargestContour | LargestContour/最大轮廓；AllContours/全部轮廓；FirstContour/第一条轮廓 | Yes | - |
| `RansacIterations` | RANSAC迭代次数 | `int` | 500 | [10, 5000] | Yes | - |
| `RansacInlierThreshold` | RANSAC内点阈值（px） | `double` | 2 | [0.1, 50] | Yes | - |
| `MinArcAngle` | 最小圆弧角（度） | `double` | 30 | [5, 350] | Yes | - |
| `MaxArcAngle` | 最大圆弧角（度） | `double` | 330 | [10, 360] | Yes | - |
| `OutlierRatio` | 期望离群比例 | `double` | 0.3 | [0, 0.9] | Yes | - |
| `CheckConditionNumber` | 检查条件数 | `bool` | true | - | Yes | - |

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

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

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
- 组合指纹 (Generation Fingerprint)：`5B1D43A4379040CE39667F5AD29137B4432AF2B34E6A0E8C2F9CBBEDB59C92A4`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

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
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
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
| 1.0.1 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
