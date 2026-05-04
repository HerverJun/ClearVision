# 直方图分析 / Histogram Analysis

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `HistogramAnalysisOperator` |
| 枚举值 (Enum) | `OperatorType.HistogramAnalysis` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子对选定通道的像素强度进行直方图统计，提取均值、标准差、众数、中位数、峰值与谷值等分布特征。

**核心流程：**

1. **通道提取**：根据 `Channel` 参数提取 Gray/R/G/B 单通道图像。
2. **ROI 裁剪**：按 `RoiX/Y/W/H` 参数裁剪感兴趣区域。
3. **直方图计算**：使用 `Cv2.CalcHist` 计算 `BinCount` 个 bin 的直方图，灰度范围 `[0, 256)`。
4. **基本统计**：`Cv2.MeanStdDev` 直接计算均值和标准差。
5. **众数（Mode）**：直方图最高 bin 的中心强度值。
6. **中位数（Median）**：从直方图左侧累加直到 `acc >= total / 2`，取该 bin 中心值。
7. **峰值（Peak）**：与众数相同，即最高 bin。
8. **谷值（Valley）**：在两个最高峰之间寻找最低 bin。算法先找 Top-4 局部峰值，再在前两峰之间搜索最低值。

**量化不确定度**：`sigma_q = binWidth / sqrt(12)`，基于均匀量化的统计模型。

**Bin 中心强度**：`I = (index + 0.5) * binWidth - 0.5`

> English: The operator computes a histogram for the selected channel over a ROI, then extracts mean, stddev, mode, median, peak, and valley. The valley is found between the two dominant peaks. Quantization uncertainty is estimated from the bin width.

## 实现策略 / Implementation Strategy
- 使用 `Cv2.CalcHist` 计算直方图，支持自定义 `BinCount`（2-1024）。
- 通道提取通过 `ExtractChannel` 实现：Gray 模式用 `CvtColor(BGR2GRAY)`；R/G/B 模式用 `Cv2.Split` 后按索引取通道（OpenCV BGR 顺序：B=0, G=1, R=2）。
- ROI 通过 `MeasurementRoiHelper.ResolveRoi` 解析，支持默认全图。
- 谷值检测 `TryFindValleyBetweenDominantPeaks` 使用 `IsLocalPeak` 找局部峰，然后在前两大峰之间找最小 bin。
- 直方图可视化 `DrawHistogram` 绘制折线图（512x220），黄色线条。
- 输出包含 `Confidence` 和 `UncertaintyPx`，基于量化不确定度计算。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetStringParam(@operator, "Channel", "Gray")` / `GetIntParam(@operator, "BinCount", 256, 2, 1024)` -- 读取参数
3. `MeasurementRoiHelper.ResolveRoi(@operator, src.Width, src.Height)` -- 解析 ROI
4. `new Mat(src, roi)` -- 裁剪 ROI
5. `ExtractChannel(roiMat, channelName)` -- 提取单通道
6. `Cv2.MeanStdDev(channelMat, out mean, out stddev)` -- 均值/标准差
7. `Cv2.CalcHist(new[] { channelMat }, new[] { 0 }, null, hist, 1, new[] { binCount }, new[] { new Rangef(0, 256) })` -- 直方图计算
8. `ArgMax(values)` -- 众数 bin
9. `ComputeMedianBin(values, total)` -- 中位数 bin
10. `TryFindValleyBetweenDominantPeaks(values, out valleyIndex)` -- 谷值检测
11. `DrawHistogram(values, 512, 220)` -- 直方图可视化
12. `CreateImageOutput(chart, output)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Channel` | `enum` | `Gray` | `Gray` / `R` / `G` / `B` | 分析通道。`Gray` 转灰度；`R`/`G`/`B` 提取对应颜色通道。 |
| `BinCount` | `int` | `256` | `[2, 1024]` | 直方图 bin 数量。越大分辨率越高，但噪声也更明显。 |
| `RoiX` | `int` | `0` | `[0, +inf)` | ROI 左上角 X 坐标。 |
| `RoiY` | `int` | `0` | `[0, +inf)` | ROI 左上角 Y 坐标。 |
| `RoiW` | `int` | `0` | `[0, +inf)` | ROI 宽度。0 表示自动计算。 |
| `RoiH` | `int` | `0` | `[0, +inf)` | ROI 高度。0 表示自动计算。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Image` | `Image` | Yes | 输入图像，支持灰度与多通道。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Image` | `Image` | 直方图折线可视化图（512x220）。 |
| `Mean` | `Mean` | `Float` | 通道均值。 |
| `StdDev` | `StdDev` | `Float` | 通道标准差。 |
| `Mode` | `Mode` | `Float` | 众数 bin 的中心强度值。 |
| `Median` | `Median` | `Float` | 中位数 bin 的中心强度值。 |
| `Peak` | `Peak` | `Float` | 峰值 bin 的中心强度值（与 Mode 相同）。 |
| `Valley` | `Valley` | `Float` | 谷值 bin 的中心强度值，若未找到则为 NaN。 |
| `ModeBinIndex` | `Mode Bin Index` | `Integer` | 众数 bin 索引。 |
| `MedianBinIndex` | `Median Bin Index` | `Integer` | 中位数 bin 索引。 |
| `PeakBinIndex` | `Peak Bin Index` | `Integer` | 峰值 bin 索引。 |
| `ValleyBinIndex` | `Valley Bin Index` | `Integer` | 谷值 bin 索引，-1 表示未找到。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `BinWidth` | `Double` | 每个 bin 的强度宽度 `256 / BinCount`。 |
| `HistogramMass` | `Double` | 直方图总质量（所有 bin 计数之和）。 |
| `ModeCount` | `Float` | 众数 bin 的计数。 |
| `SampleCount` | `Integer` | ROI 像素总数。 |
| `Confidence` | `Double` | 基于量化不确定度计算的置信度。 |
| `UncertaintyPx` | `Double` | 量化不确定度 `binWidth / sqrt(12)`。 |
| `StatusCode` | `String` | `OK`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(W*H)` 遍历 + `O(BinCount)` 统计提取。 |
| 典型耗时 (Typical Latency) | 主要耗时在 `Cv2.CalcHist`，与 ROI 大小线性相关。 |
| 内存特征 (Memory Profile) | 直方图数组 `float[BinCount]` + 通道提取临时 Mat + 可视化图，峰值约为输入的 2-3 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：分析图像亮度分布，判断过曝/欠曝/双峰等特征。
- **适合 (Suitable)**：自动阈值选择的前置分析（如 Otsu 需要双峰分布判断）。
- **适合 (Suitable)**：工业视觉中光源稳定性监控（定期检查直方图形状是否变化）。
- **适合 (Suitable)**：缺陷检测中的背景/前景分离质量评估。
- **不适合 (Not Suitable)**：需要空间分布信息的分析（本算子仅提供强度域统计）。
- **不适合 (Not Suitable)**：需要精确像素级统计（均值/标准差可直接用 PixelStatistics 获得，无需直方图中间步骤）。

## 已知限制 / Known Limitations
1. 通道提取使用 `CvtColor(BGR2GRAY)` 转灰度，灰度权重固定（ITU-R BT.601），不可自定义。
2. 谷值检测仅在直方图存在至少两个局部峰时有效，单峰或均匀分布返回 -1。
3. `BinCount` 过大时（如 1024），单 bin 计数稀疏，众数/中位数的精度反而下降。
4. 直方图范围固定为 `[0, 256)`，对 16 位或其他位深图像会先被归一化到 8 位。
5. 中位数和众数输出的是 bin 中心强度值，不是精确像素值，精度取决于 `BinCount`。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写至金标准 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
