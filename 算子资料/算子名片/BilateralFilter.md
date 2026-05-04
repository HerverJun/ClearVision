# 双边滤波 / Bilateral Filter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `BilateralFilterOperator` |
| 枚举值 (Enum) | `OperatorType.BilateralFilter` |
| 分类 (Category) | 预处理 / 滤波 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
双边滤波是一种**边缘保留的非线性平滑滤波器**。与普通高斯滤波仅考虑空间距离不同，双边滤波的权重同时由两个因素决定：

- **空间距离权重** `G_s`：距离中心像素越近，权重越大，由 `SigmaSpace` 控制衰减速度。
- **灰度距离权重** `G_r`：与中心像素亮度差异越小，权重越大，由 `SigmaColor` 控制衰减速度。

最终输出为：
```
dst(x, y) = (1 / W) * sum_{i,j in kernel} src(i, j) * G_s(||(i,j)-(x,y)||) * G_r(|src(i,j)-src(x,y)|)
```
其中 `W` 为归一化因子。

由于边缘处像素灰度差异大，`G_r` 权重会急剧下降，因此边缘不会被模糊；而在平坦区域，灰度差异小，滤波效果接近普通高斯模糊。这使得双边滤波能在平滑噪声的同时保持边缘锐利。

> English: Bilateral filtering is a non-linear, edge-preserving smoothing filter that combines a spatial Gaussian kernel with an intensity-range Gaussian kernel, preserving edges while suppressing noise in flat regions.

## 实现策略 / Implementation Strategy
当前实现直接封装 OpenCV 的 `Cv2.BilateralFilter`，结构简洁：

- 输入图像直接传入 OpenCV，无需预处理或颜色空间转换（双边滤波支持多通道输入）。
- 输出为与输入同尺寸、同类型的新 `Mat`，通过 `CreateImageOutput` 封装。
- 未做额外的灰度转换或位深归一化，保持了输入图像的原始通道数和位深。

与高斯滤波相比，双边滤波计算量显著更大，因为每个像素的权重不仅取决于空间位置，还取决于像素值本身，无法通过可分离卷积优化。

> English: The implementation directly wraps OpenCV's BilateralFilter with no preprocessing, preserving input channel count and bit depth.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetIntParam(@operator, "Diameter", 9)` / `GetDoubleParam(@operator, "SigmaColor", 75.0)` / `GetDoubleParam(@operator, "SigmaSpace", 75.0)` -- 读取参数
3. `Cv2.BilateralFilter(src, dst, diameter, sigmaColor, sigmaSpace)` -- 核心双边滤波运算
4. `CreateImageOutput(dst)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Diameter` | `int` | `9` | `[1, 25]` | 滤波核的直径。值越大，每个像素参与计算的邻域越广，平滑效果越强，但计算开销也越大。若设为负数或 0，OpenCV 会根据 `SigmaSpace` 自动推算。 |
| `SigmaColor` | `double` | `75.0` | `[1.0, 255.0]` | 灰度/颜色空间的高斯标准差。值越大，灰度差异较大的像素也会被纳入加权平均，边缘保留效果减弱，平滑更强。典型范围 10-150。 |
| `SigmaSpace` | `double` | `75.0` | `[1.0, 255.0]` | 空间坐标的高斯标准差。值越大，空间上更远的像素也会参与计算，等效于更大的滤波核。典型范围 10-150。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `图像` | `Image` | Yes | 输入待处理图像。支持单通道灰度和多通道彩色图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `图像` | `Image` | 双边滤波后的结果图像，通道数和位深与输入一致。 |

## 性能特征 / Performance
| 挌标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(H * W * D^2 * C)`，其中 `D = Diameter`，`C = 通道数`。由于每个像素的权重依赖其灰度值，无法像高斯滤波那样分离为两次一维卷积。 |
| 典型耗时 (Typical Latency) | 比同等核大小的高斯滤波慢数倍至数十倍。`Diameter` 从 5 增加到 15 时，耗时增长接近平方级。 |
| 内存特征 (Memory Profile) | 额外分配 1 张与输入同尺寸的输出 `Mat`。OpenCV 内部可能有额外的权重表分配。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：需要在平滑噪声的同时保留边缘锐利度的场景，如表面缺陷检测前的预处理。
- **适合 (Suitable)**：去除纹理噪声但保留纹理边界，如金属表面、PCB 板、纺织品的图像预处理。
- **适合 (Suitable)**：人像或物体的皮肤/表面平滑，同时保持轮廓清晰。
- **不适合 (Not Suitable)**：对速度要求极高的实时流水线，尤其在大 `Diameter` 下计算开销显著。
- **不适合 (Not Suitable)**：需要均匀模糊全部内容（包括边缘）的场景，此时普通高斯滤波更合适。
- **不适合 (Not Suitable)**：椒盐噪声或脉冲噪声的去除，双边滤波对此类噪声效果有限，应使用中值滤波。

## 已知限制 / Known Limitations
1. `Diameter` 参数范围为 `[1, 25]`，对于大尺度平滑需求可能不够，需配合 `SigmaSpace` 间接扩大影响范围。
2. 当前实现未提供 `BorderType` 参数，OpenCV 默认使用 `BORDER_DEFAULT`（通常为 `BORDER_REFLECT_101`），边缘像素行为不可自定义。
3. 双边滤波计算复杂度高，在大图像（如 4K）上配合较大 `Diameter` 可能出现明显延迟。
4. 对于高位深图像（如 16 位），`SigmaColor` 的语义会随位深变化，需要相应调整数值。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充双边滤波的数学原理（空间+灰度双重权重）、修正调用链、细化参数语义、新增性能分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
