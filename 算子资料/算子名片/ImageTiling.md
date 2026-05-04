# 图像切片 / ImageTiling

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageTilingOperator` |
| 枚举值 (Enum) | `OperatorType.ImageTiling` |
| 分类 (Category) | 拆分组合 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子将一张输入图像按网格切分为多个子图块（Tile）。核心算法为：
1. 按 `Rows` 和 `Cols` 参数计算每个网格单元的基础尺寸：`tileW = width / cols`，`tileH = height / rows`。
2. 最后一列/行自动扩展以覆盖图像边缘剩余像素，避免丢失边缘信息。
3. 每个 Tile 的实际提取区域可选地向外扩展 `Overlap` 像素（边界处裁剪到图像范围），确保相邻 Tile 之间有重叠区域，适合需要跨 Tile 边界检测的场景。
4. 同时在输入图像的克隆副本上绘制黄色网格线作为可视化标注。

输出包含 Tiles 列表（`List<ImageWrapper>`）、Tile 数量和带标注的原图。

> English: This operator splits an input image into a grid of sub-tiles. Base tile dimensions are `width/cols` and `height/rows`, with the last row/column auto-expanded to cover edge pixels. Each tile can be expanded by `Overlap` pixels (clamped to image bounds) for cross-boundary detection. A yellow grid overlay is drawn on a cloned copy of the input. Output includes a tile list, tile count, and annotated image.

## 实现策略 / Implementation Strategy
- **边缘扩展策略**：最后一列的宽度为 `src.Width - x`，最后一行的高度为 `src.Height - y`，确保 100% 覆盖。
- **重叠区域计算**：每个 Tile 的 ROI 向外扩展 `Overlap` 像素，使用 `Math.Max(0, ...)` 和 `Math.Min(src.Width - roiX, ...)` 确保不越界。
- **Mat Clone 分离**：每个 Tile 使用 `new Mat(src, roi).Clone()` 创建独立副本，避免引用原始 Mat 导致生命周期问题。
- **可视化标注**：使用 `Cv2.Rectangle` 在克隆图上绘制黄色（`Scalar(0, 255, 255)`）1px 网格线，标注原始（非重叠）网格边界。
- **OutputMode 参数声明但未实现**：`OutputMode` 参数（Array/Sequential）在属性中声明，但源码中未实际使用，始终输出 Tiles 列表。

> English: The implementation auto-expands edge tiles for full coverage, calculates overlap ROIs clamped to image bounds, creates independent Mat clones per tile, draws yellow grid lines on a cloned annotation image, and declares an OutputMode parameter that is not yet implemented in the execution logic.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `imageWrapper.GetMat()` -- 获取 Mat 引用
3. `GetIntParam(@operator, "Rows"/"Cols"/"Overlap", ...)` -- 获取网格参数
4. `tileW = src.Width / cols`，`tileH = src.Height / rows` -- 计算基础单元尺寸
5. 双重循环 `for r in rows, c in cols`：
   - 计算原始网格边界 `(x, y, w, h)`
   - 计算重叠 ROI `(roiX, roiY, roiW, roiH)` -- 向外扩展 Overlap 像素
   - `new Mat(src, roi)` + `tileMat.Clone()` -- 提取并克隆 Tile
   - `new ImageWrapper(tileMat.Clone())` -- 包装为 ImageWrapper
   - `Cv2.Rectangle(annotated, rect, Scalar(0,255,255), 1)` -- 绘制网格线
6. `CreateImageOutput(annotated, output)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Rows` | `int` | `2` | [1, 100] | 网格行数。 |
| `Cols` | `int` | `2` | [1, 100] | 网格列数。 |
| `Overlap` | `int` | `0` | [0, 10000] | 每个 Tile 向外扩展的重叠像素数。0 表示无重叠。 |
| `OutputMode` | `enum` | `"Array"` | Array / Sequential | 输出模式。当前声明但未实现，始终输出 Tiles 列表。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待切分的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Tiles` | Tiles | `Any` | 切分后的图像块列表（`List<ImageWrapper>`）。 |
| `Count` | Count | `Integer` | Tile 总数（`Rows * Cols`）。 |
| `Image` | Image | `Image` | 带黄色网格标注的输入图像副本。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 标注图像宽度。 |
| `Height` | `Integer` | 标注图像高度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | `O(R*C*W*H)`，其中 `R`、`C` 为行列数，`W`、`H` 为平均 Tile 尺寸。主要是 `Mat.Clone()` 的内存拷贝开销。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像切分为 2x2（无重叠）：约 3-8ms。4x4 + 100px 重叠：约 10-25ms。 |
| 内存特征 (Memory Profile) | 峰值内存约为原始图像大小（标注副本）+ 各 Tile 的独立副本总和。重叠区域会增加每个 Tile 的面积。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：大图检测，将超大图像切分为小块后并行处理。
- **适合 (Suitable)**：跨边界检测，通过重叠区域确保目标不被网格边界截断。
- **适合 (Suitable)**：ROI 粗定位，先切分再对每个 Tile 独立检测，最后合并结果。
- **适合 (Suitable)**：可视化调试，通过标注图查看网格划分是否合理。
- **不适合 (Not Suitable)**：超大图像（如 10000x10000+）的实时切分，内存开销可能过大。
- **不适合 (Not Suitable)**：非均匀切分（如按内容自适应切分），当前仅支持均匀网格。

## 已知限制 / Known Limitations
1. `OutputMode` 参数（Array/Sequential）已声明但未在执行逻辑中实现，始终输出 Tiles 列表。
2. Grid 模式仅支持均匀切分，不支持自定义每个 Tile 的位置和大小。
3. 最后一列/行的 Tile 尺寸可能与其他 Tile 不同（边缘扩展），下游处理需考虑尺寸不一致。
4. 重叠区域不区分"原始区域"和"重叠区域"，下游需自行处理重叠部分的去重。
5. 可视化标注使用 1px 黄色线，在高分辨率图像上可能不明显。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充网格切分算法、重叠区域计算、边缘扩展策略、Mat Clone 生命周期、OutputMode 未实现状态等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
