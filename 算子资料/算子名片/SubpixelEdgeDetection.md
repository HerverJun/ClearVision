# 亚像素边缘检测 / SubpixelEdgeDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SubpixelEdgeDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.SubpixelEdgeDetection` |
| 分类 (Category) | Feature Extraction / 特征提取 |
| 成熟度 (Maturity) | 实验性 Experimental |
| 标签 (Tags) | experimental, non-industrial-reference, subpixel-edge |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | edge-subpixel |

> **注意 / Warning**: 本算子标记为"非工业参考实现"(NonIndustrialReference)，不等同于经验证的工业级亚像素计量模型。在计量用途前必须经过 golden data、校准、重复性和 GR&R 验证。

## 算法原理 / Algorithm Principle
亚像素边缘检测在像素级 Canny 边缘基础上，通过梯度插值将边缘定位精度提升到亚像素级别。本算子支持三种方法：

**GradientInterp（梯度插值法）**：
1. 高斯模糊降噪 -> Canny 二值边缘 -> `FindContours` 提取候选轮廓点。
2. 对每个候选点，沿梯度方向（归一化 Sobel 梯度）在两侧各采样一个灰度值 g1、g3。
3. 使用抛物线插值公式：`offset = 0.5 * (g1 - g3) / (g1 - 2*g2 + g3)`，其中 g2 为候选点灰度。
4. 亚像素坐标 = 候选点 + offset * 梯度方向单位向量。

**GaussianFit（高斯拟合法）**：
- 与 GradientInterp 相同的采样流程，但在偏移量计算后附加高斯衰减修正：`offset *= exp(-offset^2 / 2)`。
- 对噪声更鲁棒，但精度依赖于高斯模型假设。

**Steger（Hessian 脊线检测法）**：
- 使用独立的 `StegerSubpixelEdgeDetector` 组件，基于 Hessian 矩阵的脊线检测。
- 在高斯尺度空间中计算 Hessian 矩阵的特征值和特征向量，沿最大曲率方向做亚像素定位。
- `EdgeThreshold` 控制 Hessian 响应的最小强度阈值。
- `MaxOffset` 固定为 0.5 像素。

每个检测到的边缘点包含：`(X, Y)` 亚像素坐标、`(NormalX, NormalY)` 法线方向、`Strength` 梯度强度。

> English: Three subpixel methods are supported: GradientInterp (parabolic interpolation along gradient direction), GaussianFit (with Gaussian decay correction), and Steger (Hessian ridge detection). All produce subpixel coordinates with normal direction and strength.

## 实现策略 / Implementation Strategy
- Steger 模式使用独立的 `StegerSubpixelEdgeDetector` 类，通过 `using` 语句管理资源生命周期。
- GradientInterp/GaussianFit 模式通过 `DetectEdgesTraditional()` 实现：Canny -> FindContours -> Sobel 梯度 -> 沿梯度方向采样 -> 插值。
- 采样使用 `Cv2.Remap()` 批量执行，避免逐像素双线性插值的循环开销。
- 边缘候选点排除图像边界 1 像素（避免越界采样）。
- 结果图像上绘制绿色圆点（边缘位置）和蓝色短线（法线方向），并标注边缘计数。
- 输出包含多项诊断信息：`CapabilityLevel=NonIndustrialReference`、`IndustrialGradeModel=false`、`RequiresApplicationValidation=true`。
- CPU 密集型操作通过 `RunCpuBoundWork()` 封装，避免阻塞主线程。

> English: Steger mode uses a dedicated Hessian ridge detector. Traditional methods use Canny + contour candidates + gradient sampling with batched remap for efficiency. Diagnostic metadata clearly marks the operator as non-industrial reference.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetDoubleParam(@operator, "LowThreshold" / "HighThreshold" / "Sigma" / "EdgeThreshold")`
3. `GetStringParam(@operator, "Method", "GradientInterp")` -- 选择方法
4. **Steger 路径**：
   - `new StegerSubpixelEdgeDetector { Sigma, EdgeThreshold, MaxOffset=0.5 }`
   - `detector.DetectEdges(src, lowThreshold, highThreshold)` -- Hessian 脊线检测
5. **GradientInterp/GaussianFit 路径**：
   - `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
   - `Cv2.GaussianBlur(gray, blurred, kernelSize, sigma)` -- 高斯平滑
   - `Cv2.Canny(blurred, edges, lowThreshold, highThreshold)` -- Canny 边缘
   - `Cv2.FindContours(edges, contours, ...)` -- 提取轮廓
   - `Cv2.Sobel(blurred, sobelX/sobelY, CV_64F, ...)` -- Sobel 梯度
   - `BuildCandidates(contours, gray, sobelX, sobelY)` -- 构建候选点（排除边界）
   - `SampleAlongGradient(gray, candidates, -1.0/+1.0)` -- 批量沿梯度方向采样（通过 Remap）
   - 抛物线插值计算亚像素偏移（GaussianFit 附加高斯衰减）
6. 结果可视化：`Cv2.Circle()` + `Cv2.Line()` + `Cv2.PutText()`
7. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `LowThreshold` | `double` | `50.0` | [0.0, 255.0] | Canny 低阈值（GradientInterp/GaussianFit 模式）。 |
| `HighThreshold` | `double` | `150.0` | [0.0, 255.0] | Canny 高阈值（GradientInterp/GaussianFit 模式）。必须大于 LowThreshold。 |
| `Sigma` | `double` | `1.0` | [0.1, 10.0] | 高斯模糊标准差。控制平滑程度；越大抑制噪声越强但边缘越粗。Steger 模式下控制 Hessian 尺度空间。 |
| `Method` | `enum` | `"GradientInterp"` | `Steger` / `GradientInterp` / `GaussianFit` | 亚像素检测方法。Steger 为 Hessian 脊线法；GradientInterp 为梯度插值法；GaussianFit 为高斯拟合法。 |
| `EdgeThreshold` | `double` | `10.0` | [0.0, 1000.0] | Steger 模式的 Hessian 响应强度阈值。仅 Steger 方法生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Input Image | `Image` | Yes | 待检测的输入图像（支持彩色和灰度）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Result Image | `Image` | 可视化结果图，标注了亚像素边缘点（绿色圆点）和法线方向（蓝色短线）。 |
| `Edges` | Edge Points | `Any` | 亚像素边缘点列表，每个点包含 `X`、`Y`、`NormalX`、`NormalY`、`Strength` 字段。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | GradientInterp/GaussianFit：O(W*H) Canny + O(N) 轮廓提取 + O(N) 梯度采样与插值（N 为候选点数）。Steger：O(W*H*sigma) Hessian 卷积 + O(W*H) 脊线追踪。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像，GradientInterp 约 20-80ms；Steger 约 30-120ms（取决于 sigma 和边缘密度）。 |
| 内存特征 (Memory Profile) | 需分配灰度图、模糊图、Canny 边缘图、Sobel 梯度图、结果图等多张中间 Mat。峰值约为输入图像 5-8 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：需要比像素级 Canny 更精确的边缘定位场景。
- **适合 (Suitable)**：圆拟合、线拟合等几何拟合前的边缘点提取。
- **适合 (Suitable)**：Steger 模式适合有明显亮线/暗线的场景（如激光线、刻痕）。
- **适合 (Suitable)**：GradientInterp 模式适合一般边缘检测场景的快速亚像素化。
- **不适合 (Not Suitable)**：需要工业计量级精度的场景（本算子为非工业参考实现）。
- **不适合 (Not Suitable)**：高噪声图像且 sigma 设置过小的场景（会产生大量伪边缘点）。
- **不适合 (Not Suitable)**：需要 Zernike 矩等高精度亚像素方法的场景。

## 已知限制 / Known Limitations
1. **非工业参考实现**：输出包含 `CapabilityLevel=NonIndustrialReference` 和 `RequiresApplicationValidation=true` 标记，不可直接作为计量依据。
2. GradientInterp/GaussianFit 的抛物线插值假设边缘响应为抛物线形状，在强噪声或弱边缘时可能偏差较大。
3. 候选点排除图像边界 1 像素，边缘区域可能遗漏。
4. Steger 模式的 `MaxOffset` 固定为 0.5，不支持自定义。
5. 结果可视化绘制在原始图像克隆上，不支持单独输出空白边缘图。
6. 不支持多尺度边缘检测或边缘细化后处理。
7. `LowThreshold >= HighThreshold` 时参数校验失败。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（三种亚像素方法数学描述）、实现策略（批量 Remap 采样、Steger Hessian 脊线）、参数语义、实验性标记说明、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
