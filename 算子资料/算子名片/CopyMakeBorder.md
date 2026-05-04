# 边界填充 / CopyMakeBorder

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CopyMakeBorderOperator` |
| 枚举值 (Enum) | `OperatorType.CopyMakeBorder` |
| 分类 (Category) | 图像处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | border, pad, copy make border |
| 图标 (Icon) | border |

## 算法原理 / Algorithm Principle
边界填充（CopyMakeBorder）是 OpenCV 提供的图像扩边操作，在原始图像四周按指定像素数添加边框。支持四种填充策略：

- **Constant（常量填充）**：用指定颜色（`Color` 参数）填充扩展区域。适合需要明确标记边界的场景。
- **Replicate（复制填充）**：复制图像边缘像素值向外延伸。适合避免边界伪影的连续性场景。
- **Reflect（镜像填充）**：以图像边缘为对称轴做镜像反射。适合纹理连续性要求高的场景。
- **Wrap（环绕填充）**：将图像对侧的像素环绕填充。适合周期性纹理场景。

数学表达：对于输出图像 `dst`，若坐标 `(x, y)` 位于原图区域则 `dst[y, x] = src[y-top, x-left]`；否则按填充策略从原图边界或指定值推导。

> English: CopyMakeBorder adds borders around an image using one of four strategies: Constant (fill with a specified color), Replicate (extend edge pixels), Reflect (mirror at edges), or Wrap (wrap around from opposite side). The operator delegates to `Cv2.CopyMakeBorder()`.

## 实现策略 / Implementation Strategy
- 直接封装 OpenCV `Cv2.CopyMakeBorder()`，四边填充量独立控制。
- 颜色参数 `Color` 接受十六进制字符串（如 `#FF0000`），内部解析为 BGR `Scalar`：先去掉 `#` 前缀，再按 RR GG BB 顺序解析为 `(B, G, R)` 排列。
- 颜色解析容错：非 6 位十六进制或解析失败时退化为黑色 (`Scalar.Black`)。
- 填充类型通过 `ParseBorderType()` 做大小写不敏感映射。
- `ValidateParameters()` 检查四边值非负，以及 BorderType 在合法枚举内。

> English: The operator wraps `Cv2.CopyMakeBorder()` with independent control over top/bottom/left/right padding. The color parameter is parsed from hex string to BGR scalar with fallback to black on parse failure.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `GetIntParam(@operator, "Top" / "Bottom" / "Left" / "Right", 0, 0, 10000)` -- 四边填充量
3. `GetStringParam(@operator, "BorderType", "Constant")` -- 填充类型
4. `ParseBorderType(text)` -- 映射为 `BorderTypes` 枚举
5. `GetStringParam(@operator, "Color", "#000000")` -- 填充颜色
6. `ParseColor(value)` -- 十六进制转 BGR Scalar
7. `Cv2.CopyMakeBorder(src, result, top, bottom, left, right, borderType, color)` -- 执行扩边
8. `CreateImageOutput(result)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Top` | `int` | `0` | [0, 10000] | 顶部填充像素数。 |
| `Bottom` | `int` | `0` | [0, 10000] | 底部填充像素数。 |
| `Left` | `int` | `0` | [0, 10000] | 左侧填充像素数。 |
| `Right` | `int` | `0` | [0, 10000] | 右侧填充像素数。 |
| `BorderType` | `enum` | `"Constant"` | `Constant` / `Replicate` / `Reflect` / `Wrap` | 填充策略。Constant 用指定颜色填充；Replicate 复制边缘像素；Reflect 镜像反射；Wrap 环绕填充。 |
| `Color` | `string` | `"#000000"` | - | Constant 模式下的填充颜色，十六进制格式 `#RRGGBB`。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待扩边的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 扩边后的输出图像。尺寸 = 原图 + Top + Bottom 行、Left + Right 列。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O((W + Left + Right) * (H + Top + Bottom) * C)，与输出图像总像素数成正比。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像、100px 填充量约 1-3ms。 |
| 内存特征 (Memory Profile) | 分配一张输出 Mat，大小为 `(H+Top+Bottom) x (W+Left+Right)`。无额外中间缓冲区。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：卷积/滤波前的边界扩展，避免边缘效应。
- **适合 (Suitable)**：图像拼接前为对齐预留边距。
- **适合 (Suitable)**：模板匹配前扩展搜索区域。
- **适合 (Suitable)**：可视化展示时添加注释边框或分隔线。
- **不适合 (Not Suitable)**：需要自适应填充量的场景（当前为固定像素值）。
- **不适合 (Not Suitable)**：渐变填充或加权混合填充（仅支持四种标准策略）。

## 已知限制 / Known Limitations
1. `Color` 参数仅在 `BorderType=Constant` 时生效；其他模式下此参数被忽略。
2. 颜色解析仅支持 6 位十六进制 `#RRGGBB` 格式，不支持 3 位缩写、`rgb()` 或命名颜色。
3. 四边填充量独立设置，不支持统一设置或按比例填充。
4. 输出图像尺寸 = 原图 + 填充量，大填充量可能导致内存显著增长。
5. 不支持自动根据后续算子需求（如卷积核大小）计算填充量。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（四种填充策略数学描述）、颜色解析逻辑、参数语义、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
