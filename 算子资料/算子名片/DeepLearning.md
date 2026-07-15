# 深度学习 / DeepLearning

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DeepLearningOperator` |
| 枚举值 (Enum) | `OperatorType.DeepLearning` |
| 分类 ID (CategoryId) | `AiInference` |
| 分类 (Category) | AI推理 |
| 分类顺序 (CategoryOrder) | 9 |
| 版本 (Version) | `1.1.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | No |
| AI 必须披露状态 (Requires Disclosure) | Yes |
| 标签 (Tags) | `分类:AiInference`, `分类显示:AI推理`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于统一 ONNX 深度学习推理入口，支持目标检测、图像分类和语义分割；默认保持历史 YOLO 目标检测行为。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Image`；缺失时通常返回失败结果。
- 参数解析覆盖 27 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Cv2.Resize`
- `Cv2.Rectangle`
- `Cv2.PutText`
- `Cv2.CvtColor`
- `Cv2.MinMaxLoc`
- `Cv2.Split`
- `Cv2.GetTextSize`
- `File.Exists`
- `Path.GetDirectoryName`
- `Path.Combine`
- `JsonSerializer.Deserialize`
- `Math.Clamp`
- `Math.Min`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TaskType` | 任务类型 | `enum` | ObjectDetection | ObjectDetection/目标检测；ImageClassification/图像分类；SemanticSegmentation/语义分割；Auto/可靠自动识别 | Yes | 默认 ObjectDetection 保持旧流程；Auto 仅在模型目录类型或输出形状能唯一判定时生效。 |
| `ModelPath` | 模型路径 | `file` | "" | - | Yes | - |
| `Confidence` | 置信度阈值 | `double` | 0.5 | [0, 1] | Yes | - |
| `ModelVersion` | YOLO版本 | `enum` | Auto | Auto/自动检测；YOLOv5；YOLOv6；YOLOv8；YOLOv11 | Yes | - |
| `InputSize` | 输入尺寸 | `int` | 640 | [320, 1280] | Yes | - |
| `UseGpu` | 使用GPU | `bool` | true | - | Yes | - |
| `GpuDeviceId` | GPU设备ID | `int` | 0 | [0, 15] | Yes | - |
| `TargetClasses` | 目标类别 | `string` | "" | - | Yes | 检测目标类别（逗号分隔，如 person,car），为空则检测所有类别 |
| `LabelsPath` | 标签文件路径 | `file` | "" | - | Yes | 无 ONNX metadata names 时的后备标签文件路径（每行一个标签）；模型包含 metadata names 时忽略此项。为空时查找模型目录 labels.txt，仍不可用则执行失败。 |
| `EnableInternalNms` | 启用内部NMS | `bool` | true | - | Yes | 仅用于 RawYolo 输出的后处理开关；OutputFormat=EndToEndNms 时信任 ONNX 模型内部候选框抑制/NMS，平台侧不再额外拆出 BoxNms。 |
| `NmsIouThreshold` | NMS IoU阈值 | `double` | 0.45 | [0, 1] | Yes | 内部 NMS 与预览 NMS 使用的 IoU 阈值。 |
| `OutputFormat` | 输出格式 | `enum` | Auto | Auto/自动识别；RawYolo/原始 YOLO；EndToEndNms/端到端 NMS | Yes | Auto 自动识别；RawYolo 表示原始 YOLO 输出；EndToEndNms 表示模型已输出 NMS 后的 [x1,y1,x2,y2,score,class] 检测结果。 |
| `DetectionMode` | 检测模式 | `enum` | Defect | Defect/缺陷检测；Object/目标检测 | Yes | 缺陷检测：检出目标视为缺陷(NG)；目标检测：检出目标视为正常(OK) |
| `TopK` | 分类 Top-K | `int` | 5 | [1, 100] | Yes | - |
| `ClassificationInputSize` | 分类输入尺寸 | `string` | Auto | - | Yes | Auto 使用模型目录或 ONNX 静态输入尺寸；也可填写 Width,Height。 |
| `ClassificationScoreMode` | 分类分数模式 | `enum` | Auto | Auto/自动识别 logits/概率；Logits/执行 Softmax；Probabilities/概率直出 | Yes | - |
| `ClassNames` | 类别名称 | `string` | "" | - | Yes | JSON 数组或逗号分隔；ONNX metadata names 和模型目录 class_names 优先。 |
| `SegmentationInputSize` | 分割输入尺寸 | `string` | Auto | - | Yes | Auto 使用模型目录或 ONNX 静态输入尺寸；也可填写 Width,Height。 |
| `NumClasses` | 分割类别数 | `int` | 21 | [2, 4096] | Yes | - |
| `MaxClassMasks` | 最大类别掩码数 | `int` | 32 | [0, 4096] | Yes | - |
| `ExecutionProvider` | 执行后端 | `enum` | Auto | Auto/跟随 UseGpu；CPU；CUDA/CUDA 优先并允许 CPU 回退 | Yes | - |
| `ScaleToUnitRange` | 缩放到 0-1 | `bool` | true | - | Yes | - |
| `ChannelOrder` | 通道顺序 | `enum` | RGB | RGB；BGR | Yes | - |
| `Mean` | 归一化均值 | `string` | 0,0,0 | - | Yes | - |
| `Std` | 归一化标准差 | `string` | 1,1,1 | - | Yes | - |
| `ModelId` | 模型ID | `string` | "" | - | Yes | - |
| `ModelCatalogPath` | 模型目录路径 | `file` | "" | - | Yes | - |

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
| `TaskType` | 实际任务类型 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `RequestedTaskType` | 请求任务类型 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `TaskResolutionSource` | 任务识别来源 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `TaskResolutionEvidence` | 任务识别依据 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `StatusCode` | 状态码 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `StatusMessage` | 状态信息 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `TopClassLabel` | 最高类别 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `TopClassConfidence` | 最高类别置信度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ClassificationTopK` | 分类 Top-K | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ClassificationResult` | 分类结果 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `SegmentationMap` | 分割类别图 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ColoredMap` | 分割可视化 | `Image` | 图像输出，可供后续图像处理、显示或保存节点使用。 |
| `ClassMasks` | 类别掩码 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ClassCount` | 分割类别数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `ClassMaskCount` | 类别掩码数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `OmittedClassMaskCount` | 未输出类别掩码数 | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `PresentClasses` | 出现类别 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `ResolvedModelPath` | 解析后的模型路径 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelId` | 解析后的模型ID | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ResolvedModelCatalogPath` | 解析后的模型目录路径 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelSource` | 模型来源 | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `ModelProvenance` | 模型来源信息 | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `PostprocessDiagnostics` | Postprocess Diagnostics | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `OutputFormat` | Output Format | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| `ChannelOrder` | metadata; - | visible: -; hidden: ALL(TaskType == ObjectDetection) | enabled: -; disabled: ALL(TaskType == ObjectDetection) | ALL(TaskType == ObjectDetection) | - | - | `DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION` |
| `ClassNames` | optional; - | visible: -; hidden: ALL(TaskType == ObjectDetection) | enabled: -; disabled: ALL(TaskType == ObjectDetection) | ALL(TaskType == ObjectDetection) | - | - | `DEEP_LEARNING_CLASS_NAMES_FOR_CLASSIFICATION_OR_SEGMENTATION` |
| `ClassificationInputSize` | metadata; - | visible: -; hidden: ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_INPUT_SIZE_ONLY_FOR_CLASSIFICATION` |
| `ClassificationScoreMode` | metadata; - | visible: -; hidden: ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_SCORE_MODE_ONLY_FOR_CLASSIFICATION` |
| `Confidence` | metadata; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_CONFIDENCE_ONLY_FOR_DETECTION` |
| `DetectionMode` | metadata; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_DETECTION_MODE_ONLY_FOR_DETECTION` |
| `EnableInternalNms` | metadata; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation \|\| OutputFormat == EndToEndNms) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_MODEL_OWNS_END_TO_END_NMS` |
| `ExecutionProvider` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `DEEP_LEARNING_EXECUTION_PROVIDER` |
| `GpuDeviceId` | metadata; - | visible: -; hidden: - | enabled: -; disabled: ALL(ExecutionProvider == CPU) | - | - | - | `DEEP_LEARNING_GPU_DEVICE_DISABLED_WITHOUT_GPU` |
| `InputSize` | metadata; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_DETECTION_INPUT_SIZE_ONLY_FOR_DETECTION` |
| `LabelsPath` | optional; - | visible: -; hidden: ALL(TaskType == SemanticSegmentation) | enabled: -; disabled: ALL(TaskType == SemanticSegmentation) | ALL(TaskType == SemanticSegmentation) | model_labels | - | `DEEP_LEARNING_LABELS_FOR_DETECTION_OR_CLASSIFICATION` |
| `MaxClassMasks` | metadata; - | visible: -; hidden: ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | enabled: -; disabled: ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | - | - | `DEEP_LEARNING_CLASS_MASKS_ONLY_FOR_SEGMENTATION` |
| `Mean` | metadata; - | visible: -; hidden: ALL(TaskType == ObjectDetection) | enabled: -; disabled: ALL(TaskType == ObjectDetection) | ALL(TaskType == ObjectDetection) | - | - | `DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION` |
| `ModelCatalogPath` | optional; - | visible: -; hidden: - | enabled: -; disabled: ANY(ModelId is empty \|\| ModelPath is not empty) | - | model_catalog | - | `DEEP_LEARNING_CATALOG_REQUIRES_MODEL_ID` |
| `ModelId` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | model_resource | - | `DEEP_LEARNING_MODEL_SOURCE_REQUIRED` |
| `ModelPath` | required; - | visible: -; hidden: - | enabled: -; disabled: - | - | model_resource | - | `DEEP_LEARNING_MODEL_SOURCE_REQUIRED` |
| `ModelVersion` | metadata; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_YOLO_VERSION_ONLY_FOR_DETECTION` |
| `NmsIouThreshold` | metadata; ALL(OutputFormat == RawYolo && EnableInternalNms == true); ANY(TaskType == ObjectDetection \|\| TaskType == Auto) | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation \|\| OutputFormat == EndToEndNms \|\| EnableInternalNms == false) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation \|\| OutputFormat == EndToEndNms \|\| EnableInternalNms == false) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation \|\| OutputFormat == EndToEndNms \|\| EnableInternalNms == false) | - | - | `DEEP_LEARNING_NMS_THRESHOLD_ACTIVE_FOR_INTERNAL_NMS` |
| `NumClasses` | metadata; - | visible: -; hidden: ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | enabled: -; disabled: ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | - | - | `DEEP_LEARNING_CLASS_COUNT_ONLY_FOR_SEGMENTATION` |
| `OutputFormat` | metadata; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_OUTPUT_FORMAT_ONLY_FOR_DETECTION` |
| `ScaleToUnitRange` | metadata; - | visible: -; hidden: ALL(TaskType == ObjectDetection) | enabled: -; disabled: ALL(TaskType == ObjectDetection) | ALL(TaskType == ObjectDetection) | - | - | `DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION` |
| `SegmentationInputSize` | metadata; - | visible: -; hidden: ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | enabled: -; disabled: ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | ANY(TaskType == ObjectDetection \|\| TaskType == ImageClassification) | - | - | `DEEP_LEARNING_INPUT_SIZE_ONLY_FOR_SEGMENTATION` |
| `Std` | metadata; - | visible: -; hidden: ALL(TaskType == ObjectDetection) | enabled: -; disabled: ALL(TaskType == ObjectDetection) | ALL(TaskType == ObjectDetection) | - | - | `DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION` |
| `TargetClasses` | optional; - | visible: -; hidden: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ImageClassification \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_TARGET_CLASSES_ONLY_FOR_DETECTION` |
| `TaskType` | metadata; - | visible: -; hidden: - | enabled: -; disabled: - | - | - | - | `DEEP_LEARNING_TASK_TYPE` |
| `TopK` | metadata; - | visible: -; hidden: ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | enabled: -; disabled: ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | ANY(TaskType == ObjectDetection \|\| TaskType == SemanticSegmentation) | - | - | `DEEP_LEARNING_TOP_K_ONLY_FOR_CLASSIFICATION` |
| `UseGpu` | metadata; - | visible: -; hidden: - | enabled: -; disabled: ANY(ExecutionProvider == CPU \|\| ExecutionProvider == CUDA) | - | - | - | `DEEP_LEARNING_USE_GPU_ONLY_FOR_AUTO_PROVIDER` |

## 图像输入域合同 / Image Input Domain Contracts
| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |
|------|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Allowed:3, Rejected:0, Unknown:25 | Legacy 8U compatibility allowance — unverified | CV_8U | CV_8U | 1, 3, 4 | Legacy 8U compatibility allowance — unverified. Higher-depth and undeclared combinations remain Unknown and fail closed. | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit native numeric domain; no implicit MinMax conversion. | NotApplicableFor8U | `IMAGE_DEPTH_UNSUPPORTED` | `2.1` |

### 精确运行变体 / Exact Runtime Variants
| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |
|------|------|------|------|------|------|------|------|------|------|------|------|
| `Image` | Default | CV_8UC1, CV_8UC3, CV_8UC4 | Legacy 8U execution path retained for compatibility; no per-operator E2 evidence. | `Allowed` | `LegacyCompatibilityAllowance` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | 8-bit legacy numeric domain. | `Any` | `IMAGE_DEPTH_UNSUPPORTED` | `E0_SOURCE_AUDIT` |
| `Image` | Default | CV_8UC2, CV_8SC1, CV_8SC2, CV_8SC3, CV_8SC4, CV_16UC1, CV_16UC2, CV_16UC3, CV_16UC4, CV_16SC1, CV_16SC2, CV_16SC3, CV_16SC4, CV_32SC1, CV_32SC2, CV_32SC3, CV_32SC4, CV_32FC1, CV_32FC2, CV_32FC3, CV_32FC4, CV_64FC1, CV_64FC2, CV_64FC3, CV_64FC4 | No operator-specific executable evidence is registered. | `Unknown` | `Unknown` | None | Operator-specific legacy output policy; no Stage 2 depth widening. | Undefined until verified. | `Any` | `IMAGE_CONTRACT_UNKNOWN` | `Unknown` |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| `ClassCount` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `ClassMaskCount` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `ClassMasks` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `ClassificationResult` | ALL(TaskType == ImageClassification) | `DEEP_LEARNING_CLASSIFICATION_OUTPUT` |
| `ClassificationTopK` | ALL(TaskType == ImageClassification) | `DEEP_LEARNING_CLASSIFICATION_OUTPUT` |
| `ColoredMap` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `DefectCount` | ALL(TaskType == ObjectDetection && DetectionMode == Defect) | `DEEP_LEARNING_DEFECT_OUTPUT` |
| `Defects` | ALL(TaskType == ObjectDetection && DetectionMode == Defect) | `DEEP_LEARNING_DEFECT_OUTPUT` |
| `DetectionList` | ALL(TaskType == ObjectDetection) | `DEEP_LEARNING_DETECTION_OUTPUT` |
| `ObjectCount` | ALL(TaskType == ObjectDetection && DetectionMode == Object) | `DEEP_LEARNING_OBJECT_OUTPUT` |
| `Objects` | ALL(TaskType == ObjectDetection && DetectionMode == Object) | `DEEP_LEARNING_OBJECT_OUTPUT` |
| `OmittedClassMaskCount` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `PresentClasses` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `SegmentationMap` | ALL(TaskType == SemanticSegmentation) | `DEEP_LEARNING_SEGMENTATION_OUTPUT` |
| `TopClassConfidence` | ALL(TaskType == ImageClassification) | `DEEP_LEARNING_CLASSIFICATION_OUTPUT` |
| `TopClassLabel` | ALL(TaskType == ImageClassification) | `DEEP_LEARNING_CLASSIFICATION_OUTPUT` |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`C438192588112353D92F31927FBA32A49E0955DACE787A203ACBDC3B7523F42B`
- `type:ClearVision.Product.Infrastructure.Operators.DeepLearningTaskResolver`
- `type:ClearVision.Product.Infrastructure.Operators.SemanticSegmentationOperator`
- `type:ClearVision.Product.Infrastructure.Services.DeepLearningLabelResolver`

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `CandidatesByClass` | `Any` | 源码通过输出字典索引赋值写入。 |
| `ClassId` | `String` | 源码通过输出字典索引赋值写入。 |
| `DroppedBeforeNms` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Height` | `Integer` | 由图像输出封装自动附加，表示输出图像高度。 |
| `InputHeight` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `InputLayout` | `Any` | 源码通过输出字典索引赋值写入。 |
| `InputSizeSource` | `String` | 源码通过输出字典索引赋值写入。 |
| `InputWidth` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `InternalNmsEnabled` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `Label` | `Any` | 源码通过输出字典索引赋值写入。 |
| `LabelSource` | `String` | 源码通过输出字典索引赋值写入。 |
| `LabelValidationStatus` | `Boolean` | 源码输出字典初始化中可见字段。 |
| `ModelMetadataLabels` | `String` | 源码输出字典初始化中可见字段。 |
| `NmsApplied` | `Any` | 源码通过输出字典索引赋值写入。 |
| `NmsCandidateLimit` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `NmsIoUComparisons` | `Any` | 源码通过输出字典索引赋值写入。 |
| `NmsPrefilteredCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `OutputName` | `Any` | 源码通过输出字典索引赋值写入。 |
| `OutputShape` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Rank` | `Any` | 源码通过输出字典索引赋值写入。 |
| `RawCandidateCount` | `Integer` | 源码通过输出字典索引赋值写入。 |
| `ResolvedLabels` | `Any` | 源码输出字典初始化中可见字段。 |
| `ResolvedScoreMode` | `Float` | 源码通过输出字典索引赋值写入。 |
| `VisualizationDetectionCount` | `Integer` | 源码输出字典初始化中可见字段。 |
| `Width` | `Integer` | 由图像输出封装自动附加，表示输出图像宽度。 |

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
- 执行失败契约：源码中发现 16 条 `OperatorExecutionOutput.Failure(...)` 路径。

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
| 1.1.0 | 2026-07-15 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |
