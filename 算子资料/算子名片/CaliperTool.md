# 卡尺工具 / Caliper Tool

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CaliperToolOperator` |
| 枚举值 (Enum) | `OperatorType.CaliperTool` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

卡尺工具模拟工业卡尺的边缘对检测原理：沿一条扫描线对灰度图进行带状采样（band profile），在采样轮廓上检测梯度边缘，再将相邻的明暗/暗明边缘配对为边缘对（edge pair），最终以边缘对之间的欧氏距离作为宽度测量值。

The operator simulates an industrial caliper edge-pair detector: it samples a band-averaged intensity profile along a single scan line, detects gradient edges on that profile, pairs adjacent dark-to-light / light-to-dark edges into edge pairs, and reports the Euclidean distance between each pair as the measured width.

**边缘检测核心 / Edge Detection Core:**
- 采样轮廓通过 `IndustrialCaliperKernel.SampleBandProfile` 获取，沿扫描线方向对多行像素做带状均值以降低噪声。
- 自适应阈值 `max(EdgeThreshold, EstimateEdgeThreshold(profile))` 确保在低对比度场景下仍能稳定工作。
- 梯度边缘由 `IndustrialCaliperKernel.DetectEdges` 按极性（DarkToLight / LightToDark / Both）检测。

**边缘配对策略 / Edge Pairing Strategy:**
- 相邻且极性相反的边缘被配对为一对；`PairDirection` 参数控制配对方向约束（正到负、负到正、任意）。
- 最多输出 `ExpectedCount` 对边缘。

**亚像素精化 / Subpixel Refinement:**
- 可选的亚像素模式（`SubpixelAccuracy=true`）通过梯度质心（`gradient_centroid`）或梯度矩（`gradient_moment`）方法将边缘位置精化到亚像素级。
- 亚像素采样密度为扫描长度的 6 倍（非亚像素为 2.5 倍）。

**不确定度估计 / Uncertainty Estimation:**
- 每个边缘的定位不确定度基于局部梯度和噪声水平估算（`EstimateLocalizationSigmaPx`）。
- 边缘对的合成不确定度为两端边缘定位不确定度的 RSS（Root Sum of Squares）。

## 实现策略 / Implementation Strategy

- **扫描线构建**：根据 `Direction`（Horizontal/Vertical/Custom）和 ROI 中心构建扫描线起止点。Custom 模式下使用 `Angle` 参数计算对角线方向的扫描线。
- **带状采样**：使用 `IndustrialCaliperKernel.SampleBandProfile` 在扫描线两侧做带状均值采样，带宽由 ROI 短边的 1/6 决定（clamp 到 [3, 9] 像素）。
- **自适应阈值**：`EstimateEdgeThreshold` 基于轮廓梯度分布自动估算阈值，与用户设定的 `EdgeThreshold` 取较大值。
- **亚像素检测器**：`SubPixelEdgeDetector` 类提供梯度质心和梯度矩两种亚像素方法，内部将梯度窗口 reshape 为 1xN Mat 后调用 OpenCV 矩计算。
- **可视化输出**：结果图上绘制扫描线（黄色）、配对边缘（绿色）和未配对边缘（橙色），并标注宽度和配对数。

与 Halcon `measure_pos` 类似但更简化：本算子只支持单条扫描线，不支持多条平行卡尺；亚像素方法提供 gradient_centroid 和 gradient_moment 两种，而 Halcon 使用更复杂的亚像素内核。

## 核心 API 调用链 / Core API Call Chain

1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `GetStringParam / GetDoubleParam / GetIntParam / GetBoolParam` -- 读取所有参数
3. `ParseSearchRect(inputs, width, height)` -- 解析搜索区域 ROI
4. `BuildScanLine(roi, direction, angleDeg)` -- 构建扫描线起止点
5. `ResolveProfileSampleCount(scanLength, subpixel)` -- 确定采样密度
6. `IndustrialCaliperKernel.SampleBandProfile(gray, start, end, thickness, sampleCount)` -- 带状采样
7. `IndustrialCaliperKernel.EstimateEdgeThreshold(profile, minimumThreshold)` -- 自适应阈值
8. `IndustrialCaliperKernel.DetectEdges(profile, threshold, polarity)` -- 梯度边缘检测
9. `RefineSubpixel(profile, idx, ...)` (可选) -- 亚像素精化 (centroid / gradient_moment)
10. `IndustrialCaliperKernel.InterpolatePosition(start, end, position, sampleCount)` -- 轮廓位置映射回图像坐标
11. `BuildEdgePairs(detectedEdges, pairDirection, expectedCount)` -- 边缘配对
12. `DrawScanAndEdges(resultImage, scan, edges, pairCount, width)` -- 可视化绘制
13. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Direction` | `enum` | `"Horizontal"` | Horizontal / Vertical / Custom | 扫描线方向。Horizontal: 水平扫描；Vertical: 垂直扫描；Custom: 使用 Angle 参数自定义角度。 |
| `Angle` | `double` | `0.0` | [-180.0, 180.0] | 自定义扫描角度（度），仅在 Direction=Custom 时生效。 |
| `Polarity` | `enum` | `"Both"` | DarkToLight / LightToDark / Both | 边缘极性过滤。DarkToLight: 只检测从暗到亮的边缘；LightToDark: 只检测从亮到暗的边缘；Both: 检测所有边缘。 |
| `EdgeThreshold` | `double` | `18.0` | [1.0, 255.0] | 边缘梯度阈值。实际使用 max(此值, 自适应阈值)。 |
| `ExpectedCount` | `int` | `1` | [1, 100] | 期望的边缘对数量。检测到的配对数少于此值时返回 NoFeature 失败。 |
| `MeasureMode` | `enum` | `"edge_pairs"` | edge_pairs | 测量模式，当前仅支持 edge_pairs。 |
| `PairDirection` | `enum` | `"any"` | positive_to_negative / negative_to_positive / any | 边缘对配对方向约束。 |
| `SubpixelAccuracy` | `bool` | `false` | - | 是否启用亚像素边缘精化。启用后采样密度提升至 6x。 |
| `SubPixelMode` | `enum` | `"gradient_centroid"` | gradient_centroid / gradient_moment / zernike (legacy alias) | 亚像素算法。gradient_centroid: 梯度质心法；gradient_moment: 梯度矩法；zernike: gradient_moment 的旧别名。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入灰度或彩色图像。彩色图像会自动转换为灰度。 |
| `SearchRegion` | Search Region | `Rectangle` | No | 搜索区域 ROI。不提供时使用整幅图像。支持 Rect 对象或 {X, Y, Width, Height} 字典。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 可视化结果图，绘制扫描线、边缘点和宽度标注。 |
| `Width` | Width | `Float` | 边缘对的平均距离（像素），即测量宽度。 |
| `EdgePairs` | Edge Pairs | `PointList` | 配对边缘的坐标点列表，每两个点为一对。 |
| `PairCount` | Pair Count | `Integer` | 检测到的边缘对数量。 |
| `PairDistances` | Pair Distances | `Any` | 每对边缘的距离列表 (List<double>)。 |
| `AverageDistance` | Average Distance | `Float` | 所有边缘对距离的平均值。 |
| `DistanceStdDev` | Distance StdDev | `Float` | 边缘对距离的样本标准差。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `PairUncertainties` | `List<double>` | 每对边缘的合成定位不确定度 (RSS)。 |
| `SamplePitchPx` | `double` | 采样点间距（像素）。 |
| `ProfileSampleCount` | `int` | 轮廓采样点总数。 |
| `StatusCode` | `string` | `"OK"` 或 `"NoFeature"`。 |
| `StatusMessage` | `string` | 状态描述信息。 |
| `Confidence` | `double` | 置信度，检测到边缘对时为 1.0，否则为 0.0。 |
| `UncertaintyPx` | `double` | 宽度测量的合成不确定度（像素），取 DistanceStdDev 和 PairUncertainty 的较大值。 |
| `RequestedSubPixelMode` | `string` | 用户请求的亚像素模式原始值。 |
| `SubPixelMode` | `string` | 归一化后的亚像素模式。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(L + S)，L 为扫描线长度，S 为采样点数（S = O(L * oversample)，oversample 为 2.5 或 6） |
| 典型耗时 (Typical Latency) | 取决于扫描线长度和采样密度。典型 ROI（200px 扫描线）约 0.5-2ms；亚像素模式下约 2-5ms。 |
| 内存特征 (Memory Profile) | 主要内存开销为采样轮廓数组（最多 4096 个 double）和结果图像克隆。峰值内存约 O(W*H + S)。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业场景中沿直线方向的宽度测量，如 PCB 线宽、引脚间距、狭缝宽度。
- 适合 (Suitable)：需要边缘对检测的定位场景，如零件边缘位置确认。
- 适合 (Suitable)：高精度亚像素边缘定位需求，配合 gradient_centroid 或 gradient_moment 模式。
- 不适合 (Not Suitable)：需要沿曲线或圆弧路径扫描的场景（请使用 ArcCaliper 算子）。
- 不适合 (Not Suitable)：需要同时检测多条平行扫描线的场景（本算子仅支持单条扫描线）。
- 不适合 (Not Suitable)：无明显边缘梯度的低对比度场景。

## 已知限制 / Known Limitations
1. 当前仅支持单条扫描线的边缘对检测，不支持多条平行卡尺的批量测量。
2. MeasureMode 参数目前仅支持 `edge_pairs` 模式，其他模式会返回错误。
3. 亚像素精化依赖梯度窗口质量，在极低对比度或噪声较大的场景下可能退化为整像素精度。
4. SearchRegion 解析支持 Rect 对象和字典两种格式，但不支持旋转矩形。
5. `zernike` 亚像素模式已被重定向为 `gradient_moment` 的别名，不提供独立的 Zernike 矩实现。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码重写为金标准文档：补充完整的算法原理（带状采样、自适应阈值、亚像素精化、不确定度估计）、核心 API 调用链、参数语义、运行时附加输出和性能分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
