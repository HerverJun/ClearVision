# 线线距离 / Line-Line Distance

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LineLineDistanceOperator` |
| 枚举值 (Enum) | `OperatorType.LineLineDistance` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子计算两条线段/直线之间的距离、夹角和交点信息。支持两种距离模型：线段距离 (Segment) 和无限直线距离 (InfiniteLine)。

This operator computes the distance, angle, and intersection information between two lines/segments. It supports two distance models: segment distance and infinite line distance.

**角度计算 (Angle Computation)**：
通过 `MeasurementGeometryHelper.AngleBetweenLineDirections` 计算两条线段方向之间的夹角 (度)，结果范围 [0, 90]。

**Angle Computation**: Computes the angle (in degrees) between the two segment directions via `MeasurementGeometryHelper.AngleBetweenLineDirections`, result range [0, 90].

**平行判定 (Parallel Detection)**：
若夹角 <= `ParallelThreshold` 参数值，则判定为平行。

**Parallel Detection**: If the angle <= `ParallelThreshold`, the lines are considered parallel.

**交点计算 (Intersection Computation)**：
- 线段模式 (Segment)：使用 `MeasurementGeometryHelper.TryGetSegmentIntersection` 计算两线段的实际交点。
- 无限直线模式 (InfiniteLine)：使用 `MeasurementGeometryHelper.TryGetInfiniteLineIntersection` 计算无限延长线的交点。平行时不计算交点。

**Intersection Computation**:
- Segment mode: Uses `MeasurementGeometryHelper.TryGetSegmentIntersection` for actual segment intersection.
- InfiniteLine mode: Uses `MeasurementGeometryHelper.TryGetInfiniteLineIntersection` for extended line intersection. No intersection when parallel.

**距离计算 (Distance Computation)**：
- 线段模式 (Segment)：`MeasurementGeometryHelper.DistanceSegmentToSegment` -- 两线段间最短距离，考虑端点到线段的投影。
- 无限直线模式 (InfiniteLine)：平行时 = `DistancePointToInfiniteLine(line1.Start, line2)`；非平行时 = 0 (无限直线相交)。

**Distance Computation**:
- Segment mode: `MeasurementGeometryHelper.DistanceSegmentToSegment` -- minimum distance between two segments, considering endpoint-to-segment projections.
- InfiniteLine mode: when parallel = `DistancePointToInfiniteLine(line1.Start, line2)`; when non-parallel = 0 (infinite lines intersect).

**不确定度传播 (Uncertainty Propagation)**：
通过 `MeasurementGeometryHelper.PropagateLineLineDistanceUncertainty` 基于两条线段的 sigma 估计值传播距离不确定度。置信度 = `1 / (1 + uncertaintyPx)`。

**Uncertainty Propagation**: Distance uncertainty is propagated via `MeasurementGeometryHelper.PropagateLineLineDistanceUncertainty` based on sigma estimates of both lines. Confidence = `1 / (1 + uncertaintyPx)`.

## 实现策略 / Implementation Strategy

- 纯几何计算算子，不依赖图像处理。仅使用 `MeasurementGeometryHelper` 静态方法。
- 输入线段支持多种格式自动解析：`LineData`、`IDictionary<string,object>` (StartX/StartY/EndX/EndY 或 X1/Y1/X2/Y2)、`IDictionary` (旧版字典)。
- 几何退化检查：坐标必须为有限数，线段长度必须 > 1e-9。
- 单位参数 (Unit) 当前仅支持 Pixel，传入其他值会返回失败。
- 无图像输入/输出，输出为纯数据字典。

## 核心 API 调用链 / Core API Call Chain

1. `TryParseLine(line1Obj, out line1)` -- 解析 Line1 输入 (支持 LineData/Dict/LegacyDict)
2. `TryParseLine(line2Obj, out line2)` -- 解析 Line2 输入
3. `GetDoubleParam(@operator, "ParallelThreshold", 2.0, 0, 45)` -- 读取平行阈值
4. `GetStringParam(@operator, "DistanceModel", "Segment")` -- 读取距离模型
5. `GetStringParam(@operator, "Unit", "Pixel")` -- 读取单位
6. `TryParseDistanceModel(distanceModel, out parsedModel)` -- 解析距离模型枚举
7. `MeasurementGeometryHelper.IsFinite(line1)` / `IsFinite(line2)` -- 退化检查
8. `MeasurementGeometryHelper.AngleBetweenLineDirections(line1, line2)` -- 夹角计算
9. `MeasurementGeometryHelper.TryGetInfiniteLineIntersection(line1, line2, out intersection)` -- 无限直线交点
10. `MeasurementGeometryHelper.TryGetSegmentIntersection(line1, line2, out intersection)` -- 线段交点
11. `MeasurementGeometryHelper.DistanceSegmentToSegment(line1, line2)` -- 线段距离 (Segment 模式)
12. `MeasurementGeometryHelper.DistancePointToInfiniteLine(x, y, line2)` -- 点到无限直线距离 (InfiniteLine + 平行)
13. `MeasurementGeometryHelper.EstimateLineSigma(line1)` / `EstimateLineSigma(line2)` -- 线段 sigma 估计
14. `MeasurementGeometryHelper.PropagateLineLineDistanceUncertainty(...)` -- 不确定度传播
15. `ComputeConfidence(uncertaintyPx)` -- 置信度计算
16. `OperatorExecutionOutput.Success(outputData)` -- 输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ParallelThreshold` | `double` | `2.0` | [0.0, 45.0] | 平行判定阈值 (度)。两线夹角 <= 此值时判定为平行。 |
| `DistanceModel` | `enum` | `"Segment"` | Segment / InfiniteLine | 距离计算模型。Segment = 线段最短距离；InfiniteLine = 无限直线距离 (非平行时为 0)。 |
| `Unit` | `enum` | `"Pixel"` | Pixel | 距离单位。当前仅支持 Pixel。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Line1` | Line 1 | `LineData` | Yes | 第一条线段。支持 LineData、字典等多种格式。 |
| `Line2` | Line 2 | `LineData` | Yes | 第二条线段。支持同 Line1 的多种格式。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Distance` | Distance | `Float` | 两线段/直线之间的距离 (像素)。 |
| `Angle` | Angle | `Float` | 两线段方向之间的夹角 (度)，范围 [0, 90]。 |
| `Intersection` | Intersection | `Point` | 交点坐标。无交点时为 (-1, -1)。 |
| `HasIntersection` | Has Intersection | `Boolean` | 是否存在有效交点。 |
| `IsParallel` | Is Parallel | `Boolean` | 是否判定为平行 (夹角 <= ParallelThreshold)。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
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
- 适合 (Suitable)：工业视觉中两条线段/边缘之间的距离和角度测量。
- 适合 (Suitable)：平行度检测 (通过 IsParallel 输出和 Angle 值)。
- 适合 (Suitable)：管线化中接收上游线段检测算子 (如直线测量) 的输出进行二次分析。
- 适合 (Suitable)：交点计算，用于定位两条边缘的交叉位置。
- 不适合 (Not Suitable)：非直线特征 (曲线、圆弧) 之间的距离计算。
- 不适合 (Not Suitable)：需要考虑图像信息 (如边缘强度) 的加权距离。

## 已知限制 / Known Limitations
1. 单位参数 (Unit) 当前仅支持 Pixel，传入其他值会返回失败。
2. 退化几何 (零长度线段、无限坐标值) 会直接返回失败。
3. `Intersection` 输出在无交点时返回 `NoIntersection` 常量 (-1, -1)，需通过 `HasIntersection` 判断有效性。
4. InfiniteLine 模式下非平行线距离恒为 0，可能不符合某些使用场景的期望。
5. 线段输入解析不支持字符串格式 (与 MeasureDistanceOperator 不同)。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述距离模型、角度计算、交点判定、不确定度传播等完整算法 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
