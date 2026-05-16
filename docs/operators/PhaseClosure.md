# Phase Closure / PhaseClosure

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PhaseClosureOperator` |
| 枚举值 (Enum) | `OperatorType.PhaseClosure` |
| 分类 (Category) | 检测 |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Unwraps wrapped phase maps while preserving the original phase domain semantics。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Itoh/quality-guided phase unwrapping` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`PhaseImage`；缺失时通常返回失败结果。
- 可选输入用于覆盖或补充参数配置：`Wavelength`、`UnwrapMethod`、`QualityMap`。
- 当前元数据未声明参数，执行逻辑主要由输入数据和源码默认策略决定。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `ImageWrapper -> wrapped CV_32F phase -> Itoh/quality/floodfill unwrap -> discontinuity map`
- `Cv2.MinMaxLoc`
- `Cv2.Sobel`
- `Cv2.Magnitude`
- `Cv2.Add`
- `Cv2.Divide`
- `Cv2.MeanStdDev`
- `Cv2.CvtColor`
- `Cv2.AddWeighted`
- `Cv2.PutText`
- `Cv2.Normalize`
- `Cv2.ApplyColorMap`
- `Math.PI`
- `Math.Atan2`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| - | - | - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PhaseImage` | Wrapped Phase Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Wavelength` | Wavelength (nm) | `Float` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `UnwrapMethod` | Unwrapping Method | `String` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `QualityMap` | Quality Map (optional) | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `UnwrappedPhase` | Unwrapped Phase | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Discontinuities` | Phase Discontinuities | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `Quality` | Quality Metric | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Image` | Visualization | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `Method` | `Any` | 源码输出字典初始化中可见字段。 |
| `ProcessingTimeMs` | `Any` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H log(W*H)) for quality-guided mode, O(W*H) for Itoh/floodfill |
| 典型耗时 (Typical Latency) | Avg 1.429 ms, max 4.590 ms over 22 synthetic golden cases |
| 内存特征 (Memory Profile) | O(W*H) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Smooth wrapped phase maps whose adjacent phase step stays within the unwrap assumptions.
- 适合 (Suitable)：Interferometry-style inspection where a discontinuity map and quality metric are needed with the unwrapped phase.
- 不适合 (Not Suitable)：Severely noisy phase maps without masking or preprocessing.
- 不适合 (Not Suitable)：Topology-heavy phase fields that require branch-cut optimization or domain-specific residue handling.

## 已知限制 / Known Limitations
1. Quality-guided mode uses a local gradient-derived quality map when no external map is provided.
2. The current output quality is a stability heuristic, not a calibrated metrology uncertainty.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
