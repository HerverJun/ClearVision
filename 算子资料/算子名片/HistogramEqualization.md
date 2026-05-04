# 直方图均衡化 / Histogram Equalization

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `HistogramEqualizationOperator` |
| 枚举值 (Enum) | `OperatorType.HistogramEqualization` |
| 分类 (Category) | 预处理 / 对比度增强 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子支持两种直方图均衡化方法：

### Global（全局直方图均衡化）
对整幅图像计算累积分布函数（CDF），然后通过映射函数将原始灰度级重新分配，使输出直方图近似均匀分布：

```
cdf(x) = sum_{i=0}^{x} histogram(i)
dst(x, y) = cdf(src(x, y)) * (L - 1) / (H * W)
```

其中 `L` 为灰度级数（256），`H * W` 为像素总数。全局方法简单高效，但在光照不均时可能导致局部过度增强或欠增强。

### CLAHE（对比度受限的自适应直方图均衡化）
将图像划分为 `TileGridSize x TileGridSize` 的小网格，每个网格独立做直方图均衡化，但通过 `ClipLimit` 限制对比度放大倍数，避免噪声过度增强。相邻网格之间通过双线性插值平滑过渡。

CLAHE 比全局均衡化更适合处理光照不均的图像，因为它在局部区域内做均衡化。

> English: This operator supports both global histogram equalization (using CDF mapping) and CLAHE (tile-based adaptive equalization with contrast clipping).

## 实现策略 / Implementation Strategy
当前实现根据 `Method` 参数选择两条处理路径，并根据图像通道数和 `ApplyToEachChannel` 参数选择不同的通道处理策略：

- **单通道灰度图**：直接对灰度图执行均衡化（`EqualizeHist` 或 `clahe.Apply`）。
- **多通道 + `ApplyToEachChannel = false`**（默认）：仅处理亮度/明度通道，保持颜色不变。
  - Global 模式：转 YUV，对 Y 通道均衡化，再转回 BGR。
  - CLAHE 模式：转 Lab，对 L 通道均衡化，再转回 BGR。
- **多通道 + `ApplyToEachChannel = true`**：对每个通道分别执行均衡化，可能改变通道间比例导致色偏。

**位深兼容处理**：所有通道处理都通过 `ApplySingleChannelByteCompatible` 包装，先用 `ConvertSingleChannelToByte` 转为 `CV_8U`（`EqualizeHist` 和 `CLAHE.Apply` 仅支持 8/16 位），处理后再用 `RestoreByteImageToSourceDepth` 恢复原始位深。

**TileGridSize 兼容性**：支持 `TileGridSize` 和旧版 `TileSize` 两个参数名，通过 `ResolveRawTileGridSize` 做向后兼容解析。

> English: The implementation supports Global and CLAHE modes with per-channel or luminance-only processing, plus automatic bit-depth normalization for 8-bit compatibility.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetStringParam(@operator, "Method")` / `GetDoubleParam(@operator, "ClipLimit")` / `GetTileGridSize(@operator)` / `GetBoolParam(@operator, "ApplyToEachChannel")` -- 读取参数
3. **Global 路径**：
   - 单通道：`ApplySingleChannelByteCompatible` -> `Cv2.EqualizeHist(channel, result)`
   - 多通道无逐通道：`ApplyLumaChannelByteCompatible(BGR2YUV, YUV2BGR)` -> `Cv2.EqualizeHist(channels[0], result)`
   - 多通道逐通道：`ApplyPerChannelByteCompatible` -> 每通道 `Cv2.EqualizeHist`
4. **CLAHE 路径**：
   - `Cv2.CreateCLAHE(clipLimit, new Size(tileGridSize, tileGridSize))` -- 创建 CLAHE 对象
   - 单通道：`clahe.Apply(channel, result)`
   - 多通道无逐通道：`ApplyLumaChannelByteCompatible(BGR2Lab, Lab2BGR)` -> `clahe.Apply(channels[0], result)`
   - 多通道逐通道：`ApplyPerChannelByteCompatible` -> 每通道 `clahe.Apply`
5. 位深处理子链：`OperatorImageDepthHelper.ConvertSingleChannelToByte(src)` -> 处理 -> `OperatorImageDepthHelper.RestoreByteImageToSourceDepth(processed, src)`
6. `CreateImageOutput(dst, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `Global` | `Global` / `CLAHE` | 均衡化方法。`Global` 全局均衡化简单快速；`CLAHE` 自适应均衡化更适合光照不均的图像。 |
| `ClipLimit` | `double` | `2.0` | `[0.0, 100.0]` | CLAHE 模式的对比度裁剪限制。仅在 `Method = CLAHE` 时生效。值越大，对比度增强越强，噪声放大风险越高。`0` 表示不做裁剪。 |
| `TileGridSize` | `int` | `8` | `[1, 64]` | CLAHE 模式的网格边长（像素）。仅在 `Method = CLAHE` 时生效。网格大小为 `TileGridSize x TileGridSize`。兼容旧版参数名 `TileSize`。 |
| `ApplyToEachChannel` | `bool` | `false` | `true` / `false` | 多通道图像的处理策略。`false` 时仅处理亮度通道（Global 用 YUV-Y，CLAHE 用 Lab-L），保持颜色不变；`true` 时对每个通道分别均衡化，可能改变颜色比例。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `输入图像` | `Image` | Yes | 输入待处理图像。支持单通道灰度和多通道彩色图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `输出图像` | `Image` | 均衡化后的结果图像，位深与输入一致。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Method` | `String` | 本次执行实际使用的均衡化方法（`Global` 或 `CLAHE`）。 |
| `ClipLimit` | `Double` | 本次执行实际使用的对比度裁剪限制。 |
| `TileGridSize` | `Integer` | 本次执行实际使用的网格大小。 |
| `ApplyToEachChannel` | `Boolean` | 本次执行是否逐通道处理。 |
| `Channels` | `Integer` | 输出图像的通道数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Global：`O(H * W + L)`，其中 `L = 256` 为灰度级数。CLAHE：`O(H * W)` 用于直方图计算和均衡化，加上颜色空间转换开销。 |
| 典型耗时 (Typical Latency) | Global 模式通常很快（单通道约 1-2ms）。CLAHE 模式因涉及分块、插值和颜色空间转换，耗时约为 Global 的 2-5 倍。`ApplyToEachChannel = true` 时耗时按通道数倍增。 |
| 内存特征 (Memory Profile) | 需要颜色空间转换 `Mat`、通道分离数组、处理后的通道和合并结果。位深兼容处理还会额外分配 `CV_8U` 工作图。多通道处理时峰值内存约为输入的 3-5 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：低对比度工业图像的增强，如 X 光检测、PCB 板缺陷检测前的预处理。
- **适合 (Suitable)**：光照不均匀的场景下使用 CLAHE 模式增强局部对比度。
- **适合 (Suitable)**：需要快速全局对比度拉伸的场景下使用 Global 模式。
- **适合 (Suitable)**：医学影像、显微镜图像的对比度增强。
- **不适合 (Not Suitable)**：图像本身对比度已经足够，过度均衡化会放大噪声并产生不自然的视觉效果。
- **不适合 (Not Suitable)**：`ApplyToEachChannel = true` 时对颜色一致性要求高的场景，可能产生色偏。
- **不适合 (Not Suitable)**：需要精确保持灰度关系的定量测量场景，均衡化会改变像素值的绝对含义。

## 已知限制 / Known Limitations
1. `Cv2.EqualizeHist` 和 `Cv2.CreateCLAHE` 仅支持 `CV_8U` 和 `CV_16U` 输入，其他位深通过 `ConvertSingleChannelToByte` 转换，可能损失高位深精度。
2. `ClipLimit` 和 `TileGridSize` 仅在 `Method = CLAHE` 时生效，Global 模式下这些参数被忽略。
3. `TileGridSize` 兼容旧版参数名 `TileSize`，但若两个参数同时存在且 `TileGridSize` 为默认值，则优先使用 `TileSize` 的值。
4. Global 模式的全局 CDF 映射在直方图有严重偏斜时（如大面积纯黑背景），可能导致前景区域过度增强。
5. CLAHE 模式的 `ClipLimit = 0` 时不做裁剪，等效于普通自适应均衡化，可能过度放大噪声。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 Global 和 CLAHE 算法原理、修正通道处理策略（YUV-Y/Lab-L）、说明位深兼容处理机制、补充 TileSize 向后兼容逻辑 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
