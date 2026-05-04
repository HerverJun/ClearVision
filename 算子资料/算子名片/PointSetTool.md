# 点集工具 / PointSetTool

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PointSetToolOperator` |
| 枚举值 (Enum) | `OperatorType.PointSetTool` |
| 分类 (Category) | Logic Tools（逻辑工具） |
| 图标 (Icon) | `point-set` |
| 关键词 (Keywords) | `point set`, `sort points`, `convex hull`, `bounding rect` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子对二维点集执行五种操作。**合并 (Merge)**：将两组点集简单拼接。**排序 (Sort)**：按 X 坐标、Y 坐标或到原点距离排序。**筛选 (Filter)**：按矩形区域（MinX/MinY/MaxX/MaxY）过滤点。**凸包 (ConvexHull)**：调用 OpenCV `Cv2.ConvexHull` 计算凸包顶点。**外接矩形 (BoundingRect)**：计算所有点的轴对齐包围矩形并返回四角坐标。所有操作后均计算结果点集的质心和包围矩形。

> English: This operator performs five operations on 2D point sets. **Merge**: concatenates two point lists. **Sort**: orders by X coordinate, Y coordinate, or distance from origin. **Filter**: retains points within a rectangular region (MinX/MinY/MaxX/MaxY). **ConvexHull**: computes convex hull vertices via OpenCV `Cv2.ConvexHull`. **BoundingRect**: computes the axis-aligned bounding rectangle and returns its four corner coordinates. After all operations, the centroid and bounding rectangle of the result set are calculated.

## 实现策略 / Implementation Strategy
> 中文：算子首先通过 `TryGetPoints` 从输入中提取点集，该方法支持 `Position` 对象、OpenCV `Point`、`IDictionary<string, object>`（含 X/Y 键）和非泛型 `IDictionary` 四种格式。Points2 若存在则追加到 Points1 后面。然后根据 `Operation` 参数用 switch 表达式分派到对应处理逻辑。凸包使用 OpenCV 的 `Cv2.ConvexHull`，需要至少 3 个点。外接矩形通过取 Min/Max 的 Floor/Ceiling 整数化后构造 `OpenCvSharp.Rect`。质心通过 `Average(p.X)` 和 `Average(p.Y)` 计算。

> English: The operator first extracts point sets via `TryGetPoints`, which supports four formats: `Position` objects, OpenCV `Point`, `IDictionary<string, object>` (with X/Y keys), and non-generic `IDictionary`. Points2 is appended to Points1 if present. Then a switch expression on the `Operation` parameter dispatches to the corresponding logic. ConvexHull uses OpenCV's `Cv2.ConvexHull` and requires at least 3 points. BoundingRect computes min/max with Floor/Ceiling integerization to construct an `OpenCvSharp.Rect`. Centroid is computed via `Average(p.X)` and `Average(p.Y)`.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetPoints(inputs, "Points1", out points)` - 从输入提取主点集
2. `TryGetPoints(inputs, "Points2", out points2)` - 从输入提取可选第二点集
3. `GetStringParam(@operator, "Operation")` - 读取操作类型
4. 根据 Operation 分派:
   - Merge: 直接返回合并后的列表
   - `SortPoints(points, sortBy)` - 按 X/Y/Distance 排序
   - Filter: `points.Where(p => p.X >= minX && ...)` - 矩形区域筛选
   - `BuildConvexHull(points)` -> `Cv2.ConvexHull(pts)` - 凸包计算
   - `BuildBoundingRectPoints(points)` -> `BuildBoundingRect(points)` - 外接矩形
5. 计算质心: `new Position(Average(X), Average(Y))`
6. `BuildBoundingRect(resultPoints)` - 计算包围矩形
7. 返回 `OperatorExecutionOutput.Success(...)` 包含 Points, Count, Center, BoundingBox

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `enum` | `"Merge"` | `Merge` / `Sort` / `Filter` / `ConvexHull` / `BoundingRect` | 点集操作类型 |
| `SortBy` | `enum` | `"X"` | `X` / `Y` / `Distance` | 排序依据（仅 Sort 模式生效）；Distance 为到原点欧氏距离的平方 |
| `FilterMinX` | `double` | `-1000000000.0` | 任意 double | 筛选区域左边界 X（含） |
| `FilterMinY` | `double` | `-1000000000.0` | 任意 double | 筛选区域上边界 Y（含） |
| `FilterMaxX` | `double` | `1000000000.0` | 任意 double | 筛选区域右边界 X（含） |
| `FilterMaxY` | `double` | `1000000000.0` | 任意 double | 筛选区域下边界 Y（含） |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Points1` | Points 1 | `PointList` | Yes | 主点集；支持 Position、OpenCV Point、字典格式 |
| `Points2` | Points 2 | `PointList` | No | 第二点集（Merge 模式下追加到 Points1） |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Points` | Points | `PointList` | 操作结果点集（BoundingRect 模式返回四角坐标） |
| `Count` | Count | `Integer` | 结果点集的点数 |
| `Center` | Center | `Point` | 结果点集的质心坐标（X/Y 均值） |
| `BoundingBox` | Bounding Box | `Rectangle` | 结果点集的轴对齐包围矩形（含 X/Y/Width/Height） |

> 注：BoundingBox 输出为 `Dictionary<string, object>` 格式，包含 X、Y、Width、Height 四个键。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Merge: O(n)；Sort: O(n log n)；Filter: O(n)；ConvexHull: O(n log n)；BoundingRect: O(n) |
| 典型耗时 (Typical Latency) | 取决于点集大小；千级点数 < 10 ms |
| 内存特征 (Memory Profile) | O(n) - 结果点集的完整副本 |
| 外部依赖 | OpenCvSharp（ConvexHull 和 Rect） |

## 适用场景 / Use Cases
- 适合 (Suitable)：合并多个检测算子的输出点集
- 适合 (Suitable)：按坐标排序后进行有序处理
- 适合 (Suitable)：在感兴趣区域内筛选点（Filter 模式）
- 适合 (Suitable)：计算点集的凸包轮廓（配合轮廓显示）
- 适合 (Suitable)：获取点集的包围矩形用于 ROI 定义
- 不适合 (Not Suitable)：三维点集处理（仅支持 2D）
- 不适合 (Not Suitable)：需要精确浮点凸包的场景（OpenCV 凸包使用整数坐标，会 Round）

## 已知限制 / Known Limitations
1. 凸包计算将 `Position` 的 double 坐标 Round 为 int 后传给 OpenCV `Point`，会损失亚像素精度。
2. `SortBy = Distance` 使用 `p.X * p.X + p.Y * p.Y`（距离平方）而非实际欧氏距离，排序结果一致但值不是真实距离。
3. `TryGetPoints` 对非泛型 `IDictionary` 的处理通过 `Cast<DictionaryEntry>` 实现，若字典值无法转换为 double 会被静默忽略。
4. BoundingRect 模式在点数为 0 或宽高 <= 0 时返回空列表，而非空矩形的四角。
5. 筛选操作的默认范围 (-1e9 到 1e9) 而非 `double.MinValue`/`double.MaxValue`（代码中使用 `double.MinValue`/`double.MaxValue`，但属性声明默认值不同）。
6. `Points1` 为空或无效时返回 Failure，但 `Points2` 无效时静默忽略。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确定位五种操作的实现差异、发现凸包 Round 精度损失问题、明确 Distance 使用距离平方、补充 TryGetPoints 四种输入格式支持、分析默认范围与代码实际值的差异 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
