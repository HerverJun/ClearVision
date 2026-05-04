# 几何拟合 / Geometric Fitting

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GeometricFittingOperator` |
| 枚举值 (Enum) | `OperatorType.GeometricFitting` |
| 分类 (Category) | Measurement |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

几何拟合算子从输入图像中自动提取轮廓点集，然后对点集执行直线、圆或椭圆拟合。支持最小二乘和 RANSAC 两种鲁棒方法。

The Geometric Fitting operator automatically extracts contour point sets from the input image and fits a line, circle, or ellipse. It supports both Least Squares and RANSAC robust methods.

**轮廓提取流程 / Contour Extraction Pipeline:**
1. 灰度转换（彩色图像）
2. 4x 上采样（`Cv2.Resize` with Cubic 插值）-- 提升轮廓精度
3. 固定阈值二值化（`ThresholdTypes.Binary`）
4. 外轮廓提取（`RetrievalModes.External, ApproxNone`）-- 保留所有轮廓点
5. 按面积过滤（阈值按 4x 上采样缩放）
6. 轮廓选择：`BestResidual`（按拟合残差排序取最优）或 `LargestContour`（按面积取最大）
7. 所有选中轮廓的点合并为一个总点集
8. 坐标除以 4 映射回原始分辨率

**直线拟合 / Line Fitting:**
- `Cv2.FitLine(points, DistanceTypes.L2)` -- L2 最小二乘
- 输出：方向向量 (vx, vy)、经过点 (x0, y0)、角度 angle = atan2(vy, vx) * 180/pi
- RANSAC：随机采样 2 点构建直线模型，收集内点后用 `Cv2.FitLine` 精修
- 残差：点到直线距离 |Ax + By + C|

**圆拟合 / Circle Fitting:**
- 自定义最小二乘 `FitCircleLeastSquares`：归一化坐标（减均值除以尺度因子）后求解线性方程组
- 输出：圆心 (cx, cy)、半径 r
- RANSAC：随机采样 3 点构建圆模型，收集内点后用最小二乘精修
- 残差：|dist(point, center) - radius|

**椭圆拟合 / Ellipse Fitting:**
- `Cv2.FitEllipse(points)` -- Fitzgibbon 直接最小二乘法
- 输出：RotatedRect（中心、长短轴、旋转角）
- RANSAC：随机采样 5 点拟合椭圆，收集内点后重新拟合
- 残差：一阶几何距离近似 |F(x,y)| / ||grad(F)||，其中 F(x,y) = x^2/a^2 + y^2/b^2 - 1（椭圆局部坐标系）

**RANSAC 评分策略 / RANSAC Scoring:**
- 按内点数量降序、平均残差升序、最大残差升序三级排序选择最优模型
- 精修步骤：RANSAC 找到最优内点集后，用最小二乘对内点重新拟合
- 二次内点收集：精修后的模型重新收集内点，确保结果一致

**不确定度估计 / Uncertainty Estimation:**
- `UncertaintyPx = max(quantizationFloor, ResidualMean / sqrt(pointCount))`
- `quantizationFloor = 0.5 / ContourUpscale = 0.125px`（4x 上采样的量化极限）

## 实现策略 / Implementation Strategy

- **4x 上采样**：轮廓提取前将图像放大 4 倍，提升轮廓点的定位精度。坐标最终除以 4 映射回原始分辨率。
- **轮廓选择策略**：`BestResidual` 模式会预拟合每个轮廓并选择残差最小的；`LargestContour` 模式选择面积最大的。
- **FitResult 字典结构**：所有拟合结果封装在一个 `FitResult` 字典中，包含 `Geometry` 子字典存放具体几何参数。
- **执行状态与拟合状态分离**：算子执行成功但拟合失败时，仍返回成功执行结果，通过 `FitResult.Success=false` 和 `Message` 标识失败。
- **可视化**：结果图绘制选中轮廓（蓝色）和拟合图形（绿色 + 红色圆心）。

## 核心 API 调用链 / Core API Call Chain

1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetStringParam / GetIntParam / GetDoubleParam` -- 读取参数
3. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
4. `TryExtractContourPoints(gray, threshold, minArea, contourSelection, fitType, ...)`:
   a. `Cv2.Resize(gray, upsampled, 4x, Cubic)` -- 4x 上采样
   b. `Cv2.Threshold(upsampled, binary, threshold, 255, Binary)` -- 二值化
   c. `Cv2.FindContours(binary, contours, External, ApproxNone)` -- 轮廓提取
   d. 面积过滤 + 轮廓选择 + 坐标映射回原始分辨率
5. 分支拟合：
   - Line: `Cv2.FitLine(points, L2)` 或 RANSAC -> `TryEstimateLineModelRansac` -> `Cv2.FitLine(inliers)`
   - Circle: `FitCircleLeastSquares(points)` 或 RANSAC -> `TryEstimateCircleModelRansac` -> `FitCircleLeastSquares(inliers)`
   - Ellipse: `Cv2.FitEllipse(points)` 或 RANSAC -> `TryEstimateEllipseModelRansac` -> `Cv2.FitEllipse(inliers)`
6. `ComputeModelUncertaintyPx(fitResult, pointCount)` -- 不确定度计算
7. `MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(uncertaintyPx)` -- 置信度
8. `Cv2.DrawContours(resultImage, selectedContours, ...)` -- 绘制轮廓
9. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FitType` | `enum` | `"Circle"` | Line / Circle / Ellipse | 拟合类型。决定最终拟合模型及输出字段结构。 |
| `Threshold` | `double` | `127.0` | [0.0, 255.0] | 固定二值阈值。4x 上采样后使用此阈值进行二值化。 |
| `MinArea` | `int` | `100` | [0, +inf) | 最小轮廓面积（原始分辨率像素^2）。内部按 16x 缩放后与上采样轮廓比较。 |
| `MinPoints` | `int` | `5` | [3, 10000] | 合并后点集的最小点数门槛。不足时返回 DegenerateGeometry 失败。 |
| `ContourSelection` | `enum` | `"BestResidual"` | LargestContour / BestResidual | 轮廓选择策略。BestResidual: 按预拟合残差选最优；LargestContour: 按面积选最大。 |
| `RobustMethod` | `enum` | `"LeastSquares"` | LeastSquares / Ransac | 鲁棒方法。LeastSquares: 纯最小二乘；Ransac: RANSAC + 最小二乘精修。 |
| `RansacIterations` | `int` | `200` | [10, 5000] | RANSAC 迭代次数。仅 RobustMethod=Ransac 时生效。 |
| `RansacInlierThreshold` | `double` | `2.0` | (0, 100] | RANSAC 内点阈值（像素）。点到模型距离小于此值视为内点。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 输入图像。所有拟合都从图像分割轮廓点开始。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 结果图，绘制轮廓和拟合图形。 |
| `FitResult` | Fit Result | `Any` | 拟合结果字典，包含 Geometry 子字典和拟合质量指标。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `FitType` | `string` | 当前拟合类型。 |
| `PointCount` | `int` | 参与拟合的总点数（合并后的点集大小）。 |
| `ContourCount` | `int` | 有效轮廓数量。 |
| `SelectedContourCount` | `int` | 选中参与拟合的轮廓数量。 |
| `UncertaintyPx` | `double` | 拟合不确定度（像素）。 |
| `Confidence` | `double` | 置信度。 |
| `ResidualMean` | `double` | 所有点到拟合模型的平均残差。 |
| `ResidualMax` | `double` | 所有点到拟合模型的最大残差。 |

### FitResult 典型字段 / Typical FitResult Keys
- **通用字段**：`Success`, `FitType`, `RobustMethod`, `ContourSelection`, `PointCount`, `UncertaintyPx`, `Confidence`, `ResidualMean`, `ResidualMax`
- **直线拟合**：`Geometry.Line` = {Vx, Vy, X0, Y0, Angle}
- **圆拟合**：`Geometry.Circle` = {Center: Position, Radius}; `Geometry.Center`, `Geometry.Radius`
- **椭圆拟合**：`Geometry.Center`, `Geometry.MajorAxis`, `Geometry.MinorAxis`, `Geometry.Angle`
- **RANSAC 辅助字段**：`InlierCount`, `InlierRatio`, `RansacMeanResidual`, `RansacMaxResidual`, `RansacModel`

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 预处理 O(16*W*H)（4x 上采样）；RANSAC 模式额外 O(iterations * pointCount) |
| 典型耗时 (Typical Latency) | 最小二乘模式：约 5-20ms（取决于图像大小和轮廓复杂度）；RANSAC 模式：额外 10-50ms（取决于迭代次数和点数）。 |
| 内存特征 (Memory Profile) | 需要 4x 上采样图像（16x 像素数）、灰度图、二值图、轮廓点集和结果图。峰值约 O(16*W*H + P)。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：目标在阈值分割后轮廓清晰、需要拟合直线/圆/椭圆参数的场景。
- 适合 (Suitable)：需要从图像直接得到几何模型参数，不需要单独准备点集输入。
- 适合 (Suitable)：存在少量异常点时，RANSAC 模式可提升线/圆拟合的鲁棒性。
- 适合 (Suitable)：需要拟合质量评估（残差、不确定度、置信度）的精密测量场景。
- 不适合 (Not Suitable)：多个无关目标同时存在且会被并入同一总点集的图像。
- 不适合 (Not Suitable)：需要亚像素边缘采样或卡尺测量的高精度场景（请使用 CaliperTool 或 ArcCaliper）。
- 不适合 (Not Suitable)：低对比度或固定阈值难以稳定分割的复杂背景。

## 已知限制 / Known Limitations
1. 4x 上采样使内存消耗增加约 16 倍，大图像可能遇到内存压力。
2. 预处理仅使用固定阈值 `ThresholdTypes.Binary`，没有自适应阈值或形态学清理。
3. RobustMethod=Ransac 对三种拟合类型均有效（包括椭圆），但椭圆 RANSAC 需要至少 5 个点。
4. 轮廓选择策略 BestResidual 需要对每个轮廓预拟合一次，轮廓数量多时会增加耗时。
5. FitResult 字段结构会随 FitType 改变，下游消费前应根据拟合类型判断字段是否存在。
6. 执行成功但拟合失败时，返回成功执行结果 + FitResult.Success=false，流程编排时不能只看执行状态。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充 4x 上采样流程、三种拟合算法细节、RANSAC 评分策略、椭圆 RANSAC 支持、不确定度估计 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码补充轮廓合并逻辑、RANSAC 适用范围、FitResult 结构与执行语义说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
