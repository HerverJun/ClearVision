# 图像相减 / ImageSubtract

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageSubtractOperator` |
| 枚举值 (Enum) | `OperatorType.ImageSubtract` |
| 分类 (Category) | Preprocessing |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子计算两幅图像的逐像素差值，支持两种模式：

**绝对差值模式**（`AbsoluteDiff=true`，默认）：
```
dst(x,y) = |Image1(x,y) - Image2(x,y)|
```
通过 `Cv2.Absdiff(src1, src2, dst)` 实现，结果始终非负，避免无符号 8-bit 下溢截断。

**有符号减法模式**（`AbsoluteDiff=false`）：
```
dst(x,y) = Image1(x,y) - Image2(x,y)
```
通过 `Cv2.Subtract(src1, src2, dst)` 实现，差值为负时被截断为 0（对 8-bit 图像）。

算子同时输出三个统计指标：
- `MinDifference`：差异图最小像素值（通过 `Cv2.MinMaxLoc`）。
- `MaxDifference`：差异图最大像素值。
- `MeanDifference`：差异图平均像素值（通过 `Cv2.Mean`）。

若输入为彩色图像，统计指标基于灰度化后的差异图计算（`Cv2.CvtColor BGR2GRAY`）。

> English: The operator computes per-pixel subtraction or absolute difference via `Cv2.Absdiff`/`Cv2.Subtract`, then outputs min/max/mean statistics. Multi-channel differences are grayscale-reduced for statistics.

## 实现策略 / Implementation Strategy
- 默认使用 `Absdiff` 而非 `Subtract`，因为无符号 8-bit 减法的下溢截断会产生误导性结果（负差值变为 0）。
- 与 `ImageDiff` 不同，本算子提供 `AbsoluteDiff` 开关，允许有符号减法场景（如配合后续 `Normalize` 处理）。
- 尺寸不匹配时自动缩放 Image2 至 Image1 尺寸，而非直接报错，提升灵活性。
- 统计指标基于灰度化差异图，避免多通道非零判断的歧义。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image1", ...)`
2. `TryGetInputImage(inputs, "Image2", ...)`
3. `GetBoolParam(@operator, "AbsoluteDiff", true)`
4. `image1Wrapper.GetMat()` / `image2Wrapper.GetMat()`
5. **尺寸不匹配时**：`Cv2.Resize(src2, resized2, src1.Size())`
6. **AbsoluteDiff=true**：`Cv2.Absdiff(src1, src2, dst)`
7. **AbsoluteDiff=false**：`Cv2.Subtract(src1, src2, dst)`
8. **多通道时**：`Cv2.CvtColor(dst, statsSource, BGR2GRAY)`
9. `Cv2.MinMaxLoc(statsSource, out minVal, out maxVal)`
10. `Cv2.Mean(statsSource).Val0`
11. `CreateImageOutput(dst, additionalData)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `AbsoluteDiff` | `bool` | `true` | `true` / `false` | `true` 使用绝对差值（`Absdiff`）；`false` 使用有符号减法（`Subtract`）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image1` | Image1 | `Image` | Yes | 被减数图像（减法左操作数）。 |
| `Image2` | Image2 | `Image` | Yes | 减数图像（减法右操作数）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Difference Image | `Image` | 差值结果图像。 |
| `MinDifference` | Min Difference | `Float` | 差异图像素最小值。 |
| `MaxDifference` | Max Difference | `Float` | 差异图像素最大值。 |
| `MeanDifference` | Mean Difference | `Float` | 差异图像素平均值。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `AbsoluteDiff` | `Boolean` | 实际使用的差值模式。 |
| `MinDifference` | `Double` | 差异图最小像素值。 |
| `MaxDifference` | `Double` | 差异图最大像素值。 |
| `MeanDifference` | `Double` | 差异图平均像素值。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W x H)，线性于像素总数。 |
| 典型耗时 (Typical Latency) | 1080p 图像约 2-5 ms（含灰度转换和统计计算）。 |
| 内存特征 (Memory Profile) | 额外分配一幅差异图 Mat 和一幅灰度统计用 Mat（多通道时）。尺寸不匹配时额外分配缩放 Mat。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：与标准模板图做差值，量化生产样品的外观偏差程度。
- **适合 (Suitable)**：绝对差值模式下快速定位两幅图像中发生变化的区域。
- **适合 (Suitable)**：有符号减法模式下获取方向性差异（哪边更亮/更暗），配合后续归一化处理。
- **不适合 (Not Suitable)**：仅需差异率（DiffRate）指标而不需要差异图的场景，使用 `ImageDiff` 更轻量。
- **不适合 (Not Suitable)**：两幅图像存在位移或旋转差异的场景，需先做配准对齐。

## 已知限制 / Known Limitations
1. 有符号减法模式（`AbsoluteDiff=false`）对 8-bit 图像的负差值会被截断为 0，结果不对称。
2. 尺寸不匹配时自动缩放 Image2，缩放插值可能引入误差，影响差异精度。
3. 统计指标基于灰度化后的差异图，彩色空间的细微色差在灰度化后可能被掩盖。
4. MeanDifference 基于 `Cv2.Mean` 返回的 `Val0`，对单通道图像准确，但对多通道图像仅反映第一个通道的均值。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 Absdiff/Subtract 双模式、统计指标计算、尺寸自动缩放、API 调用链 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
