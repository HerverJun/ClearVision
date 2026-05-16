# Contour Extrema / ContourExtrema

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ContourExtremaOperator` |
| 枚举值 (Enum) | `OperatorType.ContourExtrema` |
| 分类 (Category) | 检测 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Finds extremal points of a contour in specified directions。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Directional contour extrema scan` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Contour`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Direction`、`ReferencePoint`。
- 当前元数据未声明参数，执行逻辑主要由输入数据和源码默认策略决定。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `contour points -> scalar projection/distance -> deterministic extrema ordering -> visualization`
- `Cv2.BoundingRect`
- `Cv2.Polylines`
- `Cv2.Circle`
- `Cv2.PutText`
- `Cv2.Line`
- `Math.Max`
- `Math.Round`
- `Math.Abs`
- `Math.Sqrt`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| - | - | - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Contour` | Input Contour (Points) | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Direction` | Search Direction | `String` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `ReferencePoint` | Reference Point (optional) | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `ExtremaPoints` | Extremal Points | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `MinPoint` | Minimum Point | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `MaxPoint` | Maximum Point | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Image` | Visualization | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `MinValue` | Minimum Value | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `MaxValue` | Maximum Value | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `ProcessingTimeMs` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N log N) |
| 典型耗时 (Typical Latency) | Avg 0.244 ms, max 1.171 ms over 22 synthetic golden cases |
| 内存特征 (Memory Profile) | O(N) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 2 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Finding left/right, top/bottom, or nearest/farthest points on a known contour.
- 适合 (Suitable)：Stable downstream measurement where deterministic tie-breaking matters for collinear or duplicate extrema.
- 不适合 (Not Suitable)：Extracting the contour from an image; use FindContours before this operator.
- 不适合 (Not Suitable)：Subpixel contour fitting or curvature extrema estimation beyond the provided contour points.

## 已知限制 / Known Limitations
1. Direction defaults to horizontal for unknown direction strings.
2. Distance mode requires a reference point and reports Euclidean distance extrema only.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
