# 宽度测量 / WidthMeasurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `WidthMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.WidthMeasurement` |
| 分类 (Category) | 检测 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:测量`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Measures width between parallel edges or lines。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Line1`、`Line2`。
- 参数解析覆盖 8 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.CvtColor`
- `Cv2.Canny`
- `Cv2.HoughLinesP`
- `Cv2.Line`
- `Cv2.Circle`
- `Cv2.PutText`
- `Math.Clamp`
- `Math.Abs`
- `Math.Sqrt`
- `Math.Max`
- `Math.Floor`
- `Math.Ceiling`
- `Math.PI`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `MeasureMode` | Measure Mode | `enum` | AutoEdge | AutoEdge/AutoEdge；ManualLines/ManualLines | Yes | - |
| `SampleCount` | Sample Count | `int` | 24 | [10, 256] | Yes | - |
| `Direction` | Direction | `enum` | Perpendicular | Perpendicular/Perpendicular；Custom/Custom | Yes | - |
| `CustomAngle` | Custom Angle | `double` | 0 | [-180, 180] | Yes | - |
| `RobustMode` | Robust Mode | `bool` | true | - | Yes | - |
| `OutlierSigmaK` | Outlier Sigma K | `double` | 3 | [0.5, 10] | Yes | - |
| `MinValidSamples` | Min Valid Samples | `int` | 0 | [0, 256] | Yes | - |
| `MultiScanCount` | Multi Scan Count | `int` | 24 | [10, 256] | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Line1` | Line 1 | `LineData` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `Line2` | Line 2 | `LineData` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Width` | Width | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MeanWidth` | Mean Width | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MinWidth` | Min Width | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MaxWidth` | Max Width | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `P95Width` | P95 Width | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `StdDev` | StdDev | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ValidSampleRate` | Valid Sample Rate | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Confidence` | `Float` | 源码输出字典初始化中可见字段。 |
| `ExecutedScanCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `ImageHeight` | `Integer` | 由带自定义尺寸字段名的图像输出封装附加。 |
| `ImageWidth` | `Integer` | 由带自定义尺寸字段名的图像输出封装附加。 |
| `RefinedSampleCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `StatusCode` | `Any` | 源码输出字典初始化中可见字段。 |
| `StatusMessage` | `String` | 源码输出字典初始化中可见字段。 |
| `UncertaintyPx` | `Any` | 源码输出字典初始化中可见字段。 |
| `ValidSampleCount` | `Integer` | 源码输出字典初始化中可见字段。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 多数图像路径近似 `O(W*H)`；涉及轮廓、匹配或排序时会叠加候选数量相关开销。 |
| 典型耗时 (Typical Latency) | 未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。 |
| 内存特征 (Memory Profile) | 通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 8 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
