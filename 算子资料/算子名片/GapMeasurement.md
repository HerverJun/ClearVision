# 间隙测量 / Gap Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GapMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.GapMeasurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子测量目标边缘或轮廓之间的间隙 (间距) 距离。支持两种输入模式：基于图像投影的自动检测，以及基于已知点列表的直接计算。

This operator measures the gap (spacing) distance between target edges or contours. It supports two input modes: automatic detection via image projection profiles, and direct computation from a known point list.

**图像投影模式 (Image Projection Mode)**：
当仅提供 Image 输入时，算子将灰度图像沿水平 (X) 和垂直 (Y) 方向分别进行多段聚合投影 (`Cv2.Reduce`)，选择方差更大的方向作为主扫描方向 (Auto 模式) 或使用指定方向。投影曲线经过 `IndustrialCaliperKernel.EstimateEdgeThreshold` 自适应阈值估计、`IndustrialCaliperKernel.DetectEdges` 边缘检测 (支持正负/双向)、`IndustrialCaliperKernel.BuildPairs` 构建明/暗条纹对，最后计算条纹对之间的间隙。明暗两组间隙集合中选择标准差更小的一组作为最终结果。若条纹对方法无结果，退化为 `DetectBrightStripeCenters` 检测亮条纹中心间距。

**Image Projection Mode**: When only Image input is provided, the operator builds multi-scan aggregated projection profiles (`Cv2.Reduce`) along both horizontal (X) and vertical (Y) directions, selects the direction with higher variance (Auto mode) or uses the specified direction. The projection curve undergoes adaptive threshold estimation (`IndustrialCaliperKernel.EstimateEdgeThreshold`), edge detection (`IndustrialCaliperKernel.DetectEdges`, supporting positive/negative/both polarities), bright/dark stripe pair building (`IndustrialCaliperKernel.BuildPairs`), then inter-stripe gap computation. The set with lower standard deviation (bright vs dark gaps) is chosen. If no stripe pair result, falls back to bright stripe center detection (`DetectBrightStripeCenters`).

**点列表模式 (Point List Mode)**：
当提供 Points 输入时，按指定方向 (或自动推断方向) 对点集排序，计算相邻点之间的坐标差作为间隙值。

**Point List Mode**: When Points input is provided, the point set is sorted along the specified direction (or auto-inferred), and coordinate differences between adjacent points yield gap values.

**鲁棒过滤 (Robust Filtering)**：
当 `RobustMode = true` 时，使用 MAD (Median Absolute Deviation) 方法过滤离群间隙值，阈值 = MAD * 1.4826 * `OutlierSigmaK`。

**Robust Filtering**: When `RobustMode = true`, gap outliers are filtered using MAD (Median Absolute Deviation) with threshold = MAD * 1.4826 * `OutlierSigmaK`.

## 实现策略 / Implementation Strategy

- 输入优先级：先检查 Points 输入，有则走点列表模式；否则检查 Image 输入，走投影模式。
- 投影模式下，`MultiScanCount` 控制聚合投影的扫描分段数 (1~64)，每段独立投影后取平均值，提高抗噪声能力。
- 边缘检测后执行最小峰间距过滤 (minPeakDistance = max(6, profileLength/150))，避免重复检测。
- 明暗条纹对的间隙选择策略：集合大小不同时取更多的一组；相同时取标准差更小的一组。
- 诊断系统 (`GapDiagnostics`) 检测低对比度 (stdDev < 8)、过曝 (>=35% 像素 >= 245) 和宽亮条纹三种异常状态。

## 核心 API 调用链 / Core API Call Chain

以图像投影模式为例：

1. `GetStringParam(@operator, "Direction", "Auto")` / `GetDoubleParam` / `GetIntParam` / `GetBoolParam` -- 读取全部参数
2. `TryGetPointList(inputs, out points)` -- 尝试获取点列表 (此处无，跳过)
3. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
4. `imageWrapper.GetMat()` -- 解码为 `Mat`
5. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换 (若需要)
6. `BuildAggregatedProjection(gray, true, multiScanCount)` -- 水平方向多段聚合投影
   - `Cv2.Reduce(band, projection, ReduceDimension.Row, ReduceTypes.Avg, CV_64F)` -- 每段独立投影
7. `BuildAggregatedProjection(gray, false, multiScanCount)` -- 垂直方向多段聚合投影
8. `ComputeVariance(xProjection)` / `ComputeVariance(yProjection)` -- 方差比较，选择主方向
9. `AssessDiagnostics(gray, profile)` -- 诊断低对比度/过曝/宽亮条纹
10. `AnalyzeProfileFeatures(profile, robustMode)` -- 核心分析
    - `IndustrialCaliperKernel.EstimateEdgeThreshold(profile, 2.0)` -- 自适应阈值
    - `IndustrialCaliperKernel.DetectEdges(profile, threshold, "Both", sigma)` -- 双向边缘检测
    - `IndustrialCaliperKernel.BuildPairs(edges, "positive_to_negative", ...)` -- 明条纹对
    - `IndustrialCaliperKernel.BuildPairs(edges, "negative_to_positive", ...)` -- 暗条纹对
    - `ChooseMoreStableGapSet(brightGaps, darkGaps)` -- 选择更稳定的间隙集
11. `ApplyGapOutlierFilter(gaps, outlierSigmaK)` -- MAD 离群过滤 (若 RobustMode=true)
12. `Cv2.PutText(imageToDraw, ...)` -- 标注统计信息
13. `CreateImageOutput(imageToDraw, outputData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Direction` | `enum` | `"Auto"` | Horizontal / Vertical / Auto | 投影方向。Auto = 自动选择方差更大的方向。 |
| `MinGap` | `double` | `0.0` | [0.0, 1000000.0] | 间隙下限过滤 (像素)。小于此值的间隙将被丢弃。0 = 不过滤。 |
| `MaxGap` | `double` | `0.0` | [0.0, 1000000.0] | 间隙上限过滤 (像素)。大于此值的间隙将被丢弃。0 = 不过滤。 |
| `ExpectedCount` | `int` | `0` | [0, 10000] | 期望间隙数量。超过此数量时只保留前 N 个。0 = 不限制。 |
| `RobustMode` | `bool` | `true` | true / false | 是否启用 MAD 离群值过滤。 |
| `OutlierSigmaK` | `double` | `3.0` | [0.5, 10.0] | 离群过滤系数 K。阈值 = MAD * 1.4826 * K。值越大保留越多样本。 |
| `MinValidSamples` | `int` | `0` | [0, 256] | 最少有效样本数。过滤后样本数不足时返回失败。0 = 不校验。 |
| `MultiScanCount` | `int` | `8` | [1, 64] | 投影聚合扫描分段数。分段越多抗噪性越强，但计算量增大。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | No | 输入待处理图像。与 Points 二选一。 |
| `Points` | Points | `PointList` | No | 预提取的点列表。与 Image 二选一；若同时提供则优先使用 Points。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 叠加了特征标注线 (黄色) 和统计信息的结果图像。 |
| `Gaps` | Gaps | `Any` | 所有有效间隙值的列表 (List<double>)。 |
| `MeanGap` | Mean Gap | `Float` | 间隙均值 (像素)。 |
| `MinGap` | Min Gap | `Float` | 最小间隙 (像素)。 |
| `MaxGap` | Max Gap | `Float` | 最大间隙 (像素)。 |
| `P95Gap` | P95 Gap | `Float` | 95 百分位间隙 (像素)。 |
| `StdDev` | StdDev | `Float` | 间隙标准差 (像素)。 |
| `ValidSampleRate` | Valid Sample Rate | `Float` | 有效样本率 = 过滤后数量 / 原始数量。 |
| `Count` | Count | `Integer` | 最终有效间隙数量。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `RawCount` | `int` | 过滤前的原始间隙数量。 |
| `RobustMode` | `bool` | 是否启用鲁棒过滤。 |
| `OutlierSigmaK` | `double` | 实际使用的离群过滤系数。 |
| `MultiScanCount` | `int` | 实际使用的扫描分段数。 |
| `LowContrast` | `bool` | 诊断标记：图像全局标准差 < 8，提示低对比度。 |
| `OverExposed` | `bool` | 诊断标记：>=35% 像素值 >= 245，提示过曝。 |
| `WideBrightStripe` | `bool` | 诊断标记：投影曲线中存在过宽的亮区域。 |
| `Confidence` | `double` | 置信度，等于 ValidSampleRate，范围 [0, 1]。 |
| `UncertaintyPx` | `double` | 不确定度 (像素)，等于 StdDev；无有效样本时为 NaN。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 图像投影 O(W*H)，边缘检测 O(P) (P = 投影长度)，MAD 过滤 O(N log N) (N = 间隙数)。总体 O(W*H + P + N log N)。 |
| 典型耗时 (Typical Latency) | 1-2MP 图像约 5-20ms。MultiScanCount 增大会线性增加投影时间。 |
| 内存特征 (Memory Profile) | 灰度副本 + 结果图像副本 + 投影数组 + 间隙列表。峰值约 2x 图像大小 + O(P + N)。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业视觉中周期性特征的间距/节距测量，如 PCB 焊盘间距、齿条齿距、栅格间距。
- 适合 (Suitable)：已提取点列表的快速间距计算 (Points 模式，无需图像处理)。
- 适合 (Suitable)：需要鲁棒统计输出 (均值/方差/百分位/有效率) 的批量间距检测。
- 不适合 (Not Suitable)：非均匀或非周期性特征的单点间距测量 (应使用距离测量或点线距离算子)。
- 不适合 (Not Suitable)：严重低对比度或过曝场景 (诊断系统会标记异常但仍可能无结果)。

## 已知限制 / Known Limitations
1. 图像投影模式假设特征在投影方向上呈现亮-暗交替的周期性图案；非周期特征可能无法正确检测。
2. `Auto` 方向选择基于投影方差，对于方差相近的双向特征可能选择不稳定的方向。
3. 过曝诊断 (OverExposed) 会抑制稳定峰检测，导致返回 NoFeature 失败。
4. 点列表模式不支持鲁棒过滤 (MAD)，直接计算相邻点间距。
5. 多段聚合投影 (`MultiScanCount`) 在图像高度/宽度不足以均分时可能导致部分分段为空。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述投影分析、条纹对间隙计算、MAD 鲁棒过滤、诊断系统等完整算法流程 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
