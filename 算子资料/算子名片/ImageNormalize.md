# 图像归一化 / ImageNormalize

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageNormalizeOperator` |
| 枚举值 (Enum) | `OperatorType.ImageNormalize` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子对图像像素值进行归一化处理，支持三种算法：

**MinMax 归一化**：将像素值线性映射到 `[Alpha, Beta]` 目标范围。
```
dst(x,y) = (src(x,y) - min) / (max - min) * (Beta - Alpha) + Alpha
```
通过 `Cv2.Normalize(src, normalized, alpha, beta, NormTypes.MinMax)` 实现。

**Z-Score 归一化**：将像素值标准化为零均值单位方差，再映射到目标范围。
```
z(x,y) = (src(x,y) - mean) / stddev
dst = Normalize(z, Alpha, Beta, MinMax)
```
通过 `Cv2.MeanStdDev` 计算均值和标准差，`Cv2.Subtract` 和 `Cv2.Divide` 执行标准化。

**Histogram 归一化（直方图均衡化）**：通过 `Cv2.EqualizeHist` 拉伸灰度直方图，增强对比度。仅支持单通道 8-bit 输入。

**颜色模式**：
- `LumaOnly`：将 BGR 转换为 YUV 色彩空间，仅对 Y（亮度）通道执行归一化，保持色度不变。
- `PerChannel`：通过 `Cv2.Split` 分离各通道，逐通道独立归一化后 `Cv2.Merge` 合并。

> English: The operator normalizes pixel distributions using MinMax scaling, Z-Score standardization, or histogram equalization. Color images can be processed in luma-only (YUV Y-channel) or per-channel (BGR split) mode.

## 实现策略 / Implementation Strategy
- **目标范围自适应**：当用户保持 Alpha/Beta 默认值时，根据输入图像深度自动选择合理范围：8-bit 默认 `[0, 255]`，16-bit 默认 `[0, 65535]`，32F/64F 默认 `[0, 1.0]`。
- **LumaOnly 模式**通过 YUV 色彩空间分离亮度，避免归一化改变色度信息，适合保持色彩真实性的场景。
- **PerChannel 模式**独立处理每个通道，可能改变色度比例，但适合需要各通道独立增强的场景。
- **深度兼容性**：LumaOnly 模式下若归一化后的深度与原始通道不匹配，会自动回退到 8-bit 兼容路径重新处理。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, ...)`
2. `GetStringParam(@operator, "Method", "MinMax")` / `GetDoubleParam("Alpha"/"Beta"/"ColorMode")`
3. **MinMax 分支**：`Cv2.Normalize(src, normalized, alpha, beta, NormTypes.MinMax, type)`
4. **ZScore 分支**：
   - `Cv2.MeanStdDev(src, out mean, out stddev)`
   - `src.ConvertTo(src32, CV_32FC1)`
   - `Cv2.Subtract(src32, mean, centered)`
   - `Cv2.Divide(centered, sigma, z)`
   - `Cv2.Normalize(z, normalized, alpha, beta, NormTypes.MinMax, type)`
5. **Histogram 分支**：`Cv2.EqualizeHist(byteChannel, normalized)`
6. **LumaOnly 颜色处理**：
   - `Cv2.CvtColor(src, yuv, BGR2YUV)`
   - `Cv2.Split(yuv, channels)` + 处理 Y 通道 + `Cv2.Merge(channels, merged)`
   - `Cv2.CvtColor(merged, result, YUV2BGR)`
7. **PerChannel 颜色处理**：
   - `Cv2.Split(src, channels)` + 逐通道处理 + `Cv2.Merge(processed, result)`
8. `CreateImageOutput(result, additionalData)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"MinMax"` | `MinMax` / `ZScore` / `Histogram` | 归一化算法。MinMax 线性映射；ZScore 标准化；Histogram 直方图均衡化。 |
| `Alpha` | `double` | `0.0` | [-10000, 10000] | MinMax/ZScore 目标范围下限。默认值根据输入深度自适应。 |
| `Beta` | `double` | `255.0` | [-10000, 10000] | MinMax/ZScore 目标范围上限。默认值根据输入深度自适应。 |
| `ColorMode` | `enum` | `"LumaOnly"` | `LumaOnly` / `PerChannel` | 彩色图像处理模式。LumaOnly 仅归一化亮度通道；PerChannel 逐通道独立处理。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待归一化的输入图像（1 通道或 3 通道）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 归一化后的输出图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Method` | `String` | 实际使用的归一化方法。 |
| `ColorMode` | `String` | 实际使用的颜色模式（单通道图像输出 "Gray"）。 |
| `Channels` | `Integer` | 输出图像通道数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W x H)，线性于像素总数；Z-Score 额外有一次全局统计遍历。 |
| 典型耗时 (Typical Latency) | MinMax 1080p 约 1-2 ms；Z-Score 约 3-5 ms；Histogram 约 2-3 ms。LumaOnly/PerChannel 模式增加色彩空间转换开销。 |
| 内存特征 (Memory Profile) | MinMax 和 Histogram 基本只需一幅输出 Mat；Z-Score 额外需要 32-bit 中间 Mat；PerChannel 模式需要分离和合并的临时 Mat 数组。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：统一不同光照条件下采集的图像亮度范围，消除曝光差异。
- **适合 (Suitable)**：将 16-bit 深度图或 32-bit 浮点图映射到 8-bit 可视化范围。
- **适合 (Suitable)**：Z-Score 标准化后送入基于统计阈值的检测算法。
- **适合 (Suitable)**：直方图均衡化增强低对比度图像的细节可见性。
- **不适合 (Not Suitable)**：4 通道 RGBA 图像，当前仅支持 1 通道和 3 通道。
- **不适合 (Not Suitable)**：需要保持精确像素值比例的定量分析场景（Histogram 模式会改变像素值分布）。

## 已知限制 / Known Limitations
1. 仅支持 1 通道和 3 通道图像，4 通道（如 RGBA）会直接返回失败。
2. Histogram 模式要求输入能转换为 8-bit，对 32-bit 浮点图的处理依赖自动范围缩放，可能损失精度。
3. LumaOnly 模式在归一化后深度不匹配时会自动回退到 8-bit 处理，可能导致非 8-bit 输入的精度损失。
4. Z-Score 模式的 `Alpha`/`Beta` 参数含义与 MinMax 不同——它们控制的是标准化后的映射范围，而非原始像素范围。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充三种归一化算法详解、LumaOnly/PerChannel 颜色模式、深度自适应、API 调用链 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
