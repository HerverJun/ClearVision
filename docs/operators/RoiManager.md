# ROI裁剪与掩膜 / RoiManager

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RoiManagerOperator` |
| 枚举值 (Enum) | `OperatorType.RoiManager` |
| 分类 (Category) | 辅助 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于按矩形、圆形或多边形 ROI 裁剪图像或应用掩膜，并输出空间上下文。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 10 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Rectangle`
- `Cv2.BitwiseAnd`
- `Cv2.Circle`
- `Cv2.FillPoly`
- `JsonDocument.Parse`
- `JsonSerializer.Deserialize`
- `Math.Max`
- `Math.Min`
- `Math.Clamp`
- `Math.Round`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Shape` | 形状 | `enum` | Rectangle | Rectangle/矩形；Circle/圆形；Polygon/多边形 | Yes | - |
| `Operation` | 操作 | `enum` | Crop | Crop/裁剪；Mask/掩膜 | Yes | - |
| `X` | X | `int` | 0 | >= 0 | Yes | - |
| `Y` | Y | `int` | 0 | >= 0 | Yes | - |
| `Width` | 宽度 | `int` | 200 | >= 1 | Yes | - |
| `Height` | 高度 | `int` | 200 | >= 1 | Yes | - |
| `CenterX` | 圆心X | `int` | 100 | - | Yes | - |
| `CenterY` | 圆心Y | `int` | 100 | - | Yes | - |
| `Radius` | 半径 | `int` | 50 | >= 1 | Yes | - |
| `PolygonPoints` | 多边形顶点(JSON) | `string` | [[10,10],[200,10],[200,200],[10,200]] | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | ROI图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Mask` | 掩膜 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `SpatialContext` | 空间上下文 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `ParentHeight` | `Integer` | 源码输出字典初始化中可见字段。 |
| `ParentWidth` | `Integer` | 源码输出字典初始化中可见字段。 |

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
| 1.0.0 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
