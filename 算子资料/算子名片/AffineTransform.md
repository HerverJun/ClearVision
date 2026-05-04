# 仿射变换 / AffineTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AffineTransformOperator` |
| 枚举值 (Enum) | `OperatorType.AffineTransform` |
| 分类 (Category) | 图像处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | affine, warp, rotate, scale, translate |
| 图标 (Icon) | affine |

## 算法原理 / Algorithm Principle
仿射变换是一种保持平行线和平行线间距离比例不变的二维几何变换，由 2x3 矩阵定义：

```
| a  b  tx |   | x |
| c  d  ty | * | y |
              | 1 |
```

变换后坐标：`x' = a*x + b*y + tx`，`y' = c*x + d*y + ty`。

本算子支持两种模式构建仿射矩阵：

- **ThreePoint 模式**：通过源图和目标图各 3 个对应点，调用 `Cv2.GetAffineTransform()` 求解 2x3 仿射矩阵。三点不能共线，否则矩阵奇异。
- **RotateScaleTranslate 模式**：以图像中心为旋转基点，先由 `Cv2.GetRotationMatrix2D(center, angle, scale)` 生成旋转+缩放矩阵，再叠加 `TranslateX / TranslateY` 平移分量。

最终通过 `Cv2.WarpAffine()` 执行仿射重映射，输出图像尺寸由 `OutputWidth / OutputHeight` 控制（为 0 时取原图尺寸）。

> English: An affine transform preserves parallelism and distance ratios. The operator builds a 2x3 matrix either from 3 point correspondences (`GetAffineTransform`) or from rotation-scale-translate parameters (`GetRotationMatrix2D` + translation offset), then applies it via `WarpAffine`.

## 实现策略 / Implementation Strategy
- 采用双模式架构：ThreePoint 模式适合已知标定点对的场景，RotateScaleTranslate 模式适合简单旋转/缩放/平移操作。
- ThreePoint 模式下对点集做 JSON 解析（支持 `[[x,y],...]` 和 `[{x,y},...]` 两种格式），并执行三点共线性检测（行列式 `|det| > 1e-6`），避免退化矩阵。
- 仿射矩阵生成后会做有限值检查和奇异性验证（`|det| > 1e-12`），不合法时返回错误。
- 边界填充使用 `BorderTypes.Constant` + `Scalar.Black`，超出输出尺寸的区域填充黑色。
- 变换矩阵以 2x3 `double[][]` 数组形式通过 `TransformMatrix` 端口输出，供下游算子使用。

> English: The operator validates point collinearity and matrix singularity before applying the transform. Border regions are filled with black. The resulting 2x3 matrix is exposed on the TransformMatrix output port.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `GetStringParam(@operator, "Mode", "RotateScaleTranslate")` -- 读取模式
3. 分支一 (ThreePoint)：
   - `GetStringParam(@operator, "SrcPoints" / "DstPoints")` -- 读取点集 JSON
   - `TryParsePointArray(json, out points, out error)` -- JSON 解析为 `Point2f[]`
   - `TryEnsureNonCollinear(points, label, out error)` -- 三点共线性检查
   - `Cv2.GetAffineTransform(srcPoints[0..3], dstPoints[0..3])` -- 求解仿射矩阵
   - `TryValidateAffineMatrix(matrix, out error)` -- 矩阵有限值与奇异性校验
4. 分支二 (RotateScaleTranslate)：
   - `GetDoubleParam(@operator, "Angle" / "Scale" / "TranslateX" / "TranslateY")`
   - `Cv2.GetRotationMatrix2D(center, angle, scale)` -- 旋转+缩放矩阵
   - 矩阵元素 `[0,2]` 和 `[1,2]` 叠加平移量
5. `Cv2.WarpAffine(src, transformed, affineMatrix, size, Linear, Constant, Black)` -- 执行仿射变换
6. `CreateImageOutput(transformed, { "TransformMatrix": matrixArray })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"RotateScaleTranslate"` | `ThreePoint` / `RotateScaleTranslate` | 变换模式。ThreePoint 用三点对应求矩阵；RotateScaleTranslate 用角度+缩放+平移参数。 |
| `SrcPoints` | `string` | `[[0,0],[100,0],[0,100]]` | - | 源图三点坐标 JSON。格式：`[[x,y],...]` 或 `[{x,y},...]`。仅 ThreePoint 模式生效。 |
| `DstPoints` | `string` | `[[0,0],[100,0],[0,100]]` | - | 目标图三点坐标 JSON。格式同 SrcPoints。仅 ThreePoint 模式生效。 |
| `Angle` | `double` | `0.0` | [-3600.0, 3600.0] | 旋转角度（度）。正值为逆时针。仅 RotateScaleTranslate 模式生效。 |
| `Scale` | `double` | `1.0` | [0.001, 1000.0] | 缩放因子。1.0 为原始尺寸。仅 RotateScaleTranslate 模式生效。 |
| `TranslateX` | `double` | `0.0` | [-100000.0, 100000.0] | X 方向平移量（像素）。正值向右。仅 RotateScaleTranslate 模式生效。 |
| `TranslateY` | `double` | `0.0` | [-100000.0, 100000.0] | Y 方向平移量（像素）。正值向下。仅 RotateScaleTranslate 模式生效。 |
| `OutputWidth` | `int` | `0` | [0, 10000] | 输出图像宽度。为 0 时取输入图像宽度。 |
| `OutputHeight` | `int` | `0` | [0, 10000] | 输出图像高度。为 0 时取输入图像高度。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待变换的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 仿射变换后的输出图像。 |
| `TransformMatrix` | Transform Matrix | `Any` | 2x3 仿射矩阵，格式为 `double[2][3]`，供下游定位或坐标变换使用。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H)，与输出图像像素数线性相关。矩阵构建阶段为 O(1)。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像约 5-15ms（取决于插值方式和输出尺寸）。 |
| 内存特征 (Memory Profile) | 分配一张输出 Mat + 临时仿射矩阵 Mat（2x3 double）。峰值内存约为输入+输出图像大小。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：图像旋转校正（如文档倾斜矫正）、缩放预处理、平移对齐。
- **适合 (Suitable)**：已知三点标定对应关系的配准任务。
- **适合 (Suitable)**：机器视觉中传送带上目标位置校正、模板预对齐。
- **不适合 (Not Suitable)**：透视变形校正（需要单应性矩阵而非仿射矩阵）。
- **不适合 (Not Suitable)**：非刚性形变（如弯曲、拉伸）场景。
- **不适合 (Not Suitable)**：需要亚像素级精度的大角度旋转（建议分步旋转或使用更高阶插值）。

## 已知限制 / Known Limitations
1. 边界填充固定为黑色 (`Scalar.Black`)，不支持自定义填充颜色或镜像/复制边界。
2. 插值方式固定为 `InterpolationFlags.Linear`，不支持最近邻、双三次或 Lanczos 等高阶插值。
3. ThreePoint 模式只取前 3 个点，多余点被忽略；三点共线时返回错误。
4. RotateScaleTranslate 模式的旋转中心固定为图像中心，不支持自定义旋转中心。
5. `SrcPoints` / `DstPoints` 的 JSON 解析对格式要求严格，嵌套数组或对象中的非数字值会导致解析失败。
6. 输出图像超出原图范围的区域全部填黑，可能导致目标信息丢失。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（双模式矩阵构建）、实现策略（共线性检测、矩阵校验）、详细参数语义、API 调用链、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
