# 异常检测 / AnomalyDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AnomalyDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.AnomalyDetection` |
| 分类 (Category) | AI检测 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `anomaly-detection` |

## 算法说明 / Algorithm
当前实现是简化版 PatchCore 思路，默认特征提取器为轻量级 `lab_gradient_stats`：

1. 训练模式下，从 `NormalImages` 提取局部 patch 特征并构建 feature bank。
2. 推理模式下，计算待测图像 patch 到 feature bank 的最近邻距离。
3. 输出异常分数、热力图、二值异常掩膜、阈值和诊断信息。

这条路径保留了后续接入真实 embedding 模型的参数入口，但现阶段不应被理解为完整深度 PatchCore。

## 参数 / Parameters
| 名称 (Name) | 类型 (Type) | 默认值 (Default) | 说明 (Description) |
|------|------|------|------|
| `Mode` | `enum` | `inference` | `train` 或 `inference`。 |
| `FeatureBankPath` | `file` | `""` | 推理时显式 feature bank 路径。 |
| `SaveFeatureBankPath` | `file` | `""` | 训练后 feature bank 保存路径。 |
| `ModelId` | `string` | `""` | 通过模型目录解析特征库。 |
| `ModelCatalogPath` | `file` | `""` | 模型目录路径。 |
| `Backbone` | `string` | `simple_patchcore` | 当前仅支持 `simple_patchcore`。 |
| `FeatureExtractorId` | `string` | `lab_gradient_stats` | 当前默认特征提取器。 |
| `EmbeddingModelId` | `string` | `""` | 后续 embedding 模型入口。 |
| `EmbeddingModelPath` | `file` | `""` | 后续 embedding 模型路径。 |
| `PatchSize` | `int` | `32` | patch 大小。 |
| `PatchStride` | `int` | `16` | patch 步长。 |
| `CoresetRatio` | `double` | `0.2` | feature bank 采样比例。 |
| `Threshold` | `double` | `0.35` | 异常判定阈值。 |

## 输入/输出 / Inputs & Outputs
### 输入 / Inputs
| 名称 (Name) | 类型 (Type) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Image` | 是 | 推理图像；训练模式下也可作为预览图。 |
| `NormalImages` | `Any` | 否 | 训练模式下的正常样本集合。 |

### 输出 / Outputs
| 名称 (Name) | 类型 (Type) | 说明 (Description) |
|------|------|------|
| `AnomalyScore` | `Float` | 最大异常分数。 |
| `IsAnomaly` | `Boolean` | 是否判为异常。 |
| `AnomalyMap` | `Image` | 异常热力图。 |
| `AnomalyMask` | `Image` | 二值异常掩膜。 |
| `FeatureBankPath` | `String` | 实际使用的特征库路径。 |
| `PatchCount` | `Integer` | patch 数量。 |
| `ThresholdUsed` | `Float` | 实际阈值。 |
| `Diagnostics` | `Any` | feature bank 来源、schema、训练样本数等诊断信息。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 推理成本主要随 patch 数和 feature bank 规模增长；默认轻量特征比深度 embedding 低成本，但表达能力也更弱。 |
| 典型耗时 (Typical Latency) | `AnomalyDetection_mvtec_baseline.md` 在 MVTec AD Lite 子集记录 120 张测试图，总运行约 5104 ms；现场耗时会随图像尺寸、patch 参数、bank 规模和存储路径变化。 |
| 内存特征 (Memory Profile) | 需要保留 feature bank、patch 特征、异常分数图、热力图和掩膜；feature bank 越大，推理和内存压力越高。 |

## 证据与失败契约 / Evidence & Failure Contracts
- Dataset baseline：`quality/evals/reports/AnomalyDetection_mvtec_baseline.md`，324 张训练图、120 张测试图；Image AUROC=0.6609，Pixel AUROC=0.6709。
- 证据口径：该结果是 SimplePatchCore-Lite + `lab_gradient_stats` 的 baseline 记录，用于锁定当前实现能力和后续回归，不是生产级异常检测精度承诺。
- 失败契约重点：训练模式需要有效正常样本；推理模式需要可解析的 feature bank 或模型目录；输入图像、feature bank schema、patch 参数和阈值异常都应 fail closed。

## 适用场景 / Use Cases
- 适合：缺陷样本少、正常样本容易采集的表面异常初筛。
- 适合：需要热力图辅助人工复核、或作为后续真实 embedding anomaly 路线的兼容基线。
- 不适合：直接作为高风险产线唯一判定依据，尤其是跨材质、跨光照或跨批次变化很大的任务。

## 已知限制 / Known Limitations
1. 默认特征仍是统计型 patch 特征，不是深度 embedding；能力上限有限。
2. 推理复杂度随 feature bank 规模增长，适合中小规模样本库。
3. MVTec AD Lite baseline 的 AUROC 已明确显示它更像“可追踪基线”，不是可直接上线的生产精度。
4. 若要提升跨批次鲁棒性，建议后续切换到真实 ONNX embedding 路线并配套公开/现场数据集评估。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-28 | 补充 MVTec AD Lite baseline、AUROC 口径、失败契约和真实能力限制 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
