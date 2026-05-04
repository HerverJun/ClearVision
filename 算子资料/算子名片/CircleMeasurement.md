# 圆测量 / Circle Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CircleMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.CircleMeasurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

圆测量算子提供两种圆形检测方法：霍夫圆变换（HoughCircle）和椭圆拟合（FitEllipse），检测后可选卡尺精化（Caliper Refinement）提升圆心和半径精度。

The Circle Measurement operator provides two circular detection methods: Hough Circle Transform and Ellipse Fitting. After detection, optional caliper-based refinement improves center and radius accuracy.

**方法一：霍夫圆变换 / Method 1: Hough Circle Transform**
- 输入图像先经高斯模糊（kernel 9x9, sigma=2）降噪
- 调用 `Cv2.HoughCircles(image, HoughModes.Gradient, dp, minDist, param1, param2, minRadius, maxRadius)`
- `dp`: 累加器分辨率与图像分辨率的比值
- `minDist`: 检测到的圆之间的最小距离
- `param1`: Canny 边缘检测的高阈值
- `param2`: 累加器投票阈值
- 检测到的圆可选通过卡尺精化

**方法二：椭圆拟合 / Method 2: Fit Ellipse**
- 高斯模糊（kernel 5x5, sigma=1.5）后，使用 Otsu 二值化（正反两种极性）
- 提取外轮廓，过滤掉帧大小轮廓和面积过小的轮廓
- 对每个有效轮廓（>= 5 点）调用 `Cv2.FitEllipse` 拟合椭圆
- 计算等效半径 = (width + height) / 4，按 minRadius/maxRadius 过滤
- 圆度 = 4 * pi * area / perimeter^2，限制在 [0, 1]

**卡尺精化 / Caliper Refinement (HoughCircle 专用):**
- 以霍夫圆的圆心和半径为种子，沿圆周均匀采样 `angularSamples` 个方向（clamp 到 [36, 120]）
- 每个方向上从 `(radius - searchHalfWidth)` 到 `(radius + searchHalfWidth)` 提取带状轮廓
- 通过 `IndustrialCaliperKernel` 检测边缘，取最接近期望位置的边缘
- 收集 >= 8 个边缘点后，通过最小二乘法（`FitCircleLeastSquares`）拟合精化圆
- 计算 RMSE 作为拟合残差指标

**最小二乘圆拟合 / Least Squares Circle Fit:**
- 使用归一化坐标（减均值除以尺度因子）提升数值稳定性
- 求解线性方程组得到圆心 (cx, cy) 和半径 r
- RMSE = sqrt(mean((dist(point, center) - radius)^2))

**圆度计算 / Circularity Calculation:**
- 在检测到的圆 ROI 上做 Canny 边缘检测
- 提取最大轮廓，计算圆度 = 4 * pi * area / perimeter^2

## 实现策略 / Implementation Strategy

- **双方法架构**：HoughCircle 适合快速检测，FitEllipse 适合轮廓清晰的场景。两种方法共享输出结构。
- **卡尺精化**：仅对 HoughCircle 结果生效。利用 `IndustrialCaliperKernel` 的带状采样和边缘检测能力，将像素级精度提升到亚像素级。
- **结果排序**：多圆场景下，按 `ResidualRmse`（升序）和 `Circularity`（降序）排序，最优圆排在最前。
- **状态报告**：输出 `StatusCode`（OK/NoFeature）、`Confidence`（检测到为 1.0，否则 0.0）和 `UncertaintyPx`（基于 RMSE）。
- **帧轮廓过滤**：FitEllipse 方法会过滤触及图像边界的轮廓，避免误检图像边框。

## 核心 API 调用链 / Core API Call Chain

1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetStringParam(@operator, "Method")` / `GetIntParam` / `GetDoubleParam` -- 读取参数
3. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
4. 分支 A (HoughCircle):
   a. `Cv2.GaussianBlur(gray, blurred, Size(9,9), 2)` -- 高斯模糊
   b. `Cv2.HoughCircles(blurred, Gradient, dp, minDist, param1, param2, minRadius, maxRadius)` -- 霍夫圆检测
   c. `TryRefineCircleByCaliper(gray, cx, cy, radius, ...)` -- 卡尺精化:
      - `IndustrialCaliperKernel.SampleBandProfile(gray, start, end, thickness, sampleCount)` -- 带状采样
      - `IndustrialCaliperKernel.DetectEdges(profile, threshold, "Both", sigma: 1.2)` -- 边缘检测
      - `FitCircleLeastSquares(edgePoints)` -- 最小二乘圆拟合
   d. `CalculateCircularity(gray, center, radius)` -- 圆度计算
5. 分支 B (FitEllipse):
   a. `Cv2.GaussianBlur(gray, blurred, Size(5,5), 1.5)` -- 高斯模糊
   b. `Cv2.Threshold(blurred, binary, 0, 255, Binary|Otsu)` -- Otsu 二值化（正反两种极性）
   c. `Cv2.FindContours(binary, contours, External, ApproxSimple)` -- 轮廓提取
   d. `Cv2.FitEllipse(contour)` -- 椭圆拟合
6. `SortCircleCandidates(circleResults, circleDataList)` -- 按 RMSE 和圆度排序
7. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"HoughCircle"` | HoughCircle / FitEllipse | 检测方法。HoughCircle: 霍夫圆变换；FitEllipse: 椭圆拟合。 |
| `MinRadius` | `int` | `10` | [0, +inf) | 最小检测半径（像素）。 |
| `MaxRadius` | `int` | `200` | [0, +inf) | 最大检测半径（像素）。必须大于 MinRadius。 |
| `Dp` | `double` | `1.0` | [0.5, 4.0] | 累加器分辨率比。值越大分辨率越低，检测速度越快。仅 HoughCircle 生效。 |
| `MinDist` | `double` | `50.0` | [1.0, +inf) | 检测到的圆之间的最小圆心距离（像素）。仅 HoughCircle 生效。 |
| `Param1` | `double` | `100.0` | [0.0, 255.0] | Canny 边缘检测高阈值。仅 HoughCircle 生效。 |
| `Param2` | `double` | `30.0` | [0.0, 255.0] | 累加器投票阈值，越小检测到越多圆。仅 HoughCircle 生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 输入灰度或彩色图像。彩色图像会自动转换为灰度。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 可视化结果图，绘制检测到的圆和圆心。 |
| `Radius` | 半径 | `Float` | 最优圆的半径（像素）。HoughCircle 模式下为卡尺精化后的值。 |
| `Center` | 圆心 | `Point` | 最优圆的圆心坐标 (Position 对象)。 |
| `Circle` | 圆数据 | `CircleData` | 最优圆的 CircleData 结构体 (CenterX, CenterY, Radius)。 |
| `CircleCount` | 圆数量 | `Integer` | 检测到的圆总数。 |
| `Circularity` | 圆度 | `Float` | 最优圆的圆度指标，范围 [0, 1]，1 为完美圆。 |
| `CircleDataList` | 圆数据列表 | `Any` | 所有检测到的圆的 CircleData 列表。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Circles` | `List<Dictionary>` | 所有圆的详细信息列表，包含 Center, Radius, Circularity, ResidualRmse 等。 |
| `Method` | `string` | 实际使用的检测方法。 |
| `ResidualRmse` | `double` | 最优圆的拟合残差 RMSE（仅 HoughCircle+Caliper 时有效）。 |
| `RefinedEdgePointCount` | `int` | 卡尺精化使用的边缘点数量。 |
| `StatusCode` | `string` | `"OK"` 或 `"NoFeature"`。 |
| `StatusMessage` | `string` | 状态描述。 |
| `Confidence` | `double` | 检测到圆时为 1.0，否则为 0.0。 |
| `UncertaintyPx` | `double` | 不确定度，基于 ResidualRmse 或默认 0.5px。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | HoughCircle: O(W*H) + O(R*angularSamples*S) 精化；FitEllipse: O(W*H) + O(N_contours * contour_points) |
| 典型耗时 (Typical Latency) | HoughCircle: 约 5-20ms（取决于图像大小和半径范围）；FitEllipse: 约 3-15ms。卡尺精化额外增加 1-5ms。 |
| 内存特征 (Memory Profile) | 需要灰度图、模糊图、结果图克隆。HoughCircle 需要累加器空间。峰值约 O(W*H) + O(accum)。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：孔径检测、圆形零件定位、圆心坐标和半径测量。
- 适合 (Suitable)：需要亚像素精度的圆测量场景（HoughCircle + 卡尺精化）。
- 适合 (Suitable)：多圆检测场景（通过 MinDist 控制圆间距）。
- 适合 (Suitable)：椭圆度评估（FitEllipse 方法可输出长短轴信息）。
- 不适合 (Not Suitable)：严重遮挡或只有部分圆弧可见的场景。
- 不适合 (Not Suitable)：低对比度且无明显边缘的圆形结构。

## 已知限制 / Known Limitations
1. 卡尺精化仅对 HoughCircle 方法生效，FitEllipse 方法不支持后续精化。
2. 卡尺精化要求种子半径 >= 8px，过小的圆无法精化。
3. FitEllipse 方法在 Otsu 二值化失败（无有效轮廓）时返回 NoFeature，不提供降级策略。
4. 圆度计算基于 ROI 区域的 Canny 边缘轮廓，可能受背景纹理干扰。
5. 多圆场景下仅输出最优圆的详细信息（Radius, Center, Circularity），其他圆需通过 CircleDataList 获取。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充双方法架构原理、卡尺精化流程、最小二乘圆拟合算法、圆度计算、结果排序逻辑 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
