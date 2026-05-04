# 极坐标展开 / PolarUnwrap

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PolarUnwrapOperator` |
| 枚举值 (Enum) | `OperatorType.PolarUnwrap` |
| 分类 (Category) | 图像处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | polar, unwrap, ring, annular |
| 图标 (Icon) | polar |

## 算法原理 / Algorithm Principle
极坐标展开将笛卡尔坐标系下的环形区域转换为矩形图像。变换的数学关系为：

```
极坐标 (r, theta) -> 直角坐标 (x, y):
x = centerX + r * cos(theta)
y = centerY + r * sin(theta)
```

展开后图像的几何语义：
- **横轴**：角度方向，对应 `StartAngle` 到 `EndAngle` 的范围。
- **纵轴**：半径方向，对应 `InnerRadius` 到 `OuterRadius` 的范围。

输出尺寸规则：
- `height = OuterRadius - InnerRadius`
- `width = OutputWidth > 0 ? OutputWidth : round(2*pi * OuterRadius * angleSpan / 360)`

若不指定 `OutputWidth`，算子按外圈弧长自动推导宽度，保证近似各向同性采样。

> English: The operator maps an annular region in Cartesian coordinates to a rectangular image in polar coordinates. The horizontal axis represents angle and the vertical axis represents radius. Output width defaults to the outer arc length for approximately isotropic sampling.

## 实现策略 / Implementation Strategy
当前实现提供两条展开路径：

**优先路径：WarpPolar**
1. 调用 `Cv2.WarpPolar()` 生成整圈极坐标展开图（行数 = max(OutputWidth, 360)）。
2. 按 `StartAngle` 和角度跨度 `angleSpan` 从展开图中切出对应行范围。
3. 若角度跨越 0/360 度边界，通过 `SliceRowsWithWrap()` 做环绕拼接。
4. 对径向切片（InnerRadius 到 OuterRadius 列范围）后转置。
5. 必要时 `Cv2.Resize()` 调整到目标尺寸。

**回退路径：Remap**
1. 构建 `mapX / mapY` 两张浮点映射表，逐像素计算极坐标到直角坐标的映射。
2. 调用 `Cv2.Remap(src, unwrapped, mapX, mapY, Linear, Constant, Black)` 执行重映射。

WarpPolar 路径在大尺寸展开时性能更优，但实现更复杂；Remap 路径更直接但逐像素构图开销较大。当 `UseWarpPolar=true` 时优先尝试 WarpPolar，失败时静默回退到 Remap。

> English: The operator prefers OpenCV WarpPolar for performance, with angle slicing and optional wrap-around merging. On failure or when UseWarpPolar=false, it falls back to a custom per-pixel remap implementation.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `ResolveCenter(@operator, inputs, width, height)` -- 解析中心点（输入端口优先于参数）
3. `GetIntParam / GetDoubleParam / GetBoolParam` -- 读取半径、角度、输出宽度等参数
4. 计算 `angleSpan`、自动输出宽度、输出高度
5. **分支一：`TryUnwrapByWarpPolar(...)`**
   - `Cv2.WarpPolar(src, polar, Size(outerRadius, fullAngleRows), center, outerRadius, Linear, Linear)` -- 整圈展开
   - `SliceRowsWithWrap(polar, startRow, rowsForSpan)` -- 角度切片（支持环绕）
   - `angleSlice.ColRange(inner, inner + span)` -- 径向切片
   - `Cv2.Transpose(radialSlice, transposed)` -- 转置为最终方向
   - `Cv2.Resize(transposed, unwrapped, targetSize)` -- 尺寸调整
6. **分支二：`UnwrapByRemap(...)`**
   - 构建 `mapX[y,x] = centerX + r*cos(theta)`, `mapY[y,x] = centerY + r*sin(theta)`
   - `Cv2.Remap(src, unwrapped, mapX, mapY, Linear, Constant, Black)` -- 极坐标重映射
7. `CreateImageOutput(unwrapped, { "Method", "UseWarpPolar" })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `CenterX` | `int` | `0` | - | 展开中心 X 坐标。未提供 `Center` 输入端口时使用；默认退化为图像中心。 |
| `CenterY` | `int` | `0` | - | 展开中心 Y 坐标。未提供 `Center` 输入端口时使用；默认退化为图像中心。 |
| `InnerRadius` | `int` | `0` | [0, min(W,H)/2] | 内圆半径（展开起始半径）。 |
| `OuterRadius` | `int` | `100` | [1, min(W,H)/2] | 外圆半径（展开终止半径）。必须大于 InnerRadius。 |
| `StartAngle` | `double` | `0.0` | [-3600.0, 3600.0] | 起始角度（度）。 |
| `EndAngle` | `double` | `360.0` | [-3600.0, 3600.0] | 结束角度（度）。若 EndAngle < StartAngle，自动加 360 处理。 |
| `OutputWidth` | `int` | `0` | [0, 20000] | 输出图像宽度。为 0 时按外圈弧长自动估算。 |
| `UseWarpPolar` | `bool` | `true` | true / false | 是否优先使用 `Cv2.WarpPolar()`。失败时自动回退到 Remap。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待展开的输入图像。 |
| `Center` | Center | `Point` | No | 可选中心点输入。若提供，优先于 CenterX/CenterY 参数。支持 Position、Point、Point2f、Point2d 和字典格式。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 展开后的极坐标矩形图像。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | WarpPolar 路径：O(fullAngleRows * outerRadius) 极坐标变换 + O(outputW * outputH) 切片转置缩放。Remap 路径：O(outputW * outputH) 映射表构建 + O(outputW * outputH) 重映射。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像、完整 360 度展开约 10-50ms（取决于半径和输出尺寸）。 |
| 内存特征 (Memory Profile) | WarpPolar 路径分配中间极坐标图、切片图、转置图。Remap 路径分配 mapX/mapY 两张浮点映射表（各 outputW*outputH*4 字节）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：圆环形目标的展开查看，如瓶盖、轴承、O 型圈、法兰密封面。
- **适合 (Suitable)**：圆柱面缺陷检测，将侧面展开为平面后做模板匹配或缺陷分析。
- **适合 (Suitable)**：环形字符识别（如瓶盖日期码、轮胎标识），展开后做 OCR。
- **适合 (Suitable)**：需要指定部分角度区间的扇形展开。
- **不适合 (Not Suitable)**：中心点位置未知或估计不准确的情况（偏差直接导致展开畸变）。
- **不适合 (Not Suitable)**：期望自动识别环形区域的任务（需外部提供中心、半径参数）。
- **不适合 (Not Suitable)**：对数极坐标展开需求（当前仅支持线性极坐标）。

## 已知限制 / Known Limitations
1. 输出高度固定为 `OuterRadius - InnerRadius`，不支持独立的径向分辨率控制。
2. 当 `UseWarpPolar=true` 时，WarpPolar 失败会静默回退到 Remap，仅通过输出的 `Method` 字段可区分实际使用的方法。
3. 中心点优先级：输入端口 `Center` > 参数 `CenterX/CenterY` > 图像中心（默认值）。
4. 自动宽度估计依赖 `OuterRadius`，不同外半径直接影响横向分辨率和后续检测尺度。
5. 不支持对数极坐标展开，也未暴露插值方式或边界填充值的配置。
6. Remap 路径的边界超出区域固定填充黑色，不支持镜像或复制边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（极坐标数学映射、双路径策略）、WarpPolar 切片与环绕逻辑、参数语义、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码补充 WarpPolar/Remap 双路径、输出尺寸规则和中心点优先级说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
