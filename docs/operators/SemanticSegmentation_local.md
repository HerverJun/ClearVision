# 语义分割 / SemanticSegmentation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SemanticSegmentationOperator` |
| 枚举值 (Enum) | `OperatorType.SemanticSegmentation` |
| 分类 (Category) | AI检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子通过 ONNX Runtime 执行语义分割模型，将输入图像预处理为模型张量，读取单个分割输出张量并按类别维度做 argmax，最终输出类别图、着色预览图和逐类别掩膜。

> English: Runs an ONNX semantic segmentation model and returns a class map, colored visualization, and per-class masks.

## 实现策略 / Implementation Strategy
- 支持 `ModelPath` 直接加载 ONNX，也支持通过 `ModelId + ModelCatalogPath` 从 `models/model_catalog.json` 解析模型仓库。
- 当 `InputSize`、`NumClasses`、`ClassNames` 仍为默认值时，会优先回填模型目录里的输入尺寸、类别数和类别名。
- 预处理支持 `RGB/BGR` 通道顺序、`ScaleToUnitRange`、`Mean`、`Std`，灰度图会提升为三通道。
- `ExecutionProvider` 支持 `cpu/cuda`；CUDA 不可用时会回退到 CPU，避免仅因运行环境缺 CUDA 而直接中断流程。
- 输出除分割结果外，还会带出 `ResolvedModelPath`、`ResolvedModelId`、`ResolvedModelCatalogPath`、`ModelSource` 和 `ModelProvenance`，便于追踪模型来源。

## 核心 API 调用链 / Core API Call Chain
1. `SemanticSegmentationOperator.ExecuteCoreAsync`
2. `ResolveModelTarget(...)`
3. `ModelCatalog.ResolveExplicitOrCatalogPath(...)`
4. `GetOrCreateSessionAsync(...)`
5. `PreprocessToTensor(...)`
6. `InferenceSession.Run(...)`
7. `BuildColorizedMap(...)`
8. `BuildClassMasks(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ModelId` | `string` | `""` | - | 模型目录中的模型标识。 |
| `ModelCatalogPath` | `file` | `""` | - | 模型目录 JSON 路径。 |
| `ModelPath` | `file` | `""` | - | 显式 ONNX 模型路径，优先级高于目录解析。 |
| `InputSize` | `string` | `512,512` | `width,height` | 模型输入尺寸；默认值可由模型目录回填。 |
| `NumClasses` | `int` | `21` | `[2, 4096]` | 类别数量；默认值可由模型目录回填。 |
| `ClassNames` | `string` | `""` | JSON array 或逗号分隔 | 类别名称；为空时生成兜底类别名。 |
| `ExecutionProvider` | `enum` | `cpu` | `cpu/cuda` | ONNX Runtime 执行提供方。 |
| `ScaleToUnitRange` | `bool` | `true` | - | 是否将像素归一化到 0-1。 |
| `ChannelOrder` | `enum` | `RGB` | `RGB/BGR` | 输入给模型的通道顺序。 |
| `Mean` | `string` | `0,0,0` | 三个数值 | 均值归一化参数。 |
| `Std` | `string` | `1,1,1` | 三个正数 | 标准差归一化参数；任一值小于等于 0 会失败。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待分割图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `SegmentationMap` | Segmentation Map | `Image` | 类别索引图。 |
| `ColoredMap` | Colored Map | `Image` | 按稳定 palette 着色的可视化图。 |
| `ClassMasks` | Class Masks | `Any` | 每个出现类别对应的二值掩膜字典。 |
| `ClassCount` | Class Count | `Integer` | 当前图中出现的类别数量。 |
| `PresentClasses` | Present Classes | `Any` | 当前图中出现的类别名列表。 |
| `ResolvedModelPath` | Resolved Model Path | `String` | 实际使用的模型路径。 |
| `ResolvedModelId` | Resolved Model Id | `String` | 实际使用的模型 ID。 |
| `ResolvedModelCatalogPath` | Resolved Model Catalog Path | `String` | 实际使用的模型目录路径。 |
| `ModelSource` | Model Source | `String` | 模型来源：显式路径或目录解析。 |
| `ModelProvenance` | Model Provenance | `Any` | 模型来源、目录条目和解析细节。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 预处理、类别图构建、着色和掩膜生成约为 `O(W * H * C)`；ONNX 推理成本由模型结构、输入尺寸和 provider 决定。 |
| 典型耗时 (Typical Latency) | `SemanticSegmentation_contract_baseline.md` 记录 27/27 passed，总运行约 134 ms；`SemanticSegmentation_dataset_baseline.md` 记录 36/36 passed，总运行约 725 ms。报告里的延迟来自 repo-local identity/protocol bridge，不等同于 1920x1080 生产模型耗时。 |
| 内存特征 (Memory Profile) | 包含输入张量、ONNX 输出张量、类别索引图、着色图、逐类 mask 和静态 session cache；内存随输入尺寸、类别数和输出掩膜数量增长。 |

## 证据与失败契约 / Evidence & Failure Contracts
- Contract baseline：`quality/evals/reports/SemanticSegmentation_contract_baseline.md`，27/27 passed，覆盖 identity ONNX 模型、class map argmax、class mask、palette、模型目录解析、参数解析、预处理和失败路径。
- Dataset baseline：`quality/evals/reports/SemanticSegmentation_dataset_baseline.md`，36/36 passed，PixelAccuracy / MeanIoU / MeanDice / MeanBoundaryIoU 均为 1.0；它是 VOC-style 半合成协议桥，不声明真实生产模型精度。
- 失败契约包括缺失图像、缺失模型、错误 `InputSize`、错误 `Mean`、`Std <= 0`、非法 `ExecutionProvider` 和非法 class-name JSON。

## 适用场景 / Use Cases
- 适合：工件区域前景/背景分离、涂胶范围检查、语义区域裁切、后续 ROI 联动和可视化复核。
- 适合：需要通过模型目录统一管理分割模型、类别名、输入尺寸和版本来源的流程。
- 不适合：需要实例级目标分离、目标 ID 跟踪或复杂后处理的场景；这类需求应使用实例分割或检测模型。

## 已知限制 / Known Limitations
1. 当前实现以单输出张量的常见语义分割模型为主，复杂多分支输出需要在模型侧或后续算子中对齐。
2. CUDA 路径依赖部署环境安装对应 ONNX Runtime CUDA 运行时；仓库默认包仍以 CPU 运行时为主。
3. 现有 dataset baseline 是协议桥证据，不代表现场材质、光照、相机和生产模型上的真实 mIoU。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-28 | 补充 contract/dataset evidence、性能口径、模型来源输出和失败契约说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
