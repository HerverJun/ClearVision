# 形态学（旧版） / Morphology (Legacy)

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MorphologyOperator` |
| 枚举值 (Enum) | `OperatorType.Morphology` |
| 分类 (Category) | Preprocessing |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
本算子是形态学操作的旧版实现，用于保持与已有工作流的向后兼容。其底层算法与 `MorphologicalOperationOperator` 完全相同，均基于结构元素对图像做集合运算。

支持的操作类型包括：Erode（腐蚀）、Dilate（膨胀）、Open（开运算）、Close（闭运算）、Gradient（形态学梯度）、TopHat（顶帽）、BlackHat（黑帽）。

与新版算子的关键区别：本算子使用单一 `KernelSize` 参数（正方形核），而非独立的 KernelWidth / KernelHeight。这意味着无法创建非正方形结构元素。

> English: This is the legacy morphology operator kept for backward compatibility with existing flows. The underlying algorithm is identical to `MorphologicalOperationOperator`. Key difference: uses a single `KernelSize` parameter (square kernel only) instead of separate KernelWidth / KernelHeight. New flows should prefer `MorphologicalOperationOperator`.

## 实现策略 / Implementation Strategy
- 与 `MorphologicalOperationOperator` 共享同一个 `MorphologyExecutionHelper.Execute()` 辅助类，内部调用 `Cv2.GetStructuringElement` + `Cv2.MorphologyEx`。
- 本算子将 `KernelSize` 同时传入辅助器的 `kernelWidth` 和 `kernelHeight` 参数，强制为正方形核。
- 源码中包含一次性警告日志（`LogLegacyUsageOnce`），首次执行时会通过 `Logger.LogWarning` 提示开发者迁移到新版算子。该警告使用 `Interlocked.Exchange` 保证多线程下仅输出一次。
- 输出附加数据中包含 `LegacyCompatible = true` 标记，下游算子可通过该标记判断数据来源。
- Tags 中标记了 `Legacy`、`Deprecated`、`Compatibility`、`ImageOnly`，表明这是仅用于图像工作流的兼容性节点。

> English: Shares `MorphologyExecutionHelper.Execute()` with the new operator. Passes `KernelSize` as both width and height, enforcing square kernels. Logs a one-time legacy warning via `Interlocked.Exchange`. Output includes `LegacyCompatible = true` flag. Tagged as `Legacy`, `Deprecated`, `Compatibility`, `ImageOnly`.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `LogLegacyUsageOnce()` -- 首次执行时记录一次性警告
   - `Interlocked.Exchange(ref _legacyWarningLogged, 1)`
   - `Logger.LogWarning("[MorphologyOperator] Legacy node is kept for compatibility...")`
3. `GetStringParam(@operator, "Operation", "Erode")` -- 读取操作类型
4. `GetIntParam(@operator, "KernelSize", 3, min: 1, max: 51)` -- 读取核大小
5. `GetStringParam(@operator, "KernelShape", "Rect")` -- 读取结构元素形状
6. `GetIntParam(@operator, "Iterations", 1, min: 1, max: 10)` -- 读取迭代次数
7. `GetIntParam(@operator, "AnchorX", -1)` / `GetIntParam(@operator, "AnchorY", -1)` -- 读取锚点
8. `MorphologyExecutionHelper.Execute(src, operation, kernelShape, kernelSize, kernelSize, iterations, anchorX, anchorY)` -- 委托执行（kernelSize 同时用于宽和高）
   - `Cv2.GetStructuringElement(shape, new Size(kernelSize, kernelSize), anchor)`
   - `Cv2.MorphologyEx(src, dst, morphType, kernel, anchor, iterations)`
9. `CreateImageOutput(dst, additionalData)` -- 封装输出，附带 LegacyCompatible 标记

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `string` | `"Erode"` | Erode, Dilate, Open, Close, Gradient, TopHat, BlackHat | 形态学操作类型。默认为 Erode（与新版的 Close 不同）。 |
| `KernelSize` | `int` | `3` | [1, 51] | 结构元素边长（像素）。同时用于宽度和高度，强制正方形核。 |
| `KernelShape` | `string` | `"Rect"` | Rect, Cross, Ellipse | 结构元素形状。 |
| `Iterations` | `int` | `1` | [1, 10] | 操作重复执行次数。 |
| `AnchorX` | `int` | `-1` | - | 结构元素锚点 X 坐标。-1 表示锚点在核中心。 |
| `AnchorY` | `int` | `-1` | - | 结构元素锚点 Y 坐标。-1 表示锚点在核中心。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待处理的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 形态学操作后的输出图像，尺寸与输入相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |
| `Operation` | `String` | 实际执行的形态学操作名称。 |
| `KernelShape` | `String` | 实际使用的结构元素形状。 |
| `KernelSize` | `String` | 实际核大小，格式为 "宽x高"（如 "3x3"）。 |
| `Iterations` | `Integer` | 实际执行的迭代次数。 |
| `LegacyCompatible` | `Boolean` | 固定为 `true`，标识数据来源为旧版算子。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N * k^2)，与 `MorphologicalOperationOperator` 相同。 |
| 典型耗时 (Typical Latency) | 与 `MorphologicalOperationOperator` 相同。首次执行因日志记录有微小额外开销。 |
| 内存特征 (Memory Profile) | 与 `MorphologicalOperationOperator` 相同。输出附加数据略多（包含 LegacyCompatible 标记）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：已有工作流中使用了 `MorphologyOperator` 的旧版流程，迁移到新版算子前的兼容性运行。
- **适合 (Suitable)**：只需要正方形核且不需要独立宽高控制的简单形态学操作。
- **不适合 (Not Suitable)**：新设计的工作流，应直接使用 `MorphologicalOperationOperator`。
- **不适合 (Not Suitable)**：需要非正方形结构元素（如 1x15 用于方向性特征处理）的场景。
- **不适合 (Not Suitable)**：区域（Region）工作流，本算子仅适用于图像工作流（ImageOnly）。

## 已知限制 / Known Limitations
1. 本算子已被标记为 Deprecated，新工作流不应使用。功能由 `MorphologicalOperationOperator` 完全覆盖。
2. 仅支持正方形核，无法创建非正方形结构元素。新版算子通过独立的 KernelWidth / KernelHeight 解决了此限制。
3. Operation 和 KernelShape 参数类型为 `string` 而非 `enum`，面板上不会显示下拉选项列表，用户需要手动输入操作名称。
4. 默认操作为 Erode，与新版算子的默认 Close 不同，迁移时需注意参数差异。
5. 多线程环境下首次执行会输出一条警告日志，该日志仅输出一次，不会影响后续执行。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充算法原理（与新版算子的区别）、实现策略（Legacy 兼容机制、一次性警告、LegacyCompatible 标记）、完整参数语义、API 调用链、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
