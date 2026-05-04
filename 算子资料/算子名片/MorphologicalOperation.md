# 形态学操作 / MorphologicalOperation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MorphologicalOperationOperator` |
| 枚举值 (Enum) | `OperatorType.MorphologicalOperation` |
| 分类 (Category) | Preprocessing |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
形态学操作是基于形状的图像处理方法，使用一个称为"结构元素"（structuring element）的小矩阵在图像上滑动，根据邻域像素的集合运算结果修改中心像素。该算子支持以下 7 种操作：

- **Erode（腐蚀）**：取窗口内像素最小值。效果：白色区域收缩，小的白色噪点被消除。
- **Dilate（膨胀）**：取窗口内像素最大值。效果：白色区域扩展，小的黑色孔洞被填充。
- **Open（开运算）**：先腐蚀后膨胀。效果：去除小的白色噪点，同时保持大物体形状基本不变。
- **Close（闭运算）**：先膨胀后腐蚀。效果：填充小的黑色孔洞和缝隙，同时保持大物体形状基本不变。
- **Gradient（形态学梯度）**：膨胀图减去腐蚀图。效果：提取物体边缘轮廓。
- **TopHat（顶帽）**：原图减去开运算结果。效果：提取比周围亮的局部区域（亮细节）。
- **BlackHat（黑帽）**：闭运算结果减去原图。效果：提取比周围暗的局部区域（暗细节）。

结构元素形状支持矩形（Rect）、十字形（Cross）和椭圆形（Ellipse），不同形状影响邻域像素的选取方式。

> English: Morphological operations are shape-based image processing methods using a structuring element. This operator supports 7 operations: Erode (min), Dilate (max), Open (erode then dilate, removes small bright noise), Close (dilate then erode, fills small dark holes), Gradient (dilation minus erosion, extracts edges), TopHat (original minus open, extracts bright details), BlackHat (close minus original, extracts dark details). Three structuring element shapes are supported: Rect, Cross, and Ellipse.

## 实现策略 / Implementation Strategy
- 所有操作统一委托给 `MorphologyExecutionHelper.Execute()`，该辅助类内部调用 `Cv2.GetStructuringElement` 创建结构元素，再调用 `Cv2.MorphologyEx` 执行操作。
- `Cv2.MorphologyEx` 是 OpenCV 的统一形态学入口，内部会根据操作类型自动选择最优实现（例如开/闭运算会使用与核大小无关的优化算法）。
- 支持独立设置核宽和核高（KernelWidth / KernelHeight），允许创建非正方形结构元素，这在处理具有方向性特征的物体时很有用（例如水平裂缝用宽大于高的核）。
- 支持自定义锚点位置（AnchorX / AnchorY），默认 (-1, -1) 表示锚点在核中心。
- 相比旧版 `MorphologyOperator`，本算子将核宽和核高分离为独立参数，提供更精细的控制。

> English: All operations delegate to `MorphologyExecutionHelper.Execute()` which calls `Cv2.GetStructuringElement` + `Cv2.MorphologyEx`. Supports independent width/height for non-square structuring elements (useful for directional features) and custom anchor points. Compared to the legacy `MorphologyOperator`, this version separates kernel width and height into independent parameters for finer control.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetStringParam(@operator, "Operation", "Close")` -- 读取操作类型
3. `GetStringParam(@operator, "KernelShape", "Rect")` -- 读取结构元素形状
4. `GetIntParam(@operator, "KernelWidth", 3, min: 1, max: 51)` -- 读取核宽
5. `GetIntParam(@operator, "KernelHeight", 3, min: 1, max: 51)` -- 读取核高
6. `GetIntParam(@operator, "Iterations", 1, min: 1, max: 10)` -- 读取迭代次数
7. `GetIntParam(@operator, "AnchorX", -1)` / `GetIntParam(@operator, "AnchorY", -1)` -- 读取锚点
8. `MorphologyExecutionHelper.Execute(src, operation, kernelShape, kernelWidth, kernelHeight, iterations, anchorX, anchorY)` -- 委托执行
   - `Cv2.GetStructuringElement(shape, new Size(kernelWidth, kernelHeight), anchor)` -- 创建结构元素
   - `Cv2.MorphologyEx(src, dst, morphType, kernel, anchor, iterations)` -- 执行形态学操作
9. `CreateImageOutput(dst, additionalData)` -- 封装输出，附带 Operation / KernelShape / KernelSize / Iterations

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `enum` | `"Close"` | Erode, Dilate, Open, Close, Gradient, TopHat, BlackHat | 形态学操作类型。不同操作对亮/暗区域的效果不同。 |
| `KernelShape` | `enum` | `"Rect"` | Rect, Cross, Ellipse | 结构元素形状。Rect 覆盖矩形区域，Cross 仅覆盖十字线，Ellipse 为椭圆形。 |
| `KernelWidth` | `int` | `3` | [1, 51] | 结构元素宽度（像素）。宽度和高度可独立设置以创建非正方形核。 |
| `KernelHeight` | `int` | `3` | [1, 51] | 结构元素高度（像素）。宽度和高度可独立设置以创建非正方形核。 |
| `Iterations` | `int` | `1` | [1, 10] | 操作重复执行次数。多次迭代等效于使用更大的结构元素，但保留了中间结果的语义。 |
| `AnchorX` | `int` | `-1` | - | 结构元素锚点 X 坐标。-1 表示锚点在核中心。 |
| `AnchorY` | `int` | `-1` | - | 结构元素锚点 Y 坐标。-1 表示锚点在核中心。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待处理的输入图像，支持单通道或多通道。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 形态学操作后的输出图像，尺寸与输入相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |
| `Operation` | `String` | 实际执行的形态学操作名称（如 "Close"）。 |
| `KernelShape` | `String` | 实际使用的结构元素形状（如 "Rect"）。 |
| `KernelSize` | `String` | 实际核大小，格式为 "宽x高"（如 "3x3"）。 |
| `Iterations` | `Integer` | 实际执行的迭代次数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N * k^2)，其中 N 为像素总数，k 为核大小。OpenCV 对部分操作（如开/闭）有与核大小无关的优化路径。 |
| 典型耗时 (Typical Latency) | 1080p 图像、3x3 核约 1-3ms；51x51 核约 20-50ms。Iterations 线性增加执行时间。 |
| 内存特征 (Memory Profile) | 需要分配一张与输入等大的输出 Mat，加上结构元素 Mat（很小）。峰值内存约为输入图像的 2 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：二值图像的噪点清理——用开运算去除白色小噪点，用闭运算填充黑色小孔洞。
- **适合 (Suitable)**：边缘提取——形态学梯度操作可从二值或灰度图像中提取物体轮廓。
- **适合 (Suitable)**：光照不均匀补偿——TopHat 操作可提取暗背景上的亮细节（如划痕检测），BlackHat 可提取亮背景上的暗细节。
- **适合 (Suitable)**：方向性特征处理——通过设置非正方形核（如 1x15），可选择性地消除特定方向的噪声或连接特定方向的断裂。
- **不适合 (Not Suitable)**：需要亚像素精度边缘定位的场景，形态学操作的精度受结构元素离散化限制。
- **不适合 (Not Suitable)**：灰度图像的噪声平滑（高斯噪声），此时线性滤波器（高斯/均值）更合适。
- **不适合 (Not Suitable)**：核大小超过 51 或需要自定义非规则形状结构元素的场景。

## 已知限制 / Known Limitations
1. 核大小上限为 51x51，无法使用更大的结构元素。若需更大范围的形态学操作，可通过增加 Iterations 间接实现。
2. 多通道图像的形态学操作是对每个通道独立执行的，不会考虑通道间的关联，这可能导致颜色空间边缘不连续。
3. AnchorX/AnchorY 未在 `OperatorParam` 中声明范围，传入超出核范围的值可能导致 OpenCV 异常。
4. Operations 和 KernelShape 的验证通过 `MorphologyExecutionHelper.IsValidOperation/IsValidShape` 进行，支持多种别名（如 "opening" = "open"），但面板选项仅显示主名称。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 7 种形态学操作的算法原理、实现策略（MorphologyExecutionHelper 委托、独立宽高核）、完整参数语义、API 调用链、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
