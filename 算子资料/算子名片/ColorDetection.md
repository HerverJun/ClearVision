# 颜色检测 / ColorDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ColorDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.ColorDetection` |
| 分类 (Category) | 颜色处理 |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 实验性 Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `color-inspection` |
| 图标 (Icon) | `color` |

## 算法原理 / Algorithm Principle
**中文：**
该算子提供五种颜色分析模式，核心围绕 HSV 阈值分割与 CIE Lab 色差计算展开：

1. **Average（传统均值）**：将 ROI 转换到目标颜色空间（HSV 或 Lab），对全部像素求通道均值，输出平均颜色与白平衡状态。
2. **Dominant（主色聚类）**：将 ROI 缩放至 64x64 后，使用 OpenCV `Cv2.Kmeans` 在 BGR 空间做 K-Means 聚类（K 由 `DominantK` 控制），按占比排序输出主色列表。
3. **Range（范围检测）**：在目标颜色空间中对 ROI 像素做 `Cv2.InRange` 阈值分割，计算命中覆盖率；当颜色空间为 HSV 且 `HueLow > HueHigh` 时自动启用色相环绕（wrap-around），将 [HueLow,180] 与 [0,HueHigh] 两段掩码做 `BitwiseOr` 合并。
4. **HsvInspection（HSV 工业检测）**：专用 HSV 路径，同样支持色相环绕掩码，输出覆盖率与掩码内均值，适合工业场景的单一颜色合格/不合格判定。
5. **LabDeltaE（Lab 色差量化）**：通过 `CieLabConverter.ComputeMeanLabBgr8U` 计算 ROI 平均 Lab 值，结合参考色（来自 `ReferenceColor` 端口或 `RefL/RefA/RefB` 参数），调用 `ColorDifference.DeltaE76` 或 `ColorDifference.DeltaE00`（CIEDE2000）计算色差。需要提供完整参考色，否则报错。

所有模式均附带基于灰世界假设（Gray World）的白平衡诊断：计算 ROI 三通道均值的最大偏差，与 `WhiteBalanceTolerance` 比较，输出 "Balanced" 或 "Suspect"。

**English:**
This operator provides five color analysis modes centered on HSV threshold segmentation and CIE Lab color difference computation:

1. **Average**: Converts the ROI to the target color space (HSV or Lab) and computes per-channel means.
2. **Dominant**: Resizes the ROI to 64x64 and applies K-Means clustering in BGR space via `Cv2.Kmeans`, returning dominant colors ranked by pixel percentage.
3. **Range**: Applies `Cv2.InRange` thresholding in the target color space with automatic hue wrap-around support when `HueLow > HueHigh`.
4. **HsvInspection**: A dedicated HSV path with wrap-around mask support, outputting coverage ratio and masked-region mean values for industrial pass/fail color checks.
5. **LabDeltaE**: Computes the ROI mean Lab color via `CieLabConverter.ComputeMeanLabBgr8U`, then evaluates CIE76 or CIEDE2000 color difference against a reference color. Requires a complete reference (via input port or RefL/RefA/RefB parameters).

All modes include a Gray World white balance diagnostic comparing the three-channel mean deviation against `WhiteBalanceTolerance`.

## 实现策略 / Implementation Strategy
- **中文：** 算子采用策略模式，`ExecuteCoreAsync` 根据归一化后的 `AnalysisMode` 字符串分派到五个独立的分析方法（`AnalyzeAverageColor`、`AnalyzeDominantColors`、`AnalyzeColorRange`、`AnalyzeHsvInspection`、`AnalyzeLabDeltaE`），每个方法独立完成 ROI 裁剪、颜色空间转换、核心计算和结果图像绘制。输入图像统一经 `EnsureBgr` 转为 3 通道 BGR，ROI 通过 `ResolveRoi` 从参数解析并经 `ClampRect` 做边界保护。参考色解析支持多种输入格式：`double[]`、`float[]`、`IDictionary<string,object>`，以及参数端口的 `RefL/RefA/RefB`。结果通过 `CreateImageOutput` 统一封装，所有模式共享相同的输出端口结构。
- **English:** The operator uses a strategy pattern where `ExecuteCoreAsync` dispatches to five independent analysis methods based on the normalized `AnalysisMode`. Each method independently handles ROI cropping, color space conversion, core computation, and result visualization. Input images are normalized to 3-channel BGR via `EnsureBgr`, and ROI is resolved from parameters with boundary clamping. Reference color resolution supports `double[]`, `float[]`, `IDictionary<string,object>`, and the `RefL/RefA/RefB` parameter fallback. All modes share the same output port structure via `CreateImageOutput`.

## 核心 API 调用链 / Core API Call Chain
```
TryGetInputImage(...)
  -> EnsureBgr(src)                          // 统一转 BGR 3 通道
  -> ResolveRoi(@operator, bgr)              // 从 RoiX/RoiY/RoiW/RoiH 解析 ROI
  -> NormalizeAnalysisMode(analysisMode)     // 归一化分析模式字符串
  -> [dispatch by mode]:
     Average:
       Cv2.CvtColor(BGR2HSV | BGR2Lab)
       Cv2.Mean(roiView)
       EvaluateWhiteBalance(src, roi, tolerance)
     Dominant:
       Cv2.Resize(roiView, 64x64)
       Mat.ConvertTo(CV_32FC3) + Reshape
       Cv2.Kmeans(data, k, ...)
       EvaluateWhiteBalance(src, roi, tolerance)
     Range:
       Cv2.CvtColor(roiView, ...)
       CreateSimpleMask / CreateHueWrappedMask -> Cv2.InRange
       Cv2.CountNonZero(mask)
       Cv2.Mean(converted, mask)
     HsvInspection:
       Cv2.CvtColor(roiView, BGR2HSV)
       CreateHueWrappedMask -> Cv2.InRange -> Cv2.BitwiseOr (wrap)
       Cv2.CountNonZero(mask)
       Cv2.Mean(hsv, mask)
     LabDeltaE:
       CieLabConverter.ComputeMeanLabBgr8U(src, roi)
       TryResolveReferenceLab(@operator, inputs)
       ColorDifference.DeltaE76 / DeltaE00
  -> Cv2.Rectangle + Cv2.PutText (结果标注)
  -> CreateImageOutput(resultImage, output)
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ColorSpace` | `enum` | `HSV` | HSV, Lab | 兼容模式（Average/Dominant/Range）下的颜色空间，HsvInspection 和 LabDeltaE 模式忽略此参数 |
| `AnalysisMode` | `enum` | `Average` | Average, Dominant, Range, HsvInspection, LabDeltaE | 分析模式。Average=均值统计；Dominant=K-Means 主色聚类；Range=阈值范围检测；HsvInspection=HSV 工业检测；LabDeltaE=Lab 色差量化 |
| `HueLow` | `int` | `0` | [0, 180] | HSV 色相下限。当 HueLow > HueHigh 时自动启用色相环绕（如红色 170->10） |
| `HueHigh` | `int` | `180` | [0, 180] | HSV 色相上限 |
| `SatLow` | `int` | `50` | [0, 255] | HSV 饱和度下限 |
| `SatHigh` | `int` | `255` | [0, 255] | HSV 饱和度上限 |
| `ValLow` | `int` | `50` | [0, 255] | HSV 明度下限 |
| `ValHigh` | `int` | `255` | [0, 255] | HSV 明度上限 |
| `DominantK` | `int` | `3` | [1, 10] | Dominant 模式下 K-Means 聚类的簇数 K |
| `DeltaEMethod` | `enum` | `CIEDE2000` | CIE76, CIEDE2000 | LabDeltaE 模式下的色差计算方法。CIE76 为欧氏距离；CIEDE2000 为感知均匀色差 |
| `RefL` | `double` | `0.0` | - | 参考色 Lab 的 L* 分量（0-100），LabDeltaE 模式必需（若未通过 ReferenceColor 端口提供） |
| `RefA` | `double` | `0.0` | - | 参考色 Lab 的 a* 分量 |
| `RefB` | `double` | `0.0` | - | 参考色 Lab 的 b* 分量 |
| `RoiX` | `int` | `0` | [0, 图像宽度) | 感兴趣区域左上角 X 坐标 |
| `RoiY` | `int` | `0` | [0, 图像高度) | 感兴趣区域左上角 Y 坐标 |
| `RoiW` | `int` | `0` | [0, +inf) | 感兴趣区域宽度，0 表示使用图像剩余宽度 |
| `RoiH` | `int` | `0` | [0, +inf) | 感兴趣区域高度，0 表示使用图像剩余高度 |
| `WhiteBalanceTolerance` | `double` | `12.0` | [0.0, 255.0] | 白平衡判定阈值，ROI 三通道均值的最大偏差 <= 此值时判定为 Balanced |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 输入待处理图像，支持灰度/BGR/BGRA，内部自动转 BGR |
| `ReferenceColor` | Reference Color | `Any` | No | 参考 Lab 颜色，支持 double[3]、float[3] 或含 L/A/B 键的字典，LabDeltaE 模式使用 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 标注了 ROI 矩形和分析信息的结果图 |
| `ColorInfo` | 颜色信息 | `Any` | 包含 Mode、AnalysisMode、ColorSpace、PrimaryData、Coverage、WhiteBalanceStatus 的统一结构 |
| `AnalysisMode` | 分析模式 | `String` | 实际执行的分析模式名称 |
| `ColorSpace` | 颜色空间 | `String` | 实际使用的颜色空间（HSV/Lab/BGR） |
| `DeltaE` | DeltaE | `Float` | LabDeltaE 模式下的色差值，其他模式为 0.0 |
| `Coverage` | Coverage | `Float` | 颜色命中覆盖率（0.0-1.0），Average/Dominant/LabDeltaE 模式固定为 1.0 |
| `WhiteBalanceStatus` | White Balance Status | `String` | 白平衡状态："Balanced" 或 "Suspect" |
| `MeanColor` | Mean Color | `Any` | 平均颜色字典，HSV 模式含 Hue/Saturation/Value，Lab 模式含 L/a/b |
| `DominantColors` | Dominant Colors | `Any` | Dominant 模式下的主色列表（含 Rank/Percentage/B/G/R/Hex），其他模式为空数组 |
| `Diagnostics` | Diagnostics | `Any` | 诊断信息：ROI 坐标、灰世界偏差、MeanB/MeanG/MeanR、Coverage 等 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度 |
| `Height` | `Integer` | 输出图像高度 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Average/Range/HsvInspection: O(W*H) 线性扫描；Dominant: O(W*H*K*iter) K-Means 迭代；LabDeltaE: O(W*H) 像素级 BGR->Lab 转换 |
| 典型耗时 (Typical Latency) | 无专用 benchmark；Average/Range/HsvInspection 通常 <10ms (1080p)；Dominant 受 K 和迭代次数影响；LabDeltaE 依赖 BGR->Lab 逐像素转换性能 |
| 内存特征 (Memory Profile) | 峰值约 2-3 倍输入图像大小（BGR 副本 + 颜色空间转换 + 掩码 + 结果图）；Dominant 模式额外分配 64x64 缩放图和 K-Means 浮点矩阵 |

## 适用场景 / Use Cases
- **适合 (Suitable)：**
  - 工业产线上的单一颜色合格/不合格判定（HsvInspection 模式）
  - 产品色差量化验收，需要与标准色板对比 DeltaE 值（LabDeltaE 模式）
  - 颜色分选、混料检测中需要提取主色调或计算覆盖率的场景（Dominant/Range 模式）
  - 需要白平衡诊断辅助判断成像链路质量的场景
  - 有明确 ROI 的局部颜色一致性检查
- **不适合 (Not Suitable)：**
  - 复杂纹理或多色混合表面上的精细色差分析（LabDeltaE 当前仅使用 ROI 均值）
  - 需要像素级色差热力图的场景（当前不输出逐像素 DeltaE 图）
  - 未做白平衡和颜色校正的成像链路（阈值与 DeltaE 的可迁移性会明显下降）
  - 需要实时高速处理的场景（K-Means 聚类路径有额外开销）

## 已知限制 / Known Limitations
1. `LabDeltaE` 模式当前仅使用 ROI 平均颜色与参考色比较，不适合处理复杂纹理或多色混合表面的精细色差分析。
2. `WhiteBalanceStatus` 是基于灰世界假设的轻量诊断，不等同于完整的色彩标定；三通道偏差阈值需要根据实际光源和相机特性调整。
3. 若现场成像链路没有做白平衡和颜色校正，HSV 阈值与 Lab DeltaE 的可迁移性会明显下降，换线或换光源时需要重新标定参数。
4. `Dominant` 模式将 ROI 缩放至固定 64x64 再做 K-Means，对于大 ROI 可能丢失细节，对于极小 ROI 可能引入插值噪声。
5. `EnsureBgr` 对非 3 通道输入做颜色空间转换时假设灰度图（1 通道）或 BGRA（4 通道），其他通道数未处理。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写算子名片：基于源码提取全部元数据、5 种分析模式的完整算法原理与调用链、17 个参数的详细说明、10 个输出端口语义、性能特征与适用场景 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
