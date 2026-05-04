# 距离测量 / Measure Distance

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MeasureDistanceOperator` |
| 枚举值 (Enum) | `OperatorType.Measurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子计算两点之间的距离，支持三种测量模式：点到点 (欧氏距离)、水平距离和垂直距离。同时支持参数坐标输入和 PointA/PointB 端口输入，并提供不确定度估计。

This operator calculates the distance between two points, supporting three measurement modes: point-to-point (Euclidean distance), horizontal distance, and vertical distance. It accepts both parameter-based coordinates and PointA/PointB port inputs, and provides uncertainty estimation.

**点到点模式 (PointToPoint)**：
计算两点间的欧氏距离：`d = sqrt((x2-x1)^2 + (y2-y1)^2)`。

**Point-to-Point Mode**: Euclidean distance between two points: `d = sqrt((x2-x1)^2 + (y2-y1)^2)`.

**水平模式 (Horizontal)**：
仅计算 X 轴方向距离：`d = |x2 - x1|`，终点 Y 坐标强制对齐为起点 Y。

**Horizontal Mode**: X-axis distance only: `d = |x2 - x1|`, endpoint Y is forced to match start Y.

**垂直模式 (Vertical)**：
仅计算 Y 轴方向距离：`d = |y2 - y1|`，终点 X 坐标强制对齐为起点 X。

**Vertical Mode**: Y-axis distance only: `d = |y2 - y1|`, endpoint X is forced to match start X.

**不确定度估计 (Uncertainty Estimation)**：
输入点根据类型自动分配 sigma 值：`Point` (整数) = 0.5px，`Point2f` = 0.08px，`Point2d`/`Position` (有小数) = 0.05px，`Position` (整数) = 0.5px。距离不确定度 = `sqrt(sigmaA^2 + sigmaB^2)`。置信度 = `1 / (1 + uncertaintyPx)`。

**Uncertainty Estimation**: Input points are assigned sigma values by type: `Point` (integer) = 0.5px, `Point2f` = 0.08px, `Point2d`/`Position` (fractional) = 0.05px, `Position` (integer) = 0.5px. Distance uncertainty = `sqrt(sigmaA^2 + sigmaB^2)`. Confidence = `1 / (1 + uncertaintyPx)`.

## 实现策略 / Implementation Strategy

- 输入优先级：先检查 PointA/PointB 端口输入 (支持多种类型自动解析)，有则跳过图像处理直接计算。
- 无 Point 输入时，从参数中读取 X1/Y1/X2/Y2 坐标，使用输入图像进行可视化标注。
- Point 输入支持类型自动解析：`Point`, `Point2f`, `Point2d`, `Position`, `IDictionary<string,object>`, `IDictionary`, 以及 `"(x,y)"` 格式字符串。
- 水平/垂直模式下，绘制端点 (drawnEndPoint) 被投影到对应的轴上，绘制直角标记。
- 结果图像绘制：绿色连线 (线宽 2) + 蓝色端点圆 (半径 5) + 红色距离标注文字。

## 核心 API 调用链 / Core API Call Chain

以图像 + 参数模式为例：

1. `GetStringParam(@operator, "MeasureType", "PointToPoint")` -- 读取测量类型
2. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像 (PointA/B 不存在时)
3. `imageWrapper.GetMat()` -- 解码为 `Mat`
4. `GetIntParam(@operator, "X1", 0)` / `Y1` / `X2` / `Y2` -- 读取坐标参数
5. `TryMeasure(p1, p2, normalizedType, out distance, out drawnEndPoint, out label, out error)` -- 核心测量
   - `Distance(start, end)` -- 欧氏距离计算 `sqrt(dx^2 + dy^2)`
6. `ComputeDistanceUncertaintyPx(normalizedType, sigmaA, sigmaB)` -- 不确定度计算
7. `DrawLineDistance(resultImage, p1, drawnEndPoint, label)` -- 可视化绘制
   - `Cv2.Line(image, p1, p2, green, 2)` -- 连线
   - `Cv2.Circle(image, p1, 5, blue, -1)` -- 端点标记
   - `Cv2.PutText(image, label, ...)` -- 距离标注
8. `ComputeConfidence(uncertaintyPx)` -- 置信度计算
9. `CreateImageOutput(resultImage, outputData)` -- 封装输出

以 Point 端口输入模式为例：

1. `TryParsePoint(pointAObj, out pointA, out sigmaA)` -- 解析起点 (自动识别类型并分配 sigma)
2. `TryParsePoint(pointBObj, out pointB, out sigmaB)` -- 解析终点
3. `BuildPointInputResult(pointA, sigmaA, pointB, sigmaB, measureType)` -- 直接计算
   - `TryMeasure(...)` -- 核心测量
   - `ComputeDistanceUncertaintyPx(...)` -- 不确定度
   - `ComputeConfidence(...)` -- 置信度
4. `OperatorExecutionOutput.Success(outputData)` -- 无图像输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `X1` | `int` | `0` | - | 起点 X 坐标 (像素)。无 PointA 输入时使用。 |
| `Y1` | `int` | `0` | - | 起点 Y 坐标 (像素)。无 PointA 输入时使用。 |
| `X2` | `int` | `100` | - | 终点 X 坐标 (像素)。无 PointB 输入时使用。 |
| `Y2` | `int` | `100` | - | 终点 Y 坐标 (像素)。无 PointB 输入时使用。 |
| `MeasureType` | `enum` | `"PointToPoint"` | PointToPoint / Horizontal / Vertical | 测量类型。PointToPoint = 欧氏距离；Horizontal = 水平距离；Vertical = 垂直距离。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | No | 输入图像，用于可视化标注。PointA/B 存在时可省略。 |
| `PointA` | 起点 | `Point` | No | 起点坐标。支持 Point/Point2f/Point2d/Position/Dict/String 等多种类型。 |
| `PointB` | 终点 | `Point` | No | 终点坐标。支持同 PointA 的多种类型。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 叠加了距离标注的结果图像。仅在有输入图像时输出。 |
| `Distance` | 测量距离 | `Float` | 测量得到的距离值 (像素)。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `X1` | `int` | 实际使用的起点 X 坐标。 |
| `Y1` | `int` | 实际使用的起点 Y 坐标。 |
| `X2` | `int` | 实际使用的终点 X 坐标 (水平/垂直模式下可能与输入不同)。 |
| `Y2` | `int` | 实际使用的终点 Y 坐标。 |
| `MeasureType` | `string` | 实际使用的测量类型。 |
| `DeltaX` | `int` | X 轴差值 = X2 - X1。 |
| `DeltaY` | `int` | Y 轴差值 = Y2 - Y1。 |
| `Confidence` | `double` | 置信度 = 1 / (1 + uncertaintyPx)。 |
| `UncertaintyPx` | `double` | 距离不确定度 (像素) = sqrt(sigmaA^2 + sigmaB^2)。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) 核心计算。图像模式下额外 O(1) 绘制操作。Point 输入模式无图像处理。 |
| 典型耗时 (Typical Latency) | < 1ms (纯计算)。有图像输出时约 1-5ms (取决于绘制和图像克隆)。 |
| 内存特征 (Memory Profile) | Point 输入模式几乎无额外分配。图像模式需克隆一份结果图像。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业视觉中两点间的快速距离测量，如孔间距、零件尺寸。
- 适合 (Suitable)：管线化中接收上游 Point 端口输出的在线距离计算。
- 适合 (Suitable)：需要水平/垂直分量分析的场景 (如水平偏移、垂直落差)。
- 适合 (Suitable)：需要不确定度和置信度输出的精密测量。
- 不适合 (Not Suitable)：多点间距离的批量计算 (应使用间距测量算子)。
- 不适合 (Not Suitable)：曲线距离或路径长度测量。

## 已知限制 / Known Limitations
1. 起点和终点重合时返回 `[DegenerateGeometry]` 错误。
2. 水平/垂直模式下，终点被投影到对应轴上，输出的 X2/Y2 与输入可能不同。
3. Point 输入模式不输出结果图像 (无 Image 端口输入时)。
4. 不确定度估计基于点类型的启发式 sigma 值，不反映实际像素定位精度。
5. 参数坐标 (X1/Y1/X2/Y2) 的 sigma 固定为 0.5px (整数坐标)。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述三种测量模式、不确定度估计、多类型 Point 解析、置信度计算等完整算法 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
