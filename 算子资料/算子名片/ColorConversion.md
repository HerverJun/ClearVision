# 颜色空间转换 / Color Conversion

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ColorConversionOperator` |
| 枚举值 (Enum) | `OperatorType.ColorConversion` |
| 分类 (Category) | 预处理 / 颜色空间 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
颜色空间转换是将图像从一种颜色表示方式转换为另一种的过程。常见的颜色空间及其用途：

- **BGR**：OpenCV 默认的三通道颜色空间（蓝-绿-红），对应显示器的 RGB 通道但顺序不同。
- **GRAY**：单通道灰度图，`Y = 0.299*R + 0.587*G + 0.114*B`（ITU-R BT.601 标准）。
- **HSV**：色调(H)-饱和度(S)-明度(V)，更适合颜色分析和分割，因为 H 通道对光照变化不敏感。
- **Lab**：亮度(L)-a-b，L 通道表示亮度，a/b 表示颜色信息。适合亮度独立处理。
- **YUV/YCrCb**：亮度(Y)-色度(U/V 或 Cr/Cb)，用于视频编码和亮度/色度分离处理。

转换过程中，不同颜色空间之间的映射关系由 CIE 色度学标准和 ITU 标准定义，OpenCV 内部使用标准查找表和矩阵运算实现。

> English: Color space conversion transforms pixel representation between BGR, Gray, HSV, Lab, YUV and other spaces using standard colorimetric formulas.

## 实现策略 / Implementation Strategy
当前实现封装 OpenCV 的 `Cv2.CvtColor`，通过字符串枚举映射到 `ColorConversionCodes`：

- `ConversionCode` 参数（如 `BGR2GRAY`）被映射到 OpenCV 的 `ColorConversionCodes` 枚举值。
- 实际支持的转换代码比元数据 `Options` 中列出的更多（15 种），包括 `BGR2RGB`、`BGR2RGBA`、`BGR2XYZ`、`BGR2HLS` 等。
- `SourceChannels` 参数用于记录源图像通道数，主要用于验证转换兼容性，但当前实现中未强制校验。
- 转换结果直接输出，不做额外的位深处理或通道调整。

与 Halcon 或 VisionPro 的颜色空间转换相比，当前实现更轻量，专注于流水线中的格式转换需求，不提供颜色分量的独立可视化或统计分析。

> English: The implementation maps string conversion codes to OpenCV ColorConversionCodes and performs a single Cv2.CvtColor call, supporting 15+ conversion paths.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetStringParam(@operator, "ConversionCode", "BGR2GRAY")` / `GetIntParam(@operator, "SourceChannels", 3)` -- 读取参数
3. 映射 `conversionCode.ToUpper()` 到 `ColorConversionCodes` 枚举（支持 15 种映射）
4. `Cv2.CvtColor(src, dst, colorCode)` -- 核心颜色空间转换
5. `CreateImageOutput(dst, additionalData)` -- 封装输出，附带 `Channels` 和 `ConversionCode`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ConversionCode` | `enum` | `BGR2GRAY` | `BGR2GRAY` / `BGR2HSV` / `BGR2Lab` / `BGR2YUV` / `GRAY2BGR` / `HSV2BGR`（元数据选项）| 颜色空间转换类型。实际执行支持更多代码：`Lab2BGR`、`YUV2BGR`、`BGR2RGB`、`RGB2BGR`、`BGR2RGBA`、`BGR2XYZ`、`XYZ2BGR`、`BGR2HLS`、`HLS2BGR`。 |
| `SourceChannels` | `int` | `3` | `[1, 4]` | 输入图像的通道数，用于记录和验证转换类型兼容性。`1` 对应灰度，`3` 对应 BGR，`4` 对应 BGRA。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `输入图像` | `Image` | Yes | 输入待转换的图像。通道数和位深应与所选 `ConversionCode` 兼容。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `输出图像` | `Image` | 转换后的图像。通道数取决于目标颜色空间（如 `BGR2GRAY` 输出单通道，`GRAY2BGR` 输出三通道）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Channels` | `Integer` | 输出图像的通道数。 |
| `ConversionCode` | `String` | 本次执行实际使用的转换代码。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(H * W * C)`，其中 `C` 为通道数。颜色空间转换本质上是逐像素的矩阵/查表运算。 |
| 典型耗时 (Typical Latency) | 通常很快，主要耗时在像素遍历和矩阵乘法。`BGR2GRAY` 因为涉及加权求和，比简单的通道重排（如 `BGR2RGB`）稍慢。 |
| 内存特征 (Memory Profile) | 额外分配 1 张与输出同尺寸的 `Mat`。输出通道数可能与输入不同（如 `BGR2GRAY` 输出为单通道，内存约为输入的 1/3）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：为其他算子准备输入格式，如为自适应阈值提供灰度图、为 HSV 颜色分割提供 HSV 图。
- **适合 (Suitable)**：工业检测中基于颜色的分析，如 HSV 空间下的颜色分割、Lab 空间下的亮度独立处理。
- **适合 (Suitable)**：不同图像源之间的格式统一，如将灰度图转为 BGR 以便与彩色图叠加显示。
- **不适合 (Not Suitable)**：输入通道数与所选转换代码不兼容的场景（如对单通道图执行 `BGR2HSV`），会触发 OpenCV 异常。
- **不适合 (Not Suitable)**：需要同时输出多个颜色空间结果的场景，当前算子每次只能执行一种转换。

## 已知限制 / Known Limitations
1. `ConversionCode` 的元数据 `Options` 仅列出 6 种常用转换，但实际执行代码支持 15 种。未列出的代码可通过手动输入使用，但前端下拉菜单不会显示。
2. `SourceChannels` 参数当前仅用于记录，未在 `ExecuteCoreAsync` 中做实际的兼容性校验。不兼容的组合会直接传递给 OpenCV 并抛出异常。
3. `ValidateParameters` 中校验的合法代码列表（15 种）与元数据 `Options`（6 种）不一致，可能导致前端可选值与后端校验存在差异。
4. 颜色空间转换是有损的（如 `BGR2GRAY` 丢失颜色信息，`BGR2HSV` 的 H 通道在低饱和度时不稳定），下游使用时需注意语义变化。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充颜色空间数学原理、列出全部 15 种支持的转换代码、修正调用链、说明 SourceChannels 参数的实际用途 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
