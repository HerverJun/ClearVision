# 深度学习 / DeepLearning

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DeepLearningOperator` |
| 枚举值 (Enum) | `OperatorType.DeepLearning` |
| 分类 (Category) | AI检测 |
| 显示名 (DisplayName) | 深度学习 |
| 图标 (Icon) | `ai` |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | `深度学习`, `AI`, `模型`, `推理`, `缺陷识别`, `目标检测`, `YOLO`, `判断瑕疵`, `Deep learning` |

## 算法原理 / Algorithm Principle

**中文：** 该算子基于 ONNX Runtime 执行 YOLO 系列目标检测模型推理。整体流程为：对输入图像做保持宽高比的 letterbox 预处理（填充灰色 114），将 BGR 图像转换为模型期望的 RGB CHW 张量 `[1, 3, InputSize, InputSize]`；调用 ONNX Runtime 执行推理；根据输出张量形状自动判断 YOLO 版本（YOLOv5/v6 使用 `[1, N, 85]` 格式，YOLOv8/v11 使用 `[1, 84, N]` 或 `[1, N, 84]` 格式）；走对应的后处理分支解析检测框；对同类别框执行 NMS（IoU 阈值可调，默认 0.45）；根据 `DetectionMode` 将检测结果解释为缺陷列表或目标列表。

支持的 YOLO 版本：
- `YOLOv5` / `YOLOv6`：输出格式 `[1, anchors, features]`，features = 5 + numClasses（含 objectness 置信度）
- `YOLOv8` / `YOLOv11`：输出格式 `[1, features, anchors]` 或 `[1, anchors, features]`，features = 4 + numClasses（无 objectness）
- `Auto`：根据输出张量维度自动检测版本

**English:** This operator runs YOLO object detection models via ONNX Runtime. The pipeline performs aspect-ratio-preserving letterbox preprocessing (padding with gray 114), converts BGR images to RGB CHW tensors `[1, 3, InputSize, InputSize]`, executes ONNX inference, auto-detects the YOLO version from the output tensor shape when configured as `Auto`, applies version-specific postprocessing to decode bounding boxes, performs same-class NMS with a configurable IoU threshold (default 0.45), and interprets detections as either defects or objects based on `DetectionMode`.

Supported YOLO versions:
- `YOLOv5` / `YOLOv6`: Output shape `[1, anchors, features]`, features = 5 + numClasses (includes objectness score)
- `YOLOv8` / `YOLOv11`: Output shape `[1, features, anchors]` or `[1, anchors, features]`, features = 4 + numClasses (no objectness)
- `Auto`: Auto-detects version from output tensor dimensions

## 实现策略 / Implementation Strategy

**中文：** 源码中包含多项关键工程策略：

1. **模型缓存**：使用静态 `ConcurrentDictionary<string, CachedModelSession>` 缓存模型会话，缓存键为 `modelPath_gpu_{useGpu}_{gpuDeviceId}`，带引用计数的 `ModelSessionLease` 保证并发安全。
2. **LRU 驱逐**：模型缓存上限为 3，超出后按访问顺序驱逐最久未使用模型。
3. **GPU 支持与回退**：优先尝试 TensorRT（反射调用），失败后尝试 CUDA，CUDA 失败回退 CPU。
4. **标签加载优先级**：ONNX metadata names > `LabelsPath` 显式文件 > 模型目录下 `labels.txt` > 内置标签。metadata 与外部标签不匹配时会记录 Mismatch 状态但不阻断流程。
5. **输出张量选择**：当标签数已知时，优先匹配 `features = numClasses + 4` 或 `numClasses + 5` 的张量；否则按启发式评分选择。
6. **内部 NMS 开关**：`EnableInternalNms=false` 时输出置信度筛选后的原始候选框，由下游 `BoxNms` 算子负责唯一 NMS；此时预览图仍会施加可视化 NMS 保证可读性。
7. **图像深度归一化**：预处理前自动将 CV_16U/CV_32F/CV_64F 图像转为 CV_8U，支持 `[0,1]`、`[0,255]`、`[0,65535]` 范围的浮点图。
8. **模型目录解析**：支持 `ModelId + ModelCatalogPath` 从 `models/model_catalog.json` 解析模型路径，替代显式 `ModelPath`。

**English:** The implementation includes several production-grade engineering strategies:

1. **Model caching**: Static `ConcurrentDictionary` with reference-counted `ModelSessionLease` for safe concurrent access, keyed by `modelPath_gpu_{useGpu}_{gpuDeviceId}`.
2. **LRU eviction**: Up to 3 cached models; evicts the least recently used when the limit is reached.
3. **GPU support with fallback**: Attempts TensorRT first (via reflection), then CUDA, then falls back to CPU.
4. **Label loading priority**: ONNX metadata names > explicit `LabelsPath` > `labels.txt` next to model > bundled labels. Mismatch between metadata and external labels is logged but does not block execution.
5. **Output tensor selection**: When label count is known, matches tensors where `features = numClasses + 4` or `numClasses + 5`; otherwise uses a heuristic score.
6. **Internal NMS toggle**: `EnableInternalNms=false` emits raw confidence-filtered candidates for downstream `BoxNms`; preview images still apply visual-only NMS.
7. **Image depth normalization**: Automatically converts CV_16U/CV_32F/CV_64F images to CV_8U, supporting float images in `[0,1]`, `[0,255]`, or `[0,65535]` ranges.
8. **Model catalog resolution**: Supports `ModelId + ModelCatalogPath` to resolve model paths from `models/model_catalog.json`.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像
2. `GetStringParam / GetFloatParam / GetIntParam / GetBoolParam` -- 读取参数
3. `ResolveModelTarget(@operator)` -- 解析模型路径（显式路径或目录）
4. `ModelCatalog.ResolveExplicitOrCatalog(...)` -- 模型目录解析
5. `AcquireModelSessionWithVerifiedExecutionProviderAsync(...)` -- 加载/获取缓存模型
   - `TryAppendTensorRtExecutionProvider(...)` -- TensorRT 反射调用
   - `SessionOptions.AppendExecutionProvider_CUDA(...)` -- CUDA 提供方
   - `InferenceSession(...)` -- 创建会话
   - `EvictModelsIfNeeded()` -- LRU 驱逐
6. `ResolveLabelContract(...)` -- 标签契约解析
   - `DeepLearningLabelResolver.GetMetadataLabels(session)` -- 从 ONNX metadata 读取标签
   - `LoadExternalLabels(...)` -- 从文件加载标签
7. `PreprocessImage(src, inputSize)` -- 预处理
   - `NormalizeToBgr8(src)` -- 深度/通道归一化
   - `Cv2.Resize(...)` -- 保持宽高比缩放
   - letterbox 填充 `Scalar(114,114,114)`
   - BGR -> RGB CHW 转换
8. `RunInference(session, inputTensor, knownLabelCount)` -- 推理
   - `NamedOnnxValue.CreateFromTensor(...)`
   - `SelectDetectionOutputIndex(...)` -- 输出张量选择
9. `DetectYoloVersion(outputTensor, labels.Length)` -- 自动版本检测（当 `ModelVersion=Auto`）
10. `PostprocessResultsWithDiagnostics(...)` -- 后处理
    - `PostprocessYoloV5V6(...)` 或 `PostprocessYoloV8V11(...)`
    - `ApplyNMS(detections, nmsIouThreshold)` -- 带空间网格加速的同类别 NMS
11. `BuildVisualizationDetections(...)` -- 可视化候选构建
12. `DrawResults(src, detections, labels, detectionMode)` -- 绘制结果
13. `CreateImageOutput(outputImage, additionalData)` -- 构建输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ModelPath` | `file` | `""` | 文件路径 | ONNX 模型路径。与 `ModelId` 二选一，均为空则执行失败。 |
| `ModelId` | `string` | `""` | - | 模型目录中的模型标识，配合 `ModelCatalogPath` 使用。 |
| `ModelCatalogPath` | `file` | `""` | - | 模型目录 JSON 路径。 |
| `Confidence` | `double` | `0.5` | `[0.0, 1.0]` | 置信度阈值。后处理时低于该阈值的候选会被过滤。 |
| `ModelVersion` | `enum` | `Auto` | `Auto` / `YOLOv5` / `YOLOv6` / `YOLOv8` / `YOLOv11` | YOLO 版本。`Auto` 时根据输出张量维度自动判断。 |
| `InputSize` | `int` | `640` | `[320, 1280]` | 模型输入尺寸，影响预处理和推理成本。 |
| `UseGpu` | `bool` | `true` | `true` / `false` | 是否尝试启用 GPU 推理。GPU 不可用时自动回退 CPU。 |
| `GpuDeviceId` | `int` | `0` | `[0, 15]` | GPU 设备编号。 |
| `TargetClasses` | `string` | `""` | 逗号分隔类别名或 ID | 只保留指定类别；为空表示不过滤。 |
| `LabelsPath` | `file` | `""` | 文件路径 | 自定义标签文件路径。优先级：ONNX metadata > LabelsPath > labels.txt > 内置标签。 |
| `EnableInternalNms` | `bool` | `true` | `true` / `false` | 关闭后输出置信度筛选后的候选框，由下游 `BoxNms` 负责唯一 NMS。 |
| `NmsIouThreshold` | `double` | `0.45` | `[0.0, 1.0]` | 内部 NMS 与预览 NMS 使用的 IoU 阈值。 |
| `DetectionMode` | `enum` | `Defect` | `Defect` / `Object` | 缺陷检测：检出目标视为缺陷(NG)；目标检测：检出目标视为正常(OK)。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 待推理图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 检测结果图，绘制框、标签和统计文字。 |
| `OriginalImage` | 原始图像 | `Image` | 不带任何绘制的原始图像，供下游重新绘制。 |
| `DetectionList` | 检测列表 | `DetectionList` | 统一的检测结果列表。 |
| `Defects` | 缺陷列表 | `DetectionList` | `DetectionMode=Defect` 时输出有效，否则为空列表。 |
| `DefectCount` | 缺陷数量 | `Integer` | `DetectionMode=Defect` 时为检测数量，否则为 0。 |
| `Objects` | 目标列表 | `DetectionList` | `DetectionMode=Object` 时输出有效，否则为空列表。 |
| `ObjectCount` | 目标数量 | `Integer` | `DetectionMode=Object` 时为检测数量，否则为 0。 |
| `ResolvedModelPath` | Resolved Model Path | `String` | 实际使用的模型路径。 |
| `ResolvedModelId` | Resolved Model Id | `String` | 实际使用的模型 ID。 |
| `ResolvedModelCatalogPath` | Resolved Model Catalog Path | `String` | 实际使用的模型目录路径。 |
| `ModelSource` | Model Source | `String` | 模型来源：显式路径或目录解析。 |
| `ModelProvenance` | Model Provenance | `Any` | 模型来源、目录条目和解析细节的完整溯源信息。 |

### 运行时附加输出 / Runtime Additional Outputs
以下字段通过 `additionalData` 字典输出，不是 `[OutputPort]` 声明但可通过输出字典访问：

| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `DetectionMode` | `String` | 当前使用的检测模式。 |
| `InternalNmsEnabled` | `Boolean` | 内部 NMS 是否启用。 |
| `NmsIouThreshold` | `Float` | 实际使用的 NMS IoU 阈值。 |
| `RawCandidateCount` | `Integer` | NMS 前原始候选数量。 |
| `PostprocessDiagnostics` | `Any` | 后处理诊断信息（候选数、IoU 比较次数等）。 |
| `VisualizationDetectionCount` | `Integer` | 可视化绘制的检测数量。 |
| `LabelSource` | `String` | 标签来源（ModelMetadata / ExplicitFile / ModelDirectoryFile / BundledFile / Unavailable）。 |
| `ResolvedLabels` | `Array` | 实际使用的标签数组。 |
| `ModelMetadataLabels` | `Array` | 从 ONNX metadata 读取的标签。 |
| `LabelValidationStatus` | `String` | 标签验证状态（MetadataOnly / Mismatch / MissingLabelContract 等）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要由模型推理复杂度主导。预处理为 `O(InputSize^2)`，后处理 NMS 使用空间网格加速，IoU 比较次数远低于暴力 `O(N^2)`。 |
| 典型耗时 (Typical Latency) | 与模型大小、`InputSize`、CPU/GPU 环境、标签数和检测框数量强相关。命中模型缓存时会明显快于首次加载。`DeepLearning_runtime_benchmark_baseline.md` 记录 20/20 passed，覆盖 1080p、4K、CPU fallback 和 1080p x4 batch 的预处理/YOLO 后处理路径。 |
| 内存特征 (Memory Profile) | 除图像和输出图外，还包含输入张量（`float[1*3*InputSize*InputSize]`）、输出张量、静态模型缓存（最多 3 个带引用计数的会话）以及检测结果列表。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：外观复杂、规则算法难以稳定覆盖的检测、识别和缺陷筛查任务。
- **适合 (Suitable)**：需要同一算子支持多种 YOLO 版本（v5/v6/v8/v11）和标签文件切换的流程。
- **适合 (Suitable)**：对模型切换频繁但希望复用推理会话的工程场景（模型缓存 + LRU 驱逐）。
- **适合 (Suitable)**：需要将检测结果同时输出为缺陷列表和目标列表的灵活判定流程。
- **不适合 (Not Suitable)**：模型文件、标签文件和版本配置不明确的流程。
- **不适合 (Not Suitable)**：把 `DetectionMode` 只当展示选项，而不理解其会改变输出字段集合的场景。
- **不适合 (Not Suitable)**：对严格实时性和显存可预测性要求很高，但又频繁切换大量模型的场景（缓存上限 3）。

## 已知限制 / Known Limitations
1. `UseGpu` 和 `GpuDeviceId` 在源码中实际生效，但未通过 `[OperatorParam]` 元数据声明；如果只看参数面板，容易误以为不支持 GPU 开关。
2. `ModelVersion=Auto` 的版本识别基于输出张量维度启发式判断，适合常见 YOLO 导出格式，但不保证覆盖所有非标准模型。
3. `DetectionMode` 会改变运行时实际输出字段：对象模式输出 `Objects/ObjectCount`，缺陷模式输出 `Defects/DefectCount`；集成时不能假定两组字段总是同时存在。
4. 当前 NMS 使用空间网格加速的同类别框抑制；当 `EnableInternalNms=true` 时，`NmsIouThreshold` 对所有类别统一适用，不支持按类别配置不同阈值。
5. 标签契约验证：当 ONNX metadata labels 与外部 labels.txt 不匹配时，算子会记录 Mismatch 状态但仍然使用 metadata labels，不会阻断流程。
6. 文档审计勘误已确认此前关于本算子"ArrayPool 张量踩踏"和"ONNX 张量泄漏"的两个严重结论属于误报；但这不代表模型推理的性能和稳定性可脱离现场环境单独保证。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增 ModelId/ModelCatalogPath 模型目录解析、EnableInternalNms/NmsIouThreshold 可控 NMS、TensorRT 反射调用、标签契约验证、图像深度归一化、引用计数模型会话等实现细节说明；统一五列参数表；完整运行时附加输出文档 |
| 1.0.3 | 2026-04-28 | 回写 26/26 contract、36/36 dataset protocol bridge、20/20 runtime benchmark 和失败契约说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码补充 YOLO 版本判别、模型缓存/GPU 隐含参数、输出模式切换与预处理细节 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
