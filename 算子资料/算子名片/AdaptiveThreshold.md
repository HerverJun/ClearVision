# 自适应阈值 / Adaptive Threshold

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AdaptiveThresholdOperator` |
| 枚举值 (Enum) | `OperatorType.AdaptiveThreshold` |
| 分类 (Category) | 预处理 / 二值化 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
自适应阈值不会对整幅图使用一个全局阈值，而是针对每个像素在其邻域窗口 `W(x, y)` 内计算局部阈值 `T(x, y)`：

- **Mean 模式**：`T(x, y) = mean(W(x, y)) - C`
- **Gaussian 模式**：`T(x, y) = gaussian_weighted_mean(W(x, y)) - C`

随后执行二值判定：

- **Binary**：当 `src(x, y) > T(x, y)` 时输出 `MaxValue`，否则输出 `0`
- **BinaryInv**：与 `Binary` 相反

这类方法适合处理光照不均、背景缓慢变化的图像，因为阈值会随局部亮度变化而调整，而不是被单个全局阈值限制。

> English: The operator computes a local threshold per pixel from a neighborhood window, then performs binary or inverted binary thresholding.

## 实现策略 / Implementation Strategy
当前实现直接封装 OpenCV 的 `Cv2.AdaptiveThreshold`，但在进入 OpenCV 前后增加了几层与工程集成相关的处理：

- **统一转灰度**：若输入是多通道图像，先通过 `EnsureSingleChannelGray` 转为灰度，保证满足 `AdaptiveThreshold` 对单通道输入的要求。
- **位深归一化**：通过 `ConvertSingleChannelToByte` 将灰度图转为 `CV_8U`，因为 `Cv2.AdaptiveThreshold` 仅支持 8 位输入。
- **窗口尺寸修正**：运行时先将 `BlockSize` 约束到 `[3, 51]`，如果传入偶数，会自动加 `1` 变成奇数。
- **输出位深恢复**：OpenCV 得到的是单通道 8 位二值图，随后通过 `RestoreBinaryMaskToSourceDepth` 恢复到原始输入图像的位深，保持与上游数据格式一致。
- **零拷贝输出封装**：最终通过 `CreateImageOutput` 输出 `ImageWrapper`，同时附带 `AdaptiveMethod`、`BlockSize`、`C`、`InputBitDepth` 等回传值，方便后续节点记录与调试。

> English: The implementation wraps OpenCV's adaptive threshold with grayscale conversion, 8-bit normalization, even-to-odd block size correction, depth restoration, and pipeline output packaging.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetDoubleParam(@operator, "MaxValue")` / `GetStringParam(@operator, "AdaptiveMethod")` / `GetStringParam(@operator, "ThresholdType")` / `GetIntParam(@operator, "BlockSize")` / `GetDoubleParam(@operator, "C")` -- 读取参数
3. `OperatorImageDepthHelper.EnsureSingleChannelGray(src)` -- 多通道转灰度
4. `OperatorImageDepthHelper.ConvertSingleChannelToByte(gray)` -- 归一化为 CV_8U
5. `Cv2.AdaptiveThreshold(workingGray, binary8, 255.0, adaptiveType, resolvedThresholdType, blockSize, c)` -- 核心自适应阈值运算
6. `OperatorImageDepthHelper.RestoreBinaryMaskToSourceDepth(binary8, gray, maxValue)` -- 恢复到原始位深
7. `CreateImageOutput(binary.Clone(), additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MaxValue` | `double` | `255.0` | `[0, 255]` | 二值图前景输出值。通常保持 `255`，得到标准黑白图。 |
| `AdaptiveMethod` | `enum` | `Gaussian` | `Gaussian` / `Mean` | 局部阈值计算方式。`Gaussian` 使用高斯加权均值，对光照渐变更稳健；`Mean` 使用简单算术均值，计算更直接。 |
| `ThresholdType` | `enum` | `Binary` | `Binary` / `BinaryInv` | 二值化方向。前景是亮目标时常用 `Binary`，前景是暗目标时常用 `BinaryInv`。 |
| `BlockSize` | `int` | `11` | `[3, 51]`，必须为奇数 | 局部窗口边长。值越大，阈值更平滑、越接近局部全局阈值；值越小，对局部细节和噪声都更敏感。若传入偶数会自动修正为下一个奇数。 |
| `C` | `double` | `2.0` | `[-100, 100]` | 从局部统计量中减去的常数。`C` 越大，阈值越低，在 `Binary` 模式下通常会留下更多白色前景；`C` 为负时则会抬高阈值。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Image` | `Image` | Yes | 待进行局部阈值分割的输入图像。多通道图像会先转为灰度再处理。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Image` | `Image` | 二值化结果图，位深与原始输入一致。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `AdaptiveMethod` | `String` | 本次执行实际采用的局部阈值方法。 |
| `BlockSize` | `Integer` | 本次执行实际使用的窗口大小；若输入为偶数，这里会反映修正后的奇数值。 |
| `C` | `Double` | 本次执行实际使用的常数偏置。 |
| `InputBitDepth` | `String` | 输入灰度图的原始位深（如 `UInt16`、`Byte` 等）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(H * W * k^2)`，其中 `k = BlockSize`；核心计算由 OpenCV 本地 C++ 实现完成。 |
| 典型耗时 (Typical Latency) | 主要耗时集中在局部阈值计算本身，以及前后的灰度/位深转换。`BlockSize` 越大，耗时越高。 |
| 内存特征 (Memory Profile) | 额外分配 1 张灰度图（`EnsureSingleChannelGray`）、1 张 8 位工作图（`ConvertSingleChannelToByte`）、1 张中间二值图和 1 张最终输出图。峰值约为输入图像大小的 3-4 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：背景亮度不均、存在阴影、反光或亮度渐变的图像二值化。
- **适合 (Suitable)**：作为轮廓检测、Blob 分析、缺陷分割前的预处理步骤。
- **适合 (Suitable)**：纸张、标签、字符、表面纹理等"局部对比存在但全局亮度不稳定"的场景。
- **不适合 (Not Suitable)**：需要基于颜色通道分别判断的任务，因为当前实现统一转灰度。
- **不适合 (Not Suitable)**：噪声非常强但未先做平滑/降噪的图像，尤其在 `BlockSize` 较小时容易产生碎片噪点。
- **不适合 (Not Suitable)**：光照均匀且目标与背景对比鲜明的简单场景，此时全局阈值（`Otsu`）更高效。

## 已知限制 / Known Limitations
1. 当前实现只处理"灰度化后的单通道二值分割"，不支持对彩色图像做逐通道自适应阈值。
2. `Cv2.AdaptiveThreshold` 仅接受 `CV_8U` 输入，非 8 位图像会通过 `ConvertSingleChannelToByte` 自动转换，可能损失高位深精度。
3. 当前实现没有在算子内部做去噪、归一化或形态学后处理；结果质量较依赖输入图像质量与参数选择。
4. 输出通过 `RestoreBinaryMaskToSourceDepth` 恢复到原始位深，但实际内容仍为二值（仅含 `0` 和 `MaxValue`），下游若按连续灰度处理可能产生误解。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：修正调用链（EnsureSingleChannelGray -> ConvertSingleChannelToByte -> AdaptiveThreshold -> RestoreBinaryMaskToSourceDepth），补充位深处理细节，更新参数说明与限制 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法公式、参数真实行为、输出兼容性与限制说明 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
