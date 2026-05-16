# 语义分割 / SemanticSegmentation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SemanticSegmentationOperator` |
| 枚举值 (Enum) | `OperatorType.SemanticSegmentation` |
| 分类 (Category) | AI检测 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Runs an ONNX semantic segmentation model and returns class map, colored visualization, and per-class masks。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 11 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Resize`
- `Cv2.CvtColor`
- `Cv2.Compare`
- `File.Exists`
- `Path.GetFullPath`
- `JsonSerializer.Serialize`
- `JsonSerializer.Deserialize`
- `Enumerable.Range`
- `InferenceSession`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ModelId` | Model Id | `string` | "" | - | Yes | - |
| `ModelCatalogPath` | Model Catalog Path | `file` | "" | - | Yes | - |
| `ModelPath` | Model Path | `file` | "" | - | Yes | - |
| `InputSize` | Input Size | `string` | 512,512 | - | Yes | Width,Height |
| `NumClasses` | Num Classes | `int` | 21 | [2, 4096] | Yes | - |
| `ClassNames` | Class Names | `string` | "" | - | Yes | JSON array or comma-separated names |
| `ExecutionProvider` | Execution Provider | `enum` | cpu | cpu/CPU；cuda/CUDA | Yes | - |
| `ScaleToUnitRange` | Scale To Unit Range | `bool` | true | - | Yes | - |
| `ChannelOrder` | Channel Order | `enum` | RGB | RGB/RGB；BGR/BGR | Yes | - |
| `Mean` | Mean | `string` | 0,0,0 | - | Yes | - |
| `Std` | Std | `string` | 1,1,1 | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SegmentationMap` | Segmentation Map | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ColoredMap` | Colored Map | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ClassMasks` | Class Masks | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ClassCount` | Class Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PresentClasses` | Present Classes | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ResolvedModelPath` | Resolved Model Path | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelId` | Resolved Model Id | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelCatalogPath` | Resolved Model Catalog Path | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelSource` | Model Source | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelProvenance` | Model Provenance | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 推理路径主要受模型规模、输入尺寸和硬件后端影响；后处理通常随候选数量线性或近似线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于模型大小、输入尺寸、CPU/GPU/ONNX Runtime 后端和候选数量。 |
| 内存特征 (Memory Profile) | 需要模型会话、输入张量、输出张量和后处理集合内存；峰值随模型与输入尺寸增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `Acme.Product/tests/Acme.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 9 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：模型、标签和阈值已完成现场校准，需要把推理结果接入视觉流程的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。
- 不适合 (Not Suitable)：模型未完成验证、标签映射不稳定或现场数据分布明显偏离训练数据的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。
4. 模型推理类路径依赖模型文件、标签、运行时库和硬件后端，算法准确率不由算子元数据单独保证。
5. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
