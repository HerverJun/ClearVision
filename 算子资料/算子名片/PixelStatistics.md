# 像素统计 / Pixel Statistics

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PixelStatisticsOperator` |
| 枚举值 (Enum) | `OperatorType.PixelStatistics` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子在指定 ROI 和掩码条件下，对图像像素进行统计分析，输出均值、标准差、极值、中位数及非零计数等指标。

**核心统计量：**

- **均值**：`mean = sum(x_i) / N`
- **方差**：`var = sum(x_i^2) / N - mean^2`（总体方差）
- **标准差**：`stddev = sqrt(var)`
- **中位数**：对 8 位图像通过直方图累加高效求解；对一般深度通过排序求解。
- **中位绝对偏差（MAD）**：`MAD = median(|x_i - median|)`，通过偏差直方图高效计算。
- **标准误**：`SE = stddev / sqrt(N)`
- **置信度**：`confidence = 1 / (1 + SE)`

**8 位优化路径**：当输入为 `CV_8U` 单通道时，使用 256-bin 直方图一次遍历完成所有统计量计算，避免逐像素列表分配。

**多通道处理**：
- `Gray`：转灰度后统计。
- `R`/`G`/`B`：提取单通道后统计。
- `All`：对 8 位多通道图像使用扁平化（flattened）直方图同时统计所有通道，再分别输出每通道统计。

> English: The operator computes pixel-level statistics (mean, stddev, min, max, median, MAD, non-zero count) over a masked ROI. An optimized histogram-based path handles 8-bit images in a single pass, while a generic path handles arbitrary depths via value extraction.

## 实现策略 / Implementation Strategy
- ROI 通过 `MeasurementRoiHelper.ResolveRoi` 解析，支持 `RoiX/Y/W/H` 参数或默认全图。
- 掩码通过 `ResolveMask` 处理：自动转灰度、与源图或 ROI 尺寸对齐、二值化为 `(0, 255)`。
- 8 位单通道走 `Compute8BitStatistics`，通过 `GetGenericIndexer<byte>` 直接遍历，避免 `ExtractValues` 的 `List<double>` 分配。
- 8 位多通道 `All` 模式走 `TryComputeAll8BitStatistics`，逐像素遍历 Vec3b/Vec4b，同时累积每通道和聚合直方图。
- 中位数从直方图高效求解：累加直方图直到 `seen > N/2`，时间复杂度 `O(256)`。
- MAD 通过偏差直方图（分辨率 0.5 级）求解，避免对全部像素排序。
- 非 8 位图像回退到通用路径：`ExtractValues` 遍历像素生成 `List<double>`，再调用通用 `ComputeStatistics`。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetStringParam(@operator, "Channel", "Gray")` -- 读取通道选择
3. `MeasurementRoiHelper.ResolveRoi(@operator, src.Width, src.Height)` -- 解析 ROI
4. `new Mat(src, roi)` -- 裁剪 ROI
5. `ResolveMask(inputs, roi, src.Size(), out maskError)` -- 解析掩码
6. 8 位优化路径：
   - `TryComputeAll8BitStatistics(roiMat, mask, ...)` -- All 模式
   - `Compute8BitStatistics(analysis, mask)` -- 单通道模式
7. 通用路径：
   - `ResolveAnalysisChannels(roiMat, channel)` -- 通道分离
   - `ComputeStatistics(analysisChannel.Data, mask)` / `ComputeStatistics(values)` -- 统计计算
8. `CreateStatisticsDictionary(aggregateStats)` -- 构建输出字典
9. `OperatorExecutionOutput.Success(output)` -- 返回

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `RoiX` | `int` | `0` | `[0, +inf)` | ROI 左上角 X 坐标。0 且 RoiY/RoiW/RoiH 均为 0 时使用全图。 |
| `RoiY` | `int` | `0` | `[0, +inf)` | ROI 左上角 Y 坐标。 |
| `RoiW` | `int` | `0` | `[0, +inf)` | ROI 宽度。0 表示自动计算（图像宽度 - RoiX）。 |
| `RoiH` | `int` | `0` | `[0, +inf)` | ROI 高度。0 表示自动计算（图像高度 - RoiY）。 |
| `Channel` | `enum` | `Gray` | `Gray` / `R` / `G` / `B` / `All` | 分析通道。`Gray` 转灰度；`R`/`G`/`B` 提取单通道；`All` 分别统计所有通道。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Image` | `Image` | Yes | 输入图像，支持灰度与多通道、多种位深。 |
| `Mask` | `Mask` | `Image` | No | 掩码图像，非零像素参与统计。需与源图或 ROI 尺寸匹配。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Mean` | `Mean` | `Float` | 像素均值。 |
| `StdDev` | `StdDev` | `Float` | 像素标准差。 |
| `Min` | `Min` | `Integer` | 最小像素值。 |
| `Max` | `Max` | `Integer` | 最大像素值。 |
| `Median` | `Median` | `Integer` | 像素中位数。 |
| `NonZeroCount` | `NonZero Count` | `Integer` | 非零像素数量。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Range` | `Double` | 极差 `Max - Min`。 |
| `MedianAbsoluteDeviation` | `Double` | 中位绝对偏差。 |
| `StdError` | `Double` | 标准误 `StdDev / sqrt(N)`。 |
| `SampleCount` | `Integer` | 参与统计的总像素数。 |
| `SelectedChannel` | `String` | 实际使用的通道。 |
| `ChannelsAnalyzed` | `String[]` | 已分析的通道名称数组。 |
| `AggregationMode` | `String` | `SingleChannel` 或 `FlattenedChannels`。 |
| `ChannelStats` | `Dictionary` | All 模式下每通道的独立统计字典。 |
| `Confidence` | `Double` | 基于标准误计算的置信度。 |
| `UncertaintyPx` | `Double` | 标准误值。 |
| `StatusCode` | `String` | `OK`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 8 位路径：`O(W*H)` 单次遍历；通用路径：`O(W*H)` 遍历 + `O(N log N)` 排序求中位数。 |
| 典型耗时 (Typical Latency) | 8 位优化路径极快；通用路径受像素数量与排序开销影响。 |
| 内存特征 (Memory Profile) | 8 位路径仅需 256 个 int 的直方图数组 + 掩码；通用路径需 `List<double>` 存储所有像素值，峰值约为 `8 * W * H` 字节。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：工业视觉中对 ROI 区域亮度分布的快速统计。
- **适合 (Suitable)**：配合掩码进行选择性区域分析（如仅统计缺陷区域）。
- **适合 (Suitable)**：多通道图像的逐通道对比分析。
- **适合 (Suitable)**：作为其他算子的前置质量检查（如判断图像是否过曝/欠曝）。
- **不适合 (Not Suitable)**：需要直方图可视化输出的场景（应使用 HistogramAnalysis）。
- **不适合 (Not Suitable)**：需要空间分布信息的统计（本算子仅提供全局统计量）。

## 已知限制 / Known Limitations
1. 8 位优化路径仅适用于 `CV_8U` 深度，其他深度回退到通用路径，性能下降且中位数精度可能受浮点排序影响。
2. All 模式的 8 位优化要求通道数为 3 或 4，其他通道数（如 2 通道）回退到通用路径。
3. 掩码必须与源图或 ROI 尺寸完全匹配，否则返回错误。
4. MAD 计算使用 0.5 级分辨率的偏差直方图，精度约为 0.5 灰度级。
5. `Channel` 参数的 `Gray` 模式对多通道图像使用 `CvtColor(BGR2GRAY)` 转换，可能与单通道直方图分析结果有微小差异。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写至金标准 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
