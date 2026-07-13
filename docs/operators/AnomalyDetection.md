# 异常检测 / AnomalyDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AnomalyDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.AnomalyDetection` |
| 分类 (Category) | AI检测 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `anomaly-detection`, `experimental`, `industrial-remediation`, `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于运行简化版 PatchCore 异常检测器，支持训练/推理模式和特征库持久化。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Simplified PatchCore` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Image`、`NormalImages`。
- 参数解析覆盖 16 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OpenCvSharp + memory-bank nearest-neighbor`
- `OperatorBase.Get*Param(...)`
- `Path.GetFullPath`
- `File.Exists`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `Mode` | Mode | `enum` | inference | inference/Inference；train/Train | Yes | - |
| `FeatureBankPath` | Feature Bank Path | `file` | "" | - | No | - |
| `SaveFeatureBankPath` | Save Feature Bank Path | `file` | "" | - | No | - |
| `ModelId` | Model Id | `string` | "" | - | No | - |
| `ModelCatalogPath` | Model Catalog Path | `file` | "" | - | No | - |
| `Backbone` | Backbone | `string` | simple_patchcore | - | No | - |
| `FeatureExtractorId` | Feature Extractor Id | `string` | lab_gradient_stats | - | No | - |
| `EmbeddingModelId` | Embedding Model Id | `string` | "" | - | No | - |
| `EmbeddingModelPath` | Embedding Model Path | `file` | "" | - | No | - |
| `PatchSize` | Patch Size | `int` | 32 | [4, 256] | Yes | - |
| `PatchStride` | Patch Stride | `int` | 16 | [1, 256] | Yes | - |
| `CoresetRatio` | Coreset Ratio | `double` | 0.2 | [0.01, 1] | Yes | - |
| `Threshold` | Threshold | `double` | 0.35 | [0, 1] | Yes | - |
| `EnableCandidateProfile` | Enable Candidate Profile | `bool` | false | - | Yes | - |
| `CandidateProfile` | Candidate Profile | `enum` | default | default/Default；mvtec_lite_v2/MVTec Lite v2 | Yes | - |
| `CandidateFallbackMode` | Candidate Fallback Mode | `enum` | UseDefault | UseDefault/Use Default；Fail/Fail | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |
| `NormalImages` | Normal Images | `Any` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `AnomalyScore` | Anomaly Score | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `IsAnomaly` | Is Anomaly | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `AnomalyMap` | Anomaly Map | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `AnomalyMask` | Anomaly Mask | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `FeatureBankPath` | Feature Bank Path | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `PatchCount` | Patch Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ThresholdUsed` | Threshold Used | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Diagnostics` | Diagnostics | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `CandidateProfileApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `CandidateProfileEnabled` | `Boolean` | 源码通过输出字典索引赋值写入。 |
| `CandidateProfileFallbackReason` | `String` | 源码通过输出字典索引赋值写入。 |
| `EmbeddingSource` | `String` | 源码通过输出字典索引赋值写入。 |
| `FeatureBankCatalogPath` | `String` | 源码通过输出字典索引赋值写入。 |
| `FeatureBankModelId` | `String` | 源码通过输出字典索引赋值写入。 |
| `FeatureBankSource` | `String` | 源码通过输出字典索引赋值写入。 |
| `FeatureSchemaVersion` | `Any` | 源码通过输出字典索引赋值写入。 |
| `MeanNearestDistance` | `Float` | 源码通过输出字典索引赋值写入。 |
| `RequestedThreshold` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ResolvedEmbeddingPath` | `String` | 源码通过输出字典索引赋值写入。 |
| `ResolvedFeatureBankPath` | `String` | 源码通过输出字典索引赋值写入。 |
| `StdNearestDistance` | `Float` | 源码通过输出字典索引赋值写入。 |
| `TrainingImageCount` | `Integer` | 源码通过输出字典索引赋值写入。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P * B) |
| 典型耗时 (Typical Latency) | 未固定；取决于模型大小、输入尺寸、CPU/GPU/ONNX Runtime 后端和候选数量。 |
| 内存特征 (Memory Profile) | O(B) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 13 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：模型、标签和阈值已完成现场校准，需要把推理结果接入视觉流程的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。
- 不适合 (Not Suitable)：模型未完成验证、标签映射不稳定或现场数据分布明显偏离训练数据的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
3. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。
4. 模型推理类路径依赖模型文件、标签、运行时库和硬件后端，算法准确率不由算子元数据单独保证。
5. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-13 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
