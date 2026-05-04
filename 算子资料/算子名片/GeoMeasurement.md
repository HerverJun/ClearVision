# 几何测量 / Geo Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GeoMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.GeoMeasurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

几何测量算子在两个几何元素（点、线、圆）之间执行测量，自动识别元素类型并计算距离、角度和交点。支持 9 种元素组合（Point-Point, Point-Line, Point-Circle, Line-Line, Line-Circle, Circle-Circle）及其反向排列。

The Geo Measurement operator measures between two geometric elements (point, line, circle), auto-detects element types, and computes distance, angle, and intersection points. It supports 9 element combinations and their reverse orderings.

**元素类型自动识别 / Element Type Auto-Detection:**
- `Element1Type` / `Element2Type` 参数默认为 "Auto"
- 自动识别顺序：先尝试解析为 Point (Position)，再尝试 Line (LineData)，最后尝试 Circle (CircleData)
- 支持多种输入格式：强类型对象、`IDictionary<string, object>`、旧式 `IDictionary`

**距离模型 / Distance Model:**
- `Segment`（默认）：线段间最短距离，点到线段距离
- `InfiniteLine`：无限直线间距离，点到无限直线距离

**各组合测量语义 / Measurement Semantics by Combination:**

| 组合 | 距离含义 | 角度 | 交点 |
|------|---------|------|------|
| Point-Point | 欧氏距离 | 0 | 无 |
| Point-Line | 点到线（段/无限）距离 | 0 | 垂足点 |
| Point-Circle | 点到圆边界距离 = |centerDist - radius| | 0 | 无（输出 Inside/Outside 关系） |
| Line-Line | 线段间最短距离 / 无限直线距离 | 方向夹角 | 交点（段交点或无限直线交点） |
| Line-Circle | 线到圆边界距离 = max(0, centerDist - radius) | 0 | 0-2 个交点 |
| Circle-Circle | 圆边界间距离 | 0 | 0-2 个交点；关系: Separated/Contained/Tangent/Intersecting |

**交点求解 / Intersection Solving:**
- 线-圆交点：参数化直线代入圆方程，求解一元二次方程
- 圆-圆交点：基于圆心距和半径的几何公式
- 线段模式下会 clamp 参数 t 到 [0, 1]

**不确定度传播 / Uncertainty Propagation:**
- 每种组合都有专门的不确定度传播函数（基于 `MeasurementGeometryHelper`）
- 点不确定度、线不确定度、圆不确定度通过误差传播公式合成
- 置信度 = 1 / (1 + uncertaintyPx)

## 实现策略 / Implementation Strategy

- **纯数值计算，无图像处理**：该算子不处理图像，仅对几何元素执行数学计算。
- **多态输入解析**：`TryParsePoint` / `TryParseLine` / `TryParseCircle` 支持强类型和字典两种输入格式，兼容上下游算子的不同输出结构。
- **对称处理**：Point-Line 和 Line-Point、Circle-Line 和 Line-Circle 等反向组合会自动处理，输出 MeasureType 标注实际组合方向。
- **关系判定**：Circle-Circle 组合输出 Separated / Contained / Tangent / Intersecting 四种关系。
- **不确定度可选**：如果输入元素携带 `UncertaintyPx` 字段，会使用该值；否则使用启发式估计。

## 核心 API 调用链 / Core API Call Chain

1. `inputs.TryGetValue("Element1" / "Element2")` -- 获取两个几何元素
2. `ResolveType(element, preferredType)` -- 自动识别或按指定类型解析元素
3. `TryParseDistanceModel(model)` -- 解析距离模型
4. `TryMeasure(element1, element2, type1, type2, distanceModel, out measurement)` -- 核心测量:
   - `TryParsePoint / TryParseLine / TryParseCircle` -- 解析元素
   - `MeasurementGeometryHelper.Distance(...)` -- 距离计算
   - `MeasurementGeometryHelper.AngleBetweenLineDirections(...)` -- 角度计算
   - `MeasurementGeometryHelper.TryGetSegmentIntersection(...)` / `TryGetInfiniteLineIntersection(...)` -- 交点
   - `SolveLineCircleIntersections(...)` / `SolveCircleCircleIntersections(...)` -- 线圆/圆圆交点
5. `ComputeMeasurementUncertainty(...)` -- 不确定度传播
6. `ComputeConfidence(uncertaintyPx)` -- 置信度计算
7. 封装输出字典

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Element1Type` | `enum` | `"Auto"` | Auto / Point / Line / Circle | 第一个元素的类型。Auto 时自动识别。 |
| `Element2Type` | `enum` | `"Auto"` | Auto / Point / Line / Circle | 第二个元素的类型。Auto 时自动识别。 |
| `DistanceModel` | `enum` | `"Segment"` | Segment / InfiniteLine | 距离模型。Segment: 线段距离；InfiniteLine: 无限直线距离。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Element1` | Element 1 | `Any` | Yes | 第一个几何元素。支持 Position (Point)、LineData (Line)、CircleData (Circle) 或字典格式。 |
| `Element2` | Element 2 | `Any` | Yes | 第二个几何元素。同上。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Distance` | Distance | `Float` | 两个元素之间的距离（像素）。 |
| `Angle` | Angle | `Float` | 两个元素之间的角度（度）。仅 Line-Line 组合非零。 |
| `Intersection1` | Intersection 1 | `Point` | 第一个交点坐标。无交点时为 NaN。 |
| `Intersection2` | Intersection 2 | `Point` | 第二个交点坐标。最多两个交点。 |
| `MeasureType` | Measure Type | `String` | 测量类型标识，如 "Point-Point", "Line-Line", "Circle-Circle" 等。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `DistanceModel` | `string` | 使用的距离模型 ("Segment" 或 "InfiniteLine")。 |
| `DistanceUnit` | `string` | 距离单位，固定为 "Pixel"。 |
| `DistanceMeaning` | `string` | 距离语义，如 "CenterDistance", "BoundaryGap", "PointToSegmentDistance" 等。 |
| `Relation` | `string` | 元素关系，如 "Separated", "Inside", "Outside", "Intersecting", "Tangent", "Contained", "Projected"。 |
| `IntersectionCount` | `int` | 交点数量。 |
| `StatusCode` | `string` | 固定为 "OK"。 |
| `StatusMessage` | `string` | 固定为 "Success"。 |
| `Confidence` | `double` | 置信度，基于不确定度计算。 |
| `UncertaintyPx` | `double` | 距离测量的合成不确定度（像素）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) -- 纯数学计算，与输入元素复杂度无关 |
| 典型耗时 (Typical Latency) | < 0.1ms，极快。不含图像处理。 |
| 内存特征 (Memory Profile) | 极低。仅分配输出字典和少量临时变量。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：点到线距离、点到圆距离、线间角度、圆间间距等几何关系测量。
- 适合 (Suitable)：作为流水线中的中间算子，接收上游检测算子的几何输出并计算关系。
- 适合 (Suitable)：需要交点坐标的场景，如线-线交叉定位、线-圆切点计算。
- 适合 (Suitable)：需要不确定度传播和置信度评估的精密测量流水线。
- 不适合 (Not Suitable)：需要从图像中提取几何元素的场景（需配合检测算子使用）。
- 不适合 (Not Suitable)：三维空间中的几何测量。

## 已知限制 / Known Limitations
1. 仅支持 2D 平面几何，不支持三维空间测量。
2. 元素类型自动识别依赖输入数据的结构，非标准格式可能无法正确解析。
3. DistanceModel=InfiniteLine 仅影响 Line-Line、Point-Line、Line-Circle 组合的距离计算，对其他组合无影响。
4. Circle-Circle 的交点在两圆相切时可能因浮点精度只返回 1 个交点。
5. 不确定度传播基于启发式估计，输入元素如果没有显式的 UncertaintyPx 字段，估计值可能不精确。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充 9 种元素组合的测量语义、交点求解算法、不确定度传播、多态输入解析机制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
