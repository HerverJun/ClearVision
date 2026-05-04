# Canny 边缘检测 / CannyEdgeDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CannyEdgeOperator` |
| 枚举值 (Enum) | `OperatorType.EdgeDetection` |
| 分类 (Category) | Feature Extraction / 特征提取 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Edge, Canny, Contour, Threshold |
| 图标 (Icon) | edge |

## 算法原理 / Algorithm Principle
本算子支持两种边缘检测方法：

**Canny 边缘检测**（经典方法）：
Canny 算子是一种多阶段边缘检测算法：
1. **高斯平滑**：用高斯核对灰度图做卷积，抑制高频噪声。
2. **梯度计算**：用 Sobel 算子计算水平和垂直梯度 Gx、Gy。
3. **非极大值抑制**：沿梯度方向保留局部极大值，细化边缘。
4. **双阈值滞后**：用低阈值 `Threshold1` 和高阈值 `Threshold2` 做滞后阈值处理。高于高阈值的为强边缘，低于低阈值的被抑制，介于两者之间的仅在与强边缘连通时保留。

**自动阈值策略**（`AutoThreshold=true`）：
- **MedianIntensity**：以灰度中值为基础，`T1 = (1-sigma)*median`，`T2 = (1+sigma)*median`。
- **GradientPercentile**：计算 Sobel 梯度幅值，取 70% 和 90% 分位数作为低/高阈值。
- **RecallGuardPercentile**：取梯度幅值 50% 和 82% 分位数，更保守的阈值以保留更多边缘。
- **OtsuGradient**：对归一化梯度幅值图做 Otsu 阈值分割，据此推导 Canny 双阈值。

**ONNX 深度学习边缘检测**（`Method=OnnxEdge`）：
- 加载 ONNX 边缘检测模型，将输入图像 resize 到模型要求尺寸后推理。
- 输出概率图经 sigmoid 归一化到 [0,1]，再用 `EdgeBinarizationThreshold` 二值化。
- 支持通过 `EdgeModelPath` 直接指定模型路径，或通过 `EdgeModelId` + `ModelCatalogPath` 从模型目录解析。

> English: The operator supports classical Canny edge detection (with optional auto-thresholding via 4 strategies) and ONNX deep learning edge detection. Auto-threshold strategies include MedianIntensity, GradientPercentile, RecallGuardPercentile, and OtsuGradient.

## 实现策略 / Implementation Strategy
- 输入图像先通过 `EnsureSingleChannelGray()` 转灰度，再 `ConvertSingleChannelToByte()` 转 8 位，确保 Canny 算子兼容。
- 高斯模糊核大小自动保证为奇数（偶数时 +1）。
- 自动阈值策略中，GradientPercentile 通过对梯度幅值图做亚采样（最多 262144 个样本）估算分位数，避免全图排序的高开销。
- ONNX 模型输入通过 `ModelCatalog.ResolveExplicitOrCatalog()` 解析，支持直接路径和目录查找两种方式。
- ONNX 推理输出自动适配多种张量维度格式（2D/3D/4D），并对超出 [0,1] 范围的原始 logits 自动应用 sigmoid。
- 所有路径均输出 `EdgePixelRatio`（边缘像素占比），供下游质量评估使用。

> English: Input is normalized to 8-bit grayscale before processing. Gradient percentile estimation uses subsampling for efficiency. ONNX inference auto-adapts to various tensor dimension formats and applies sigmoid normalization when needed.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `OperatorImageDepthHelper.EnsureSingleChannelGray(src)` -- 灰度转换
3. `OperatorImageDepthHelper.ConvertSingleChannelToByte(gray)` -- 8 位归一化
4. **Canny 路径**：
   - `Cv2.GaussianBlur(workingGray, processedSrc, kernelSize, 1.0)` -- 高斯平滑（可选）
   - 自动阈值计算（根据 `AutoThresholdStrategy` 选择算法）
   - `Cv2.Canny(processedSrc, dst, threshold1, threshold2, apertureSize, l2Gradient)` -- Canny 边缘检测
   - `Cv2.CountNonZero(dst)` -- 计算边缘像素比
5. **OnnxEdge 路径**：
   - `ModelCatalog.ResolveExplicitOrCatalog(...)` -- 解析模型路径
   - `new InferenceSession(modelPath)` -- 加载 ONNX 模型
   - `BuildOnnxEdgeInput(src, width, height)` -- 构建 NCHW 张量（RGB 归一化到 [0,1]）
   - `session.Run([NamedOnnxValue])` -- 推理
   - `EdgeOutputToProbabilityMap(...)` -- 输出转概率图（含 sigmoid 归一化）
   - `Cv2.Threshold(probabilityByte, dst, threshold*255, 255, Binary)` -- 二值化
6. `CreateImageOutput(dst, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"Canny"` | `Canny` / `OnnxEdge` | 边缘检测方法。Canny 为经典算法；OnnxEdge 为 ONNX 深度学习模型。 |
| `Threshold1` | `double` | `50.0` | [0.0, 255.0] | Canny 低阈值。梯度低于此值的边缘被抑制。AutoThreshold=true 时被自动计算覆盖。 |
| `Threshold2` | `double` | `150.0` | [0.0, 255.0] | Canny 高阈值。梯度高于此值的为强边缘。AutoThreshold=true 时被自动计算覆盖。 |
| `AutoThreshold` | `bool` | `false` | true / false | 是否启用自动阈值计算。启用后 Threshold1/Threshold2 被自动覆盖。 |
| `AutoThresholdSigma` | `double` | `0.33` | [0.01, 1.0] | MedianIntensity 策略的 sigma 系数。越大阈值范围越宽。 |
| `AutoThresholdStrategy` | `enum` | `"MedianIntensity"` | `MedianIntensity` / `GradientPercentile` / `RecallGuardPercentile` / `OtsuGradient` | 自动阈值策略。 |
| `EnableGaussianBlur` | `bool` | `true` | true / false | 是否在 Canny 前做高斯模糊降噪。 |
| `GaussianKernelSize` | `int` | `5` | [3, 15] | 高斯核大小（奇数）。偶数自动+1。 |
| `ApertureSize` | `enum` | `"3"` | `3` / `5` / `7` | Sobel 算子孔径大小。越大对噪声越鲁棒但边缘越粗。 |
| `L2Gradient` | `bool` | `false` | true / false | 是否使用 L2 范数计算梯度幅值。更精确但稍慢。 |
| `EdgeModelPath` | `file` | `""` | - | ONNX 边缘检测模型文件路径。仅 OnnxEdge 方法生效。 |
| `EdgeModelId` | `string` | `""` | - | 模型目录中的模型 ID。仅 OnnxEdge 方法生效。 |
| `ModelCatalogPath` | `file` | `""` | - | 模型目录文件路径。仅 OnnxEdge 方法生效。 |
| `EdgeBinarizationThreshold` | `double` | `0.5` | [0.0, 1.0] | ONNX 输出概率图的二值化阈值。仅 OnnxEdge 方法生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待检测的输入图像（支持彩色和灰度）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 边缘检测结果图（二值边缘图）。 |
| `Edges` | Edges | `Image` | 边缘图的 PNG 字节数据（运行时附加输出）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Canny：O(W*H) 高斯模糊 + O(W*H) Sobel + O(W*H) 非极大值抑制 + O(W*H) 滞后阈值。OnnxEdge：O(W*H) resize + 模型推理（取决于模型复杂度）+ O(W*H) 后处理。 |
| 典型耗时 (Typical Latency) | Canny 模式 1920x1080 约 5-15ms。OnnxEdge 模式取决于模型大小，通常 50-500ms。 |
| 内存特征 (Memory Profile) | Canny：灰度图 + 模糊图 + 边缘结果图。OnnxEdge：额外分配模型输入张量和概率图。峰值约为输入图像 2-4 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：工业缺陷检测中的边缘提取预处理。
- **适合 (Suitable)**：轮廓提取前的二值化边缘图生成。
- **适合 (Suitable)**：自动阈值模式适合光照不均匀、需要自适应的场景。
- **适合 (Suitable)**：ONNX 模式适合需要深度学习精度但不想部署独立推理服务的场景。
- **不适合 (Not Suitable)**：需要亚像素级边缘定位精度的任务（应使用 SubpixelEdgeDetection）。
- **不适合 (Not Suitable)**：需要保留边缘方向和梯度强度信息的场景（输出为二值图）。
- **不适合 (Not Suitable)**：高噪声图像且未开启高斯模糊的场景（会产生大量伪边缘）。

## 已知限制 / Known Limitations
1. Canny 模式下 Sobel 孔径大小限制为 3/5/7，不支持更大的核。
2. 高斯核大小上限 15，对强噪声场景可能不够。
3. 自动阈值的 GradientPercentile 策略使用亚采样估算分位数，结果有一定随机性。
4. ONNX 模型推理不支持 GPU 加速（使用 CPU InferenceSession）。
5. ONNX 输出张量格式自动适配有限（支持 2D/3D/4D），非标准格式可能解析失败。
6. `L2Gradient=true` 时 Sobel 孔径必须为 3（OpenCV 限制）。
7. 输出边缘图为 8 位二值图，不保留梯度强度信息。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（Canny 多阶段流程、4 种自动阈值策略、ONNX 推理流程）、实现策略、详细参数语义（14 个参数全部覆盖）、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
