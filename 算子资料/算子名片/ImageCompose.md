# 图像组合 / ImageCompose

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageComposeOperator` |
| 枚举值 (Enum) | `OperatorType.ImageCompose` |
| 分类 (Category) | 拆分组合 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子将 2-4 张输入图像按指定布局模式组合为一张输出图像。支持四种组合模式：
1. **水平拼接 (Horizontal)**：将所有图像从左到右排列，高度取最大值，宽度为各图宽度之和加间距。
2. **垂直拼接 (Vertical)**：将所有图像从上到下排列，宽度取最大值，高度为各图高度之和加间距。
3. **网格拼接 (Grid)**：固定 2 列，行数为 `ceil(N/2)`，按行优先顺序填充，每个单元格取所有图像的最大宽高。
4. **通道合并 (ChannelMerge)**：将各输入图像转为灰度后合并为多通道图像（最多 4 通道），不足 3 通道时用全黑填充。适合将多个单通道处理结果（如不同阈值分割）合并为彩色可视化。

单通道图像在拼接前自动转为 BGR 三通道以保证通道一致性。背景区域用 `BackgroundColor` 参数指定的颜色填充。

> English: This operator composes 2-4 input images into a single output using one of four modes: horizontal concatenation, vertical concatenation, 2-column grid layout, or channel merge. Single-channel images are auto-converted to BGR before composition. Background padding uses the configured `BackgroundColor`. Channel merge converts inputs to grayscale and merges up to 4 channels, padding with black if fewer than 3.

## 实现策略 / Implementation Strategy
- **输入灵活性**：`Image1` 和 `Image2` 必填，`Image3` 和 `Image4` 可选，最少 2 张最多 4 张图像。
- **自动颜色空间转换**：`EnsureBgr` 将灰度图转为 BGR（拼接模式），`EnsureGray` 将 BGR 转为灰度（通道合并模式），确保通道数一致。
- **十六进制颜色解析**：`ParseColor` 支持 `#RRGGBB` 格式，解析为 OpenCV 的 `Scalar(B, G, R)`（注意 BGR 顺序）。
- **固定 2 列网格**：Grid 模式硬编码为 2 列，不支持动态列数配置。
- **通道合并上限**：ChannelMerge 模式最多取前 4 个通道，超过时截断；合并时仅取前 3 个通道（BGR）。

> English: The implementation requires exactly 2-4 inputs, auto-converts color spaces for consistency, parses hex colors to OpenCV BGR Scalar, uses a fixed 2-column grid, and caps channel merge at 4 channels (merging first 3 for BGR output).

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image1"/"Image2"/"Image3"/"Image4", ...)` -- 获取 2-4 张输入图像
2. `GetStringParam(@operator, "Mode", "Horizontal")` -- 获取组合模式
3. `GetIntParam(@operator, "Padding", 0, 0, 1000)` -- 获取间距
4. `ParseColor(GetStringParam(@operator, "BackgroundColor", "#000000"))` -- 解析背景颜色
5. 根据模式分发：
   - `ComposeHorizontal(images, padding, bgColor)` -- 水平拼接
   - `ComposeVertical(images, padding, bgColor)` -- 垂直拼接
   - `ComposeGrid(images, padding, bgColor)` -- 网格拼接
   - `ComposeChannels(images)` -- 通道合并
6. `EnsureBgr(img)` / `EnsureGray(img)` -- 颜色空间转换
7. `new Mat(height, width, MatType.CV_8UC3, bg)` -- 创建画布
8. `new Mat(result, new Rect(...))` + `bgr.CopyTo(roi)` -- ROI 拷贝
9. `Cv2.CvtColor(src, result, ColorConversionCodes.GRAY2BGR/BGR2GRAY)` -- 通道转换
10. `Cv2.Merge(channels, merged)` -- 通道合并
11. `CreateImageOutput(result)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"Horizontal"` | Horizontal / Vertical / Grid / ChannelMerge | 组合模式。Horizontal 左右拼接；Vertical 上下拼接；Grid 2 列网格；ChannelMerge 灰度通道合并。 |
| `Padding` | `int` | `0` | [0, 1000] | 图像之间的间距（像素）。仅对 Horizontal、Vertical、Grid 模式生效。 |
| `BackgroundColor` | `string` | `"#000000"` | `#RRGGBB` 格式 | 背景填充颜色。用于间距区域和尺寸不一致时的空白填充。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image1` | Image 1 | `Image` | Yes | 第一张输入图像。 |
| `Image2` | Image 2 | `Image` | Yes | 第二张输入图像。 |
| `Image3` | Image 3 | `Image` | No | 第三张输入图像（可选）。 |
| `Image4` | Image 4 | `Image` | No | 第四张输入图像（可选）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 组合后的输出图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度。 |
| `Height` | `Integer` | 输出图像高度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | `O(N * W * H * C)`，其中 `N` 为输入图像数量，`W`、`H` 为最大宽高，`C` 为通道数。主要开销为画布创建和像素拷贝。 |
| 典型耗时 (Typical Latency) | 4 张 1920x1080 BGR 图像水平拼接：约 5-15ms。通道合并模式因需颜色空间转换略慢。 |
| 内存特征 (Memory Profile) | 峰值内存为输出画布大小 + 各输入图像的 BGR 转换副本。Grid 模式因取最大宽高可能产生较大画布。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：检测结果可视化，将原始图、处理图、掩膜图等拼接为对比图。
- **适合 (Suitable)**：多相机/多角度图像的并排展示。
- **适合 (Suitable)**：通道合并，将多个单通道分割结果合并为伪彩色图。
- **适合 (Suitable)**：报告生成，将多张检测图组合为一张缩略图。
- **不适合 (Not Suitable)**：图像融合/混合（如加权叠加），应使用 `ImageBlend` 算子。
- **不适合 (Not Suitable)**：超过 4 张图像的拼接（当前限制为 4 个输入端口）。
- **不适合 (Not Suitable)**：需要动态列数的网格布局（当前固定 2 列）。

## 已知限制 / Known Limitations
1. 输入端口限制为 2-4 张图像，不支持更多图像的批量拼接。
2. Grid 模式硬编码为 2 列，不支持配置列数。
3. 当输入图像尺寸差异较大时，Grid 模式以最大宽高为单元格大小，较小图像周围会产生大量背景填充。
4. ChannelMerge 模式最多合并 4 通道，输出固定为 3 通道 BGR（取前 3 个）。
5. `BackgroundColor` 仅支持 `#RRGGBB` 格式，不支持颜色名称（如 `red`）或带 Alpha 的格式。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充四种组合模式的详细算法、颜色空间自动转换、十六进制颜色解析、通道合并上限等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
