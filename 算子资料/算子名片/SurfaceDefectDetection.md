# 表面缺陷检测 / SurfaceDefectDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SurfaceDefectDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.SurfaceDefectDetection` |
| 分类 (Category) | AI检测 |
| 显示名 (DisplayName) | 表面缺陷检测 |
| 图标 (Icon) | `surface-defect` |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 实验性 Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `surface-defect` |
| 关键词 (Keywords) | `surface defect`, `scratch`, `stain`, `traditional detection` |

## 算法原理 / Algorithm Principle

**中文：** 该算子面向可解释的传统表面缺陷筛查，支持三条响应路径：

1. **GradientMagnitude**：先做局部背景归一化（可选 CLAHE 增强），再计算 Sobel 梯度幅值响应，适合划痕、边缘类缺陷增强。
2. **ReferenceDiff**：对参考图做尺寸对齐和可选相位相关（PhaseCorrelation）平移配准，再与当前图做绝对差分，适合有稳定基准图的缺陷比对。
3. **LocalContrast**：通过局部均值背景扣除得到局部对比度响应，适合弱纹理表面上的局部异常。

响应图生成后，根据 `ThresholdMode`（Auto/Manual/Otsu/ReferenceStats）二值化，通过面积范围、形态学清理、响应统计过滤和形状过滤筛选缺陷区域。支持 CLAHE + LocalMean 混合归一化模式和 robust reference stats（基于中位数和 MAD）。

**English:** This operator provides interpretable traditional surface defect screening with three response paths:

1. **GradientMagnitude**: Local background normalization (optional CLAHE enhancement) followed by Sobel gradient magnitude response, suitable for scratch and edge-type defect enhancement.
2. **ReferenceDiff**: Size alignment and optional phase-correlation translation registration of the reference image, followed by absolute difference with the current image, suitable for stable baseline comparison.
3. **LocalContrast**: Local mean background subtraction for local contrast response, suitable for local anomalies on weakly textured surfaces.

After response map generation, binarization is applied based on `ThresholdMode` (Auto/Manual/Otsu/ReferenceStats), then defect regions are filtered by area range, morphological cleanup, response statistics filtering, and shape filtering. Supports CLAHE + LocalMean hybrid normalization and robust reference stats (median + MAD based).

## 实现策略 / Implementation Strategy

**中文：** 源码中的关键实现策略：

1. **多级归一化**：`NormalizationMode` 支持 `None`（不归一化）、`LocalMean`（高斯背景扣除）、`ClaheLocalMean`（先 CLAHE 增强再背景扣除）。CLAHE 参数可通过 `ClaheClipLimit` 和 `ClaheTileGridSize` 独立控制。
2. **相位相关配准**：`ReferenceDiff` 路径的配准使用 `Cv2.PhaseCorrelate`，带三重拒绝条件：响应分数过低（< 0.02）、偏移量过大（> 45% 图像尺寸）、配准后相似度未改善。
3. **阈值策略**：`Auto` 模式下 `GradientMagnitude`/`LocalContrast` 默认使用 Otsu，`ReferenceDiff` 默认使用 ReferenceStats（均值 + sigma 倍标准差）。`ReferenceStats` 支持 robust 变体（中位数 + 1.4826 * MAD）。
4. **组件过滤**：`ComponentFilterMode` 支持三级过滤：
   - `AreaOnly`：仅面积过滤
   - `ResponseStats`：增加组件响应均值/峰值门控
   - `ShapeAndResponseStats`：增加形状过滤（小面积低细长度拒绝、紧凑纹理噪声拒绝、局部响应显著性拒绝）
5. **响应归一化**：`ResponseNormalizeMode` 支持 `RawClamp`（直接截断到 0-255）、`MinMax`（最小最大归一化）、`PercentileClip`（百分位裁剪后归一化）。
6. **形态学模式**：`MorphMode` 支持 `None`、`OpenClose`（先开后闭）、`CloseOpen`（先闭后开）、`CloseOnly`（仅闭运算）。
7. **Candidate Profile**：支持 `default` 和 `taxonomy_v2`。`taxonomy_v2` 会锁定 `LocalContrast` 方法、`Manual` 阈值（下限 15）、`ClaheLocalMean` 归一化、`ShapeAndResponseStats` 过滤等组合参数。

**English:** Key implementation strategies:

1. **Multi-level normalization**: `NormalizationMode` supports `None`, `LocalMean` (Gaussian background subtraction), `ClaheLocalMean` (CLAHE + background subtraction). CLAHE parameters are independently controlled via `ClaheClipLimit` and `ClaheTileGridSize`.
2. **Phase-correlation alignment**: The `ReferenceDiff` alignment uses `Cv2.PhaseCorrelate` with triple rejection: response too low (< 0.02), shift too large (> 45% of image size), or insufficient improvement after alignment.
3. **Threshold strategies**: In `Auto` mode, `GradientMagnitude`/`LocalContrast` default to Otsu, `ReferenceDiff` defaults to ReferenceStats (mean + sigma * stddev). `ReferenceStats` supports a robust variant (median + 1.4826 * MAD).
4. **Component filtering**: `ComponentFilterMode` supports three levels: `AreaOnly`, `ResponseStats` (adds component mean/peak gate), `ShapeAndResponseStats` (adds shape filtering for small low-elongation, compact texture noise, and local response prominence).
5. **Response normalization**: `ResponseNormalizeMode` supports `RawClamp`, `MinMax`, `PercentileClip`.
6. **Morphological modes**: `MorphMode` supports `None`, `OpenClose`, `CloseOpen`, `CloseOnly`.
7. **Candidate profile**: Supports `default` and `taxonomy_v2`, which locks `LocalContrast` method, `Manual` threshold (floor 15), `ClaheLocalMean` normalization, `ShapeAndResponseStats` filtering, etc.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像
2. `GetStringParam / GetDoubleParam / GetIntParam / GetBoolParam` -- 读取参数
3. `ResolveCandidateProfile(@operator)` -- 解析 candidate profile
4. `OperatorImageDepthHelper.EnsureSingleChannelGray(src)` -- 灰度转换
5. `BuildResponseMap(...)` -- 构建响应图
   - **GradientMagnitude**: `NormalizeForComparison(...)` -> `Cv2.Sobel(...)` -> `Cv2.Magnitude(...)` -> `NormalizeResponseToByte(...)`
   - **ReferenceDiff**: `EnsureSize(...)` -> `AlignReferenceToSource(...)` -> `Cv2.Absdiff(...)`
   - **LocalContrast**: `NormalizeForComparison(...)` -> 直接返回局部对比度响应
6. `ApplyThreshold(response, binary, ...)` -- 二值化（Manual/Otsu/ReferenceStats）
7. `ApplyMorphology(binary, kernel, morphMode)` -- 形态学清理
8. `Cv2.FindContours(...)` -- 轮廓提取
9. 面积过滤 + `AcceptComponentByResponseStats(...)` + `AcceptComponentByShapeStats(...)` + `AcceptComponentByLocalResponseProminence(...)` -- 多级组件过滤
10. `CreateImageOutput(resultImage, additional)` -- 构建输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `GradientMagnitude` | `GradientMagnitude` / `ReferenceDiff` / `LocalContrast` | 缺陷响应模式。 |
| `Threshold` | `double` | `35.0` | `[0.0, 255.0]` | 手动阈值或自动阈值下限。 |
| `MinArea` | `int` | `20` | `[0, 10000000]` | 最小缺陷面积（像素）。 |
| `MaxArea` | `int` | `1000000` | `[0, 10000000]` | 最大缺陷面积（像素）。 |
| `MorphCleanSize` | `int` | `3` | `[1, 301]` | 形态学清理核尺寸（自动调整为奇数）。 |
| `MorphMode` | `enum` | `OpenClose` | `None` / `OpenClose` / `CloseOpen` / `CloseOnly` | 形态学运算模式。 |
| `AlignmentMode` | `enum` | `PhaseCorrelation` | `None` / `PhaseCorrelation` | 参考图配准模式。仅 `ReferenceDiff` 路径使用。 |
| `NormalizationMode` | `enum` | `LocalMean` | `None` / `LocalMean` / `ClaheLocalMean` | 响应前归一化模式。 |
| `ThresholdMode` | `enum` | `Auto` | `Auto` / `Manual` / `Otsu` / `ReferenceStats` | 阈值模式。`Auto` 根据 Method 自动选择。 |
| `BackgroundKernelSize` | `int` | `31` | `[3, 301]` | 局部背景核大小（自动调整为奇数）。 |
| `ClaheClipLimit` | `double` | `2.0` | `[0.1, 40.0]` | CLAHE 对比度限制。仅 `ClaheLocalMean` 模式使用。 |
| `ClaheTileGridSize` | `int` | `8` | `[2, 64]` | CLAHE 分块网格大小。 |
| `ReferenceStatsSigma` | `double` | `2.5` | `[0.1, 10.0]` | `ReferenceStats` 阈值的 sigma 倍数。 |
| `RobustReferenceStats` | `bool` | `false` | `true` / `false` | 是否使用 robust（中位数+MAD）替代标准（均值+标准差）ReferenceStats。 |
| `ResponseNormalizeMode` | `enum` | `RawClamp` | `RawClamp` / `MinMax` / `PercentileClip` | 响应图归一化到字节范围的方式。 |
| `ComponentFilterMode` | `enum` | `AreaOnly` | `AreaOnly` / `ResponseStats` / `ShapeAndResponseStats` | 组件过滤级别。 |
| `SmallNoiseAreaMax` | `int` | `0` | `[0, 10000000]` | 小噪声面积上限。0 表示禁用。面积 <= 此值且细长度不足的组件被拒绝。 |
| `MinElongationForSmallComponent` | `double` | `0.0` | `[0.0, 50.0]` | 小组件最小细长度要求。0 表示禁用。 |
| `CompactNoiseAreaMax` | `int` | `0` | `[0, 10000000]` | 紧凑噪声面积上限。0 表示禁用。 |
| `CompactNoiseCircularityMin` | `double` | `0.0` | `[0.0, 1.0]` | 紧凑噪声最小圆度。 |
| `CompactNoiseFillRatioMin` | `double` | `0.0` | `[0.0, 1.0]` | 紧凑噪声最小填充比。 |
| `MinLocalResponseProminence` | `double` | `0.0` | `[0.0, 255.0]` | 小组件局部响应显著性最小要求。0 表示禁用。 |
| `EnableCandidateProfile` | `bool` | `false` | `true` / `false` | 是否启用 candidate profile。 |
| `CandidateProfile` | `enum` | `default` | `default` / `taxonomy_v2` | 预配置方案。`taxonomy_v2` 锁定多组参数。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 当前待检图像。 |
| `Reference` | Reference | `Image` | No | 参考图，仅 `ReferenceDiff` 路径使用。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 标注后的结果图（红色矩形框 + 统计文字）。 |
| `DefectMask` | Defect Mask | `Image` | 通过面积筛选后的缺陷二值掩膜。 |
| `ResponseImage` | Response Image | `Image` | 阈值前响应图，便于调参可视化。 |
| `DefectCount` | Defect Count | `Integer` | 缺陷连通域数量。 |
| `DefectArea` | Defect Area | `Float` | 缺陷总面积（像素）。 |
| `AlignmentScore` | Alignment Score | `Float` | 配准响应分数。仅 `ReferenceDiff` 路径有值。 |
| `RejectedReason` | Rejected Reason | `String` | 配准或筛选异常原因。非空时仅 `ReferenceDiff` 路径会阻断执行。 |
| `Diagnostics` | Diagnostics | `Any` | 方法、阈值、配准偏移、候选数、响应统计等诊断信息。 |

### Diagnostics 字段详情 / Diagnostics Fields
| 字段名 (Field) | 类型 (Type) | 说明 (Description) |
|------|------|------|
| `Method` | `String` | 实际使用的响应方法。 |
| `AlignmentMode` | `String` | 配准模式。 |
| `NormalizationMode` | `String` | 归一化模式。 |
| `ResponseNormalizeMode` | `String` | 响应归一化模式。 |
| `ComponentFilterMode` | `String` | 组件过滤模式。 |
| `ThresholdMode` | `String` | 实际阈值模式。 |
| `AppliedThreshold` | `Double` | 实际应用的阈值。 |
| `ClaheClipLimit` / `ClaheTileGridSize` | `Double` / `Int` | CLAHE 参数。 |
| `RobustReferenceStats` | `Boolean` | 是否使用 robust stats。 |
| `MorphMode` | `String` | 形态学模式。 |
| `CandidateAreaBeforeMorph` / `CandidateAreaAfterMorph` | `Integer` | 形态学前后的候选像素面积。 |
| `AlignmentScore` / `AlignmentShiftX` / `AlignmentShiftY` | `Double` | 配准分数和偏移量。 |
| `CandidateCount` / `AcceptedCount` | `Integer` | 候选轮廓数和接受轮廓数。 |
| `ComponentRejectedCount` / `ComponentResponseRejectedCount` / `ComponentShapeRejectedCount` / `ComponentCompactNoiseRejectedCount` / `ComponentLocalProminenceRejectedCount` | `Integer` | 各级过滤的拒绝计数。 |
| `ComponentMeanGate` / `ComponentPeakGate` | `Double` | 响应统计过滤的门控阈值。 |
| `SmallNoiseAreaMax` / `MinElongationForSmallComponent` / `CompactNoiseAreaMax` / `CompactNoiseCircularityMin` / `CompactNoiseFillRatioMin` / `MinLocalResponseProminence` | `Various` | 形状过滤参数。 |
| `CandidateProfileEnabled` / `CandidateProfile` / `CandidateProfileApplied` | `Various` | candidate profile 状态。 |
| `RejectedReason` | `String` | 配准拒绝原因。 |
| `ResponseMean` / `ResponseStdDev` | `Double` | 响应图均值和标准差。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要与图像像素数线性相关 `O(W * H)`。`ReferenceDiff` 叠加参考图配准和差分成本。轮廓提取和过滤成本与候选区域数量相关。 |
| 典型耗时 (Typical Latency) | `P2InspectionResidual_baseline.md` 记录 24/24 passed，SurfaceDefectDetection 平均约 1.5 ms 的半合成契约场景。 |
| 内存特征 (Memory Profile) | 包含灰度图、响应图（float32 和 uint8 各一份）、二值图、缺陷 mask、结果图、轮廓数组和诊断字典。CLAHE 模式额外分配增强图。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：有稳定背景或参考图的表面差异检测。
- **适合 (Suitable)**：划痕、污点、异物、局部亮暗异常的可解释初筛。
- **适合 (Suitable)**：作为传统检测链路中的诊断节点，为后续人工或 AI 复核提供 mask 和 response。
- **适合 (Suitable)**：需要 taxonomy_v2 candidate profile 的标准化表面检测流程。
- **不适合 (Not Suitable)**：复杂材质、强反光、图案变化大且没有稳定参考的场景直接上线。
- **不适合 (Not Suitable)**：配准涉及明显旋转、透视变形或尺度漂移的场景（当前仅支持平移级补偿）。

## 已知限制 / Known Limitations
1. 当前配准只做平移级补偿（PhaseCorrelation），不适合明显旋转、透视变形或尺度漂移。
2. `GradientMagnitude` 和 `LocalContrast` 仍属启发式方法，效果高度依赖光照、成像、ROI 和阈值策略。
3. `taxonomy_v2` candidate profile 锁定的参数组合是基于特定评估数据集的经验配置，不一定适用于所有材质和成像条件。
4. P2 residual baseline 是半合成契约证据，不代表真实产线缺陷召回率或误检率。
5. `PercentileClip` 响应归一化使用采样估算百分位，对超大图像可能有精度损失。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.1 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增 ClaheLocalMean 归一化、Candidate Profile 机制、多级组件过滤（响应统计+形状+局部显著性）、Diagnostics 字段详情；统一五列参数表；补全英文算法原理 |
| 2.0.0 | 2026-04-28 | 回写 P2InspectionResidual 24/24 baseline、失败契约和真实限制说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
