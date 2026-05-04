# 异常检测 / AnomalyDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AnomalyDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.AnomalyDetection` |
| 分类 (Category) | AI检测 |
| 显示名 (DisplayName) | 异常检测 |
| 图标 (Icon) | `anomaly-detection` |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 实验性 Experimental |
| 标签 (Tags) | `experimental`, `industrial-remediation`, `anomaly-detection` |
| 关键词 (Keywords) | `anomaly`, `patchcore`, `feature bank`, `异常检测` |

### 算法信息 / Algorithm Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 算法名 (Name) | Simplified PatchCore |
| 核心 API (CoreApi) | OpenCvSharp + memory-bank nearest-neighbor |
| 时间复杂度 (TimeComplexity) | O(P * B) |
| 空间复杂度 (SpaceComplexity) | O(B) |
| 依赖 (Dependencies) | `OpenCvSharp` |

## 算法原理 / Algorithm Principle

**中文：** 该算子实现简化版 PatchCore 异常检测思路，支持训练和推理两种模式。训练模式下，从正常样本图像中提取局部 patch 特征并构建 feature bank（特征库）；推理模式下，计算待测图像 patch 到 feature bank 的最近邻距离，输出异常分数、热力图、二值异常掩膜、阈值和诊断信息。

默认特征提取器为轻量级 `lab_gradient_stats`（基于 LAB 色彩空间梯度统计），同时预留 `onnx_embedding` 路径以接入真实深度 embedding 模型。feature bank 支持 Coreset 采样以压缩规模，支持序列化保存/加载，支持通过 `ModelId + ModelCatalogPath` 从模型目录解析。

**English:** This operator implements a simplified PatchCore-style anomaly detection approach with train and inference modes. In train mode, it extracts local patch features from normal sample images and builds a feature bank. In inference mode, it computes nearest-neighbor distances from test image patches to the feature bank, outputting anomaly scores, heatmaps, binary anomaly masks, thresholds, and diagnostics.

The default feature extractor is the lightweight `lab_gradient_stats` (LAB color space gradient statistics), while an `onnx_embedding` path is reserved for real deep embedding models. The feature bank supports Coreset subsampling, serialization (save/load), and resolution via `ModelId + ModelCatalogPath` from a model catalog.

## 实现策略 / Implementation Strategy

**中文：** 源码中的关键实现策略：

1. **双模式执行**：`Mode=train` 构建 feature bank 并可选保存到磁盘，同时用第一张正常图做预览推理；`Mode=inference` 加载已有 feature bank 执行推理。
2. **Feature Bank 解析优先级**：`FeatureBankPath` 显式路径 > `ModelId + ModelCatalogPath` 目录解析（支持 `anomaly_detection`、`anomaly_feature_bank`、`feature_bank` 类型）。
3. **Embedding 模型解析**：`EmbeddingModelPath` 显式路径 > `EmbeddingModelId + ModelCatalogPath` 目录解析 > feature bank 元数据中记录的路径/ID。仅当 `FeatureExtractorId=onnx_embedding` 时才需要。
4. **Candidate Profile 机制**：支持 `default` 和 `mvtec_lite_v2` 两种预配置。`mvtec_lite_v2` 会锁定 `PatchSize=16`、`PatchStride=8`、`CoresetRatio=0.02`、`Threshold=0.10`、`Backbone=simple_patchcore`、`FeatureExtractorId=lab_gradient_stats`。推理时会校验 feature bank 与 profile 的兼容性，不兼容时按 `CandidateFallbackMode`（`UseDefault` 回退默认参数 / `Fail` 直接失败）。
5. **CPU-bound 工作调度**：feature bank 构建和推理均通过 `RunCpuBoundWork` 调度，避免阻塞 async 上下文。
6. **输出诊断信息**：`Diagnostics` 字典包含完整的模式、路径来源、backbone、extractor、schema 版本、训练图像数、patch 参数、阈值和 candidate profile 状态。

**English:** Key implementation strategies in the source code:

1. **Dual-mode execution**: `Mode=train` builds a feature bank with optional disk persistence and runs a preview inference on the first normal image; `Mode=inference` loads an existing feature bank for inference.
2. **Feature bank resolution priority**: Explicit `FeatureBankPath` > `ModelId + ModelCatalogPath` catalog resolution (supports `anomaly_detection`, `anomaly_feature_bank`, `feature_bank` types).
3. **Embedding model resolution**: Explicit `EmbeddingModelPath` > `EmbeddingModelId + ModelCatalogPath` > feature bank metadata path/ID. Only required when `FeatureExtractorId=onnx_embedding`.
4. **Candidate profile mechanism**: Supports `default` and `mvtec_lite_v2` presets. `mvtec_lite_v2` locks `PatchSize=16`, `PatchStride=8`, `CoresetRatio=0.02`, `Threshold=0.10`, `Backbone=simple_patchcore`, `FeatureExtractorId=lab_gradient_stats`. During inference, compatibility with the loaded feature bank is validated; incompatible cases fall back per `CandidateFallbackMode` (`UseDefault` reverts to defaults / `Fail` aborts).
5. **CPU-bound work scheduling**: Feature bank construction and inference are dispatched via `RunCpuBoundWork` to avoid blocking the async context.
6. **Diagnostic output**: The `Diagnostics` dictionary contains complete mode, path source, backbone, extractor, schema version, training image count, patch parameters, threshold, and candidate profile state.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam / GetDoubleParam / GetIntParam / GetBoolParam` -- 读取参数
2. `NormalizeMode(mode)` -- 规范化模式
3. `ResolveCandidateProfile(@operator)` -- 解析 candidate profile
4. **训练路径 (Mode=train)**：
   5. `TryGetNormalImages(inputs)` -- 获取正常样本
   6. `ResolveEmbeddingModelTarget(...)` -- 解析 embedding 模型
   7. `SimplePatchCoreDetector.BuildFeatureBank(images, options)` -- 构建特征库
   8. `SimplePatchCoreDetector.Save(path, bank)` -- 保存特征库
   9. `SimplePatchCoreDetector.Analyze(previewImage, bank, threshold, options)` -- 预览推理
5. **推理路径 (Mode=inference)**：
   10. `ResolveFeatureBankInputTarget(@operator)` -- 解析特征库路径
   11. `SimplePatchCoreDetector.Load(path)` -- 加载特征库
   12. `IsMvtecLiteV2FeatureBankCompatible(bank, ...)` -- 兼容性校验
   13. `SimplePatchCoreDetector.Analyze(image, bank, threshold, options)` -- 推理
6. `CreateOutputs(...)` -- 构建输出（含 `Diagnostics` 诊断字典）

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `inference` | `inference` / `train` | 工作模式。训练模式构建特征库，推理模式使用已有特征库。 |
| `FeatureBankPath` | `file` | `""` | 文件路径 | 推理时显式 feature bank 路径。优先级高于 ModelId 目录解析。 |
| `SaveFeatureBankPath` | `file` | `""` | 文件路径 | 训练后 feature bank 保存路径。为空时回退到 `FeatureBankPath` 或 `ModelId` 解析。 |
| `ModelId` | `string` | `""` | - | 模型目录中的特征库标识，配合 `ModelCatalogPath` 使用。 |
| `ModelCatalogPath` | `file` | `""` | - | 模型目录 JSON 路径。 |
| `Backbone` | `string` | `simple_patchcore` | - | 特征提取骨干网络。当前仅支持 `simple_patchcore`。 |
| `FeatureExtractorId` | `string` | `lab_gradient_stats` | `lab_gradient_stats` / `onnx_embedding` | 特征提取器标识。`onnx_embedding` 需要提供 embedding 模型。 |
| `EmbeddingModelId` | `string` | `""` | - | embedding 模型目录标识。仅 `FeatureExtractorId=onnx_embedding` 时需要。 |
| `EmbeddingModelPath` | `file` | `""` | 文件路径 | embedding 模型文件路径。优先级高于 ModelId 解析。 |
| `PatchSize` | `int` | `32` | `[4, 256]` | patch 大小（像素）。 |
| `PatchStride` | `int` | `16` | `[1, 256]` | patch 步长（像素）。 |
| `CoresetRatio` | `double` | `0.2` | `[0.01, 1.0]` | feature bank Coreset 采样比例。越小压缩越强但可能损失精度。 |
| `Threshold` | `double` | `0.35` | `[0.0, 1.0]` | 异常判定阈值。分数超过该阈值判为异常。 |
| `EnableCandidateProfile` | `bool` | `false` | `true` / `false` | 是否启用 candidate profile 预配置。 |
| `CandidateProfile` | `enum` | `default` | `default` / `mvtec_lite_v2` | 预配置方案。`mvtec_lite_v2` 会锁定 patch/coreset/backbone 参数。 |
| `CandidateFallbackMode` | `enum` | `UseDefault` | `UseDefault` / `Fail` | candidate profile 与 feature bank 不兼容时的回退策略。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | No | 推理图像。训练模式下可作为预览图。 |
| `NormalImages` | Normal Images | `Any` | No | 训练模式下的正常样本集合（必须非空）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `AnomalyScore` | Anomaly Score | `Float` | 最大异常分数。 |
| `IsAnomaly` | Is Anomaly | `Boolean` | 是否判为异常（`AnomalyScore > ThresholdUsed`）。 |
| `AnomalyMap` | Anomaly Map | `Image` | 异常热力图。 |
| `AnomalyMask` | Anomaly Mask | `Image` | 二值异常掩膜。 |
| `FeatureBankPath` | Feature Bank Path | `String` | 实际使用的特征库路径。 |
| `PatchCount` | Patch Count | `Integer` | 推理时的 patch 数量。 |
| `ThresholdUsed` | Threshold Used | `Float` | 实际使用的阈值（可能被 candidate profile 覆盖）。 |
| `Diagnostics` | Diagnostics | `Any` | 完整诊断信息字典（见下方）。 |

### Diagnostics 字段详情 / Diagnostics Fields
| 字段名 (Field) | 类型 (Type) | 说明 (Description) |
|------|------|------|
| `Mode` | `String` | 执行模式：`train` 或 `inference`。 |
| `ResolvedFeatureBankPath` | `String` | 解析后的特征库绝对路径。 |
| `FeatureBankSource` | `String` | 特征库来源：`ExplicitPath` / `ModelCatalog` / `Unspecified`。 |
| `FeatureBankModelId` | `String` | 特征库的 ModelId。 |
| `FeatureBankCatalogPath` | `String` | 特征库的模型目录路径。 |
| `Backbone` | `String` | 实际使用的骨干网络。 |
| `FeatureExtractorId` | `String` | 实际使用的特征提取器。 |
| `FeatureSchemaVersion` | `String` | 特征 schema 版本。 |
| `EmbeddingModelId` | `String` | embedding 模型标识。 |
| `EmbeddingModelPath` | `String` | embedding 模型路径。 |
| `ResolvedEmbeddingPath` | `String` | 解析后的 embedding 模型绝对路径。 |
| `EmbeddingSource` | `String` | embedding 来源。 |
| `TrainingImageCount` | `Integer` | 训练时使用的图像数量。 |
| `PatchSize` / `PatchStride` | `Integer` | 实际 patch 参数。 |
| `RequestedThreshold` | `Double` | 用户请求的阈值。 |
| `ThresholdUsed` | `Double` | 实际使用的阈值。 |
| `CandidateProfileEnabled` | `Boolean` | candidate profile 是否启用。 |
| `CandidateProfile` | `String` | 使用的 candidate profile 名称。 |
| `CandidateProfileApplied` | `Boolean` | candidate profile 是否实际生效。 |
| `CandidateFallbackMode` | `String` | 回退模式。 |
| `CandidateProfileFallbackReason` | `String` | 不兼容时的回退原因。 |
| `MeanNearestDistance` / `StdNearestDistance` | `Double` | feature bank 中最近邻距离的统计量。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 推理成本 `O(P * B)`，其中 P 为 patch 数量，B 为 feature bank 规模。Coreset 采样可有效压缩 B。 |
| 典型耗时 (Typical Latency) | `AnomalyDetection_mvtec_baseline.md` 记录 120 张测试图总运行约 5104 ms。现场耗时随图像尺寸、patch 参数、bank 规模和存储路径变化。 |
| 内存特征 (Memory Profile) | 需要保留 feature bank、patch 特征、异常分数图、热力图和掩膜。feature bank 越大，推理和内存压力越高。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：缺陷样本少、正常样本容易采集的表面异常初筛。
- **适合 (Suitable)**：需要热力图辅助人工复核、或作为后续真实 embedding anomaly 路线的兼容基线。
- **适合 (Suitable)**：需要特征库持久化和跨批次复用的工业现场。
- **不适合 (Not Suitable)**：直接作为高风险产线唯一判定依据，尤其是跨材质、跨光照或跨批次变化很大的任务。
- **不适合 (Not Suitable)**：需要实时推理且 feature bank 规模极大的场景（默认轻量特征的表达能力有限）。

## 已知限制 / Known Limitations
1. 默认特征仍是统计型 `lab_gradient_stats` patch 特征，不是深度 embedding；能力上限有限。
2. `Backbone` 当前仅支持 `simple_patchcore`；`FeatureExtractorId` 仅支持 `lab_gradient_stats` 和 `onnx_embedding`。
3. 推理复杂度随 feature bank 规模增长，适合中小规模样本库。
4. `mvtec_lite_v2` candidate profile 与已有 feature bank 不兼容时（patch size/stride/backbone/extractor 不匹配），会按 `CandidateFallbackMode` 回退。
5. MVTec AD Lite baseline 的 AUROC（Image=0.6609, Pixel=0.6709）是 SimplePatchCore-Lite + `lab_gradient_stats` 的 baseline 记录，用于锁定当前实现能力和后续回归，不是生产级异常检测精度承诺。
6. 若要提升跨批次鲁棒性，建议切换到 `onnx_embedding` 路线并配套公开/现场数据集评估。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增 [AlgorithmInfo] 字段、Candidate Profile 机制、Embedding 模型解析链、Diagnostics 字段详情、ModelId/ModelCatalogPath 模型目录解析；统一五列参数表；补全英文算法原理 |
| 1.0.1 | 2026-04-28 | 补充 MVTec AD Lite baseline、AUROC 口径、失败契约和真实能力限制 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
