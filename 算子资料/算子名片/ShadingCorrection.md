# 光照校正 / ShadingCorrection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ShadingCorrectionOperator` |
| 枚举值 (Enum) | `OperatorType.ShadingCorrection` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
光照校正（Shading Correction / Flat-Field Correction）用于补偿因光源不均匀、镜头渐晕或物体表面曲率导致的图像亮度不一致。该算子支持三种校正方法：

**1. DivideByBackground（背景除法）**
经典的平场校正算法。给定一张背景参考图（无物体时拍摄的空场图），将原图逐像素除以背景图，再乘以背景图的平均亮度作为目标灰度。公式：`corrected = (src / (background + eps)) * mean(background)`。其中 eps=1.0 防止除零。

**2. GaussianModel（高斯模型）**
不需要背景参考图的方法。使用大核高斯模糊对原图做低通滤波，估计出光照分布（即背景分量），然后用与 DivideByBackground 相同的除法公式消除光照不均匀。核大小应大于被检物体的典型尺寸，以确保物体特征被平滑掉而只保留光照梯度。

**3. MorphologicalTopHat（形态学顶帽）**
使用形态学开运算（先腐蚀后膨胀）估计背景，然后用原图减去开运算结果提取亮细节。使用椭圆形结构元素，核大小应大于被检物体的典型尺寸。适合提取暗背景上的亮缺陷（如划痕、污点）。

> English: Shading correction compensates for uneven illumination. Three methods: (1) DivideByBackground -- classic flat-field correction dividing by a reference background image; (2) GaussianModel -- estimates background via large-kernel Gaussian blur then divides; (3) MorphologicalTopHat -- uses morphological opening to estimate background then subtracts. The GaussianModel and MorphologicalTopHat methods do not require a separate background image.

## 实现策略 / Implementation Strategy
- **颜色空间处理**：支持单通道灰度图和 3 通道彩色图。对彩色图提供两种模式——`LumaOnly`（仅校正亮度通道，保持色度不变）和 `PerChannel`（逐通道独立校正）。LumaOnly 模式通过 BGR -> YUV 转换提取 Y 通道处理，再转回 BGR；PerChannel 模式通过 `Cv2.Split` 分离通道独立处理后 `Cv2.Merge` 合并。
- **位深自适应**：内部计算统一转换为 32 位浮点（CV_32FC1）进行，避免 8 位整数除法的精度损失。处理完成后根据输入图像的位深（8U/16U/32F/64F）自动转换回原始类型，超出范围的值会被 clamp。
- **核大小自动修正**：KernelSize 会被自动修正为奇数（偶数 +1），确保高斯模糊和形态学操作的锚点在核中心。
- **背景图尺寸自适应**：DivideByBackground 模式下，如果背景图与输入图尺寸不同，会自动用 `Cv2.Resize` 缩放背景图。
- **浮点精度保护**：除法计算中加入 eps=1.0 的偏移量防止除零，目标灰度取 `max(1.0, mean(background))` 确保输出不会过度暗化。

> English: Supports grayscale and 3-channel color images. Color images can be corrected in LumaOnly mode (BGR->YUV, correct Y channel, YUV->BGR) or PerChannel mode (split, correct each, merge). Internal computation uses CV_32FC1 for precision. KernelSize auto-corrected to odd. Background image auto-resized if dimensions mismatch. Division uses eps=1.0 to prevent division by zero.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `GetStringParam(@operator, "Method", "GaussianModel")` -- 读取校正方法
3. `ToOdd(GetIntParam(@operator, "KernelSize", 51, 3, 501))` -- 读取并修正核大小
4. `GetStringParam(@operator, "ColorMode", "LumaOnly")` -- 读取颜色模式
5. `TryGetInputImage(inputs, "Background", out backgroundWrapper)` -- DivideByBackground 模式下获取背景图

**GaussianModel 路径：**
6. `Cv2.GaussianBlur(gray, background, new Size(kernelSize, kernelSize), 0)` -- 高斯模糊估计背景
7. `gray.ConvertTo(src32, MatType.CV_32FC1)` -- 转浮点
8. `background.ConvertTo(bg32, MatType.CV_32FC1)` -- 转浮点
9. `Cv2.Add(bg32, eps, denom)` -- 加 eps 防除零
10. `Cv2.Divide(src32, denom, corrected32, targetLevel)` -- 除法校正
11. `ConvertBackToSourceDepth(corrected32, gray)` -- 转回原始位深

**MorphologicalTopHat 路径：**
6. `Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize))` -- 创建椭圆结构元素
7. `Cv2.MorphologyEx(gray, result, MorphTypes.TopHat, kernel)` -- 形态学顶帽

**DivideByBackground 路径：**
6. `Cv2.Resize(backgroundChannel, resizedBg, gray.Size())` -- 背景图缩放（如需）
7. `Cv2.Divide(src32, denom, corrected32, targetLevel)` -- 除法校正

**LumaOnly 颜色模式（附加步骤）：**
- `Cv2.CvtColor(src, yuv, ColorConversionCodes.BGR2YUV)` -- BGR 转 YUV
- `Cv2.Split(yuv, out channels)` -- 分离 Y/U/V
- (对 Y 通道执行上述校正)
- `Cv2.Merge(channels, merged)` -- 合并通道
- `Cv2.CvtColor(merged, result, ColorConversionCodes.YUV2BGR)` -- YUV 转 BGR

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"GaussianModel"` | DivideByBackground, GaussianModel, MorphologicalTopHat | 校正方法。GaussianModel 最常用（无需背景图）；DivideByBackground 精度最高但需额外拍摄背景；MorphologicalTopHat 适合亮缺陷检测。 |
| `KernelSize` | `int` | `51` | [3, 501] | 高斯模糊或形态学操作的核大小（像素）。偶数会自动 +1。核大小应大于被检物体的典型尺寸，以确保只估计光照分布而非物体特征。 |
| `ColorMode` | `enum` | `"LumaOnly"` | LumaOnly, PerChannel | 彩色图像的处理模式。LumaOnly 仅校正亮度（保持色度），PerChannel 逐通道独立校正（可能改变颜色平衡）。灰度图忽略此参数。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待校正的输入图像，支持 1 通道和 3 通道。 |
| `Background` | Background | `Image` | No | 背景参考图（空场图）。仅在 Method=DivideByBackground 时必填。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 光照校正后的输出图像，尺寸与输入相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |
| `Method` | `String` | 实际执行的校正方法名称。 |
| `ColorMode` | `String` | 实际使用的颜色模式（灰度图显示 "Gray"）。 |
| `Channels` | `Integer` | 输出图像的通道数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | GaussianModel: O(N * k^2)（高斯模糊主导）；DivideByBackground: O(N)（除法操作）；MorphologicalTopHat: O(N * k^2)（形态学操作）。PerChannel 模式下时间约为 LumaOnly 的 2-3 倍。 |
| 典型耗时 (Typical Latency) | 1080p 图像、KernelSize=51：GaussianModel 约 10-20ms，MorphologicalTopHat 约 15-30ms，DivideByBackground 约 5-10ms（不含背景图加载）。LumaOnly 模式约比 PerChannel 快 2 倍。 |
| 内存特征 (Memory Profile) | 除输入输出外，需要浮点转换的中间 Mat（2x 输入大小）、背景估计 Mat、eps Mat 等。LumaOnly 模式额外需要 YUV 转换缓冲区。峰值内存约为输入图像的 5-8 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：机器视觉中因环形光源、条形光源或镜头渐晕导致的图像亮度不均匀补偿，特别是后续需要全局阈值分割的场景。
- **适合 (Suitable)**：使用 GaussianModel 方法作为通用的光照归一化预处理，无需额外拍摄背景参考图。
- **适合 (Suitable)**：高精度检测场景使用 DivideByBackground 方法，配合定期采集的空场背景图实现最佳校正效果。
- **适合 (Suitable)**：暗背景上的亮缺陷（划痕、污点）检测，使用 MorphologicalTopHat 方法直接提取缺陷特征。
- **不适合 (Not Suitable)**：光照变化剧烈且不连续的场景（如多光源交叉照射），大核高斯模糊会引入伪影。
- **不适合 (Not Suitable)**：KernelSize 设置过小（接近或小于被检物体尺寸），会导致物体特征被当作光照梯度消除。
- **不适合 (Not Suitable)**：PerChannel 模式下对颜色一致性要求严格的场景，逐通道独立校正可能改变颜色平衡。

## 已知限制 / Known Limitations
1. 仅支持 1 通道和 3 通道图像，不支持 4 通道（如 RGBA）。传入非支持通道数的图像会直接返回错误。
2. KernelSize 上限为 501，对于超高分辨率图像（如 8K）可能不够大，无法覆盖足够范围的光照梯度。
3. DivideByBackground 模式下，背景图与输入图的位深差异可能导致精度损失，内部虽有浮点转换但未做位深匹配校验。
4. LumaOnly 模式通过 BGR -> YUV -> BGR 转换链处理，两次颜色空间转换会引入舍入误差，对精度敏感的场景应考虑 PerChannel 模式。
5. `ColorMode` 参数在灰度输入时被忽略（固定为 "Gray"），但不会向用户发出提示，面板上仍显示用户选择的模式。
6. MorphologicalTopHat 方法使用椭圆形结构元素，不支持自定义形状。
7. 浮点图像（CV_32F/CV_64F）的范围检测逻辑假定 [0,1] 或 [0,255] 为常见范围，超出这些范围的非标准浮点图像可能被错误归一化。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充三种校正方法的算法原理（平场校正公式、高斯模型估计、形态学顶帽）、实现策略（LumaOnly/PerChannel 颜色模式、位深自适应、浮点精度保护）、完整参数语义、API 调用链（三条方法路径）、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
