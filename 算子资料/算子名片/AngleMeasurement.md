# 角度测量 / Angle Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AngleMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.AngleMeasurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子从三个点或两条线段测量角度，支持子像素兼容输入。输出角度可选度 (Degree) 或弧度 (Radian)。

This operator measures an angle from three points or two lines with subpixel-compatible inputs. The output angle can be in degrees or radians.

**三点模式 (Three Points Mode)**：
给定三个点 P1, P2, P3，其中 P2 为顶点 (vertex)，计算向量 P2->P1 和 P2->P3 之间的夹角。公式：

Given three points P1, P2, P3 where P2 is the vertex, computes the angle between vectors P2->P1 and P2->P3. Formula:

```
v1 = P1 - P2,  v2 = P3 - P2
cos(theta) = (v1 . v2) / (|v1| * |v2|)
angle = acos(clamp(cos(theta), -1, 1))
```

点的来源优先级：端口输入 (Point1/Point2/Point3) > 参数值 (Point1X/Point1Y 等)。端口输入和参数可混合使用。

Point source priority: port inputs (Point1/Point2/Point3) > parameter values (Point1X/Point1Y, etc.). Port inputs and parameters can be mixed.

**两线模式 (Two Lines Mode)**：
当 Line1 和 Line2 端口均连接时，优先使用线段输入。算法：
1. 计算两条无限直线的交点 (`MeasurementGeometryHelper.TryGetInfiniteLineIntersection`)。
2. 若有交点，以交点为顶点，取 Line1 的起点和 Line2 的终点构成角度。
3. 若无交点 (平行)，取两线中点的平均值作为虚拟顶点。
4. 角度 = `acos(|v1 . v2| / (|v1| * |v2|))`，使用绝对值确保结果为锐角。

**Two Lines Mode**: When both Line1 and Line2 ports are connected, line inputs take priority. Algorithm:
1. Compute the intersection of two infinite lines (`MeasurementGeometryHelper.TryGetInfiniteLineIntersection`).
2. If intersecting, use the intersection as vertex, take Line1's start and Line2's end to form the angle.
3. If no intersection (parallel), use the average of the two midpoints as a virtual vertex.
4. Angle = `acos(|v1 . v2| / (|v1| * |v2|))`, using absolute value to ensure acute angle result.

**不确定度估计 (Uncertainty Estimation)**：
每个点的 sigma 根据输入类型自动分配：`Point2d`/`Position` = 0.05px，`Point2f` = 0.08px，`Point` = 0.5px。角度不确定度传播公式：

**Uncertainty Estimation**: Each point's sigma is auto-assigned by input type: `Point2d`/`Position` = 0.05px, `Point2f` = 0.08px, `Point` = 0.5px. Angle uncertainty propagation:

```
sigmaArm1 = sqrt(sigmaP1^2 + sigmaP2^2)   // P2 = vertex
sigmaArm2 = sqrt(sigmaP3^2 + sigmaP2^2)
sigmaAngleRad = sqrt((sigmaArm1/len1)^2 + (sigmaArm2/len2)^2)
sigmaAngleDeg = sigmaAngleRad * 180 / PI
```

置信度 = `clamp(1 / (1 + uncertaintyDeg * 4), 0, 1)`。

Confidence = `clamp(1 / (1 + uncertaintyDeg * 4), 0, 1)`.

## 实现策略 / Implementation Strategy

- 输入解析优先级：先尝试 `TryResolveLineMode` 检测两线输入，成功则进入两线模式。
- 两线模式失败后，进入三点模式：尝试从端口解析 Point1/Point2/Point3，未连接的点使用参数默认值。
- 退化检查：任一臂长度 < 1e-9 时返回 `[DegenerateGeometry]` 错误。
- 角度单位转换：参数为 Radian 时直接输出弧度；Degree 时乘以 `180/PI`。
- 可视化：三点模式用红/绿/蓝三色圆标记三个点，黄色连线表示两臂。两线模式用黄色和橙色线段标记两条线，绿色圆标记顶点。
- 输入点支持类型自动解析：`Position`、`Point2d`、`Point2f`、`Point`、`IDictionary<string,object>`。
- 输入线段支持：`LineData`、`IDictionary<string,object>` (StartX/StartY/EndX/EndY)。

## 核心 API 调用链 / Core API Call Chain

以三点模式为例：

1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像 (必须)
2. `GetStringParam(@operator, "Unit", "Degree")` -- 读取角度单位
3. `TryResolveAngleGeometry(@operator, inputs, out geometry, out error)` -- 解析几何
   - `TryResolveLineMode(inputs, out geometry)` -- 尝试两线模式 (失败则继续)
   - `TryResolvePoint(inputs, "Point1", out point1, out sigma1)` -- 解析 Point1
   - `TryParsePoint(raw, out point, out sigmaPx)` -- 类型自动识别 + sigma 分配
   - `TryResolvePoint(inputs, "Point2", out point2, out sigma2)` -- 解析 Point2 (顶点)
   - `TryResolvePoint(inputs, "Point3", out point3, out sigma3)` -- 解析 Point3
   - 参数回退: `GetIntParam(@operator, "Point1X", 0)` 等
4. 角度计算:
   - `v1x = P1.X - P2.X; v1y = P1.Y - P2.Y` -- 向量 1
   - `v2x = P3.X - P2.X; v2y = P3.Y - P2.Y` -- 向量 2
   - `len1 = sqrt(v1x^2 + v1y^2); len2 = sqrt(v2x^2 + v2y^2)` -- 臂长
   - `dot = v1x*v2x + v1y*v2y` -- 点积
   - `cosTheta = clamp(dot / (len1*len2), -1, 1)` -- 余弦值
   - `angleRad = acos(cosTheta)` -- 弧度角
5. 单位转换: `angle = angleRad * 180 / PI` (Degree 模式)
6. `ComputeAngleUncertaintyDegrees(geometry, len1, len2)` -- 不确定度计算
7. `ComputeConfidence(uncertaintyDeg)` -- 置信度计算
8. `DrawPointGeometry(resultImage, P1, P2, P3, angle, unit)` -- 可视化
   - `Cv2.Circle(image, point1, 5, red, -1)` -- P1 红色
   - `Cv2.Circle(image, point2, 5, green, -1)` -- P2 绿色 (顶点)
   - `Cv2.Circle(image, point3, 5, blue, -1)` -- P3 蓝色
   - `Cv2.Line(image, point1, point2, yellow, 2)` -- 臂 1
   - `Cv2.Line(image, point2, point3, yellow, 2)` -- 臂 2
   - `Cv2.PutText(image, "Angle: X.XXXX Unit", ...)` -- 标注
9. `CreateImageOutput(resultImage, outputData)` -- 封装输出

以两线模式为例 (差异部分):

3. `TryResolveLineMode(inputs, out geometry)` -- 成功
   - `TryResolveLine(inputs, "Line1", out line1, out sigma1)` -- 解析 Line1
   - `TryResolveLine(inputs, "Line2", out line2, out sigma2)` -- 解析 Line2
   - `MeasurementGeometryHelper.TryGetInfiniteLineIntersection(line1, line2, out intersection)` -- 计算交点
   - 构建 AngleGeometry: P1=Line1.Start, P2=交点, P3=Line2.End
4. `ComputeLineAngleRadians(line1, line2)` -- 两线角度
   - `dot = |v1x*v2x + v1y*v2y|` -- 取绝对值 (锐角)
   - `acos(clamp(dot / (len1*len2), -1, 1))`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Point1X` | `int` | `0` | - | 点 1 的 X 坐标。无 Point1 端口输入时使用。 |
| `Point1Y` | `int` | `0` | - | 点 1 的 Y 坐标。无 Point1 端口输入时使用。 |
| `Point2X` | `int` | `100` | - | 点 2 (顶点) 的 X 坐标。无 Point2 端口输入时使用。 |
| `Point2Y` | `int` | `100` | - | 点 2 (顶点) 的 Y 坐标。无 Point2 端口输入时使用。 |
| `Point3X` | `int` | `200` | - | 点 3 的 X 坐标。无 Point3 端口输入时使用。 |
| `Point3Y` | `int` | `0` | - | 点 3 的 Y 坐标。无 Point3 端口输入时使用。 |
| `Unit` | `enum` | `"Degree"` | Degree / Radian | 角度输出单位。Degree = 度；Radian = 弧度。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 输入图像，用于可视化标注。 |
| `Point1` | Point 1 | `Point` | No | 第一个点 (非顶点)。支持 Position/Point2d/Point2f/Point/Dict。 |
| `Point2` | Point 2 | `Point` | No | 第二个点 (顶点)。 |
| `Point3` | Point 3 | `Point` | No | 第三个点 (非顶点)。 |
| `Line1` | Line 1 | `LineData` | No | 第一条线段。与 Line2 同时连接时启用两线模式。 |
| `Line2` | Line 2 | `LineData` | No | 第二条线段。与 Line1 同时连接时启用两线模式。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 叠加了角度标注的结果图像。 |
| `Angle` | Angle | `Float` | 测量的角度值。 |
| `Vertex` | Vertex | `Point` | 角度顶点坐标。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Unit` | `string` | 角度单位 (Degree/Radian)。 |
| `InputMode` | `string` | 输入模式：ThreePointsParameters (纯参数)、ThreePointsInput (端口输入)、TwoLines (两线+交点)、TwoLinesNoIntersection (两线+无交点)。 |
| `Confidence` | `double` | 置信度 = clamp(1 / (1 + uncertaintyDeg * 4), 0, 1)。 |
| `UncertaintyPx` | `double` | 点位置平均不确定度 (像素) = (sigma1 + sigma2 + sigma3) / 3。 |
| `UncertaintyDeg` | `double` | 角度不确定度 (度)。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)。全部为常数时间几何计算 + O(1) 绘制操作。 |
| 典型耗时 (Typical Latency) | < 1ms (纯计算)。含图像绘制约 1-3ms。 |
| 内存特征 (Memory Profile) | 克隆一份结果图像。AngleGeometry 为值类型 (record struct)，栈分配。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业视觉中两边缘/两线段之间的夹角测量，如 V 型槽角度、焊点引脚角度。
- 适合 (Suitable)：三点定位场景，如机械臂关节角度、零件倒角检测。
- 适合 (Suitable)：管线化中接收上游线段检测算子 (如直线测量) 的输出进行角度分析。
- 适合 (Suitable)：需要不确定度和置信度输出的精密角度测量。
- 不适合 (Not Suitable)：曲线上某点的切线角度测量。
- 不适合 (Not Suitable)：超过 180 度的反射角测量 (结果始终为锐角)。

## 已知限制 / Known Limitations
1. 两线模式使用 `|v1 . v2|` (绝对值)，结果始终为锐角 (0-90 度)，无法区分钝角。
2. 三点模式输出范围为 0-180 度，但两线模式限制为 0-90 度，行为不一致。
3. 两线模式下无交点 (平行) 时使用中点平均值作为虚拟顶点，角度值可能无物理意义。
4. 输入图像为必须 (IsRequired=true)，即使不需要可视化也必须提供图像。
5. 端口输入的 Point 和 Line 均为可选 (IsRequired=false)，但至少需要一组输入 (端口或参数) 才能计算。
6. `InputMode` 输出字段帮助区分结果来源，但下游需自行判断模式差异对精度的影响。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述三点/两线两种模式的算法、向量角度公式、不确定度传播、输入模式枚举等 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
