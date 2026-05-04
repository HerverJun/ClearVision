# 四边形查找 / QuadrilateralFind

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `QuadrilateralFindOperator` |
| 枚举值 (Enum) | `OperatorType.QuadrilateralFind` |
| 分类 (Category) | 定位 |
| IconName | `quadrilateral` |
| Keywords | `quadrilateral`, `polygon`, `trapezoid` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**中文：**
该算子在图像中查找四边形轮廓，不施加直角约束（区别于矩形检测算子）。核心流程：灰度转换 -> 高斯模糊 (5x5) -> Canny 边缘检测 (60,180) -> 形态学闭运算 (3x3 矩形核) -> `FindContours` 提取外层轮廓 -> `ApproxPolyDP` 多边形逼近 -> 筛选 4 顶点轮廓。对通过筛选的轮廓使用 `CornerSubPix` 在 5x5 窗口内精化角点至亚像素精度。可选的 `ConvexOnly` 过滤非凸四边形（梯形等凹四边形）。质心通过 `Cv2.Moments` 计算。顶点排序规则：按质心极角排序 -> 确保逆时针方向（通过有符号面积判定）-> 从最上方顶点开始（Y 最小，X 最小优先）。

**English:**
This operator finds quadrilateral contours in an image without right-angle constraints (unlike rectangle detection). Core pipeline: grayscale conversion -> Gaussian blur (5x5) -> Canny edge detection (60,180) -> morphological closing (3x3 rect kernel) -> `FindContours` for external contours -> `ApproxPolyDP` polygon approximation -> filter for 4-vertex contours. Passing contours are refined with `CornerSubPix` in a 5x5 window to sub-pixel accuracy. Optional `ConvexOnly` filters non-convex quadrilaterals (e.g., trapezoids). Centroid is computed via `Cv2.Moments`. Vertex ordering: sort by centroid polar angle -> ensure counter-clockwise (via signed area) -> start from topmost vertex (min Y, then min X).

## 实现策略 / Implementation Strategy
- **无直角约束：** 与 `RectangleDetectionOperator` 的核心区别——本算子仅要求轮廓逼近后恰好 4 个顶点，不要求内角接近 90 度，因此能检测梯形、菱形、平行四边形等任意四边形。
- **可选凸性过滤：** `ConvexOnly=false`（默认）时检测所有四边形（含凹四边形）；`ConvexOnly=true` 时仅保留凸四边形。
- **亚像素角点精化：** `RefineCorners` 使用 `CornerSubPix`（5x5 窗口，30 次迭代/epsilon=0.01），但会检查角点是否靠近图像边界（距边缘 < 1 pixel），若是则跳过精化避免边界效应。
- **确定性顶点排序：** `OrderVertices` 保证输出顶点顺序一致：(1) 按极角排序 (2) 有符号面积 < 0 时翻转确保逆时针 (3) 从最上方（Y 最小）顶点开始循环。这使得下游算子可以依赖一致的顶点语义。
- **面积自适应逼近：** `ApproxEpsilon <= 1.0` 时解释为周长比例（`perimeter * epsilon`），> 1.0 时解释为绝对像素值，兼顾不同尺度目标。
- **面积降序排列：** 多个四边形按面积降序排列，主输出（Vertices/Center/Area）取面积最大的一个。

## 核心 API 调用链 / Core API Call Chain
```
1. TryGetInputImage(inputs)                        -- 获取输入图像
2. GetIntParam("MinArea", 100, 0, 100_000_000)     -- 读取最小面积
3. GetIntParam("MaxArea", 10_000_000, 0, 100_000_000) -- 读取最大面积
4. GetDoubleParam("ApproxEpsilon", 0.02, 0.0001, 1000) -- 读取逼近精度
5. GetBoolParam("ConvexOnly", false)               -- 读取凸性过滤开关
6. Cv2.CvtColor(src, gray, BGR2GRAY)               -- 灰度转换
7. Cv2.GaussianBlur(gray, blurred, Size(5,5), 0)   -- 高斯降噪
8. Cv2.Canny(blurred, edge, 60, 180)               -- 边缘检测
9. Cv2.MorphologyEx(edge, closed, Close, 3x3)      -- 形态学闭运算
10. Cv2.FindContours(closed, External, ApproxSimple) -- 轮廓提取
11. [循环] Cv2.ContourArea -> 面积过滤
12. [循环] Cv2.ApproxPolyDP -> 4 顶点过滤
13. [循环] [若 ConvexOnly] Cv2.IsContourConvex -> 凸性过滤
14. [循环] RefineCorners(gray, approx)
    └─ Cv2.CornerSubPix(gray, corners, Size(5,5), Size(-1,-1), TermCriteria(30,0.01))
15. [循环] Cv2.Moments -> 计算质心
16. OrderVertices(corners) -> 极角排序 + 逆时针 + 顶部起点
17. quads.OrderByDescending(Area)
18. Cv2.Polylines(resultImage, orderedPoints, green, 2)
19. CreateImageOutput(result, {Vertices, OrderedVertices, Count, Area, Center})
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MinArea` | `int` | `100` | [0, 100000000] | 最小轮廓面积（像素^2）。低于此值的四边形被过滤。 |
| `MaxArea` | `int` | `10000000` | [0, 100000000] | 最大轮廓面积（像素^2）。高于此值的四边形被过滤。 |
| `ApproxEpsilon` | `double` | `0.02` | [0.0001, 1000.0] | 多边形逼近精度。<=1.0 时为周长比例（如 0.02 = 周长的 2%）；>1.0 时为绝对像素距离。值越小逼近越精确。 |
| `ConvexOnly` | `bool` | `false` | - | 是否仅保留凸四边形。false 时检测所有四边形（含梯形等凹形）；true 时仅返回凸四边形。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像，支持灰度和彩色。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 可视化结果图，四边形以绿色多边形绘制。 |
| `Vertices` | Vertices | `PointList` | 最大面积四边形的原始角点坐标（逼近顺序，未经排序）。 |
| `OrderedVertices` | Ordered Vertices | `PointList` | 最大面积四边形的排序角点坐标（逆时针，从最上方顶点开始）。 |
| `Count` | Count | `Integer` | 检测到的四边形数量。 |
| `Area` | Area | `Float` | 最大面积四边形的面积（像素^2）。 |
| `Center` | Center | `Point` | 最大面积四边形的质心坐标（基于图像矩计算）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) 边缘检测与轮廓提取 + O(M) 多边形逼近与筛选 + O(K) CornerSubPix 精化，N 为像素数，M 为轮廓数，K 为四边形数。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像约 25-70ms（取决于轮廓数量和四边形数量）。 |
| 内存特征 (Memory Profile) | 需分配灰度图、模糊图、边缘图、闭运算图、结果图各一份 Mat；四边形列表随检测数量增长。 |

## 适用场景 / Use Cases
- **适合 (Suitable)：** 梯形/平行四边形/菱形等非矩形四边形检测；标签/卡片/屏幕定位（允许透视变形）；任意四边形工件的位置和姿态估计；与矩形检测互补使用（不约束直角）。
- **不适合 (Not Suitable)：** 需要严格直角约束的矩形检测（应使用 RectangleDetection）；圆形或椭圆形目标检测；高密度遮挡场景（轮廓提取困难）。

## 已知限制 / Known Limitations
1. Canny 阈值 (60, 180) 和高斯核 (5x5) 为硬编码，对低对比度或高噪声图像可能需要预处理。
2. 形态学闭运算核大小 (3x3) 为硬编码，对大尺度断裂边缘可能连接不足。
3. `RefineCorners` 跳过靠近边界的角点（距边缘 < 1 pixel），边界处的四边形精度可能降低。
4. 仅输出面积最大的四边形的详细信息（Vertices/OrderedVertices/Area/Center），其余四边形仅计入 Count。
5. `FindContours` 使用 `RetrievalModes.External`，嵌套轮廓内部的四边形不会被检测。
6. 质心使用图像矩 `Moments` 计算，对非凸四边形，质心可能落在轮廓外部。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充无直角约束与矩形检测的差异说明、CornerSubPix 边界检测逻辑、确定性顶点排序算法（极角+有符号面积+顶部起点）、面积自适应逼近策略；完善参数语义和输出端口说明（OrderedVertices vs Vertices）；增加适用场景与已知限制的源码级分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
