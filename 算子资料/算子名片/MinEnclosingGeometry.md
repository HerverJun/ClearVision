# 最小包围几何 / Min Enclosing Geometry

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MinEnclosingGeometryOperator` |
| 枚举值 (Enum) | `OperatorType.MinEnclosingGeometry` |
| 分类 (Category) | Measurement |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

最小包围几何算子从输入图像中提取轮廓点集，计算 7 种包围/拟合几何体：最小外接圆、最小面积旋转矩形、最小面积三角形、凸包、RANSAC 圆弧拟合、鲁棒圆拟合和直接椭圆拟合。

The Min Enclosing Geometry operator extracts contour points from the input image and computes 7 types of enclosing/fitting geometries: smallest enclosing circle, minimum area rotated rectangle, minimum area triangle, convex hull, RANSAC arc fitting, robust circle fitting, and direct ellipse fitting.

**轮廓提取 / Contour Extraction:**
1. 灰度转换 + 固定阈值二值化（`ThresholdTypes.Binary`）
2. 外轮廓提取（`RetrievalModes.External, ApproxSimple`）
3. 按 MinArea 过滤
4. 轮廓选择策略：`LargestContour`（面积最大）、`AllContours`（全部合并）、`FirstContour`（第一个）

**7 种操作详解 / 7 Operations:**

**1. SmallestCircle -- 最小外接圆:**
- `Cv2.MinEnclosingCircle(points, center, radius)` -- Welzl 算法
- 条件数检查：基于点分布的协方差矩阵特征值比评估数值稳定性
- 包围率 = 被包含点数 / 总点数

**2. MinAreaRect -- 最小面积旋转矩形:**
- `Cv2.MinAreaRect(points)` -- 旋转卡壳法
- 输出：中心、宽高、旋转角、长宽比、面积、四顶点

**3. MinAreaTriangle -- 最小面积三角形:**
- 先计算凸包 `Cv2.ConvexHull(points)`
- 简化的最小包围三角形算法：在凸包顶点上枚举三元组
- 大凸包（>50 点）时按步长采样以控制性能

**4. ConvexHull -- 凸包:**
- `Cv2.ConvexHull(points)`
- 输出：顶点列表、面积、周长、凸度 = 原始面积 / 凸包面积

**5. FitArc -- RANSAC 圆弧拟合:**
- RANSAC 随机采样 3 点构建圆模型
- 计算所有点的角度和残差，收集内点
- 通过最大间隙法确定圆弧的起止角度
- 检查圆弧角度是否在 [MinArcAngle, MaxArcAngle] 范围内
- 评分 = -inliers * arcAngle（内点多且角度大的得分高）

**6. FitCircleRobust -- 鲁棒圆拟合 (MSAC):**
- MSAC (M-estimator Sample Consensus) 改进的 RANSAC
- 评分：内点用残差，外点用惩罚常数（threshold）
- 收集内点后用 Kasa 最小二乘精修
- 条件数检查：基于雅可比矩阵的 SVD 分解

**7. FitEllipseDirect -- 直接椭圆拟合:**
- `Cv2.FitEllipse(points)` -- Fitzgibbon 直接最小二乘法
- 条件数检查：基于长短轴比
- 输出：中心、长短轴、旋转角、离心率、面积

**Kasa 最小二乘圆拟合 / Kasa Least Squares Circle Fit:**
- 用于 MSAC 精修步骤
- 最小化代数距离 sum((x^2 + y^2 - 2*cx*x - 2*cy*y + cx^2 + cy^2 - r^2)^2)
- 求解 2x2 线性方程组得到圆心，再计算半径

## 实现策略 / Implementation Strategy

- **与 Halcon 对标**：SmallestCircle 对标 `smallest_circle`，MinAreaRect 对标 `smallest_rectangle2`，FitArc 对标 `fit_circle_contour_xld`。
- **统一轮廓处理**：所有操作共享相同的轮廓提取和选择流程。
- **条件数检查**：SmallestCircle、FitCircleRobust、FitEllipseDirect 都可选启用条件数检查（`CheckConditionNumber=true`），评估数值稳定性。
- **RANSAC 参数复用**：FitArc 和 FitCircleRobust 共用 RansacIterations 和 RansacInlierThreshold 参数。
- **OutlierRatio**：用于 RANSAC 验证，当内点比例 > (1 - OutlierRatio - 0.1) 时认为拟合有效。
- **可视化**：结果图绘制选中轮廓（蓝色）、拟合几何体（绿色）、中心点（红色）和属性标注。

## 核心 API 调用链 / Core API Call Chain

1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetStringParam / GetIntParam / GetDoubleParam / GetBoolParam` -- 读取参数
3. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
4. `Cv2.Threshold(gray, binary, threshold, 255, Binary)` -- 二值化
5. `Cv2.FindContours(binary, contours, External, ApproxSimple)` -- 轮廓提取
6. 面积过滤 + `SelectContours(validContours, contourSelection)` -- 轮廓选择
7. 分支操作:
   - SmallestCircle: `Cv2.MinEnclosingCircle` + `CalculatePointDistributionCondition`
   - MinAreaRect: `Cv2.MinAreaRect` + `rotatedRect.Points()`
   - MinAreaTriangle: `Cv2.ConvexHull` + `FindMinEnclosingTriangle`
   - ConvexHull: `Cv2.ConvexHull` + `Cv2.ContourArea` + `Cv2.ArcLength`
   - FitArc: `FitArcWithRansac` (RANSAC + 最大间隙角度计算)
   - FitCircleRobust: `FitCircleMsac` (MSAC) + `RefineCircleLeastSquares` (Kasa)
   - FitEllipseDirect: `Cv2.FitEllipse` + 条件数检查
8. `Cv2.DrawContours(resultImage, selectedContours, ...)` -- 绘制轮廓
9. `CreateImageOutput(resultImage, outputData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `enum` | `"SmallestCircle"` | SmallestCircle / MinAreaRect / MinAreaTriangle / ConvexHull / FitArc / FitCircleRobust / FitEllipseDirect | 操作类型。 |
| `Threshold` | `double` | `127.0` | [0.0, 255.0] | 二值化阈值。 |
| `MinArea` | `int` | `100` | [0, +inf) | 最小轮廓面积过滤。 |
| `ContourSelection` | `enum` | `"LargestContour"` | LargestContour / AllContours / FirstContour | 轮廓选择策略。 |
| `RansacIterations` | `int` | `500` | [10, 5000] | RANSAC/MSAC 迭代次数。仅 FitArc 和 FitCircleRobust 生效。 |
| `RansacInlierThreshold` | `double` | `2.0` | [0.1, 50.0] | RANSAC 内点阈值（像素）。仅 FitArc 和 FitCircleRobust 生效。 |
| `MinArcAngle` | `double` | `30.0` | [5.0, 350.0] | 最小圆弧角度（度）。仅 FitArc 生效。 |
| `MaxArcAngle` | `double` | `330.0` | [10.0, 360.0] | 最大圆弧角度（度）。仅 FitArc 生效。 |
| `OutlierRatio` | `double` | `0.3` | [0.0, 0.9] | 预期异常点比例。用于 RANSAC 拟合有效性验证。 |
| `CheckConditionNumber` | `bool` | `true` | - | 是否检查数值条件数。SmallestCircle/FitCircleRobust/FitEllipseDirect 生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 输入灰度或彩色图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 可视化结果图，绘制轮廓和拟合几何体。 |
| `GeometryResult` | Geometry Result | `Any` | 几何结果字典，包含 Success、GeometryType 和具体几何参数。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Operation` | `string` | 实际执行的操作类型。 |
| `PointCount` | `int` | 参与计算的总点数。 |
| `ContourCount` | `int` | 选中的轮廓数量。 |

### GeometryResult 典型字段 / Typical GeometryResult Keys

**通用字段 / Common:**
- `Success`: bool, `GeometryType`: string

**SmallestCircle:**
- `Center`: Position, `Radius`: float
- `EnclosedPoints`: int, `EnclosureRatio`: double, `IsValid`: bool (ratio > 0.95)
- `ConditionNumber`: double, `Quality`: "Good"/"Fair"/"Poor" (可选)

**MinAreaRect:**
- `Center`: Position, `Size`: {Width, Height}, `Angle`: float
- `AspectRatio`: double, `Area`: double, `Vertices`: List<Position>

**MinAreaTriangle:**
- `Vertices`: List<Position>, `Area`: double

**ConvexHull:**
- `HullVertices`: List<Position>, `HullArea`: double, `HullPerimeter`: double
- `Convexity`: double, `VertexCount`: int

**FitArc:**
- `Center`: Position, `Radius`: float
- `StartAngle`/`EndAngle`/`ArcAngle`: double (度)
- `InlierCount`: int, `InlierRatio`: double
- `StartPoint`/`EndPoint`: Position, `IsValid`: bool

**FitCircleRobust:**
- `Center`: Position, `Radius`: float
- `InlierCount`/`OutlierCount`: int, `InlierRatio`: double
- `MeanResidual`/`MaxResidual`: double, `IsValid`: bool
- `ConditionNumber`: double, `FitQuality`: "Good"/"Fair"/"Poor" (可选)

**FitEllipseDirect:**
- `Center`: Position, `MajorAxis`/`MinorAxis`: float, `Angle`: float
- `Eccentricity`: double, `Area`: double
- `ConditionNumber`: double, `FitQuality`: "Good"/"Fair"/"Poor" (可选)

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H) 轮廓提取 + O(P log P) 凸包/排序 + O(I*P) RANSAC（I=迭代次数，P=点数） |
| 典型耗时 (Typical Latency) | 无专用基准测试。SmallestCircle/MinAreaRect/ConvexHull 通常 < 5ms；FitArc/FitCircleRobust 取决于 RANSAC 迭代次数。 |
| 内存特征 (Memory Profile) | O(W*H) 图像 + O(P) 轮廓点集。RANSAC 模式有额外的临时点集和模型存储。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：分割后零件的最小外接圆、旋转矩形、三角形或凸包测量。
- 适合 (Suitable)：需要从轮廓点拟合圆、圆弧或椭圆，且存在异常点需要鲁棒处理的场景。
- 适合 (Suitable)：形状分析中的凸度评估、包围率计算。
- 适合 (Suitable)：圆弧段的角度范围和拟合质量评估（FitArc）。
- 不适合 (Not Suitable)：低对比度场景，阈值分割无法隔离目标轮廓。
- 不适合 (Not Suitable)：需要标定亚像素边缘提取后再拟合的精密计量场景。

## 已知限制 / Known Limitations
1. 轮廓提取基于固定阈值二值化，仅使用外轮廓（External），不支持层级轮廓。
2. RANSAC 拟合的精度依赖 RansacIterations 和 RansacInlierThreshold 参数，需要根据场景调优。
3. MinAreaTriangle 使用简化的凸包顶点枚举算法，大凸包时按步长采样可能错过最优三角形。
4. MSAC (FitCircleRobust) 的惩罚常数使用 threshold 值，不是标准 Welsch 损失函数。
5. FitEllipseDirect 没有 RANSAC 模式，直接使用所有点拟合，对异常点敏感。
6. 条件数检查仅评估数值稳定性，不直接反映测量精度。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充 7 种操作的完整算法原理、MSAC 改进、Kasa 最小二乘精修、条件数检查、GeometryResult 字段结构 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
