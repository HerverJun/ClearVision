# 拉普拉斯锐化 / LaplacianSharpen

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LaplacianSharpenOperator` |
| 枚举值 (Enum) | `OperatorType.LaplacianSharpen` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子基于拉普拉斯二阶微分算子实现边缘增强锐化。拉普拉斯算子计算图像的二阶空间导数，在边缘处产生极值，将边缘信息叠加到原图即可增强边缘对比度。

流程如下：

1. **灰度转换**（彩色图像）：`Cv2.CvtColor(src, gray, BGR2GRAY)`。
2. **拉普拉斯边缘检测**：`Cv2.Laplacian(gray, laplacian, CV_32F, kernelSize, scale, delta)`。
   - 输出为 32-bit 浮点，可表示负值（拉普拉斯响应有正有负）。
   - `kernelSize` 控制卷积核大小（必须为奇数），值越大检测到的边缘越粗。
   - `scale` 为可选的微分缩放因子。
3. **转回 8-bit**：`Cv2.ConvertScaleAbs(laplacian, laplacian)`，取绝对值并截断到 [0, 255]。
4. **加权叠加锐化**：
   ```
   dst = src * 1.0 + laplacian * SharpenStrength
   ```
   通过 `Cv2.AddWeighted(src, 1.0, laplacian, sharpenStrength, 0, dst)` 实现。

对彩色图像，拉普拉斯在灰度通道上计算后通过 `Cv2.CvtColor(GRAY2BGR)` 扩展为 3 通道再叠加。

> English: The operator applies Laplacian edge detection on a grayscale channel, converts the result to 8-bit absolute values, then blends it with the original via `Cv2.AddWeighted` to enhance edges.

## 实现策略 / Implementation Strategy
- 拉普拉斯在灰度通道上计算而非逐通道处理，避免彩色图像的通道间边缘不一致导致伪彩色。
- 使用 `CV_32F` 输出类型避免 8-bit 截断丢失负值信息，`ConvertScaleAbs` 在最后一步取绝对值映射回 8-bit。
- 通过 `SharpenStrength` 参数控制叠加强度，而非固定权重，允许在不同场景下灵活调节锐化程度。
- 源码中实际读取 `Delta` 参数（未通过 `OperatorParam` 声明），传递给 `Cv2.Laplacian` 的 delta 偏移。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)`
2. `GetIntParam(@operator, "KernelSize", 3, 1, 7)` / `GetDoubleParam("Scale"/"Delta"/"SharpenStrength")`
3. `imageWrapper.GetMat()`
4. **彩色图像**：`Cv2.CvtColor(src, gray, BGR2GRAY)`
5. `Cv2.Laplacian(gray, laplacian, MatType.CV_32F, kernelSize, scale, delta)`
6. `Cv2.ConvertScaleAbs(laplacian, laplacian)` — 转回 8-bit
7. **彩色图像**：`Cv2.CvtColor(laplacian, laplacian3C, GRAY2BGR)`
8. `Cv2.AddWeighted(src, 1.0, laplacian, sharpenStrength, 0, dst)`
9. `CreateImageOutput(dst, additionalData)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelSize` | `int` | `3` | [1, 7] | 拉普拉斯卷积核大小，必须为奇数。偶数会被自动 +1。较大值检测更粗的边缘。 |
| `Scale` | `double` | `1.0` | [0.1, 10.0] | 拉普拉斯微分的可选缩放因子。 |
| `SharpenStrength` | `double` | `1.0` | [0, 5.0] | 边缘叠加强度。0 表示不锐化；1.0 为标准强度；大于 1.0 为过度锐化。 |

### 源码隐含参数 / Runtime-Used But Undeclared Parameters
以下参数在源码中被实际读取，但未通过 `OperatorParam` 对外声明：

| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Delta` | `double` | `0` | [-100, 100] | 拉普拉斯计算时的加法偏移。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待锐化的输入图像（支持灰度和彩色）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 锐化图像 | `Image` | 边缘增强后的输出图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `KernelSize` | `Integer` | 实际使用的卷积核大小。 |
| `Scale` | `Double` | 实际使用的微分缩放因子。 |
| `SharpenStrength` | `Double` | 实际使用的锐化强度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W x H x K^2)，K 为 KernelSize。KernelSize=3 时近似线性。 |
| 典型耗时 (Typical Latency) | 1080p KernelSize=3 约 2-5 ms；KernelSize=7 约 5-10 ms。 |
| 内存特征 (Memory Profile) | 额外分配一幅 32-bit 浮点 Laplacian Mat 和一幅 8-bit 绝对值 Mat；彩色图像还需灰度和 3 通道扩展临时 Mat。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：增强工业零件表面划痕、裂纹等线性缺陷的可见性。
- **适合 (Suitable)**：在 OCR 或条码识别前锐化模糊文字和条纹。
- **适合 (Suitable)**：配合适度的 SharpenStrength（0.5-1.5）改善轻微离焦图像的清晰度。
- **不适合 (Not Suitable)**：高噪声图像的锐化，拉普拉斯算子会同时放大高频噪声。
- **不适合 (Not Suitable)**：需要精确边缘定位的测量场景，应使用 Canny 或 Sobel 等定向边缘检测。

## 已知限制 / Known Limitations
1. `Delta` 参数在源码中实际生效但未通过 `OperatorParam` 声明，参数面板中不可见。
2. 偶数 `KernelSize` 会被静默 +1 为奇数，用户可能不知道实际使用的核大小。
3. 拉普拉斯算子对噪声敏感，高噪声图像在锐化前应先做降噪处理（如高斯模糊）。
4. `SharpenStrength > 1.0` 时可能出现像素值饱和截断，导致高光区域细节丢失。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充拉普拉斯二阶微分原理、灰度通道处理策略、Delta 隐含参数、API 调用链 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
