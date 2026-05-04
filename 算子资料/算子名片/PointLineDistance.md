# 点线距离 / Point-Line Distance

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PointLineDistanceOperator` |
| 枚举值 (Enum) | `OperatorType.PointLineDistance` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子计算一个点到一条线段或无限直线的距离，并输出垂足坐标。支持两种距离模型：线段距离 (Segment) 和无限直线距离 (InfiniteLine)。

This operator calculates the distance from a point to a line segment or infinite line, and outputs the foot-of-perpendicular coordinates. It supports two distance models: segment distance and infinite line distance.

**线段距离模式 (Segment Mode)**：
将点投影到线段所在的有限范围上。垂足落在线段上时，距离 = 点到垂足的欧氏距离。垂足落在线段外时，距离 = 点到最近端点的距离。使用 `MeasurementGeometryHelper.DistancePointToSegment` 和 `ProjectPointToSegment`。

**Segment Mode**: Projects the point onto the finite segment range. When the foot falls on the segment, distance = Euclidean distance from point to foot. When the foot falls outside the segment, distance = distance to the nearest endpoint. Uses `MeasurementGeometryHelper.DistancePointToSegment` and `ProjectPointToSegment`.

**无限直线距离模式 (InfiniteLine Mode)**：
将点投影到线段所在的无限延长线上，距离始终为点到该直线的垂直距离。使用 `MeasurementGeometryHelper.DistancePointToInfiniteLine` 和 `ProjectPointToInfiniteLine`。

**InfiniteLine Mode**: Projects the point onto the infinite extension of the segment, distance is always the perpendicular distance to the line. Uses `MeasurementGeometryHelper.DistancePointToInfiniteLine` and `ProjectPointToInfiniteLine`.

**不确定度传播 (Uncertainty Propagation)**：
点的 sigma 通过 `MeasurementGeometryHelper.EstimatePointSigma` 估计，线段的 sigma 通过 `EstimateLineSigma` 估计。距离不确定度通过 `PropagatePointLineDistanceUncertainty` 传播。置信度 = `1 / (1 + uncertaintyPx)`。

**Uncertainty Propagation**: Point sigma is estimated via `MeasurementGeometryHelper.EstimatePointSigma`, line sigma via `EstimateLineSigma`. Distance uncertainty is propagated via `PropagatePointLineDistanceUncertainty`. Confidence = `1 / (1 + uncertaintyPx)`.

## 实现策略 / Implementation Strategy

- 纯几何计算算子，不依赖图像处理。使用 `MeasurementGeometryHelper` 静态方法和内置 `DistanceModel` 枚举。
- 输入点支持多种格式自动解析：`Position`、`Point`、`Point2f`、`Point2d`、`IDictionary<string,object>`、`IDictionary`、以及 `"(x,y)"` 格式字符串。
- 输入线段支持：`LineData`、`IDictionary<string,object>` (StartX/StartY/EndX/EndY 或 X1/Y1/X2/Y2)、`IDictionary`。
- 几何退化检查：点坐标和线段坐标必须为有限数，线段长度必须 > 1e-9。
- 单位参数 (Unit) 当前仅支持 Pixel。
- 无图像输入/输出，输出为纯数据字典。

## 核心 API 调用链 / Core API Call Chain

1. `TryParsePoint(pointObj, out point)` -- 解析 Point 输入 (支持 Position/Point/Point2f/Point2d/Dict/String)
2. `TryParseLine(lineObj, out line)` -- 解析 Line 输入 (支持 LineData/Dict/LegacyDict)
3. `MeasurementGeometryHelper.IsFinite(line)` -- 线段退化检查
4. `MeasurementGeometryHelper.DistancePointToSegment/InfiniteLine` -- 距离计算
5. `GetStringParam(@operator, "DistanceModel", "Segment")` -- 读取距离模型
6. `GetStringParam(@operator, "Unit", "Pixel")` -- 读取单位
7. `TryParseDistanceModel(distanceModel, out parsedModel)` -- 解析距离模型枚举
8. **Segment 模式**:
   - `MeasurementGeometryHelper.ProjectPointToSegment(point.X, point.Y, line)` -- 计算垂足
   - `MeasurementGeometryHelper.DistancePointToSegment(point.X, point.Y, line)` -- 计算距离
9. **InfiniteLine 模式**:
   - `MeasurementGeometryHelper.ProjectPointToInfiniteLine(point.X, point.Y, line)` -- 计算垂足
   - `MeasurementGeometryHelper.DistancePointToInfiniteLine(point.X, point.Y, line)` -- 计算距离
10. `MeasurementGeometryHelper.EstimatePointSigma(point)` -- 点 sigma 估计
11. `MeasurementGeometryHelper.EstimateLineSigma(line)` -- 线段 sigma 估计
12. `MeasurementGeometryHelper.PropagatePointLineDistanceUncertainty(...)` -- 不确定度传播
13. `ComputeConfidence(uncertaintyPx)` -- 置信度计算
14. `OperatorExecutionOutput.Success(outputData)` -- 输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `DistanceModel` | `enum` | `"Segment"` | Segment / InfiniteLine | 距离计算模型。Segment = 到线段的最短距离；InfiniteLine = 到无限延长线的垂直距离。 |
| `Unit` | `enum` | `"Pixel"` | Pixel | 距离单位。当前仅支持 Pixel。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Point` | Point | `Point` | Yes | 待测点坐标。支持 Position/Point/Point2f/Point2d/Dict/String 等多种类型。 |
| `Line` | Line | `LineData` | Yes | 参考线段。支持 LineData、字典等多种格式。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Distance` | Distance | `Float` | 点到线段/直线的距离 (像素)。 |
| `FootPoint` | Foot Point | `Point` | 垂足坐标。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `FootPointX` | `double` | 垂足 X 坐标。 |
| `FootPointY` | `double` | 垂足 Y 坐标。 |
| `DistanceModel` | `string` | 实际使用的距离模型名称。 |
| `Unit` | `string` | 距离单位，固定为 "Pixel"。 |
| `Confidence` | `double` | 置信度 = 1 / (1 + uncertaintyPx)。 |
| `UncertaintyPx` | `double` | 距离不确定度 (像素)。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)。全部为常数时间几何计算。 |
| 典型耗时 (Typical Latency) | < 0.1ms。纯数学运算，无图像处理。 |
| 内存特征 (Memory Profile) | 几乎无额外分配。仅创建输出字典。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业视觉中点到边缘/线段的距离测量，如焊点到引脚边缘的距离。
- 适合 (Suitable)：管线化中接收上游点检测和线段检测算子的输出进行二次分析。
- 适合 (Suitable)：需要垂足坐标的定位场景 (如投影定位、偏移分析)。
- 适合 (Suitable)：需要区分"到线段"和"到无限直线"距离的精密测量。
- 不适合 (Not Suitable)：点到曲线/圆弧的距离计算。
- 不适合 (Not Suitable)：点集到线段的批量距离计算 (应循环调用或使用其他算子)。

## 已知限制 / Known Limitations
1. 单位参数 (Unit) 当前仅支持 Pixel，传入其他值会返回失败。
2. 退化几何 (零长度线段、无限坐标值) 会直接返回失败。
3. Segment 模式下垂足可能落在线段外，此时 FootPoint 为最近端点的投影，可能不在实际线段上。
4. InfiniteLine 模式下距离始终为垂直距离，不考虑线段的有限范围。
5. 点输入的字符串解析仅支持 `"(x,y)"` 格式，不支持空格分隔等其他格式。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述两种距离模型、垂足计算、不确定度传播、多类型输入解析等完整算法 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
