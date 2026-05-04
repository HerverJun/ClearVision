# 中值滤波 / MedianBlur

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `MedianBlurOperator` |
| 枚举值 (Enum) | `OperatorType.MedianBlur` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中值滤波是一种非线性排序滤波器。对于图像中每个像素，取以其为中心的矩形窗口内所有像素值，排序后取中位数作为输出值。

与均值滤波不同，中值滤波不是对像素值做加权平均，而是取排序后的中间值。这使得它对椒盐噪声（salt-and-pepper noise）有极强的抑制能力：因为噪声像素通常是极端值（0 或 255），在排序时会被排到两端，不会被选为中位数。同时，中值滤波对边缘的保护能力优于均值滤波，因为边缘两侧的像素值差异不会被"平均"掉。

该算子仅支持正方形核（k x k），核大小必须为奇数。

> English: Median blur is a nonlinear rank filter that replaces each pixel with the median value in its k x k neighborhood. It excels at removing salt-and-pepper noise because outlier values sort to the extremes and are never selected as the output. It also preserves edges better than mean blur since edge contrast is not averaged away. Only square kernels are supported; kernel size must be odd.

## 实现策略 / Implementation Strategy
- 直接调用 OpenCV 的 `Cv2.MedianBlur`，该函数内部使用了高效的直方图排序算法（histogram-based sorting），对 8 位图像的时间复杂度为 O(N) 而非朴素排序的 O(N * k^2 log(k^2))。
- 若用户输入偶数核大小，代码会自动加 1 使其为奇数，因为中值滤波要求核大小为奇数才能确定唯一的中位数。
- 输出 Mat 通过 `MatPool.Shared.Rent` 从对象池借用，减少频繁的内存分配和 GC 压力，这是一种生产环境的内存优化策略。
- 相比均值滤波，中值滤波在核较大时计算量增长更快（尽管 OpenCV 已优化），因此核大小上限设为 31 而非均值滤波的 63。

> English: Uses OpenCV's optimized `Cv2.MedianBlur` which employs histogram-based sorting for O(N) complexity on 8-bit images. Even kernel sizes are auto-incremented to odd. Output Mat is rented from `MatPool.Shared` to reduce allocation pressure. Kernel size is capped at 31 (vs 63 for mean blur) because median filtering cost grows faster with kernel size despite OpenCV optimizations.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetIntParam(@operator, "KernelSize", 5, min: 1, max: 31)` -- 读取核大小
3. `imageWrapper.GetMat()` -- 解码为 OpenCV Mat
4. `MatPool.Shared.Rent(src.Width, src.Height, src.Type())` -- 从对象池借出输出缓冲区
5. `Cv2.MedianBlur(src, dst, kernelSize)` -- 执行中值滤波
6. `CreateImageOutput(dst)` -- 封装输出（零拷贝，直接使用池中 Mat）

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelSize` | `int` | `5` | [1, 31] | 中值核的边长（像素）。偶数会自动 +1 变为奇数。核越大去噪能力越强但细节损失越多，且计算量增长较快。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待滤波的输入图像，支持单通道或多通道。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 中值滤波后的图像，尺寸与输入相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) 对 8 位图像（OpenCV 内部直方图优化）；对非 8 位图像退化为 O(N * k^2 log(k^2))。 |
| 典型耗时 (Typical Latency) | 1080p 图像、KernelSize=5 约 2-5ms（CPU）；KernelSize=31 约 10-20ms。核大小对耗时的影响比均值滤波显著。 |
| 内存特征 (Memory Profile) | 输出 Mat 从对象池借出，减少堆分配。除输出外，OpenCV 内部需要为直方图排序分配临时缓冲区。峰值内存约为输入图像的 2-3 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：去除椒盐噪声（随机黑白点），例如传感器坏点、传输错误或数字化伪影。
- **适合 (Suitable)**：需要在去噪的同时尽可能保留边缘锐度的预处理步骤，例如 PCB 焊点检测、字符识别前的图像清理。
- **适合 (Suitable)**：作为二值化前的降噪手段，中值滤波不会引入新的灰度值（不像均值滤波会生成窗口内不存在的中间值）。
- **不适合 (Not Suitable)**：高斯噪声为主的场景，均值滤波或高斯滤波对高斯噪声的抑制效果更好。
- **不适合 (Not Suitable)**：需要大核（>31）进行大面积平滑的场景，受核大小上限限制且大核时性能下降明显。
- **不适合 (Not Suitable)**：实时性要求极高且需要频繁调整核大小的场景，核越大计算成本增长越快。

## 已知限制 / Known Limitations
1. 核大小上限为 31，小于均值滤波的 63。这是性能与功能的权衡：中值滤波的计算量随核大小增长更快。
2. 仅支持正方形核（k x k），不支持矩形核。这是 OpenCV `MedianBlur` API 的限制。
3. 偶数核大小会被静默修正为奇数（+1），面板显示值与实际执行值可能不同，但不会返回错误或警告。
4. 输出 Mat 从共享对象池借出，如果下游算子持有该 Mat 引用时间过长，可能影响池中其他请求的可用性。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充算法原理（非线性排序滤波、椒盐噪声抑制机制）、实现策略（MatPool 对象池优化、直方图排序）、完整参数语义、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
