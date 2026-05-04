# OCR 识别 / OcrRecognition

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `OcrRecognitionOperator` |
| 枚举值 (Enum) | `OperatorType.OcrRecognition` |
| 分类 (Category) | 识别 |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 图标 (Icon) | `text-recognition` |

## 算法原理 / Algorithm Principle
**中文：**
该算子基于 PaddleOCRSharp 引擎实现图像中的文本识别（OCR），核心流程如下：

1. **图像编码**：将输入的 OpenCV `Mat` 通过 `Cv2.ImEncode(".jpg", ...)` 编码为 JPEG 格式的字节数组。选择 JPEG 编码是为了兼容 PaddleOCRSharp 引擎的输入格式要求。
2. **引擎调用**：通过注入的 `OcrEngineProvider`（全局单例）调用 `DetectText(imageBytes)` 方法，传入编码后的图像字节数组。`OcrEngineProvider` 封装了 PaddleOCR 的模型加载和推理逻辑，算子本身不直接管理模型生命周期。
3. **结果提取**：从 OCR 结果中提取 `Text` 字段（识别到的全部文本内容），若结果为 null 则返回空字符串。
4. **异常处理**：整个识别过程包裹在 try-catch 中，捕获所有异常并返回失败结果，日志记录通过 `Logger.LogError` 输出。

**English:**
This operator implements OCR (Optical Character Recognition) based on PaddleOCRSharp. The pipeline encodes the input `Mat` to JPEG bytes via `Cv2.ImEncode`, passes them to the singleton `OcrEngineProvider.DetectText()`, and extracts the recognized text. The operator delegates model management entirely to `OcrEngineProvider` and handles all exceptions with logging.

## 实现策略 / Implementation Strategy
- **中文：** 算子设计极简，遵循统一算子框架。核心逻辑仅包含输入校验、JPEG 编码、引擎调用和结果提取四个步骤。OCR 引擎的生命周期完全委托给 `OcrEngineProvider` 服务（通过 DI 注入），算子不持有模型状态，支持多算子实例共享同一引擎。`ValidateParameters` 方法检查可选的 `ModelPath` 参数是否存在（支持文件路径或目录路径），但该参数不在 `[OperatorParam]` 元数据中声明，属于隐式参数。输出仅包含识别文本和成功标志，不输出可视化结果图像。
- **English:** The operator is minimally designed following the standard framework. Core logic involves input validation, JPEG encoding, engine invocation, and result extraction. OCR engine lifecycle is fully delegated to the injected `OcrEngineProvider` singleton. `ValidateParameters` checks an optional `ModelPath` parameter (not declared in `[OperatorParam]` metadata). Output includes only recognized text and success flag, with no visualization image.

## 核心 API 调用链 / Core API Call Chain
```
TryGetInputImage(inputs, out imageWrapper)
  -> imageWrapper.MatReadOnly                      // 获取只读 Mat
  -> Cv2.ImEncode(".jpg", mat, out imageBytes)     // JPEG 编码
  -> _ocrEngineProvider.DetectText(imageBytes)     // PaddleOCR 引擎推理
  -> ocrResult?.Text ?? string.Empty               // 提取识别文本
  -> OperatorExecutionOutput.Success({Text, IsSuccess})
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| *(无声明参数)* | - | - | - | 该算子在 `[OperatorParam]` 元数据中未声明任何运行时参数。`ValidateParameters` 中检查的 `ModelPath` 为隐式参数，用于指定 OCR 模型路径（支持文件或目录），不存在时使用默认模型。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 输入待处理图像，内部通过 JPEG 编码传递给 OCR 引擎 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Text` | 识别文本 | `String` | OCR 识别到的全部文本内容，未识别到时为空字符串 |
| `IsSuccess` | 成功 | `Boolean` | 识别是否成功，成功时为 true，异常时为 false |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| *(无)* | - | 该算子不输出运行时附加字段，不输出结果图像、Width/Height 等 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要由 PaddleOCR 模型推理复杂度主导，与图像中文字区域的数量和面积相关 |
| 典型耗时 (Typical Latency) | 无专用 benchmark；首次调用需加载模型（冷启动可能 >1s）；后续调用通常 50-200ms (1080p)，取决于文字区域复杂度 |
| 内存特征 (Memory Profile) | 模型内存由 `OcrEngineProvider` 单例持有（PaddleOCR 模型通常 ~100-200MB）；算子本身的内存开销为 JPEG 编码缓冲区，较小 |

## 适用场景 / Use Cases
- **适合 (Suitable)：**
  - 工业场景中的文字识别（产品编号、日期、批号等）
  - 文档、标签、铭牌上的文本提取
  - 需要快速集成 OCR 能力而无需管理模型生命周期的场景
  - 中英文混合文本识别（PaddleOCR 支持多语言）
- **不适合 (Not Suitable)：**
  - 需要文本位置信息（坐标、边界框）的场景（当前仅输出文本内容）
  - 需要逐字符或逐行识别结果的场景（当前输出整体文本）
  - 需要可视化标注的场景（不输出结果图像）
  - 手写体或艺术字体识别（PaddleOCR 对印刷体效果更好）
  - 超高分辨率图像的文字识别（JPEG 编码可能引入压缩伪影）

## 已知限制 / Known Limitations
1. 不输出结果图像和文本位置信息，下游无法知道文字在图像中的具体位置。
2. 图像通过 JPEG 编码传递给 OCR 引擎，JPEG 压缩可能引入伪影，影响低对比度或小字体的识别精度。
3. `ModelPath` 参数在 `ValidateParameters` 中检查但未在 `[OperatorParam]` 元数据中声明，前端 UI 不会显示该参数的配置入口。
4. 异常捕获范围较广（`catch (Exception ex)`），所有异常统一返回失败，无法区分模型加载失败、内存不足等具体错误类型。
5. `OcrEngineProvider` 为全局单例，多算子并发调用时的线程安全性取决于引擎实现。
6. 输出的 `Text` 为引擎返回的原始文本，未做后处理（如去空格、合并行等）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写算子名片：基于源码提取 PaddleOCRSharp 引擎调用流程、JPEG 编码策略、OcrEngineProvider 单例架构、隐式 ModelPath 参数、输入/输出端口完整语义与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
