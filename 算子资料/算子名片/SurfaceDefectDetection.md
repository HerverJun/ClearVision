# 表面缺陷检测 / SurfaceDefectDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SurfaceDefectDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.SurfaceDefectDetection` |
| 分类 (Category) | AI检测 |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `surface-defect` |

## 算法说明 / Algorithm
该算子面向可解释的传统表面缺陷筛查，当前支持三条响应路径：

1. `GradientMagnitude`：先做局部背景归一化，再计算梯度响应，适合划痕、边缘类缺陷增强。
2. `ReferenceDiff`：对参考图做尺寸对齐和可选相位相关平移配准，再与当前图做差分，适合有稳定基准图的缺陷比对。
3. `LocalContrast`：通过局部均值背景扣除得到局部对比度响应，适合弱纹理表面上的局部异常。

随后根据 `ThresholdMode` 二值化响应图，并通过面积范围和形态学清理筛选缺陷区域。

## 参数 / Parameters
| 名称 (Name) | 类型 (Type) | 默认值 (Default) | 说明 (Description) |
|------|------|------|------|
| `Method` | `enum` | `GradientMagnitude` | 缺陷响应模式：`GradientMagnitude` / `ReferenceDiff` / `LocalContrast`。 |
| `Threshold` | `double` | `35.0` | 手动阈值或自动阈值下限。 |
| `MinArea` | `int` | `20` | 最小缺陷面积。 |
| `MaxArea` | `int` | `1000000` | 最大缺陷面积。 |
| `MorphCleanSize` | `int` | `3` | 形态学清理核尺寸。 |
| `AlignmentMode` | `enum` | `PhaseCorrelation` | 参考图配准模式：`None` / `PhaseCorrelation`。 |
| `NormalizationMode` | `enum` | `LocalMean` | 响应前归一化模式：`None` / `LocalMean`。 |
| `ThresholdMode` | `enum` | `Auto` | 阈值模式：`Auto` / `Manual` / `Otsu` / `ReferenceStats`。 |
| `BackgroundKernelSize` | `int` | `31` | 局部背景核大小。 |
| `ReferenceStatsSigma` | `double` | `2.5` | `ReferenceStats` 阈值的均值加权倍数。 |

## 输入/输出 / Inputs & Outputs
### 输入 / Inputs
| 名称 (Name) | 类型 (Type) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Image` | 是 | 当前待检图像。 |
| `Reference` | `Image` | 否 | 参考图，仅 `ReferenceDiff` 路径使用。 |

### 输出 / Outputs
| 名称 (Name) | 类型 (Type) | 说明 (Description) |
|------|------|------|
| `Image` | `Image` | 标注后的结果图。 |
| `DefectMask` | `Image` | 通过面积筛选后的缺陷掩膜。 |
| `ResponseImage` | `Image` | 阈值前响应图，便于调参。 |
| `DefectCount` | `Integer` | 缺陷连通域数量。 |
| `DefectArea` | `Float` | 缺陷总面积。 |
| `AlignmentScore` | `Float` | 配准响应分数。 |
| `RejectedReason` | `String` | 当前帧配准或筛选异常原因。 |
| `Diagnostics` | `Any` | 方法、阈值、配准偏移、候选数、响应统计等诊断信息。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要与图像像素数线性相关；`ReferenceDiff` 会叠加参考图配准和差分成本。 |
| 典型耗时 (Typical Latency) | `P2InspectionResidual_baseline.md` 记录 24/24 passed，SurfaceDefectDetection 平均约 1.5 ms 的半合成契约场景。 |
| 内存特征 (Memory Profile) | 包含灰度图、响应图、二值图、缺陷 mask、结果图和诊断字典。 |

## 证据与失败契约 / Evidence & Failure Contracts
- Contract baseline：`quality/evals/reports/P2InspectionResidual_baseline.md`，SurfaceDefectDetection 24/24 passed。
- 覆盖范围：Gradient scratch、ReferenceDiff、LocalContrast、phase-correlation alignment、缺失图像、参数校验和异常配准边界。
- 失败契约包括缺失/空图像、`ReferenceDiff` 缺失或无法接受的参考配准、非法 `Method`、非法 `ThresholdMode`、以及面积范围或核尺寸参数异常。

## 适用场景 / Use Cases
- 适合：有稳定背景或参考图的表面差异检测。
- 适合：划痕、污点、异物、局部亮暗异常的可解释初筛。
- 适合：作为传统检测链路中的诊断节点，为后续人工或 AI 复核提供 mask 和 response。
- 不适合：复杂材质、强反光、图案变化大且没有稳定参考的场景直接上线。

## 已知限制 / Known Limitations
1. 当前配准只做平移级补偿，不适合明显旋转、透视变形或尺度漂移。
2. `GradientMagnitude` 和 `LocalContrast` 仍属启发式方法，效果高度依赖光照、成像、ROI 和阈值策略。
3. P2 residual baseline 是半合成契约证据，不代表真实产线缺陷召回率或误检率。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.1 | 2026-04-28 | 回写 P2InspectionResidual 24/24 baseline、失败契约和真实限制说明 |
| 2.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
