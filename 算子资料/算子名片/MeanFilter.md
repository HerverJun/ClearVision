# 均值滤波 / MeanFilter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MeanFilterOperator` |
| 枚举值 (Enum) | `OperatorType.MeanFilter` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
均值滤波（Box Blur）是最基本的线性平滑滤波器。其核心思想是：对于图像中每个像素，取以其为中心的矩形窗口内所有像素的算术平均值，用该平均值替换中心像素值。

数学表达：对 kernel 大小为 k x k 的窗口，输出像素值 = (1/k^2) * sum(窗口内所有像素)。

该操作相当于一个所有系数相等的归一化卷积核对图像做卷积。窗口越大，平滑效果越强，但同时图像细节和边缘也会被更显著地模糊。

> English: Mean (box blur) filtering replaces each pixel with the arithmetic mean of its k x k neighborhood. It is a normalized convolution with uniform weights. Larger kernels produce stronger smoothing but also blur edges and fine details more aggressively.

## 实现策略 / Implementation Strategy
- 直接调用 OpenCV 的 `Cv2.Blur`，这是高度优化的 box filter 实现，内部会利用积分图（integral image）加速，使得计算复杂度与核大小无关。
- 若用户输入偶数核大小，代码会自动加 1 使其为奇数，保证卷积锚点（anchor）位于核中心，避免输出图像产生亚像素偏移。
- 相比高斯滤波，均值滤波计算更快但对边缘保护能力更弱；相比中值滤波，均值滤波对椒盐噪声的抑制效果较差但对高斯噪声效果更好。

> English: Uses OpenCV's optimized `Cv2.Blur` which internally leverages integral images for O(1) per-pixel cost regardless of kernel size. Even kernel sizes are auto-incremented to odd to keep the anchor centered. Compared to Gaussian blur it is faster but less edge-preserving; compared to median blur it is less effective against salt-and-pepper noise.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetIntParam(@operator, "KernelSize", 5, min: 1, max: 63)` -- 读取核大小
3. `GetIntParam(@operator, "BorderType", 4, min: 0, max: 7)` -- 读取边界填充类型
4. `imageWrapper.GetMat()` -- 解码为 OpenCV Mat
5. `Cv2.Blur(src, dst, new Size(kernelSize, kernelSize), new Point(-1, -1), (BorderTypes)borderType)` -- 执行均值滤波
6. `CreateImageOutput(dst)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelSize` | `int` | `5` | [1, 63] | 均值核的边长（像素）。偶数会自动 +1 变为奇数。值越大平滑越强，边缘模糊也越明显。 |
| `BorderType` | `enum` | `"4"` (Default) | 0=Constant, 1=Replicate, 2=Reflect, 3=Wrap, 4=Default | 图像边界的像素填充策略。Constant 填 0，Replicate 复制边缘像素，Reflect 镜像反射，Default 使用 OpenCV 默认方式。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待滤波的输入图像，支持单通道或多通道。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 均值滤波后的平滑图像，尺寸与输入相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N)，其中 N 为像素总数。OpenCV 内部使用积分图优化，使得执行时间与核大小基本无关。 |
| 典型耗时 (Typical Latency) | 1080p 图像约 1-3ms（CPU），核大小变化对耗时影响很小。 |
| 内存特征 (Memory Profile) | 需要分配一张与输入等大的输出 Mat；OpenCV 内部会分配临时积分图缓冲区。总峰值内存约为输入图像的 3-4 倍（含积分图）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：消除图像中的高斯白噪声和传感器噪声，作为后续边缘检测或二值化的预处理步骤。
- **适合 (Suitable)**：需要快速粗略平滑的场景，例如去除细微纹理干扰以稳定 OCR 或模板匹配输入。
- **适合 (Suitable)**：核大小需要频繁调整但对延迟敏感的流水线，因为积分图优化使核大小变化几乎不影响执行时间。
- **不适合 (Not Suitable)**：需要保留边缘锐度的同时去噪的场景，此时应优先考虑双边滤波或导向滤波。
- **不适合 (Not Suitable)**：椒盐噪声严重的场景，中值滤波对椒盐噪声的抑制效果远优于均值滤波。
- **不适合 (Not Suitable)**：需要对图像做局部对比度增强或特征提取的场景，均值滤波会抹平对比度差异。

## 已知限制 / Known Limitations
1. `BorderType` 参数以整数枚举形式传入（0-7），但 `OperatorParam` 声明中仅列出了 5 个选项（0-4），超出范围的值虽可传入但行为取决于 OpenCV 内部映射，可能产生意外结果。
2. 偶数核大小会被静默修正为奇数（+1），面板上显示的值与实际执行值可能不同，但不会返回错误或警告。
3. 输出图像与输入图像的通道数和位深保持一致，不做任何归一化或类型转换；对于 16 位或浮点图像，均值计算的精度取决于 OpenCV 的内部实现。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充算法原理（积分图优化）、实现策略（偶数核修正）、完整参数语义、API 调用链细节、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
