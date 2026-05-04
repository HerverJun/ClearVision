# 图像融合 / ImageBlend

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageBlendOperator` |
| 枚举值 (Enum) | `OperatorType.ImageBlend` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子基于 OpenCV `Cv2.AddWeighted` 实现两幅图像的加权混合（alpha blending）。公式为：

```
dst(x,y) = Alpha * Background(x,y) + Beta * Foreground(x,y) + Gamma
```

- `Alpha` 和 `Beta` 分别控制背景与前景的混合权重，取值范围 `[0, 1.0]`。
- `Gamma` 为全局亮度偏移量，取值范围 `[-255, 255]`，正值提亮、负值压暗。
- 当 `Alpha + Beta = 1.0` 且 `Gamma = 0` 时，输出为两幅图像的线性插值；当 `Alpha=1, Beta=1, Gamma=0` 时，等效于像素叠加。
- 若前景与背景尺寸不一致，算子会先将前景通过 `Cv2.Resize` 缩放至背景尺寸，再执行混合。

> English: The operator performs weighted alpha blending via `Cv2.AddWeighted`. When foreground and background dimensions differ, the foreground is resized to match before blending.

## 实现策略 / Implementation Strategy
- 采用 `Cv2.AddWeighted` 而非手动逐像素计算，利用 OpenCV 内部的 SIMD 优化和并行化，性能远优于 `Mat.ForEach` 循环。
- 尺寸不匹配时先缩放前景而非裁剪背景，保留背景完整视野，适合在背景图上叠加标注层或检测框图层的场景。
- 若需保留前景原始尺寸，可在上游插入 `ImageCrop` 或 `ImageResize` 预处理。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Background", ...)`
2. `TryGetInputImage(inputs, "Foreground", ...)`
3. `GetDoubleParam(@operator, "Alpha", 0.5, 0, 1.0)`
4. `GetDoubleParam(@operator, "Beta", 0.5, 0, 1.0)`
5. `GetDoubleParam(@operator, "Gamma", 0, -255, 255)`
6. `bgWrapper.GetMat()` / `fgWrapper.GetMat()`
7. `Cv2.Resize(foreground, resizedFg, background.Size())`（仅尺寸不匹配时）
8. `Cv2.AddWeighted(background, alpha, foreground, beta, gamma, dst)`
9. `CreateImageOutput(dst, additionalData)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Alpha` | `double` | `0.5` | [0, 1.0] | 背景图像权重。设为 1.0 时前景不叠加。 |
| `Beta` | `double` | `0.5` | [0, 1.0] | 前景图像权重。设为 0.0 时仅输出背景。 |
| `Gamma` | `double` | `0` | [-255, 255] | 全局亮度偏移。正值提亮，负值压暗。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Background` | 背景 | `Image` | Yes | 作为混合底图的背景图像。 |
| `Foreground` | 前景 | `Image` | Yes | 叠加到背景上的前景图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 融合图像 | `Image` | 加权混合后的输出图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Alpha` | `Double` | 当前实际使用的背景权重。 |
| `Beta` | `Double` | 当前实际使用的前景权重。 |
| `Gamma` | `Double` | 当前实际使用的亮度偏移。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W x H)，线性于像素总数。 |
| 典型耗时 (Typical Latency) | 1080p 图像约 1-3 ms（含尺寸不匹配时的 Resize）。 |
| 内存特征 (Memory Profile) | 额外分配一幅与背景同尺寸的输出 Mat；尺寸不匹配时额外分配一幅缩放后的前景 Mat。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：在检测结果图上叠加半透明标注层或缺陷框图层。
- **适合 (Suitable)**：将多光谱或多曝光图像融合为单张可视化图像。
- **适合 (Suitable)**：通过调节 Alpha/Beta 实现渐变过渡或淡入淡出效果。
- **不适合 (Not Suitable)**：需要逐像素条件混合（如掩码混合）的场景，应使用 `Cv2.CopyTo` 配合掩码。
- **不适合 (Not Suitable)**：前景存在透明通道（Alpha Channel）的 PNG 叠加，本算子不处理 Alpha 通道。

## 已知限制 / Known Limitations
1. 前景与背景尺寸不匹配时，算子自动缩放前景至背景尺寸，不保持宽高比，可能导致前景变形。
2. `Alpha` 和 `Beta` 没有强制约束 `Alpha + Beta <= 1.0`，两者之和超过 1.0 时可能出现像素值饱和截断。
3. 不支持带 Alpha 通道的四通道图像输入。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 AddWeighted 公式、尺寸不匹配处理、API 调用链和适用场景 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
