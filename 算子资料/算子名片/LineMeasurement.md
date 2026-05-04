# 直线测量 / Line Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LineMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.LineMeasurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子从灰度图像中检测直线特征，报告线段方向角、长度和拟合残差诊断信息。支持三种检测方法：标准霍夫变换、概率霍夫变换和最小二乘拟合直线。

This operator detects line features from grayscale images and reports line direction angle, length, and fitting residual diagnostics. It supports three detection methods: Standard Hough Transform, Probabilistic Hough Transform, and Least-Squares FitLine.

**标准霍夫变换 (HoughLines)**：对 Canny 边缘图执行 `Cv2.HoughLines`，以极坐标 `(rho, theta)` 表示检测到的无限直线。每条候选线通过求解与图像边界的交点裁剪为图像跨度 (image span)，再使用卡尺 (Caliper) 亚像素细化。

**Standard Hough Transform (HoughLines)**: Runs `Cv2.HoughLines` on the Canny edge map, representing detected infinite lines in polar coordinates `(rho, theta)`. Each candidate line is clipped to image bounds by computing intersections with image edges, then refined using caliper subpixel fitting.

**概率霍夫变换 (ProbabilisticHough)**：使用 `Cv2.HoughLinesP` 直接输出有限线段，受 `MinLength` 和 `MaxGap` 约束。每段同样通过卡尺核进行亚像素细化。

**Probabilistic Hough Transform (ProbabilisticHough)**: Uses `Cv2.HoughLinesP` to directly output finite segments, constrained by `MinLength` and `MaxGap`. Each segment is further refined via the caliper kernel for subpixel accuracy.

**拟合直线 (FitLine)**：先用概率霍夫变换生成种子线段，按长度降序排列，依次尝试卡尺细化。若无有效种子，退化为收集全部边缘点后执行 `Cv2.FitLine` (L2 距离) 最小二乘拟合。

**FitLine**: Seeds candidate segments via Probabilistic Hough, sorted by length descending, each refined by caliper. If no valid seed is found, falls back to collecting all edge points and running `Cv2.FitLine` (L2 distance) least-squares fitting.

卡尺细化的核心思路：沿线段法线方向等距采样截面 (band profile)，通过 `IndustrialCaliperKernel` 检测条纹中心 (stripe center)，再用 `Cv2.FitLine` 对细化后中心点集重新拟合，从而获得亚像素级精度和残差诊断。

The caliper refinement strategy: sample band profiles at equal intervals along the segment's normal direction, detect stripe centers via `IndustrialCaliperKernel`, then re-fit the refined center set with `Cv2.FitLine` to achieve subpixel accuracy and residual diagnostics.

## 实现策略 / Implementation Strategy

- 三种检测方法共享同一入口，通过 `Method` 参数分支。所有方法均先执行灰度转换 + Canny 边缘检测。
- 标准霍夫和概率霍夫方法检测后均尝试卡尺细化 (`TryRefineLineUsingCaliper`)，仅当线段长度 >= 12px 时触发。
- 卡尺细化中，`searchHalfWidth` 和 `averagingThickness` 根据线段长度自适应缩放，截面采样数固定为 41，分段数根据线段长度在 16-128 之间自动调整。
- 检测结果按 `ResidualMean` (升序) 优先，再按 `Length` (降序) 排序，输出最优线。
- 所有直线均绘制在结果图像上 (绿色，线宽 2)，颜色空间转换在内部完成。

## 核心 API 调用链 / Core API Call Chain

以 `ProbabilisticHough` 方法为例：

1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetStringParam(@operator, "Method", "ProbabilisticHough")` / `GetIntParam` / `GetDoubleParam` -- 读取参数
3. `imageWrapper.GetMat()` -- 解码为 `Mat`
4. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换 (若需要)
5. `Cv2.Canny(gray, edges, 50, 150)` -- Canny 边缘检测
6. `Cv2.HoughLinesP(edges, 1, Math.PI/180, threshold, minLength, maxGap)` -- 概率霍夫直线检测
7. `TryRefineLineUsingCaliper(gray, lineData, ...)` -- 卡尺亚像素细化
   - `IndustrialCaliperKernel.SampleBandProfile(gray, scanStart, scanEnd, averagingThickness, sampleCount)` -- 沿法线方向采样
   - `IndustrialCaliperKernel.EstimateEdgeThreshold(profile, 4.0)` -- 自适应阈值
   - `IndustrialCaliperKernel.DetectStripeCenters(profile, threshold, "Auto", 1.2, 1)` -- 检测条纹中心
   - `IndustrialCaliperKernel.InterpolatePosition(scanStart, scanEnd, centers[0], sampleCount)` -- 亚像素插值
   - `Cv2.FitLine(refinedCenters, L2, 0, 0.01, 0.01)` -- 对细化中心点重新拟合
8. `MeasurementGeometryHelper.NormalizeLineDirectionDegrees(angle)` -- 方向角归一化
9. `Cv2.Line(resultImage, pt1, pt2, green, 2)` -- 绘制检测线段
10. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"ProbabilisticHough"` | HoughLines / ProbabilisticHough / FitLine | 检测方法选择。HoughLines = 标准霍夫变换；ProbabilisticHough = 概率霍夫变换；FitLine = 最小二乘拟合。 |
| `Threshold` | `int` | `100` | [1, +INF) | 霍夫累加器阈值。值越大，检测到的线段越少但越可靠。HoughLines 和 ProbabilisticHough 共用该阈值。 |
| `MinLength` | `double` | `50.0` | [0.0, +INF) | 最短线段长度 (像素)。仅对 ProbabilisticHough 和 FitLine 方法有效，短于此长度的线段将被过滤。 |
| `MaxGap` | `double` | `10.0` | [0.0, +INF) | 线段最大允许间隙 (像素)。仅对 ProbabilisticHough 和 FitLine 方法有效，控制断裂线段的合并。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 输入待处理灰度或彩色图像，彩色图像内部自动转灰度。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 叠加了检测直线标注 (绿色线段) 的结果图像。 |
| `Angle` | 角度 | `Float` | 最优直线的方向角 (度)，归一化到 [0, 180) 范围。 |
| `Length` | 长度 | `Float` | 最优直线的长度 (像素)。 |
| `Line` | 直线数据 | `LineData` | 最优直线的完整几何数据 (StartX, StartY, EndX, EndY)。 |
| `LineCount` | 直线数量 | `Integer` | 检测到的有效直线总数。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `ResidualMean` | `double` | 卡尺细化后点集到拟合直线的平均残差 (像素)，反映直线拟合质量。 |
| `ResidualMax` | `double` | 卡尺细化后点集到拟合直线的最大残差 (像素)。 |
| `Method` | `string` | 实际执行的检测方法名称。 |
| `Lines` | `List<dict>` | 所有检测到的直线列表，每条包含 Line, StartX, StartY, EndX, EndY, Angle, Length, ResidualMean, ResidualMax。 |
| `Confidence` | `double` | 置信度，固定为 1.0。 |
| `UncertaintyPx` | `double` | 不确定度 (像素)，取 ResidualMean 值；若不可用则回退为 0.2。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Canny 边缘检测 O(W*H)；霍夫变换 O(W*H*ThetaBins)；卡尺细化 O(S*L)，其中 S = 分段数 (16~128)，L = 采样截面长度。 |
| 典型耗时 (Typical Latency) | 1-2MP 图像约 20-80ms，取决于边缘密度和方法选择。FitLine 方法通常最慢。 |
| 内存特征 (Memory Profile) | 需要输入图像灰度副本 + Canny 边缘图 + 结果图像副本，峰值约 3x 图像大小。卡尺细化额外分配 Point2f 列表。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业视觉中的线段检测与测量，如 PCB 走线方向、金属板材边缘定位、印刷线对齐检测。
- 适合 (Suitable)：需要亚像素精度直线拟合的场景，卡尺细化可提供残差诊断以评估拟合质量。
- 适合 (Suitable)：同时需要可视化标注和数值输出的检测工位。
- 不适合 (Not Suitable)：弯曲边缘或非直线特征的检测 (应使用轮廓检测或曲线拟合算子)。
- 不适合 (Not Suitable)：低对比度或严重噪声图像直接检测 (建议先使用滤波或增强算子预处理)。

## 已知限制 / Known Limitations
1. 三条方法共享同一个 `Threshold` 参数，但标准霍夫和概率霍夫对阈值的敏感度不同，需分别调优。
2. 卡尺细化要求线段长度 >= 12px，短线段将跳过细化步骤，精度可能降低。
3. FitLine 方法在无有效种子时直接对全部边缘点拟合，若图像噪声大可能产生错误直线。
4. `ResidualMean` 和 `ResidualMax` 在标准霍夫方法中仅在线段足够长 (触发卡尺细化) 时有值，否则为 NaN。
5. 输出方向角归一化到 [0, 180)，不区分直线方向的正反。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述三种检测方法的算法原理、卡尺亚像素细化流程、完整 API 调用链和性能特征 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
