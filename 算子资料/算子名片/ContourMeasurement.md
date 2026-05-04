# 轮廓测量 / Contour Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ContourMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.ContourMeasurement` |
| 分类 (Category) | 检测 / Detection |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子对输入图像进行阈值二值化后提取轮廓，并对每个轮廓计算面积、周长、质心及多种形状因子。

**算法步骤：**

1. **灰度转换**：若输入为多通道图像，先转为单通道灰度图 `gray`。
2. **二值化**：使用固定阈值 `T` 进行全局二值化：`binary(x,y) = gray(x,y) > T ? 255 : 0`。
3. **轮廓提取**：调用 `FindContours`（`RetrievalModes.Tree`）获得所有轮廓集合 `{C_i}`。
4. **灰度加权面积**：对每个轮廓 `C_i`，创建掩码并计算灰度加权矩 `M_{00}`，面积 `A = M_{00} / 255`。
5. **质心计算**：`cx = M_{10} / M_{00}`, `cy = M_{01} / M_{00}`（若 `M_{00} > 0`）。
6. **形状因子**：
   - 圆形度：`circularity = 4 * pi * A / P^2`
   - 占空比：`extent = A / boundingRect_area`
   - 等效直径：`d_eq = sqrt(4A / pi)`
7. **不确定性估计**：`sigma = 0.5 / sqrt(max(N_pts, ceil(P)))`，限制在 `[0.01, 0.2]` px。
8. **排序输出**：按面积或周长降序排列，输出首条轮廓作为主结果。

> English: The operator binarizes the image with a fixed threshold, extracts contours via a tree hierarchy, computes grayscale-weighted area, perimeter, centroid, circularity, extent, equivalent diameter, and contour localization uncertainty, then sorts results by the selected metric.

## 实现策略 / Implementation Strategy
- 使用 `Cv2.Threshold` 做全局二值化，再用 `Cv2.FindContours`（`Tree` 模式、`ApproxNone`）提取完整轮廓。
- 面积不是简单像素计数，而是通过灰度掩码加权矩 `Cv2.Moments(maskedGray, false)` 得到，精度更高。
- 质心同样来自加权矩，避免了二值轮廓质心对噪声的敏感性。
- 不确定性通过 `EstimateContourUncertaintyPx` 估算，基于有效采样点数与周长的统计模型。
- 最终通过 `CreateImageOutput` 输出可视化结果图（绿色轮廓、红色质心、蓝色包围矩形）及所有测量数据。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetDoubleParam(@operator, "Threshold")` / `GetDoubleParam(@operator, "MinArea")` / `GetDoubleParam(@operator, "MaxArea")` / `GetStringParam(@operator, "SortBy")` -- 读取参数
3. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 多通道转灰度
4. `Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary)` -- 二值化
5. `Cv2.FindContours(binary, contours, RetrievalModes.Tree, ApproxNone)` -- 轮廓提取
6. `Cv2.DrawContours(contourMask, contours, i, White, -1)` -- 逐轮廓掩码绘制
7. `Cv2.Moments(maskedGray, false)` -- 灰度加权矩计算
8. `Cv2.ArcLength(contour, true)` -- 周长计算
9. `EstimateContourUncertaintyPx(contourPointCount, perimeter)` -- 不确定性估算
10. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Threshold` | `double` | `127.0` | `[0, 255]` | 二值化阈值。低于此值的像素视为背景，高于此值的视为前景。 |
| `MinArea` | `int` | `100` | `[0, +inf)` | 最小面积过滤阈值，低于此面积的轮廓将被忽略。 |
| `MaxArea` | `int` | `100000` | `[MinArea, +inf)` | 最大面积过滤阈值，高于此面积的轮廓将被忽略。 |
| `SortBy` | `enum` | `Area` | `Area` / `Perimeter` | 轮廓排序方式。`Area` 按面积降序，`Perimeter` 按周长降序。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Input Image` | `Image` | Yes | 输入图像，支持灰度与多通道。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Result Image` | `Image` | 可视化结果图，包含绿色轮廓、红色质心和蓝色包围矩形。 |
| `Area` | `Area` | `Float` | 首条（最大）轮廓的灰度加权面积。 |
| `Perimeter` | `Perimeter` | `Float` | 首条轮廓的周长。 |
| `ContourCount` | `Contour Count` | `Integer` | 通过面积过滤后保留的轮廓总数。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Contours` | `List<Dictionary>` | 所有轮廓的详细测量数据列表，含 Index、Area、Perimeter、CenterX/Y、Circularity、Extent、EquivalentDiameter、UncertaintyPx 等字段。 |
| `CenterX` / `CenterY` | `Double` | 首条轮廓的灰度加权质心坐标。 |
| `BoundingRect` | `String` | 首条轮廓包围矩形 `"x,y,w,h"`。 |
| `Circularity` | `Double` | 首条轮廓的圆形度。 |
| `Extent` | `Double` | 首条轮廓的占空比。 |
| `EquivalentDiameter` | `Double` | 首条轮廓的等效直径。 |
| `UncertaintyPx` | `Double` | 轮廓定位不确定度（像素）。 |
| `Confidence` | `Double` | 基于不确定度计算的置信度。 |
| `StatusCode` | `String` | `OK` 或 `NoFeature`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(N)` 其中 `N` 为图像像素数（阈值化 + FindContours），单轮轮廓处理为 `O(K * P_i)`，`K` 为轮廓数、`P_i` 为各轮廓点数。 |
| 典型耗时 (Typical Latency) | 取决于轮廓数量与面积过滤比例，主要耗时在 FindContours 与逐轮廓矩计算。 |
| 内存特征 (Memory Profile) | 峰值包含原图 clone、灰度图、二值图、逐轮廓掩码（复用），约为输入图像的 3-4 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：工业视觉中对已知形状目标的面积、周长、圆度等几何测量。
- **适合 (Suitable)**：缺陷检测中统计缺陷数量与大小分布。
- **适合 (Suitable)**：需要灰度加权面积（而非纯二值面积）的精密测量场景。
- **不适合 (Not Suitable)**：图像对比度极低、轮廓无法通过全局阈值分离的情况。
- **不适合 (Not Suitable)**：需要亚像素精度轮廓拟合的计量场景。
- **不适合 (Not Suitable)**：实时性要求极高的逐帧处理（轮廓数量多时开销较大）。

## 已知限制 / Known Limitations
1. 使用全局固定阈值，对光照不均匀的图像可能无法完整提取轮廓。
2. 轮廓面积基于灰度加权矩，与传统像素计数面积含义不同，不可直接比较。
3. `RetrievalModes.Tree` 会提取层级嵌套轮廓，包含孔洞轮廓，需注意面积过滤是否足够。
4. 不确定性估计基于采样点数的统计模型，非严格计量不确定度。
5. 输出仅包含首条轮廓的主结果端口值（Area/Perimeter），完整列表需通过附加输出 Contours 获取。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写至金标准 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
