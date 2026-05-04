# 图像加法 / Image Add

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageAddOperator` |
| 枚举值 (Enum) | `OperatorType.ImageAdd` |
| 分类 (Category) | 预处理 / 图像运算 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
图像加法算子执行**加权叠加**运算，核心公式为：

```
dst(x, y) = saturate(src1(x, y) * Scale1 + src2(x, y) * Scale2 + Offset)
```

其中 `saturate` 表示饱和截断（结果超出数据类型范围时截断到最大/最小值）。

- 当 `Scale1 = Scale2 = 1.0, Offset = 0` 时，等效于逐像素相加（饱和）。
- 当 `Scale1 = 0.5, Scale2 = 0.5, Offset = 0` 时，等效于两幅图像的平均融合。
- `Offset` 可用于整体亮度调节。

当两幅图像尺寸不一致时，算子提供 4 种对齐策略：`Resize`（缩放）、`Fail`（报错）、`Crop`（裁剪重叠区域）、`AnchorPaste`（偏移粘贴）。

> English: Image Add performs weighted addition: `dst = saturate(src1 * Scale1 + src2 * Scale2 + Offset)`, with four size-mismatch handling policies.

## 实现策略 / Implementation Strategy
当前实现的核心挑战是**两幅图像的对齐**，在执行加权叠加前需要处理多个兼容性问题：

1. **通道数对齐**：通过 `TryConvertChannels` 自动转换通道数不匹配的情况（支持 1<->3<->4 通道互转，如 `GRAY2BGR`、`BGR2BGRA` 等）。
2. **位深对齐**：通过 `ConvertTo(reference.Type())` 将 Image2 的位深匹配到 Image1。
3. **尺寸对齐**：根据 `SizeMismatchPolicy` 策略处理尺寸不一致：
   - `Resize`：使用 `Cv2.Resize` 将 Image2 缩放到 Image1 的尺寸。
   - `Fail`：直接报错，要求用户确保尺寸一致。
   - `Crop`：取两幅图像的重叠区域（左上角对齐），超出部分填充黑色。
   - `AnchorPaste`：支持 `OffsetX`/`OffsetY` 偏移粘贴，将 Image2 按指定偏移贴到 Image1 的画布上。
4. **核心运算**：`Cv2.AddWeighted(src1, scale1, alignedMat, scale2, offset, dst)`。

与 Halcon 的 `add_image` 相比，当前实现增加了通道/位深自动对齐和多种尺寸不匹配策略，更适合异构图像源的融合场景。

> English: The implementation handles channel/depth/size alignment before calling OpenCV's AddWeighted, with four size-mismatch policies for flexible image fusion.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image1")` / `TryGetInputImage(inputs, "Image2")` -- 获取两幅输入图像
2. `GetDoubleParam(@operator, "Scale1")` / `GetDoubleParam(@operator, "Scale2")` / `GetDoubleParam(@operator, "Offset")` / `GetStringParam(@operator, "SizeMismatchPolicy")` / `GetIntParam(@operator, "OffsetX")` / `GetIntParam(@operator, "OffsetY")` -- 读取参数
3. `TryPrepareAlignedImage(src1, src2, policy, offsetX, offsetY, ...)` -- 对齐处理
   - `TryConvertToReferenceType(source, reference)` -- 通道 + 位深对齐
     - `TryConvertChannels(converted, channelAdjusted, targetChannels)` -- 通道转换（GRAY2BGR/BGR2GRAY/BGR2BGRA/BGRA2BGR 等）
     - `converted.ConvertTo(depthAdjusted, reference.Type())` -- 位深转换
   - `Cv2.Resize(convertedSource, aligned, reference.Size())` -- Resize 策略
   - `Mat(aligned, new Rect(...)).CopyTo(...)` -- Crop/AnchorPaste 策略
4. `Cv2.AddWeighted(src1, scale1, alignedMat, scale2, offset, dst)` -- 核心加权叠加
5. `CreateImageOutput(dst, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Scale1` | `double` | `1.0` | `[0, 10.0]` | Image1 的权重系数。`1.0` 保持原始亮度，`0.5` 减半，`2.0` 增强一倍。 |
| `Scale2` | `double` | `1.0` | `[0, 10.0]` | Image2 的权重系数。与 `Scale1` 配合控制两幅图像的混合比例。 |
| `Offset` | `double` | `0` | `[-255, 255]` | 加权叠加后的亮度偏移。正值整体提亮，负值整体压暗。 |
| `SizeMismatchPolicy` | `enum` | `Resize` | `Resize` / `Fail` / `Crop` / `AnchorPaste` | 两幅图像尺寸不一致时的处理策略。`Resize`：缩放 Image2 匹配 Image1；`Fail`：直接报错；`Crop`：裁剪到重叠区域；`AnchorPaste`：偏移粘贴。 |
| `OffsetX` | `int` | `0` | `[-100000, 100000]` | `AnchorPaste` 策略下 Image2 在 Image1 画布上的水平偏移（像素）。正值向右偏移，负值向左偏移。 |
| `OffsetY` | `int` | `0` | `[-100000, 100000]` | `AnchorPaste` 策略下 Image2 在 Image1 画布上的垂直偏移（像素）。正值向下偏移，负值向上偏移。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image1` | `图像1` | `Image` | Yes | 第一幅输入图像。作为尺寸对齐的参考基准。 |
| `Image2` | `图像2` | `Image` | Yes | 第二幅输入图像。会根据策略自动对齐到 Image1 的尺寸、通道数和位深。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `合成图像` | `Image` | 加权叠加后的结果图像，尺寸和类型与 Image1 一致。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Scale1` | `Double` | 本次执行实际使用的 Image1 权重。 |
| `Scale2` | `Double` | 本次执行实际使用的 Image2 权重。 |
| `Offset` | `Double` | 本次执行实际使用的亮度偏移。 |
| `SizeMismatchPolicy` | `String` | 实际应用的尺寸不匹配策略（若尺寸一致则为 `SameSize`）。 |
| `PolicyMessage` | `String` | 策略执行的详细描述信息（如 "Image2 resized from 640x480 to 1920x1080"）。 |
| `OffsetX` | `Integer` | 本次执行使用的水平偏移。 |
| `OffsetY` | `Integer` | 本次执行使用的垂直偏移。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(H * W * C)` 用于核心 `AddWeighted`。额外开销取决于对齐策略：`Resize` 为 `O(H2 * W2 * C)`；通道/位深转换为 `O(H * W * C)`。 |
| 典型耗时 (Typical Latency) | 当两幅图像尺寸和类型一致时，主要耗时在 `AddWeighted`（约 1-3ms @ 1080p）。需要 `Resize` 时会额外增加插值开销。 |
| 内存特征 (Memory Profile) | 需要对齐后的 Image2 副本（`alignedMat`）、通道/位深转换的中间 `Mat`、以及输出 `Mat`。`AnchorPaste` 策略还会分配与 Image1 同尺寸的黑色画布。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：两幅图像的加权混合融合，如多曝光合成、背景叠加、水印叠加。
- **适合 (Suitable)**：帧间差分（配合负权重或预处理）、图像增强中的亮度偏移调整。
- **适合 (Suitable)**：异构图像源的融合（不同尺寸、通道数、位深），通过自动对齐策略简化预处理流程。
- **适合 (Suitable)**：`AnchorPaste` 模式可用于将小图按指定位置贴到大图上（类似 ROI 粘贴）。
- **不适合 (Not Suitable)**：需要透明度混合（Alpha blending）的场景，当前算子不支持 Alpha 通道的加权逻辑。
- **不适合 (Not Suitable)**：逐像素逻辑运算（AND/OR/XOR），应使用专门的位运算算子。
- **不适合 (Not Suitable)**：大量图像的批量叠加（如 100 帧平均），应使用 `FrameAveraging` 算子。

## 已知限制 / Known Limitations
1. `TryConvertChannels` 仅支持 1/3/4 通道之间的互转，不支持 2 通道或其他组合。
2. `AnchorPaste` 策略下，Image2 超出 Image1 画布的部分会被丢弃，不会报错（除非完全超出画布）。
3. `Crop` 策略取重叠区域时固定以左上角为锚点，不支持自定义裁剪对齐点。
4. 当前实现中 `ValidateParameters` 未校验 `Offset` 的范围，元数据声明 `[-255, 255]` 但执行代码中 `GetDoubleParam` 未设 `min`/`max`。
5. 通道转换和位深对齐是自动执行的，下游拿到的结果可能与预期的通道数或位深不同（如输入为灰度+彩色，输出为彩色）。
6. `SizeMismatchPolicy` 的元数据 `Options` 中 `Crop` 显示为 `CropToOverlap`，但实际执行代码中 `NormalizePolicy` 会将 `CropToOverlap` 映射为 `Crop`。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充加权叠加数学公式、详细说明 4 种尺寸对齐策略和通道/位深自动对齐机制、修正调用链、补充 AnchorPaste/Crop 的具体行为 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
