# 颜色测量 / ColorMeasurement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ColorMeasurementOperator` |
| 枚举值 (Enum) | `OperatorType.ColorMeasurement` |
| 分类 (Category) | 颜色处理 |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 图标 (Icon) | `color-measure` |
| 关键词 (Keywords) | `color`, `deltaE`, `lab`, `hsv` |

## 算法原理 / Algorithm Principle
**中文：**
该算子对选定 ROI 区域进行精确的颜色统计测量，提供两种测量模式：

1. **LabDeltaE 模式**：
   - 遍历 ROI 内每个像素，通过 `CieLabConverter.BgrToLab` 将 BGR 值逐像素转换为 CIE Lab 颜色空间。
   - 使用 Welford 在线算法累加 L*/a*/b* 三通道的和与平方和，计算均值和标准差。
   - 从 `RefL/RefA/RefB` 参数或 `ReferenceColor` 输入端口获取参考色（若未提供则默认使用 ROI 均值）。
   - 调用 `ColorDifference.DeltaE76`（欧氏距离）或 `ColorDifference.DeltaE00`（CIEDE2000 感知均匀色差）计算色差。
   - 对全像素进行降采样统计（最多 `MaxDeltaEStatisticsSamples=4096` 个样本），使用 Welford 在线算法计算 DeltaE 的标准差和标准误，作为测量不确定度。
   - 最终通过 `MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty` 将标准误映射为置信度。

2. **HsvStats 模式**：
   - 将 ROI 转换为 HSV 颜色空间（OpenCV 8UC3 格式，H 范围 0-180）。
   - 遍历每个像素累加 S 和 V 通道值。
   - 对满足 `S >= MinHueSaturation(12)` 且 `V >= MinHueValue(12)` 的有效像素，使用圆统计（Circular Statistics）计算色相均值：将 Hue 角度转为弧度（`H * PI/90`），累加 sin 和 cos 分量，通过 `atan2(sinMean, cosMean)` 得到圆均值角度。
   - 色相标准差通过平均合成长度（Mean Resultant Length）计算：`sqrt(-2 * ln(R))`，其中 `R = sqrt(sinMean^2 + cosMean^2)`。
   - 低饱和度或低亮度区域的色相无统计意义，此时输出 `HueValid=false`。

**English:**
This operator performs precise color statistics measurement over a selected ROI with two modes:

1. **LabDeltaE**: Converts each BGR pixel to CIE Lab via `CieLabConverter.BgrToLab`, accumulates per-channel sums and sum-of-squares using Welford's online algorithm for mean and standard deviation. Computes color difference against a reference color using CIE76 or CIEDE2000. Down-samples up to 4096 pixels for DeltaE statistics (standard deviation and standard error via Welford's algorithm), then maps standard error to confidence via `MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty`.

2. **HsvStats**: Converts ROI to HSV, accumulates S and V channel sums. For valid pixels (S >= 12, V >= 12), computes circular mean of Hue via `atan2(mean(sin), mean(cos))` and circular standard deviation via `sqrt(-2 * ln(R))` where R is the mean resultant length. Low-saturation or low-brightness regions output `HueValid=false`.

## 实现策略 / Implementation Strategy
- **中文：** 算子根据 `MeasurementMode` 参数分派到两条独立的测量路径。输入图像统一经 `EnsureColorImage` 转为 3 通道 BGR（支持灰度/BGRA 输入），ROI 通过 `MeasurementRoiHelper.ResolveRoi` 从 RoiX/RoiY/RoiW/RoiH 参数解析。输出图像使用零拷贝共享头（`CreateSharedImageHeader`）引用原始输入，通过专用的 `PassthroughImagePool`（`maxPerBucket: 0, maxTotalGb: 0.0`）管理生命周期，避免额外内存分配。LabDeltaE 模式的参考色支持参数端口（RefL/RefA/RefB）、ReferenceColor 输入端口（double[]/float[]/IDictionary）两种来源，参数端口优先。同时兼容旧版 `ColorSpace` 参数的迁移路径（Lab -> LabDeltaE, HSV -> HsvStats）。
- **English:** The operator dispatches to two independent measurement paths based on `MeasurementMode`. Input images are normalized to 3-channel BGR via `EnsureColorImage`. ROI is resolved from parameters via `MeasurementRoiHelper.ResolveRoi`. Output images use zero-copy shared headers referencing the original input via a dedicated `PassthroughImagePool`. LabDeltaE reference color supports parameter port and input port sources with parameter port priority. Legacy `ColorSpace` parameter migration is supported (Lab -> LabDeltaE, HSV -> HsvStats).

## 核心 API 调用链 / Core API Call Chain
```
TryGetInputImage(inputs, out imageWrapper)
  -> imageWrapper.GetMat()
  -> ResolveMeasurementMode(@operator)         // MeasurementMode 或旧 ColorSpace 迁移
  -> MeasurementRoiHelper.ResolveRoi(@operator, width, height)
  -> EnsureColorImage(roiSource)               // 灰度/BGRA -> BGR
  -> [dispatch by mode]:
     LabDeltaE:
       ComputeLabStatistics(roiMat)             // 逐像素 BgrToLab + Welford 累加
         -> CieLabConverter.BgrToLab(b, g, r)   // 每像素
         -> sumL/sumA/sumB + sumL2/sumA2/sumB2  // 均值与标准差
       GetDoubleParam(RefL/RefA/RefB) 或 TryOverrideReferenceLab(ReferenceColor)
       ColorDifference.DeltaE76 / DeltaE00
       ComputeDeltaEStatistics(roiMat, reference, method)  // 降采样 Welford
         -> stride = max(1, totalPixels / 4096)
         -> CieLabConverter.BgrToLab + ColorDifference.*  // 每 stride 像素
         -> mean, m2 (Welford variance)
       MeasurementStatisticsHelper.ComputeStandardError(stdDev, sampleCount)
     HsvStats:
       Cv2.CvtColor(roiMat, BGR2HSV)
       遍历像素累加 S/V 和 sin(H)/cos(H)
       atan2(sinMean, cosMean) -> hueMean
       sqrt(-2*ln(R)) -> hueStdDev
       MeasurementStatisticsHelper.ComputeStandardError(hueStdDev, validHueCount)
  -> MeasurementStatisticsHelper.ComputeConfidenceFromUncertainty(uncertainty)
  -> CreateSharedImageOutput(src, output)        // 零拷贝输出图像
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MeasurementMode` | `enum` | `LabDeltaE` | LabDeltaE, HsvStats | 测量模式。LabDeltaE=Lab 色差测量；HsvStats=HSV 统计测量。兼容旧版 ColorSpace 参数（Lab->LabDeltaE, HSV->HsvStats） |
| `DeltaEMethod` | `enum` | `CIEDE2000` | CIE76, CIEDE2000 | LabDeltaE 模式下的色差计算方法。CIE76 为 CIE Lab 欧氏距离；CIEDE2000 为感知均匀色差公式 |
| `RoiX` | `int` | `0` | [0, 图像宽度) | 感兴趣区域左上角 X 坐标 |
| `RoiY` | `int` | `0` | [0, 图像高度) | 感兴趣区域左上角 Y 坐标 |
| `RoiW` | `int` | `0` | [0, +inf) | 感兴趣区域宽度，0 表示使用图像剩余宽度 |
| `RoiH` | `int` | `0` | [0, +inf) | 感兴趣区域高度，0 表示使用图像剩余高度 |
| `RefL` | `double` | `0.0` | - | 参考色 Lab 的 L* 分量（0-100），LabDeltaE 模式下若未提供则默认使用 ROI 均值 |
| `RefA` | `double` | `0.0` | - | 参考色 Lab 的 a* 分量 |
| `RefB` | `double` | `0.0` | - | 参考色 Lab 的 b* 分量 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像，支持灰度/BGR/BGRA，内部自动转 BGR |
| `ReferenceColor` | Reference Color | `Any` | No | 参考 Lab 颜色，支持 double[3]、float[3] 或含 L/A/B 键的字典 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `LabMean` | Lab Mean | `Any` | Lab 均值字典 {L, A, B}，HsvStats 模式为空字典 |
| `ReferenceLab` | Reference Lab | `Any` | 参考色 Lab 值字典 {L, A, B}，HsvStats 模式为空字典 |
| `DeltaE` | DeltaE | `Float` | Lab 色差值，HsvStats 模式为 NaN |
| `HueMean` | Hue Mean | `Float` | 色相圆均值（度），LabDeltaE 模式为 NaN |
| `SaturationMean` | Saturation Mean | `Float` | 饱和度均值（0-100%），LabDeltaE 模式为 NaN |
| `ValueMean` | Value Mean | `Float` | 明度均值（0-100%），LabDeltaE 模式为 NaN |
| `HueValid` | Hue Valid | `Boolean` | 色相统计是否有效（需足够多的高饱和高亮度像素） |
| `Image` | Image | `Image` | 原始输入图像的零拷贝共享头，不包含标注 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度 |
| `Height` | `Integer` | 输出图像高度 |
| `MeasurementMode` | `String` | 实际执行的测量模式 |
| `StatusCode` | `String` | 执行状态码，成功时为 "OK" |
| `StatusMessage` | `String` | 执行状态消息，成功时为 "Success" |
| `Confidence` | `Double` | 由测量不确定度映射的置信度（0-1） |
| `UncertaintyPx` | `Double` | 测量不确定度（LabDeltaE 为 DeltaE 标准误，HsvStats 为色相标准误，单位：度） |
| `LabStdDev` | `Any` | Lab 标准差字典 {L, A, B}（仅 LabDeltaE 模式） |
| `DeltaEStdDev` | `Double` | DeltaE 标准差（仅 LabDeltaE 模式） |
| `DeltaEStdError` | `Double` | DeltaE 标准误（仅 LabDeltaE 模式） |
| `SampleCount` | `Int` | Lab 统计的有效像素数（LabDeltaE）或色相有效像素数（HsvStats） |
| `DeltaESampleCount` | `Int` | DeltaE 降采样统计的样本数（仅 LabDeltaE 模式） |
| `HueCircularStdDeg` | `Double` | 色相圆标准差（度），仅 HsvStats 模式 |
| `HueStdErrorDeg` | `Double` | 色相标准误（度），仅 HsvStats 模式 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | LabDeltaE: O(W*H) 逐像素 BGR->Lab 转换 + O(W*H/stride) 降采样色差统计；HsvStats: O(W*H) 逐像素 HSV 遍历 |
| 典型耗时 (Typical Latency) | 无专用 benchmark；LabDeltaE 模式的逐像素 BGR->Lab 转换为主要瓶颈，1080p ROI 可能 >20ms；HsvStats 模式通常 <10ms |
| 内存特征 (Memory Profile) | 输出图像为零拷贝共享头（无额外分配）；主要内存开销为 ROI 裁剪副本和 HSV 转换副本，峰值约 1-2 倍 ROI 大小 |

## 适用场景 / Use Cases
- **适合 (Suitable)：**
  - 工业色差量化验收，需要精确的 DeltaE 值和测量不确定度（LabDeltaE 模式）
  - 产线颜色一致性监控，需要跟踪 DeltaE 的标准差和标准误趋势
  - HSV 色相分布分析，用于判断产品色调是否偏移（HsvStats 模式的圆统计）
  - 低饱和度/低亮度场景的色相有效性判断（HueValid 标志）
  - 需要置信度评估的质量控制流程
- **不适合 (Not Suitable)：**
  - 需要像素级色差热力图或逐像素 DeltaE 分布的场景
  - 多色混合表面的精细颜色分析（当前仅输出 ROI 整体统计）
  - 需要颜色空间转换后保留原始像素数据的场景（输出图像是原始图像的零拷贝，不含标注）
  - 对测量速度有极高要求的实时场景（逐像素 BGR->Lab 转换开销较大）

## 已知限制 / Known Limitations
1. LabDeltaE 模式的 BGR->Lab 转换为逐像素自研实现（`CieLabConverter.BgrToLab`），非 OpenCV 内置批量转换，大 ROI 下性能可能不如预期。
2. DeltaE 统计使用降采样（最多 4096 样本），对于像素分布极度不均匀的区域，降采样统计可能偏离全像素统计。
3. HsvStats 模式的色相圆统计仅对 S >= 12 且 V >= 12 的像素有效；在大面积低饱和区域（如灰色/白色背景）中 `HueValid` 为 false，色相数据不可用。
4. 输出图像是原始输入的零拷贝共享头，不包含任何可视化标注，与 ColorDetection 算子的行为不同。
5. `PassthroughImagePool` 配置为 `maxPerBucket: 0, maxTotalGb: 0.0`，表示不使用池化缓存，每次执行都创建新的共享头。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写算子名片：基于源码提取全部元数据、LabDeltaE/HsvStats 两种模式的完整算法原理与调用链、9 个参数的详细说明、8 个输出端口语义、圆统计与 Welford 算法说明、性能特征与适用场景 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
