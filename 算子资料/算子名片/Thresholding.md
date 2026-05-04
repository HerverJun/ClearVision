# 二值化 / Thresholding

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ThresholdOperator` |
| 枚举值 (Enum) | `OperatorType.Thresholding` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
阈值分割（Thresholding）是最基本的图像分割方法，通过将像素灰度值与阈值比较，将图像分为前景和背景两类。该算子支持以下 6 种阈值类型：

- **Binary（二值）**：像素值 > 阈值则设为 MaxValue，否则设为 0。输出纯黑白图像。
- **Binary Inv（反向二值）**：与 Binary 相反，像素值 > 阈值则设为 0，否则设为 MaxValue。
- **Trunc（截断）**：像素值 > 阈值则截断为阈值，否则保持不变。效果：限制最大亮度。
- **To Zero（保底）**：像素值 > 阈值则保持不变，否则设为 0。效果：保留亮区域。
- **To Zero Inv（反向保底）**：像素值 > 阈值则设为 0，否则保持不变。效果：保留暗区域。

此外支持两种自动阈值算法，可与上述基础类型组合：

- **Otsu**：基于图像灰度直方图，自动计算使类间方差最大的最优阈值。适合双峰分布的图像（前景和背景灰度明显分离）。
- **Triangle**：基于直方图的三角形法，自动计算阈值。适合单峰分布的图像（如大部分为背景、少量前景）。

> English: Thresholding segments an image into foreground and background by comparing pixel values against a threshold. Supports 5 base types (Binary, BinaryInv, Trunc, ToZero, ToZeroInv) and 2 automatic threshold algorithms (Otsu for bimodal histograms, Triangle for unimodal histograms). Otsu and Triangle can be combined with base types via bitwise OR.

## 实现策略 / Implementation Strategy
- **自动灰度转换**：输入图像若为多通道，会先通过 `Cv2.CvtColor(BGR2GRAY)` 转换为单通道灰度图，再执行阈值操作。灰度图直接复制使用。
- **Otsu/Triangle 与基础类型组合**：通过位运算实现灵活组合。Otsu（值 8）和 Triangle（值 16）是标志位，可与基础类型（0-4）做 OR 组合。例如 Type=8 表示 Otsu+Binary，Type=9 表示 Otsu+BinaryInv。代码通过 `TryResolveThresholdType` 验证组合的合法性。
- **互斥校验**：Otsu 和 Triangle 不能同时启用（它们的位标志不能同时设置），`UseOtsu` 参数也不能与 Triangle 类型组合。违反时返回明确错误。
- **UseOtsu 兼容参数**：除了通过 Type 的位标志启用 Otsu 外，还可以通过独立的 `UseOtsu` 布尔参数启用。两者最终通过 OR 合并到 thresholdType 中。
- **ActualThreshold 反馈**：当使用 Otsu 或 Triangle 时，OpenCV 会自动计算实际使用的阈值，该值通过 `ActualThreshold` 附加字段返回，供下游参考。
- **输出克隆**：最终结果通过 `binary.Clone()` 输出，避免共享内部 Mat。

> English: Multi-channel images are auto-converted to grayscale via `Cv2.CvtColor(BGR2GRAY)`. Otsu (value 8) and Triangle (value 16) are bitmask flags combinable with base types (0-4) via bitwise OR. Mutual exclusion validated: Otsu and Triangle cannot coexist. `UseOtsu` boolean parameter provides an alternative way to enable Otsu. Actual computed threshold returned via `ActualThreshold` additional field.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetDoubleParam(@operator, "Threshold", 127.0, min: 0, max: 255)` -- 读取阈值
3. `GetDoubleParam(@operator, "MaxValue", 255.0, min: 0, max: 255)` -- 读取最大值
4. `GetIntParam(@operator, "Type", 0)` -- 读取阈值类型
5. `GetBoolParam(@operator, "UseOtsu", false)` -- 读取 UseOtsu 标志
6. `TryResolveThresholdType(typeValue, useOtsu, out thresholdType, out thresholdError)` -- 解析并验证阈值类型组合
7. `Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY)` -- 多通道转灰度（如需）
8. `Cv2.Threshold(gray, binary, threshold, maxValue, thresholdType)` -- 执行阈值分割，返回实际阈值
9. `binary.Clone()` -- 克隆输出
10. `CreateImageOutput(binary.Clone(), additionalData)` -- 封装输出，附带 ActualThreshold / OtsuThreshold

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Threshold` | `double` | `127.0` | [0.0, 255.0] | 手动阈值。像素灰度值与该值比较来决定前景/背景。使用 Otsu 或 Triangle 时该值作为初始值，实际阈值会被自动计算覆盖。 |
| `MaxValue` | `double` | `255.0` | [0.0, 255.0] | Binary 和 BinaryInv 模式下的前景赋值。通常设为 255（白色）。 |
| `Type` | `enum` | `"0"` | 0=Binary, 1=BinaryInv, 2=Trunc, 3=ToZero, 4=ToZeroInv, 8=Otsu, 16=Triangle | 阈值类型。Otsu(8) 和 Triangle(16) 是标志位，可与基础类型组合（如 8=Otsu+Binary, 9=Otsu+BinaryInv）。 |
| `UseOtsu` | `bool` | `false` | true / false | 是否启用 Otsu 自动阈值。等效于在 Type 中设置 Otsu 标志位。不能与 Triangle 类型同时使用。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待二值化的输入图像。多通道图像会自动转换为灰度图。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 二值化后的单通道输出图像，尺寸与输入相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |
| `ActualThreshold` | `Double` | 实际使用的阈值。手动模式下等于 Threshold 参数；Otsu/Triangle 模式下为自动计算的阈值。 |
| `OtsuThreshold` | `Double` | 仅在使用 Otsu 算法时输出，值与 ActualThreshold 相同。用于明确标识 Otsu 算法被触发。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N)，其中 N 为像素总数。Otsu 和 Triangle 的直方图计算也是 O(N)。 |
| 典型耗时 (Typical Latency) | 1080p 图像约 1-2ms（CPU）。灰度转换额外增加约 0.5ms。Otsu/Triangle 自动阈值计算的额外开销可忽略。 |
| 内存特征 (Memory Profile) | 需要灰度转换的中间 Mat（如输入为彩色）、二值化结果 Mat 和克隆输出。峰值内存约为输入图像的 3 倍（彩色输入时）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：文档扫描和 OCR 预处理，Binary 模式将灰度文档转为纯黑白以提高文字识别率。
- **适合 (Suitable)**：缺陷检测中的前景提取，Binary 模式配合合适的阈值将缺陷区域从背景中分离。
- **适合 (Suitable)**：双峰分布图像的自适应分割，Otsu 模式自动找到使类间方差最大的最优阈值，无需手动调参。
- **适合 (Suitable)**：保留特定灰度范围的像素，ToZero/ToZeroInv 模式可选择性地保留亮区或暗区。
- **适合 (Suitable)**：亮度截断，Trunc 模式可限制图像的最大灰度值。
- **不适合 (Not Suitable)**：光照不均匀的图像，全局阈值会导致部分区域过分割而另一部分欠分割，应先做光照校正或使用自适应阈值。
- **不适合 (Not Suitable)**：前景和背景灰度重叠严重的图像，单一阈值无法有效分离，应考虑多阈值或基于区域的分割方法。
- **不适合 (Not Suitable)**：需要保留灰度渐变信息的场景，二值化会永久丢失灰度细节。

## 已知限制 / Known Limitations
1. 多通道输入会被静默转换为灰度图，输出始终为单通道。下游若期望多通道输出，需要自行合并通道。
2. Otsu 和 Triangle 不能同时使用，`UseOtsu` 也不能与 Triangle 类型组合。违反时返回错误而非自动选择一种。
3. `UseOtsu` 参数与 Type 中的 Otsu 标志位（值 8）功能重叠，两者同时设置时通过 OR 合并，不会冲突但可能导致配置混淆。
4. 自动阈值算法（Otsu/Triangle）在双峰不明显或图像过于均匀时可能计算出不理想的阈值，`ActualThreshold` 字段可帮助诊断。
5. Threshold 和 MaxValue 为 double 类型但实际灰度值为 0-255 整数范围，传入小数值会被 OpenCV 内部截断或四舍五入。
6. 输出通过 `binary.Clone()` 创建独立副本，比直接传递 Mat 多一次内存拷贝，但确保了输出安全性。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充算法原理（6 种阈值类型的数学定义、Otsu/Triangle 自动阈值原理）、实现策略（位标志组合、UseOtsu 兼容、互斥校验、ActualThreshold 反馈）、完整参数语义、API 调用链、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
