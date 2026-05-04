# 条码识别 / CodeRecognition

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CodeRecognitionOperator` |
| 枚举值 (Enum) | `OperatorType.CodeRecognition` |
| 分类 (Category) | 识别 |
| 版本 (Version) | `2.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 图标 (Icon) | `barcode` |
| 关键词 (Keywords) | `条码`, `二维码`, `扫码`, `识别`, `QR`, `读取`, `Barcode`, `Decode`, `Read code` |
| 平台限制 | Windows 6.1+ (`[SupportedOSPlatform("windows6.1")]`) |

## 算法原理 / Algorithm Principle
**中文：**
该算子基于 ZXing.NET 开源库实现多格式条码识别，核心流程如下：

1. **预处理**：将输入图像转为单通道灰度图（支持 1/3/4 通道输入），使用 `Marshal.Copy` 将灰度数据复制到托管字节数组。
2. **内存管理**：使用 `ArrayPool<byte>.Shared.Rent` 租借解码缓冲区，避免大图像触发 LOH（Large Object Heap）分配；解码完成后通过 `ArrayPool.Return` 归还。
3. **LuminanceSource 构建**：以灰度字节数组和图像尺寸构建 `RGBLuminanceSource`（`BitmapFormat.Gray8`），再通过 `GlobalHistogramBinarizer` 二值化为 `BinaryBitmap`。
4. **解码配置**：设置解码提示（hints）包括 `TRY_HARDER`（深度搜索）、`ALSO_INVERTED`（同时尝试反色）、`POSSIBLE_FORMATS`（限定码制类型）。
5. **多码解码**：使用 `ZXing.Multi.GenericMultipleBarcodeReader` 包装 `MultiFormatReader`，调用 `decodeMultiple` 一次性识别图像中的多个条码。
6. **结果绘制**：对每个识别结果，在结果图像上用绿色折线绘制 `ResultPoints`（定位点连线），并收集文本、格式、坐标等信息。
7. **格式映射**：`CodeType` 参数通过 `GetBarcodeFormats` 映射为 ZXing `BarcodeFormat` 列表，"All" 模式启用 QR_CODE、CODE_128、DATA_MATRIX、EAN_13、CODE_39、EAN_8、UPC_A、UPC_E、CODABAR、ITF、AZTEC、PDF_417 共 12 种格式。

**English:**
This operator implements multi-format barcode recognition based on ZXing.NET. The pipeline converts the input image to grayscale, builds a `RGBLuminanceSource` with `GlobalHistogramBinarizer`, and uses `GenericMultipleBarcodeReader.decodeMultiple` with `TRY_HARDER` and `ALSO_INVERTED` hints to detect multiple barcodes in a single pass. The `CodeType` parameter maps to ZXing `BarcodeFormat` lists, with "All" enabling 12 formats. Detection results include text, format, and result points for visualization.

## 实现策略 / Implementation Strategy
- **中文：** 算子遵循统一算子框架，先校验输入图像和平台兼容性（仅 Windows 6.1+），再进入核心解码流程。灰度转换独立封装在 `ConvertToGray` 方法中，支持 1/3/4 通道输入，其他通道数直接报错。结果图像通过 `CreateResultImage` 确保为 3 通道 BGR（灰度/BGRA 输入会先转换）。使用 `ArrayPool<byte>.Shared` 管理解码缓冲区，在 `finally` 块中确保归还，避免内存泄漏。ZXing 的 `ReaderException` 在未找到条码时被捕获并静默忽略，属于正常流程。最终结果通过 `CreateImageOutput` 封装，同时输出主要条码文本和完整的结果列表。
- **English:** The operator follows the standard framework, validating input and platform compatibility (Windows 6.1+ only) before decoding. Grayscale conversion is encapsulated in `ConvertToGray`. The result image is ensured to be 3-channel BGR via `CreateResultImage`. `ArrayPool<byte>.Shared` manages the decode buffer with guaranteed return in `finally`. ZXing's `ReaderException` is caught and silently ignored when no barcode is found. Results are packaged via `CreateImageOutput` with both primary text and full result list.

## 核心 API 调用链 / Core API Call Chain
```
TryGetInputImage(inputs, out imageWrapper)
  -> OperatingSystem.IsWindowsVersionAtLeast(6, 1)     // 平台检查
  -> GetStringParam("CodeType", "All")                  // 码制类型
  -> GetIntParam("MaxResults", 10, 1, 100)              // 最大结果数
  -> imageWrapper.GetMat()
  -> ConvertToGray(src, out error)                      // 1/3/4 通道 -> 灰度
       -> Cv2.CvtColor(BGR2GRAY / BGRA2GRAY / CopyTo)
  -> CreateResultImage(src)                             // 确保 3 通道 BGR
  -> ArrayPool<byte>.Shared.Rent(width * height)        // 租借解码缓冲
  -> Marshal.Copy(gray.Data, luminance, 0, size)        // 灰度数据 -> 托管数组
  -> new RGBLuminanceSource(luminance, w, h, Gray8)     // 构建亮度源
  -> new GlobalHistogramBinarizer(source)                // 二值化
  -> new BinaryBitmap(binarizer)
  -> GetBarcodeFormats(codeType)                        // 码制 -> BarcodeFormat 列表
  -> new MultiFormatReader()
  -> new GenericMultipleBarcodeReader(reader)
  -> multiReader.decodeMultiple(binaryBitmap, hints)    // 多码解码
       hints: TRY_HARDER, ALSO_INVERTED, POSSIBLE_FORMATS
  -> [遍历结果]:
       Cv2.Line(resultImage, pt1, pt2, green, 2)       // 绘制定位点连线
       收集 {Index, Text, Format, Points}
  -> ArrayPool<byte>.Shared.Return(luminance)           // 归还缓冲
  -> CreateImageOutput(resultImage, additionalData)
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `CodeType` | `enum` | `All` | All/全部, QR/QR码, Code128/Code128, DataMatrix/DataMatrix, EAN13/EAN-13, Code39/Code39 | 码制类型。All 模式启用 12 种格式（QR、Code128、DataMatrix、EAN-13、Code39、EAN-8、UPC-A、UPC-E、Codabar、ITF、Aztec、PDF417） |
| `MaxResults` | `int` | `10` | [1, 100] | 单次识别的最大结果数量上限 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 输入待处理图像，支持灰度/BGR/BGRA |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 绘制了条码定位点连线的结果图 |
| `Text` | 识别内容 | `String` | 第一个识别到的条码文本内容，未识别到时为空字符串 |
| `CodeCount` | 识别数量 | `Integer` | 本次识别到的条码总数 |
| `CodeType` | 条码类型 | `String` | 第一个识别到的条码格式名称（如 QR_CODE、CODE_128） |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度 |
| `Height` | `Integer` | 输出图像高度 |
| `CodeResults` | `List<Dictionary>` | 完整结果列表，每项含 Index、Text、Format、Points |
| `ResultCount` | `Integer` | 识别结果数量（等同 CodeCount） |
| `Codes` | `List<Dictionary>` | 完整结果列表（等同 CodeResults） |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 主要由 ZXing 解码算法主导；TRY_HARDER 模式下会尝试多次扫描和旋转，复杂度高于普通模式 |
| 典型耗时 (Typical Latency) | 无专用 benchmark；单码识别通常 <50ms (1080p)；多码或 TRY_HARDER 模式下耗时显著增加 |
| 内存特征 (Memory Profile) | 使用 ArrayPool 租借解码缓冲区避免 LOH 分配；灰度副本 + 结果图像副本为主要内存开销，峰值约 2 倍输入图像大小 |

## 适用场景 / Use Cases
- **适合 (Suitable)：**
  - 工业产线上的条码/二维码自动读取和校验
  - 包装、标签上的多种码制混合识别（All 模式）
  - 需要同时识别图像中多个条码的场景（多码解码）
  - 特定码制的快速识别（指定 CodeType 可减少误识别和加速）
- **不适合 (Not Suitable)：**
  - 非 Windows 平台（算子标记了 `[SupportedOSPlatform("windows6.1")]`）
  - 严重模糊、变形或低对比度的条码（ZXing 对图像质量有一定要求）
  - 需要亚像素级定位精度的场景（ResultPoints 为像素级坐标）
  - 高密度 DataMatrix 或微小条码（可能需要预处理增强）

## 已知限制 / Known Limitations
1. 平台限制：标记了 `[SupportedOSPlatform("windows6.1")]`，非 Windows 6.1+ 平台运行时会返回失败。
2. 灰度转换仅支持 1/3/4 通道输入，其他通道数（如 2 通道）会返回错误。
3. ZXing 的 `ReaderException` 被静默捕获，未找到条码时不会报错，但也不会提供详细的失败原因。
4. `MatToBitmap` 方法（通过 PNG 编解码）标记为兼容保留接口，当前主流程不使用。
5. 结果图像上的定位点连线仅在 `ResultPoints.Length >= 2` 时绘制，单点结果不绘制。
6. All 模式启用 12 种格式可能增加误识别概率，建议在已知码制时指定具体类型。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写算子名片：基于源码提取完整解码流程（ZXing.NET + ArrayPool + GenericMultipleBarcodeReader）、2 个参数的详细说明、4 个输出端口 + 5 个运行时附加输出的语义、平台限制与多码解码机制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
