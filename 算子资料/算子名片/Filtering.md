# 高斯模糊 / Gaussian Blur

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GaussianBlurOperator` |
| 枚举值 (Enum) | `OperatorType.Filtering` |
| 分类 (Category) | Filtering / 滤波 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
高斯模糊使用二维高斯函数作为卷积核对图像进行平滑处理。高斯核的数学定义为：

```
G(x, y) = (1 / (2*pi*sigma^2)) * exp(-(x^2 + y^2) / (2*sigma^2))
```

其中 `sigma` 控制高斯分布的宽度。核越大、sigma 越大，模糊效果越强。

高斯模糊的一个重要优化是**可分离性**：二维高斯核可以分解为两个一维高斯核的外积，因此可以先对行做一维卷积，再对列做一维卷积，将时间复杂度从 `O(H*W*K^2)` 降低到 `O(H*W*K)`。

`SigmaX` 和 `SigmaY` 可以独立控制水平和垂直方向的模糊程度。当 `SigmaY = 0` 时，OpenCV 会自动使用 `SigmaX` 的值，实现各向同性模糊。

> English: Gaussian blur applies a 2D Gaussian kernel to the image. The separable property allows OpenCV to decompose it into two 1D convolutions for efficiency.

## 实现策略 / Implementation Strategy
当前实现直接封装 OpenCV 的 `Cv2.GaussianBlur`，并在输入侧做参数规范化：

- **奇数核强制**：若 `KernelSize` 为偶数，自动加 `1` 变为奇数，因为 OpenCV 的 `GaussianBlur` 要求核大小为奇数。
- **SigmaY 自动推导**：当 `SigmaY = 0` 时，不手动覆盖，让 OpenCV 内部自动使用 `SigmaX` 的值，保持了使用不同 `SigmaX`/`SigmaY` 的灵活性。
- **边界模式可选**：通过 `BorderType` 参数控制边缘像素的填充方式，默认为 `BORDER_DEFAULT`（`BORDER_REFLECT_101`）。
- 无需颜色空间转换或位深处理，直接在原始图像上执行。

与双边滤波相比，高斯模糊速度快但不保留边缘；与均值滤波相比，高斯模糊的加权方式使中心像素影响更大，模糊效果更自然。

> English: The implementation wraps OpenCV's GaussianBlur with automatic odd-kernel correction and flexible SigmaY handling.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetIntParam(@operator, "KernelSize", 5)` / `GetDoubleParam(@operator, "SigmaX", 1.0)` / `GetDoubleParam(@operator, "SigmaY", 0.0)` / `GetIntParam(@operator, "BorderType", 4)` -- 读取参数
3. 若 `kernelSize % 2 == 0` 则 `kernelSize++` -- 强制奇数核
4. `Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), sigmaX, sigmaY, borderMode)` -- 核心高斯模糊运算
5. `CreateImageOutput(dst)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelSize` | `int` | `5` | `[1, 31]` | 高斯核的边长（像素）。必须为奇数，偶数输入会自动加 1。值越大，模糊范围越广。典型值 3（轻微模糊）到 21（强烈模糊）。 |
| `SigmaX` | `double` | `1.0` | `[0.1, 10.0]` | 水平方向的高斯标准差。值越大，水平方向模糊越强。当 `KernelSize > 0` 时，OpenCV 会根据核大小自动限制 sigma 的有效范围。 |
| `SigmaY` | `double` | `0.0` | `[0.0, 10.0]` | 垂直方向的高斯标准差。设为 `0` 时自动等于 `SigmaX`，实现各向同性模糊。设为不同于 `SigmaX` 的值可实现方向性模糊。 |
| `BorderType` | `enum` | `4` (Default) | `0` Constant / `1` Replicate / `2` Reflect / `3` Wrap / `4` Default | 边缘像素填充方式。`Default` 为 `BORDER_REFLECT_101`（镜像反射），是最常用的模式。`Replicate` 为边缘复制，`Constant` 为黑色填充。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Image` | `Image` | Yes | 输入待处理图像。支持单通道和多通道图像，无需预处理。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Image` | `Image` | 高斯模糊后的结果图像，通道数和位深与输入一致。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(H * W * K)`，其中 `K = KernelSize`。OpenCV 利用可分离卷积优化，将二维卷积分解为两次一维卷积。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像，`KernelSize=5` 约 1-3ms；`KernelSize=21` 约 5-15ms。实际取决于硬件和通道数。 |
| 内存特征 (Memory Profile) | 额外分配 1 张与输入同尺寸的输出 `Mat`。OpenCV 内部可能为可分离卷积分配临时缓冲区。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：作为边缘检测（Canny、Sobel）前的降噪预处理，减少噪声引起的伪边缘。
- **适合 (Suitable)**：Blob 分析、轮廓检测前的平滑，使目标区域更均匀。
- **适合 (Suitable)**：图像金字塔构建中的降采样前平滑（高斯金字塔）。
- **适合 (Suitable)**：消除传感器噪声、JPEG 压缩伪影等高频干扰。
- **不适合 (Not Suitable)**：需要保留边缘锐利度的场景，此时应使用双边滤波。
- **不适合 (Not Suitable)**：椒盐噪声去除，应使用中值滤波。
- **不适合 (Not Suitable)**：需要精确保持纹理细节的测量场景，模糊会改变边缘位置和宽度。

## 已知限制 / Known Limitations
1. `KernelSize` 范围为 `[1, 31]`，对于需要极大模糊半径的场景（如背景虚化），可能不够，需要多次级联或预缩小图像。
2. 当前实现未提供 `SigmaY` 的独立范围校验，`ValidateParameters` 仅校验 `KernelSize`。
3. `BorderType` 参数使用整数枚举映射，前端显示为 `Constant/Replicate/Reflect/Wrap/Default`，但实际传入的是整数值（0-4），而非字符串。
4. 高斯模糊是各向同性的（除非显式设置不同 `SigmaX`/`SigmaY`），无法实现方向性模糊（如运动模糊）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充高斯核数学原理与可分离卷积优化说明、修正调用链（含奇数核修正逻辑）、细化参数语义和性能数据 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
