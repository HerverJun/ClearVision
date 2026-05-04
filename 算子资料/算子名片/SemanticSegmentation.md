# 语义分割 / SemanticSegmentation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SemanticSegmentationOperator` |
| 枚举值 (Enum) | `OperatorType.SemanticSegmentation` |
| 分类 (Category) | AI检测 |
| 显示名 (DisplayName) | 语义分割 |
| 图标 (Icon) | `semantic-segmentation` |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 关键词 (Keywords) | `semantic segmentation`, `segmentation`, `onnx`, `mask`, `语义分割` |

## 算法原理 / Algorithm Principle

**中文：** 该算子通过 ONNX Runtime 执行语义分割模型。整体流程为：将输入图像预处理为模型张量（支持 RGB/BGR 通道顺序、可选归一化到 0-1、均值/标准差归一化、灰度图自动提升为三通道），执行 ONNX 推理获取单个输出张量，根据输出形状自动推断通道布局（channels-first `[1, C, H, W]` 或 channels-last `[1, H, W, C]`），对每个像素沿类别维度做 argmax 得到类别索引图，最后用最近邻插值缩放回原始尺寸，生成着色预览图和逐类别二值掩膜。

着色方案使用 HSV 色轮映射（`hue = (classId * 53) % 180`），保证不同类别颜色稳定可区分。

**English:** This operator runs semantic segmentation models via ONNX Runtime. The pipeline preprocesses the input image into a model tensor (with RGB/BGR channel order, optional unit-range scaling, mean/std normalization, and automatic grayscale-to-BGR promotion), executes ONNX inference to obtain a single output tensor, infers the channel layout from the output shape (channels-first `[1, C, H, W]` or channels-last `[1, H, W, C]`), performs per-pixel argmax along the class dimension to produce a class index map, then resizes back to the original resolution via nearest-neighbor interpolation, generating a colored preview map and per-class binary masks.

The coloring scheme uses HSV hue mapping (`hue = (classId * 53) % 180`) to ensure stable, distinguishable colors across classes.

## 实现策略 / Implementation Strategy

**中文：** 源码中的关键实现策略：

1. **模型解析**：支持 `ModelPath` 直接加载 ONNX，也支持通过 `ModelId + ModelCatalogPath` 从 `models/model_catalog.json` 解析（支持 `segmentation` 类型）。
2. **目录回填**：当 `InputSize` 仍为默认值 `512,512` 且模型目录条目有 `InputSize` 时，优先使用目录值。`NumClasses`（默认 21）和 `ClassNames` 同理。
3. **Session 缓存**：使用静态 `ConcurrentDictionary<string, InferenceSession>` 缓存 ONNX 会话，缓存键为 `resolvedModelPath|effectiveProvider`，带 `SemaphoreSlim` 并发安全。
4. **GPU 回退**：`ExecutionProvider=cuda` 时检查 `GpuAvailabilityChecker.IsCudaAvailable`，不可用则回退到 CPU。
5. **输出张量解析**：自动检测 4D 输出的通道维度位置（`dims[1] == numClasses` 为 channels-first，`dims[3] == numClasses` 为 channels-last），不匹配时抛出异常。
6. **类别数适配**：`numClasses <= 255` 时使用 `CV_8UC1` 类别图，否则使用 `CV_16UC1`。
7. **ClassNames 解析**：支持 JSON 数组格式 `["bg","cat","dog"]` 和逗号分隔格式 `bg,cat,dog`。数量不足时自动补全 `class_N` 格式。

**English:** Key implementation strategies:

1. **Model resolution**: Supports direct `ModelPath` ONNX loading and `ModelId + ModelCatalogPath` catalog resolution (supports `segmentation` type).
2. **Catalog backfill**: When `InputSize` is still the default `512,512` and the catalog entry has `InputSize`, the catalog value takes priority. Same for `NumClasses` (default 21) and `ClassNames`.
3. **Session caching**: Static `ConcurrentDictionary<string, InferenceSession>` caches ONNX sessions keyed by `resolvedModelPath|effectiveProvider` with `SemaphoreSlim` for concurrency safety.
4. **GPU fallback**: When `ExecutionProvider=cuda`, checks `GpuAvailabilityChecker.IsCudaAvailable`; falls back to CPU if unavailable.
5. **Output tensor parsing**: Automatically detects the class dimension position in 4D output (`dims[1] == numClasses` for channels-first, `dims[3] == numClasses` for channels-last); throws if neither matches.
6. **Class count adaptation**: Uses `CV_8UC1` class map when `numClasses <= 255`, otherwise `CV_16UC1`.
7. **ClassNames parsing**: Supports JSON array `["bg","cat","dog"]` and comma-separated `bg,cat,dog` formats. Auto-pads with `class_N` format when count is insufficient.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像
2. `ResolveModelTarget(@operator)` -- 解析模型路径
   - `ModelCatalog.ResolveExplicitOrCatalog(...)` -- 模型目录解析
3. `GetStringParam / GetIntParam / GetBoolParam` -- 读取参数
4. `ShouldUseCatalogInputSize(...)` -- 判断是否回填目录输入尺寸
5. `TryParseSize(effectiveInputSize, ...)` -- 解析输入尺寸
6. `ResolveNumClasses(@operator, modelCatalogEntry)` -- 解析类别数（目录回填）
7. `TryParseFloatTriplet(Mean/Std, ...)` -- 解析均值/标准差
8. `ResolveClassNames(@operator, modelCatalogEntry, numClasses)` -- 解析类别名
9. `GetOrCreateSessionAsync(modelPath, executionProvider, ...)` -- 获取/创建 ONNX 会话
10. `ExecuteSegmentation(session, src, ...)` -- 执行分割
    - `PreprocessImage(...)` -- 预处理（resize、通道转换、归一化、CHW 张量构建）
    - `session.Run(...)` -- ONNX 推理
    - argmax 类别图构建（channels-first / channels-last 自适应）
    - `Cv2.Resize(...)` -- 最近邻缩放回原始尺寸
    - `BuildColoredMap(...)` -- HSV 着色
    - `BuildClassMasks(...)` -- 逐类别掩膜
11. `CreateImageOutput / OperatorExecutionOutput.Success(...)` -- 构建输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ModelId` | `string` | `""` | - | 模型目录中的模型标识。支持 `segmentation` 类型。 |
| `ModelCatalogPath` | `file` | `""` | - | 模型目录 JSON 路径。 |
| `ModelPath` | `file` | `""` | 文件路径 | 显式 ONNX 模型路径。优先级高于 ModelId 目录解析。 |
| `InputSize` | `string` | `512,512` | `width,height` 格式 | 模型输入尺寸。默认值可由模型目录回填。 |
| `NumClasses` | `int` | `21` | `[2, 4096]` | 类别数量。默认值可由模型目录回填。 |
| `ClassNames` | `string` | `""` | JSON array 或逗号分隔 | 类别名称。为空时生成兜底类别名 `class_N`。 |
| `ExecutionProvider` | `enum` | `cpu` | `cpu` / `cuda` | ONNX Runtime 执行提供方。CUDA 不可用时自动回退 CPU。 |
| `ScaleToUnitRange` | `bool` | `true` | `true` / `false` | 是否将像素值归一化到 [0, 1]。 |
| `ChannelOrder` | `enum` | `RGB` | `RGB` / `BGR` | 输入给模型的通道顺序。 |
| `Mean` | `string` | `0,0,0` | 三个数值，逗号分隔 | 均值归一化参数（R,G,B 或 B,G,R 顺序由 ChannelOrder 决定）。 |
| `Std` | `string` | `1,1,1` | 三个正数，逗号分隔 | 标准差归一化参数。任一值 <= 0 会执行失败。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待分割图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SegmentationMap` | Segmentation Map | `Image` | 类别索引图（单通道，像素值为类别 ID）。 |
| `ColoredMap` | Colored Map | `Image` | 按 HSV 色轮着色的可视化图。 |
| `ClassMasks` | Class Masks | `Any` | 每个出现类别对应的二值掩膜字典（`Dictionary<string, Mat>`）。 |
| `ClassCount` | Class Count | `Integer` | 当前图中出现的类别数量。 |
| `PresentClasses` | Present Classes | `Any` | 当前图中出现的类别名列表（`string[]`）。 |
| `ResolvedModelPath` | Resolved Model Path | `String` | 实际使用的模型路径。 |
| `ResolvedModelId` | Resolved Model Id | `String` | 实际使用的模型 ID。 |
| `ResolvedModelCatalogPath` | Resolved Model Catalog Path | `String` | 实际使用的模型目录路径。 |
| `ModelSource` | Model Source | `String` | 模型来源：`ExplicitPath` 或 `ModelCatalog`。 |
| `ModelProvenance` | Model Provenance | `Any` | 模型来源、目录条目和解析细节。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 预处理和后处理约为 `O(W * H * C)`；ONNX 推理成本由模型结构、输入尺寸和 provider 决定。着色和掩膜生成为 `O(W * H * presentClasses)`。 |
| 典型耗时 (Typical Latency) | `SemanticSegmentation_contract_baseline.md` 记录 27/27 passed，总运行约 134 ms；`SemanticSegmentation_dataset_baseline.md` 记录 36/36 passed，总运行约 725 ms。报告延迟来自 repo-local identity/protocol bridge，不等同于 1920x1080 生产模型耗时。 |
| 内存特征 (Memory Profile) | 包含输入张量（`float[1, 3, H, W]`）、ONNX 输出张量、类别索引图（uint8 或 uint16）、着色图（3ch uint8）、逐类 mask 和静态 session cache。内存随输入尺寸、类别数和输出掩膜数量增长。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：工件区域前景/背景分离、涂胶范围检查、语义区域裁切、后续 ROI 联动和可视化复核。
- **适合 (Suitable)**：需要通过模型目录统一管理分割模型、类别名、输入尺寸和版本来源的流程。
- **适合 (Suitable)**：需要逐类别二值掩膜作为下游算子输入的流水线。
- **不适合 (Not Suitable)**：需要实例级目标分离、目标 ID 跟踪或复杂后处理的场景；这类需求应使用实例分割或检测模型。
- **不适合 (Not Suitable)**：多分支输出的复杂分割模型（当前实现以单输出张量为主）。

## 已知限制 / Known Limitations
1. 当前实现以单输出张量的常见语义分割模型为主，复杂多分支输出需要在模型侧或后续算子中对齐。
2. CUDA 路径依赖部署环境安装对应 ONNX Runtime CUDA 运行时；仓库默认包仍以 CPU 运行时为主。
3. 着色方案使用固定 HSV 色轮映射，当类别数极大时（接近 4096 上限），颜色区分度会下降。
4. 现有 dataset baseline 是协议桥证据，不代表现场材质、光照、相机和生产模型上的真实 mIoU。
5. `InputSize`、`NumClasses`、`ClassNames` 的目录回填仅在参数值等于默认值时生效；一旦用户显式设置，目录值不再覆盖。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增 Session 缓存机制、目录回填逻辑、channels-first/last 自适应、GPU 回退、ClassNames 解析详情；统一五列参数表；补全英文算法原理 |
| 1.0.1 | 2026-04-28 | 补充 contract/dataset evidence、性能口径、模型来源输出和失败契约说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
