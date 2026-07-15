# 测量 / MeasureDistance

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MeasureDistanceOperator` |
| 枚举值 (Enum) | `OperatorType.Measurement` |
| 分类 ID (CategoryId) | `Measurement` |
| 分类 (Category) | 测量 |
| 分类顺序 (CategoryOrder) | 7 |
| 版本 (Version) | `1.1.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | No |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| 标签 (Tags) | `分类:Measurement`, `分类显示:测量`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于统一基础二维几何测量入口，支持点点距离、点线距离、线线距离/夹角和三点角度；默认保持旧版点点测量行为。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Image`、`PointA`、`PointB`、`PointC`、`Line1`、`Line2`。
- 参数解析覆盖 8 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Line`
- `Cv2.Circle`
- `Cv2.PutText`
- `Math.Abs`
- `Math.PI`
- `Math.Clamp`
- `Math.Round`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `X1` | 起点X | `int` | 0 | - | Yes | - |
| `Y1` | 起点Y | `int` | 0 | - | Yes | - |
| `X2` | 终点X | `int` | 100 | - | Yes | - |
| `Y2` | 终点Y | `int` | 100 | - | Yes | - |
| `MeasureType` | 测量类型 | `enum` | PointToPoint | PointToPoint/点到点；Horizontal/水平距离；Vertical/垂直距离；PointToLine/点到线；LineToLine/线到线；ThreePointAngle/三点角度 | Yes | 默认 PointToPoint 保持旧流程。 |
| `DistanceModel` | 线距离模型 | `enum` | Segment | Segment/线段；InfiniteLine/无限直线 | Yes | - |
| `ParallelThreshold` | 平行阈值(度) | `double` | 2 | [0, 45] | Yes | - |
| `AngleUnit` | 角度单位 | `enum` | Degree | Degree/度；Radian/弧度 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PointA` | 点A/待测点 | `Point` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PointB` | 点B/角度顶点 | `Point` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `PointC` | 点C | `Point` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Line1` | 线1 | `LineData` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Line2` | 线2 | `LineData` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Distance` | 测量距离 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `DeltaX` | 水平分量 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `DeltaY` | 垂直分量 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Angle` | 夹角 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Value` | 主测量值 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Unit` | 单位 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `MeasurementType` | 实际测量类型 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `StatusCode` | 状态码 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `StatusMessage` | 状态信息 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `FootPoint` | 垂足 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Intersection` | 交点 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `HasIntersection` | 是否相交 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `IsParallel` | 是否平行 | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Confidence` | 测量置信度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `UncertaintyPx` | 输入几何像素不确定度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `UncertaintyDeg` | 角度不确定度（度） | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `AngleUnit` | required; - | visible: -; hidden: ALL(MeasureType != ThreePointAngle) | enabled: -; disabled: ALL(MeasureType != ThreePointAngle) | ALL(MeasureType != ThreePointAngle) | - | - | `MEASUREMENT_ANGLE_UNIT_ONLY_FOR_ANGLE` |
| `DistanceModel` | metadata; - | visible: -; hidden: ANY(MeasureType == PointToPoint \|\| MeasureType == Horizontal \|\| MeasureType == Vertical \|\| MeasureType == ThreePointAngle) | enabled: -; disabled: ANY(MeasureType == PointToPoint \|\| MeasureType == Horizontal \|\| MeasureType == Vertical \|\| MeasureType == ThreePointAngle) | ANY(MeasureType == PointToPoint \|\| MeasureType == Horizontal \|\| MeasureType == Vertical \|\| MeasureType == ThreePointAngle) | - | - | `MEASUREMENT_DISTANCE_MODEL_ONLY_FOR_LINE_DISTANCE` |
| `MeasureType` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `MEASUREMENT_TYPE` |
| `ParallelThreshold` | metadata; - | visible: -; hidden: ALL(MeasureType != LineToLine) | enabled: -; disabled: ALL(MeasureType != LineToLine) | ALL(MeasureType != LineToLine) | - | - | `MEASUREMENT_PARALLEL_THRESHOLD_ONLY_FOR_LINE_TO_LINE` |
| `X1` | metadata; - | visible: -; hidden: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | enabled: -; disabled: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | - | - | `MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE` |
| `X2` | metadata; - | visible: -; hidden: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | enabled: -; disabled: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | - | - | `MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE` |
| `Y1` | metadata; - | visible: -; hidden: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | enabled: -; disabled: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | - | - | `MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE` |
| `Y2` | metadata; - | visible: -; hidden: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | enabled: -; disabled: ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | ANY(MeasureType == PointToLine \|\| MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | - | - | `MEASUREMENT_COORDINATES_ONLY_FOR_POINT_DISTANCE` |

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
| `Angle` | ANY(MeasureType == LineToLine \|\| MeasureType == ThreePointAngle) | `MEASUREMENT_ANGLE_OUTPUT` |
| `DeltaX` | ANY(MeasureType == PointToPoint \|\| MeasureType == Horizontal \|\| MeasureType == Vertical \|\| MeasureType == PointToLine) | `MEASUREMENT_DELTA_OUTPUT` |
| `DeltaY` | ANY(MeasureType == PointToPoint \|\| MeasureType == Horizontal \|\| MeasureType == Vertical \|\| MeasureType == PointToLine) | `MEASUREMENT_DELTA_OUTPUT` |
| `Distance` | ANY(MeasureType == PointToPoint \|\| MeasureType == Horizontal \|\| MeasureType == Vertical \|\| MeasureType == PointToLine \|\| MeasureType == LineToLine) | `MEASUREMENT_DISTANCE_OUTPUT` |
| `FootPoint` | ALL(MeasureType == PointToLine) | `MEASUREMENT_FOOT_POINT_OUTPUT` |
| `HasIntersection` | ALL(MeasureType == LineToLine) | `MEASUREMENT_INTERSECTION_OUTPUT` |
| `Intersection` | ALL(MeasureType == LineToLine) | `MEASUREMENT_INTERSECTION_OUTPUT` |
| `IsParallel` | ALL(MeasureType == LineToLine) | `MEASUREMENT_PARALLEL_OUTPUT` |
| `UncertaintyDeg` | ALL(MeasureType == ThreePointAngle) | `MEASUREMENT_ANGLE_UNCERTAINTY_OUTPUT` |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`636C65F419283A5408BC375DDDA88251AE3BF6185D8082F8ABD9392CC4BCE209`
- `type:ClearVision.Product.Infrastructure.Operators.MeasurementGeometryHelper`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `FootPointX` | `Any` | 源码通过输出字典索引赋值写入。 |
| `FootPointY` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Vertex` | `Any` | 源码通过输出字典索引赋值写入。 |
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
- 执行失败契约：源码中发现 14 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.1.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
