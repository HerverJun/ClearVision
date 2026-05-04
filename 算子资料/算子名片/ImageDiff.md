# 图像对比 / ImageDiff

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageDiffOperator` |
| 枚举值 (Enum) | `OperatorType.ImageDiff` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子计算两幅同尺寸图像的逐像素绝对差值，并输出差异率（DiffRate）作为量化指标。流程如下：

1. 使用 `Cv2.Absdiff(matA, matB, diff)` 计算逐像素绝对差值：`diff(x,y) = |A(x,y) - B(x,y)|`。
2. 若输入为彩色图像（3 通道），将差异图转换为灰度：`Cv2.CvtColor(diff, grayDiff, BGR2GRAY)`。
3. 计算差异率：`DiffRate = CountNonZero(grayDiff) / (W * H)`，即非零像素占总像素的比例。

差异率 `DiffRate` 的含义：值为 0 表示两幅图像完全相同；值为 1.0 表示每个像素都有差异。灰度阈值为 0，即任何非零差值都计入差异。

> English: The operator computes per-pixel absolute difference via `Cv2.Absdiff`, converts to grayscale for multi-channel inputs, then calculates DiffRate as the ratio of non-zero pixels to total pixels.

## 实现策略 / Implementation Strategy
- 使用 `Cv2.Absdiff` 而非 `Cv2.Subtract`，避免有符号减法导致的下溢截断问题（无符号 8-bit 像素相减若结果为负会被截断为 0）。
- 差异率计算基于灰度化后的差异图，避免多通道非零判断的歧义（如 RGB 三通道中仅一个通道非零的判定）。
- 两幅图像尺寸必须完全一致，不一致时直接报错而非自动缩放，避免缩放引入的插值误差干扰差异分析。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "BaseImage", ...)`
2. `TryGetInputImage(inputs, "CompareImage", ...)`
3. `imgA.GetMat()` / `imgB.GetMat()`
4. `Cv2.Absdiff(matA, matB, diff)` — 逐像素绝对差值
5. `diff.CvtColor(ColorConversionCodes.BGR2GRAY)` — 多通道时灰度化
6. `Cv2.CountNonZero(grayDiff)` — 统计非零像素数
7. `CreateImageOutput(diff.Clone())`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| （无用户可调参数） | - | - | - | 该算子不声明任何 OperatorParam。差异率 DiffRate 为自动计算输出。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `BaseImage` | 基准图 | `Image` | Yes | 差异分析的基准参考图像。 |
| `CompareImage` | 对比图 | `Image` | Yes | 与基准图进行对比的图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `DiffImage` | 差异图 | `Image` | 逐像素绝对差值结果图。 |
| `DiffRate` | 差异率 | `Float` | 非零差值像素占比，范围 [0, 1.0]。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `DiffRate` | `Double` | 非零差值像素占总像素的比例。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W x H)，线性于像素总数。 |
| 典型耗时 (Typical Latency) | 1080p 图像约 2-5 ms（含灰度转换和 CountNonZero）。 |
| 内存特征 (Memory Profile) | 额外分配一幅差异图 Mat 和一幅灰度差异图 Mat（多通道时）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：与标准样品图对比，检测生产过程中的外观缺陷或异物。
- **适合 (Suitable)**：验证图像预处理（如滤波、增强）前后是否引入非预期变化。
- **适合 (Suitable)**：监控固定场景中是否出现新物体或变化（安防/监控场景）。
- **不适合 (Not Suitable)**：需要定位具体差异区域或计算差异面积的场景，需配合阈值化和轮廓分析。
- **不适合 (Not Suitable)**：两幅图像存在位移或旋转差异的场景，需先做配准对齐。

## 已知限制 / Known Limitations
1. 两幅输入图像必须尺寸完全一致，不一致时直接返回失败，不自动缩放。
2. 差异率基于灰度化后的差异图计算，彩色空间的细微色差在灰度化后可能被掩盖。
3. `CountNonZero` 对任何非零值都计数，差值为 1（几乎无视觉差异）也会被计入差异率，可能在低噪声环境下给出偏高的差异率。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 Absdiff 算法、灰度化差异率计算、API 调用链和适用场景 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
