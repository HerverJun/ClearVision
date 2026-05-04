# 清晰度评估 / Sharpness Evaluation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `SharpnessEvaluationOperator` |
| 枚举值 (Enum) | `OperatorType.SharpnessEvaluation` |
| 分类 (Category) | 检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子通过多种梯度/聚焦度量方法评估图像清晰度，并输出锐利/模糊二值判定。

**四种评估方法：**

1. **Laplacian**：拉普拉斯方差法。`score = var(Laplacian(gray))`。对灰度图做 Laplacian 二阶微分，取方差作为清晰度指标。方差越大表示图像越锐利。
   默认阈值：`100.0`

2. **Brenner**：Brenner 梯度法。`score = (1/N) * sum((I(x+2,y) - I(x,y))^2)`。计算水平方向间隔 2 像素的灰度差平方和，归一化后作为清晰度指标。
   默认阈值：`30.0`

3. **Tenengrad**：Sobel 梯度能量法。`score = (1/N) * sum(Gx^2 + Gy^2)`。使用 Sobel 算子计算 X/Y 方向梯度，取梯度幅值平方和的均值。
   默认阈值：`800.0`

4. **SMD**（Sum of Modified Laplacian）：`score = (1/N) * sum(|I(x,y) - I(x+1,y)| + |I(x,y) - I(x,y+1)|)`。计算水平和垂直方向相邻像素灰度差绝对值之和。
   默认阈值：`10.0`

**判定逻辑**：`IsSharp = score >= threshold`，阈值可通过 `PerMethodDefault` 自动选择或 `Manual` 手动指定。

**分块均匀性评估**：将 ROI 分为最多 4x4 的 tile 网格（每 tile 32px），分别计算各 tile 的清晰度分数，再求总体标准差 `ScoreStdDev` 和标准误 `ScoreStdError`，用于评估清晰度的空间均匀性。

> English: The operator evaluates image sharpness using Laplacian variance, Brenner gradient, Tenengrad (Sobel energy), or SMD methods, makes a sharp/blur binary decision against a threshold, and assesses spatial uniformity via tile-based score variance.

## 实现策略 / Implementation Strategy
- ROI 通过 `MeasurementRoiHelper.ResolveRoi` 解析，支持默认全图。
- 输入统一转灰度后裁剪 ROI。
- 四种方法均为纯像素操作，不依赖 OpenCV 高级函数（除 Laplacian 和 Sobel）。
- 分块评分 `ComputeTileScores` 将 ROI 分为 `clamp(W/32, 1, 4) x clamp(H/32, 1, 4)` 个 tile，每个 tile 独立计算清晰度。
- 置信度基于 tile 间标准误 `ScoreStdError` 通过 `ComputeConfidenceFromUncertainty` 计算。
- 可视化在原图上绘制 ROI 矩形、方法名 + 分数、以及 Sharp/Blur 判定文字。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image")` -- 获取输入图像
2. `GetStringParam(@operator, "Method", "Laplacian")` -- 读取方法
3. `ResolveMethod(raw)` -- 校验方法名
4. `GetStringParam(@operator, "ThresholdMode", "PerMethodDefault")` -- 读取阈值模式
5. `ResolveThreshold(@operator, method, thresholdMode)` -- 解析阈值（自动或手动）
6. `MeasurementRoiHelper.ResolveRoi(@operator, src.Width, src.Height)` -- 解析 ROI
7. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
8. `new Mat(gray, roi)` -- ROI 裁剪
9. 清晰度计算（按 method 分支）：
   - `ComputeLaplacianVariance(roiGray)` -- Laplacian 方差
   - `ComputeBrenner(roiGray)` -- Brenner 梯度
   - `ComputeTenengrad(roiGray)` -- Tenengrad 能量
   - `ComputeSmd(roiGray)` -- SMD 绝对差
10. `ComputeTileScores(roiGray, method)` -- 分块评分
11. `Cv2.Rectangle(resultImage, roi, ...)` + `Cv2.PutText(...)` -- 可视化标注
12. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `Laplacian` | `Laplacian` / `Brenner` / `Tenengrad` / `SMD` | 清晰度评估方法。 |
| `ThresholdMode` | `enum` | `PerMethodDefault` | `PerMethodDefault` / `Manual` | 阈值选择模式。`PerMethodDefault` 使用方法内置默认阈值；`Manual` 使用手动指定的 Threshold。 |
| `Threshold` | `double` | `100.0` | `[0, +inf)` | 手动阈值，仅在 `ThresholdMode=Manual` 时生效。 |
| `RoiX` | `int` | `0` | 无限制 | ROI 左上角 X 坐标。 |
| `RoiY` | `int` | `0` | 无限制 | ROI 左上角 Y 坐标。 |
| `RoiW` | `int` | `0` | 无限制 | ROI 宽度。0 表示自动计算。 |
| `RoiH` | `int` | `0` | 无限制 | ROI 高度。0 表示自动计算。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Image` | `Image` | Yes | 输入图像，支持灰度与多通道。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Score` | `Score` | `Float` | 清晰度分数，值越大越锐利。 |
| `IsSharp` | `Is Sharp` | `Boolean` | 锐利判定结果，`true` 表示锐利，`false` 表示模糊。 |
| `Image` | `Image` | `Image` | 可视化图，标注 ROI 矩形、方法名、分数及 Sharp/Blur 判定。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Method` | `String` | 本次执行使用的评估方法。 |
| `ThresholdMode` | `String` | 本次执行使用的阈值模式。 |
| `ThresholdUsed` | `Double` | 本次执行实际使用的阈值。 |
| `DecisionReady` | `Boolean` | 是否有内置默认阈值（所有四种方法均为 `true`）。 |
| `NormalizedScore` | `Double` | 归一化分数 `Score / ThresholdUsed`。 |
| `MarginToThreshold` | `Double` | 分数与阈值之差 `Score - ThresholdUsed`。 |
| `TileCount` | `Integer` | 分块数量。 |
| `ScoreStdDev` | `Double` | 各 tile 分数的标准差，反映清晰度空间均匀性。 |
| `ScoreStdError` | `Double` | 标准误 `StdDev / sqrt(TileCount)`。 |
| `Confidence` | `Double` | 基于标准误计算的置信度。 |
| `UncertaintyPx` | `Double` | 标准误值。 |
| `StatusCode` | `String` | `OK`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Laplacian: `O(W*H)`；Brenner/SMD: `O(W*H)`；Tenengrad: `O(W*H)`（两次 Sobel）。分块评分为额外 `O(T * W_t * H_t)`，`T` 为 tile 数。 |
| 典型耗时 (Typical Latency) | 取决于 ROI 大小和方法选择，Tenengrad 略慢（两次 Sobel），其余方法开销接近。 |
| 内存特征 (Memory Profile) | 灰度图 + ROI 裁剪图 + 分块临时 Mat（每个 tile 独立分配释放），峰值约为输入的 2-3 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：自动对焦系统中评估镜头聚焦质量。
- **适合 (Suitable)**：在线检测中判断图像是否因运动或失焦而模糊。
- **适合 (Suitable)**：光源/镜头选型时对比不同配置的成像锐度。
- **适合 (Suitable)**：需要空间均匀性评估的场景（通过 tile 标准差判断画面边缘是否失焦）。
- **不适合 (Not Suitable)**：纹理丰富但实际模糊的图像（如毛玻璃后拍摄），梯度方法可能误判。
- **不适合 (Not Suitable)**：低对比度但锐利的图像（如均匀表面上的微小划痕），分数可能偏低。

## 已知限制 / Known Limitations
1. 所有方法在灰度图上操作，彩色信息被丢弃。
2. 默认阈值基于一般场景经验值，特定应用可能需要手动调整。
3. Brenner 方法仅考虑水平方向梯度，对垂直纹理不敏感。
4. 分块评分的 tile 大小固定为 32px，无法自定义。
5. `NormalizedScore` 在 `ThresholdUsed` 接近 0 时返回 NaN。
6. 可视化标注文字固定在左上角，可能遮挡 ROI 区域内的细节。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写至金标准 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
