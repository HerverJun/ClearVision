# 宽度测量 / Width Measurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `WidthMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.WidthMeasurement` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

该算子测量两条近似平行边缘/线段之间的宽度。支持自动边缘检测模式和手动线段指定模式，通过多扫描卡尺测量获取亚像素级宽度样本并进行鲁棒统计。

This operator measures the width between two approximately parallel edges/lines. It supports automatic edge detection mode and manual line specification mode, obtaining subpixel width samples via multi-scan caliper measurements and performing robust statistics.

**自动边缘检测模式 (AutoEdge Mode)**：
对输入图像执行 Canny 边缘检测 + 概率霍夫变换 (`Cv2.HoughLinesP`) 提取候选线段，按长度排序取前 24 条，遍历所有线段对寻找最优平行线对。评分函数 = len1 + len2 + separation*0.5 - angleDiff*10，约束条件为角度差 <= 10 度且间距 >= 2px。

**AutoEdge Mode**: Runs Canny edge detection + Probabilistic Hough Transform (`Cv2.HoughLinesP`) to extract candidate segments, takes the top 24 by length, and searches all pairs for the best parallel pair. Scoring: len1 + len2 + separation*0.5 - angleDiff*10, constrained to angle diff <= 10 deg and separation >= 2px.

**手动线段模式 (ManualLines Mode)**：
直接使用外部输入的 Line1 和 Line2 作为参考线段。

**Manual Lines Mode**: Directly uses externally provided Line1 and Line2 as reference lines.

**卡尺宽度测量 (Caliper Width Measurement)**：
在 Line1 上等距采样 `MultiScanCount` 个参考点，对每个参考点：
1. 沿法线方向 (Perpendicular) 或自定义角度 (Custom) 向 Line2 投射射线，确定扫描带。
2. 调用 `IndustrialCaliperKernel.SampleBandProfile` 沿扫描带采样灰度截面。
3. 调用 `IndustrialCaliperKernel.EstimateEdgeThreshold` 估计自适应阈值。
4. 调用 `IndustrialCaliperKernel.DetectEdges` 检测双向边缘。
5. 调用 `IndustrialCaliperKernel.BuildPairs` 构建边缘对，取最宽的一对。
6. 调用 `IndustrialCaliperKernel.InterpolatePosition` 精确定位两侧边缘位置。
7. 计算两点间欧氏距离作为该扫描线的宽度样本。

**Caliper Width Measurement**: Samples `MultiScanCount` reference points equally spaced on Line1. For each reference point:
1. Cast a ray along normal (Perpendicular) or custom angle (Custom) toward Line2 to define the scan band.
2. `IndustrialCaliperKernel.SampleBandProfile` samples the grayscale profile along the band.
3. `IndustrialCaliperKernel.EstimateEdgeThreshold` estimates adaptive threshold.
4. `IndustrialCaliperKernel.DetectEdges` detects edges in both polarities.
5. `IndustrialCaliperKernel.BuildPairs` constructs edge pairs, takes the widest.
6. `IndustrialCaliperKernel.InterpolatePosition` precisely locates both edge positions.
7. Euclidean distance between the two edge points yields the width sample for this scan line.

**鲁棒统计 (Robust Statistics)**：
优先使用亚像素细化样本；若全部为非亚像素则退化使用全部样本。通过 MAD (Median Absolute Deviation) 方法过滤离群值，阈值 = MAD * 1.4826 * `OutlierSigmaK`。最终输出均值、最小、最大、P95、标准差和有效样本率。

**Robust Statistics**: Prefers subpixel-refined samples; falls back to all samples if none are subpixel. Outliers are filtered via MAD (Median Absolute Deviation) with threshold = MAD * 1.4826 * `OutlierSigmaK`. Outputs mean, min, max, P95, standard deviation, and valid sample rate.

## 实现策略 / Implementation Strategy

- `SampleCount` 为目标采样数 (10~256)，`MultiScanCount` 为加密扫描密度 (必须 >= SampleCount)，实际通过 `MeasurementRoiHelper.ReadIntParameter` 读取，兼容历史字段名 `NumSamples`。
- `ManualLines` 模式的语义是"参考线约束下的真实边缘测量"：即使提供了两条参考线，实际宽度仍由卡尺边缘定位决定，而非几何间距。
- 没有真实边缘证据时算子直接失败，不会回退为两条参考线的几何间距。
- 采样截面的 `averagingThickness` 根据扫描线长度自适应缩放：clamp(length/18, 2, 6)。
- 结果图像叠加绘制：参考线 (绿色/黄色)、扫描测量线 (红色 = 亚像素, 橙色 = 非亚像素)、统计文字。

## 核心 API 调用链 / Core API Call Chain

1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `GetStringParam / GetIntParam / GetDoubleParam / GetBoolParam` -- 读取全部参数
3. `ResolveSampleCount(@operator)` -- 解析采样数 (兼容 NumSamples)
4. **AutoEdge 分支**:
   - `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
   - `Cv2.Canny(gray, edges, 60, 160)` -- Canny 边缘检测
   - `Cv2.HoughLinesP(edges, 1, Pi/180, 60, 50, 15)` -- 概率霍夫线段检测
   - `TryDetectParallelLines(src, out line1, out line2)` -- 寻找最优平行线对
5. **ManualLines 分支**:
   - `TryParseLine(line1Obj, out line1)` / `TryParseLine(line2Obj, out line2)` -- 解析线段输入
6. `BuildMeasurementSamples(gray, line1, line2, multiScanCount, direction, customAngle)` -- 核心测量
   - 对每个采样点: `ProjectPointToLine` 或 `TryIntersectRayWithLine` -- 确定扫描方向
   - `TryMeasureWidthByCaliper(gray, start, end, ...)` -- 卡尺边缘定位
     - `IndustrialCaliperKernel.SampleBandProfile(gray, start, end, thickness, count)` -- 灰度截面采样
     - `IndustrialCaliperKernel.EstimateEdgeThreshold(profile, 3.0)` -- 自适应阈值
     - `IndustrialCaliperKernel.DetectEdges(profile, threshold, "Both")` -- 双向边缘检测
     - `IndustrialCaliperKernel.BuildPairs(edges, "any", ...)` -- 边缘对构建
     - `IndustrialCaliperKernel.InterpolatePosition(start, end, pos, count)` -- 亚像素定位
7. `ApplyMadOutlierFilter(widths, outlierSigmaK)` -- MAD 离群过滤
8. `DrawMeasurementOverlay(resultImage, ...)` -- 绘制叠加层
9. `CreateImageOutput(resultImage, ...)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MeasureMode` | `enum` | `"AutoEdge"` | AutoEdge / ManualLines | 测量模式。AutoEdge = 自动检测平行线；ManualLines = 使用外部输入线段。 |
| `SampleCount` | `int` | `24` | [10, 256] | 目标采样数量。沿参考线等距分布的扫描截面数。 |
| `Direction` | `enum` | `"Perpendicular"` | Perpendicular / Custom | 扫描方向。Perpendicular = 沿参考线法线方向；Custom = 自定义角度。 |
| `CustomAngle` | `double` | `0.0` | [-180.0, 180.0] | 自定义扫描角度 (度)。仅当 Direction=Custom 时生效。 |
| `RobustMode` | `bool` | `true` | true / false | 是否启用 MAD 离群值过滤。 |
| `OutlierSigmaK` | `double` | `3.0` | [0.5, 10.0] | 离群过滤系数 K。阈值 = MAD * 1.4826 * K。 |
| `MinValidSamples` | `int` | `0` | [0, 256] | 最少有效样本数。不足时返回失败。0 = 不校验。 |
| `MultiScanCount` | `int` | `24` | [10, 256] | 加密扫描密度，必须 >= SampleCount。用于提高边缘覆盖率。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像。 |
| `Line1` | Line 1 | `LineData` | No | 第一条参考线。仅 ManualLines 模式必需。 |
| `Line2` | Line 2 | `LineData` | No | 第二条参考线。仅 ManualLines 模式必需。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 叠加了参考线、扫描线和统计信息的结果图像。 |
| `Width` | Width | `Float` | 宽度均值 (像素)，等于 MeanWidth。 |
| `MeanWidth` | Mean Width | `Float` | 有效宽度样本均值 (像素)。 |
| `MinWidth` | Min Width | `Float` | 有效宽度样本最小值 (像素)。 |
| `MaxWidth` | Max Width | `Float` | 有效宽度样本最大值 (像素)。 |
| `P95Width` | P95 Width | `Float` | 95 百分位宽度 (像素)。 |
| `StdDev` | StdDev | `Float` | 宽度标准差 (像素)。 |
| `ValidSampleRate` | Valid Sample Rate | `Float` | 有效样本率 = 亚像素样本数 / 总样本数。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Direction` | `string` | 实际使用的扫描方向 (Perpendicular/Custom)。 |
| `RefinedSampleCount` | `int` | 使用亚像素边缘定位的样本数。 |
| `SampleCount` | `int` | 目标采样数参数值。 |
| `MultiScanCount` | `int` | 加密扫描密度参数值。 |
| `ExecutedScanCount` | `int` | 实际完成的扫描数。 |
| `ValidSampleCount` | `int` | 通过边缘定位和鲁棒过滤后的有效样本数。 |
| `RobustMode` | `bool` | 是否启用鲁棒过滤。 |
| `OutlierSigmaK` | `double` | 实际使用的离群过滤系数。 |
| `Confidence` | `double` | 置信度，等于 ValidSampleRate，范围 [0, 1]。 |
| `UncertaintyPx` | `double` | 不确定度 (像素)，等于 StdDev。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | AutoEdge: Canny O(W*H) + HoughLinesP O(W*H) + 线对搜索 O(C^2) + 卡尺测量 O(M*L)。ManualLines: 仅卡尺测量 O(M*L)。其中 M = MultiScanCount, L = 每条截面的采样密度。 |
| 典型耗时 (Typical Latency) | ManualLines 模式 1-2MP 图像约 10-30ms。AutoEdge 模式因霍夫变换增加约 10-20ms。 |
| 内存特征 (Memory Profile) | 灰度副本 + 结果图像 + 每条扫描线的 profile 数组。峰值约 2x 图像大小 + O(M*L)。 |

## 适用场景 / Use Cases
- 适合 (Suitable)：工业视觉中板材/带材/线材的宽度测量。
- 适合 (Suitable)：已知参考线约束下的精确宽度检测 (ManualLines 模式)。
- 适合 (Suitable)：需要统计输出 (均值/方差/百分位/有效率) 和鲁棒离群过滤的批量宽度检测。
- 不适合 (Not Suitable)：弯曲边缘或非平行边界的宽度测量。
- 不适合 (Not Suitable)：宽度小于几个像素的微小特征 (卡尺精度受限于采样密度)。

## 已知限制 / Known Limitations
1. AutoEdge 模式假设图像中存在两条明确的平行线段；若线段断裂或遮挡可能检测失败。
2. 平行线对搜索限制为前 24 条最长线段，若目标线段不在其中则无法检测。
3. ManualLines 模式中若参考线方向与实际边缘方向偏差大，卡尺扫描可能无法定位到正确边缘。
4. `MultiScanCount` 必须 >= `SampleCount`，不满足时参数验证失败。
5. 射线-线段交点计算 (`TryIntersectRayWithLine`) 在射线与目标线段平行时跳过该采样点。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写为金牌文档：精确描述 AutoEdge 平行线检测、卡尺宽度测量、MAD 鲁棒过滤、SampleCount/MultiScanCount 契约等 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
