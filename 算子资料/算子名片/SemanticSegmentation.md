# 语义分割 / SemanticSegmentation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SemanticSegmentationOperator` |
| 枚举值 (Enum) | `OperatorType.SemanticSegmentation` |
| 分类 ID (CategoryId) | `AiInference` |
| 分类 (Category) | AI推理 |
| 分类顺序 (CategoryOrder) | 9 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:AiInference`, `分类显示:AI推理`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于运行 ONNX 语义分割模型，输出类别图、着色可视化结果和各类别掩码。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 12 个当前元数据字段，默认值、范围和枚举项以参数表为准。
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
- `Math.Max`
- `Math.Min`
- `Enumerable.Range`
- `InferenceSession`
- `ImageWrapper`
- `OperatorExecutionOutput.Success(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ModelId` | 模型ID | `string` | "" | - | Yes | - |
| `ModelCatalogPath` | 模型目录路径 | `file` | "" | - | Yes | - |
| `ModelPath` | 模型路径 | `file` | "" | - | Yes | - |
| `InputSize` | 输入尺寸 | `string` | 512,512 | - | Yes | 宽度,高度 |
| `NumClasses` | 类别数量 | `int` | 21 | [2, 4096] | Yes | - |
| `ClassNames` | 类别名称 | `string` | "" | - | Yes | JSON 数组或逗号分隔的名称列表 |
| `MaxClassMasks` | 最大类别掩码数 | `int` | 32 | [0, 4096] | Yes | 限制生成的类别掩码图像数量；填 0 表示不生成类别掩码。 |
| `ExecutionProvider` | 执行后端 | `enum` | cpu | cpu/CPU；cuda/CUDA | Yes | - |
| `ScaleToUnitRange` | 缩放到单位区间 | `bool` | true | - | Yes | - |
| `ChannelOrder` | 通道顺序 | `enum` | RGB | RGB；BGR | Yes | - |
| `Mean` | 均值 | `string` | 0,0,0 | - | Yes | - |
| `Std` | 标准差 | `string` | 1,1,1 | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SegmentationMap` | 分割图 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ColoredMap` | 着色结果图 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ClassMasks` | 类别掩码 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ClassCount` | 类别数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ClassMaskCount` | 类别掩码数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `OmittedClassMaskCount` | 省略类别掩码数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PresentClasses` | 存在类别 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ResolvedModelPath` | 解析后的模型路径 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelId` | 解析后的模型ID | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelCatalogPath` | 解析后的模型目录路径 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelSource` | 模型来源 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelProvenance` | 模型来源信息 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `ModelCatalogPath` | optional; - | visible: -; hidden: - | enabled: -; disabled: ANY(ModelId is empty \|\| ModelPath is not empty) | - | model_catalog | - | `SEMANTIC_SEGMENTATION_CATALOG_REQUIRES_MODEL_ID` |
| `ModelId` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | model_resource | - | `SEMANTIC_SEGMENTATION_MODEL_SOURCE_REQUIRED` |
| `ModelPath` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | model_resource | - | `SEMANTIC_SEGMENTATION_MODEL_SOURCE_REQUIRED` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 状态 | 支持位深 | 原生位深 | 支持通道 | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 失败码 | 证据 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | `Restricted` | CV_8U | CV_8U | 1, 3, 4 | Stage 2 conservative baseline: retain evidenced legacy 8U paths; reject higher depths until operator-specific evidence is added. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` | `2.0` |

### 模式限制 / Mode Restrictions
| 输入端口 | 模式 | 状态 | 位深 | 通道 | 转换 | 输出 | 动态范围 | 条件 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`BFFC56319912E37DF20C196A3EE0D739FAC3D19F30A9577773695841E09659D7`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 推理路径主要受模型规模、输入尺寸和硬件后端影响；后处理通常随候选数量线性或近似线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；取决于模型大小、输入尺寸、CPU/GPU/ONNX Runtime 后端和候选数量。 |
| 内存特征 (Memory Profile) | 需要模型会话、输入张量、输出张量和后处理集合内存；峰值随模型与输入尺寸增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 10 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
| 1.0.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
