# 深度学习 / DeepLearning

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DeepLearningOperator` |
| 枚举值 (Enum) | `OperatorType.DeepLearning` |
| 分类 (Category) | AI检测 |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:AI`, `成熟度:稳定`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于AI 深度学习推理，支持 YOLOv5/v6/v8/v11 等模型，用于缺陷检测和目标分类。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 13 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Resize`
- `Cv2.CvtColor`
- `Cv2.MinMaxLoc`
- `Cv2.Split`
- `Cv2.Rectangle`
- `Cv2.GetTextSize`
- `Cv2.PutText`
- `File.Exists`
- `Path.GetDirectoryName`
- `Path.Combine`
- `Math.Clamp`
- `Math.Min`
- `Math.Max`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `ModelPath` | 模型路径 | `file` | "" | - | Yes | - |
| `Confidence` | 置信度阈值 | `double` | 0.5 | [0, 1] | Yes | - |
| `ModelVersion` | YOLO版本 | `enum` | Auto | Auto/自动检测；YOLOv5/YOLOv5；YOLOv6/YOLOv6；YOLOv8/YOLOv8；YOLOv11/YOLOv11 | Yes | - |
| `InputSize` | 输入尺寸 | `int` | 640 | [320, 1280] | Yes | - |
| `UseGpu` | 使用GPU | `bool` | true | - | Yes | - |
| `GpuDeviceId` | GPU设备ID | `int` | 0 | [0, 15] | Yes | - |
| `TargetClasses` | 目标类别 | `string` | "" | - | Yes | 检测目标类别（逗号分隔，如 person,car），为空则检测所有类别 |
| `LabelsPath` | 标签文件路径 | `file` | "" | - | Yes | 无 ONNX metadata names 时的后备标签文件路径（每行一个标签）；模型包含 metadata names 时忽略此项。为空时查找模型目录 labels.txt，仍不可用则执行失败。 |
| `EnableInternalNms` | 启用内部NMS | `bool` | true | - | Yes | 关闭后输出置信度筛选后的候选框，由下游 BoxNms 负责唯一 NMS。 |
| `NmsIouThreshold` | NMS IoU Threshold | `double` | 0.45 | [0, 1] | Yes | 内部 NMS 与预览 NMS 使用的 IoU 阈值。 |
| `DetectionMode` | 检测模式 | `enum` | Defect | Defect/缺陷检测；Object/目标检测 | Yes | 缺陷检测：检出目标视为缺陷(NG)；目标检测：检出目标视为正常(OK) |
| `ModelId` | Model Id | `string` | "" | - | Yes | - |
| `ModelCatalogPath` | Model Catalog Path | `file` | "" | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `OriginalImage` | 原始图像 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `DetectionList` | 检测列表 | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `Defects` | 缺陷列表 | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `DefectCount` | 缺陷数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Objects` | 目标列表 | `DetectionList` | 检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。 |
| `ObjectCount` | 目标数量 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ResolvedModelPath` | Resolved Model Path | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelId` | Resolved Model Id | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelCatalogPath` | Resolved Model Catalog Path | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelSource` | Model Source | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelProvenance` | Model Provenance | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `CandidatesByClass` | `Any` | 源码通过输出字典索引赋值写入。 |
| `DroppedBeforeNms` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `InternalNmsEnabled` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `LabelSource` | `String` | 源码输出字典初始化中可见字段。 |
| `LabelValidationStatus` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `ModelMetadataLabels` | `String` | 源码输出字典初始化中可见字段。 |
| `NmsApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `NmsCandidateLimit` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `NmsIoUComparisons` | `Any` | 源码通过输出字典索引赋值写入。 |
| `NmsPrefilteredCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `PostprocessDiagnostics` | `Any` | 源码输出字典初始化中可见字段。 |
| `RawCandidateCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `ResolvedLabels` | `Any` | 源码输出字典初始化中可见字段。 |
| `VisualizationDetectionCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

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
- 执行失败契约：源码中发现 8 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：模型、标签和阈值已完成现场校准，需要把推理结果接入视觉流程的场景。
- 不适合 (Not Suitable)：外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。
- 不适合 (Not Suitable)：模型未完成验证、标签映射不稳定或现场数据分布明显偏离训练数据的场景。
- 不适合 (Not Suitable)：图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
4. 外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。
5. 模型推理类路径依赖模型文件、标签、运行时库和硬件后端，算法准确率不由算子元数据单独保证。
6. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |
