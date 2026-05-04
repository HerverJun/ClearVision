# CLAHE增强 / CLAHE Enhancement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ClaheEnhancementOperator` |
| 枚举值 (Enum) | `OperatorType.ClaheEnhancement` |
| 分类 (Category) | 预处理 / 对比度增强 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
CLAHE（Contrast Limited Adaptive Histogram Equalization，对比度受限的自适应直方图均衡化）是全局直方图均衡化的改进版本。其核心思想：

1. **分块处理**：将图像划分为 `TileWidth x TileHeight` 的小网格（tile），每个网格独立计算直方图并做局部均衡化。
2. **对比度裁剪**：在每个网格的直方图中，将超过 `ClipLimit` 的像素数裁剪掉，并将裁剪掉的像素均匀重新分配到所有灰度级，防止局部对比度被过度放大。
3. **双线性插值**：相邻网格之间通过双线性插值平滑过渡，消除网格边界的人工痕迹。

与全局直方图均衡化相比，CLAHE 能更好地处理光照不均的图像，因为它在局部区域内做均衡化，不会因为全局亮度分布偏斜而过度增强。

> English: CLAHE divides the image into tiles, applies histogram equalization per tile with contrast clipping, and uses bilinear interpolation between tiles to avoid boundary artifacts.

## 实现策略 / Implementation Strategy
当前实现支持多种颜色空间和通道选择策略，通过 `ResolveEnhancementMode` 方法动态决定处理路径：

- **灰度图**（单通道）：直接对灰度图执行 `clahe.Apply`。
- **Lab 模式**：将 BGR 转为 Lab，仅对 L（亮度）通道做 CLAHE，再转回 BGR。这样可以在增强亮度对比度的同时不改变颜色信息。
- **HSV 模式**：将 BGR 转为 HSV，仅对 V（明度）通道做 CLAHE，再转回 BGR。
- **YCrCb 模式**：将 BGR 转为 YCrCb，仅对 Y（亮度）通道做 CLAHE，再转回 BGR。
- **All 模式**：对所有通道分别执行 CLAHE，适用于需要增强每个颜色通道对比度的场景。

`Channel` 参数（`Auto`/`L`/`V`/`Y`/`All`）可以覆盖 `ColorSpace` 的默认选择，提供更灵活的控制。

> English: The implementation supports multiple color space paths (Gray, Lab, HSV, YCrCb, All) with a Channel override mechanism, applying CLAHE only to the luminance channel in color-preserving modes.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetDoubleParam(@operator, "ClipLimit")` / `GetIntParam(@operator, "TileWidth")` / `GetIntParam(@operator, "TileHeight")` / `GetStringParam(@operator, "ColorSpace")` / `GetStringParam(@operator, "Channel")` -- 读取参数
3. `Cv2.CreateCLAHE(clipLimit, new Size(tileWidth, tileHeight))` -- 创建 CLAHE 对象
4. `ResolveEnhancementMode(src, colorSpace, channel)` -- 确定处理路径
5. 按路径分支执行：
   - **Gray**: `Cv2.CvtColor(src, gray, BGR2GRAY)` -> `clahe.Apply(gray, result)`
   - **Lab**: `Cv2.CvtColor(src, converted, BGR2Lab)` -> `Cv2.Split(converted, channels)` -> `clahe.Apply(channels[0], enhanced)` -> `Cv2.Merge(channels, merged)` -> `Cv2.CvtColor(merged, result, Lab2BGR)`
   - **HSV**: 同上，使用 `BGR2HSV` / `HSV2BGR`，处理通道索引 `2`（V 通道）
   - **YCrCb**: 同上，使用 `BGR2YCrCb` / `YCrCb2BGR`，处理通道索引 `0`（Y 通道）
   - **All**: `Cv2.Split(src, channels)` -> 对每个通道 `clahe.Apply` -> `Cv2.Merge(processed, merged)`
6. `CreateImageOutput(dst, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ClipLimit` | `double` | `2.0` | `[0, 40]` | 对比度裁剪限制。值越大，局部对比度增强越强，但噪声放大的风险也越高。`0` 表示不做裁剪（等效于普通自适应均衡化）。典型范围 1.0-4.0。 |
| `TileWidth` | `int` | `8` | `[2, 64]` | 网格宽度（像素）。值越小，局部对比度增强越精细，但网格边界痕迹可能更明显。 |
| `TileHeight` | `int` | `8` | `[2, 64]` | 网格高度（像素）。与 `TileWidth` 配合定义网格大小。通常设为相同值。 |
| `ColorSpace` | `enum` | `Lab` | `Lab` / `HSV` / `Gray` / `All` | 颜色空间选择，决定 CLAHE 作用于哪个通道。`Lab` 和 `HSV` 仅处理亮度通道以保持颜色不变。 |
| `Channel` | `enum` | `Auto` | `Auto` / `L` / `V` / `Y` / `All` | 目标通道覆盖。`Auto` 时跟随 `ColorSpace` 设置；`L`/`V`/`Y`/`All` 可显式指定处理分支，覆盖 `ColorSpace`。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `图像` | `Image` | Yes | 输入待处理图像。支持单通道灰度和多通道彩色图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `增强图像` | `Image` | CLAHE 增强后的结果图像。颜色空间和通道数与输入一致。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `ClipLimit` | `Double` | 本次执行实际使用的对比度裁剪限制。 |
| `TileSize` | `String` | 本次执行实际使用的网格大小，格式为 `"WxH"`。 |
| `ColorSpace` | `String` | 配置的颜色空间参数值。 |
| `Channel` | `String` | 配置的通道参数值。 |
| `ResolvedColorSpace` | `String` | 实际执行时解析后的颜色空间（如 `Lab`、`HSV`、`Gray`）。 |
| `ResolvedChannel` | `String` | 实际执行时解析后的通道（如 `L`、`V`、`Gray`、`All`）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(H * W)` 用于直方图计算和均衡化本身；`O(H * W)` 用于颜色空间转换。总体为 `O(H * W)`。 |
| 典型耗时 (Typical Latency) | 主要耗时在颜色空间转换（两次 `CvtColor`）和 `clahe.Apply`。`All` 模式下每个通道都执行一次 `Apply`，耗时约为单通道的 `C` 倍。 |
| 内存特征 (Memory Profile) | 每个处理路径需要额外的颜色空间转换 `Mat`、分离后的通道数组、增强后的通道和合并结果。`All` 模式峰值内存最高。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：光照不均匀的工业检测图像增强，如 PCB 板、金属表面、玻璃面板的缺陷检测前预处理。
- **适合 (Suitable)**：需要增强局部对比度但不希望改变颜色信息的场景（使用 `Lab` 或 `HSV` 模式）。
- **适合 (Suitable)**：医学影像、显微镜图像的对比度增强。
- **不适合 (Not Suitable)**：图像本身对比度已经足够，过度使用 CLAHE 会放大噪声。
- **不适合 (Not Suitable)**：需要全局一致对比度调整的场景，此时全局直方图均衡化（`HistogramEqualization` 算子的 `Global` 模式）更简单高效。
- **不适合 (Not Suitable)**：对颜色一致性要求极高的场景，`All` 模式可能改变各通道比例导致色偏。

## 已知限制 / Known Limitations
1. `Cv2.CreateCLAHE` 仅支持 `CV_8U` 和 `CV_16U` 输入，其他位深会先被 OpenCV 内部转换。
2. `Channel` 参数优先级高于 `ColorSpace`，但如果输入是单通道灰度图，无论 `Channel` 和 `ColorSpace` 如何设置，都会走灰度处理路径。
3. 网格大小 `TileWidth x TileHeight` 过小时，每个网格的直方图样本不足，均衡化效果不稳定；过大时接近全局均衡化。
4. 当前实现中 `All` 模式对每个通道独立做 CLAHE，可能改变通道间的相对比例，导致输出图像色偏。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 CLAHE 算法原理（分块+裁剪+插值）、完善颜色空间路径解析逻辑、修正调用链、细化所有参数语义 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
