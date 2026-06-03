# Pixel To World Transform / PixelToWorldTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PixelToWorldTransformOperator` |
| 枚举值 (Enum) | `OperatorType.PixelToWorldTransform` |
| 分类 (Category) | 标定 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Transforms coordinates via CalibrationBundleV2 using either Transform2D or camera ray-plane intersection。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Image`、`Points`、`CalibrationData`。
- 参数解析覆盖 7 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Circle`
- `Cv2.PutText`
- `Cv2.UndistortPoints`
- `Cv2.FishEye`
- `Cv2.ProjectPoints`
- `Cv2.Invert`
- `JsonDocument.Parse`
- `Math.Abs`
- `Math.Min`
- `Math.Sqrt`
- `Math.Clamp`
- `Math.Floor`
- `Math.Ceiling`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TransformMode` | Transform Mode | `enum` | PixelToWorld | PixelToWorld/Pixel to World；WorldToPixel/World to Pixel | Yes | - |
| `WorldPlaneZ` | World Plane Z (mm) | `double` | 0 | - | Yes | - |
| `UnitScale` | Unit Scale (mm per unit) | `double` | 1 | [0.0001, 10000] | Yes | - |
| `InputPointX` | Input Point X (Single Point Mode) | `double` | 0 | - | Yes | - |
| `InputPointY` | Input Point Y (Single Point Mode) | `double` | 0 | - | Yes | - |
| `UseDistortion` | Use Distortion Model | `bool` | true | - | Yes | - |
| `GenerateReport` | Generate Accuracy Report | `bool` | true | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image (Optional) | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Points` | Input Points | `PointList` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `CalibrationData` | Calibration Bundle V2 JSON | `String` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Visualization Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `TransformedPoints` | Transformed Points | `PointList` | 点集结果，可连接几何测量、定位或标定相关节点。 |
| `TransformResult` | Transform Result Details | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `AccuracyReport` | `Any` | 源码通过输出字典索引赋值写入。 |
| `CalibrationKind` | `Float` | 源码通过输出字典索引赋值写入。 |
| `Diagnostics` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `InputCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `InputPoints` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Model` | `String` | 源码通过输出字典索引赋值写入。 |
| `OutputCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `OutputPointDimension` | `Any` | 源码通过输出字典索引赋值写入。 |
| `OutputPoints` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Path` | `String` | 源码通过输出字典索引赋值写入。 |
| `RoundTripErrors` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RoundTripMax` | `Float` | 源码通过输出字典索引赋值写入。 |
| `RoundTripMean` | `Float` | 源码通过输出字典索引赋值写入。 |
| `RoundTripP95` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RoundTripRmse` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RoundTripUnit` | `String` | 源码通过输出字典索引赋值写入。 |
| `SourceFrame` | `String` | 源码通过输出字典索引赋值写入。 |
| `TargetFrame` | `Any` | 源码通过输出字典索引赋值写入。 |
| `TimestampUtc` | `Any` | 源码通过输出字典索引赋值写入。 |
| `TransformedPlanarPoints` | `Any` | 源码通过输出字典索引赋值写入。 |
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
- 适合 (Suitable)：相机、坐标、像素到世界坐标或工装几何关系需要被显式建模和复用的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
