# 透视变换 / PerspectiveTransform

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PerspectiveTransformOperator` |
| 枚举值 (Enum) | `OperatorType.PerspectiveTransform` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
透视变换（Perspective Transform / Homography）是一种将图像从一个平面投影到另一个平面的几何变换。给定源平面上的 4 个点和目标平面上的 4 个对应点，可以求解一个 3x3 的单应性矩阵 H，使得：

```
[x']     [h11 h12 h13] [x]
[y'] ~   [h21 h22 h23] [y]
[1 ]     [h31 h32 h33] [1]
```

其中 (x,y) 是源点，(x',y') 是目标点，`~` 表示齐次坐标下的等价关系。该矩阵通过求解 8 个自由度的线性方程组获得（第 9 个自由度固定为归一化因子）。

变换后，图像中的直线仍保持为直线（仿射变换的超集），但平行线可能不再平行（这是透视效果的本质）。典型应用包括：校正倾斜拍摄的文档、将相机视角转换为鸟瞰视角、对齐不同视角拍摄的同一场景。

> English: Perspective transform (homography) maps a quadrilateral region in the source image to a quadrilateral in the destination image via a 3x3 matrix. It preserves straight lines but not parallelism. The matrix is solved from 4 point correspondences. Typical uses: document dewarping, bird's-eye view conversion, multi-view alignment.

## 实现策略 / Implementation Strategy
- **双模式点输入**：支持两种点输入方式——(1) JSON 字符串或上游端口传入的点集合（优先级高），(2) 8 个独立的 SrcX/SrcY/DstX/DstY 参数（Legacy 模式）。代码会先检查是否有显式点集输入，若无则回退到 16 个独立参数。
- **退化检测**：在计算变换矩阵前，对源点和目标点执行退化检测——检查是否有重复点、是否共线（最大三角形面积 <= 1e-6）。这避免了奇异矩阵导致的变换失败。
- **矩阵验证**：计算变换矩阵后，检查矩阵是否包含非有限值、行列式是否接近零（奇异矩阵）。只有通过验证才执行实际变换。
- **多格式点解析**：点输入支持 JSON 数组（`[x,y]`）、JSON 对象（`{"x":0,"y":0}`）、`Point2f`、`Point`、`Position` 等多种格式，通过 `TryParsePointCollection` 统一解析。
- **SrcPoints 与 DstPoints 耦合校验**：要求两者同时提供或同时不提供，防止只配置了一侧的点导致变换错误。

> English: Dual point-input mode: JSON/port point sets (priority) or 16 individual Src/Dst parameters (legacy). Pre-transform degeneracy checks (duplicate points, collinearity). Post-solve matrix validation (non-finite values, near-zero determinant). Multi-format point parsing (JSON array, JSON object, Point2f, Point, Position). SrcPoints and DstPoints must be provided together.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetIntParam(@operator, "OutputWidth", 640, min: 1, max: 8192)` -- 读取输出宽度
3. `GetIntParam(@operator, "OutputHeight", 480, min: 1, max: 8192)` -- 读取输出高度
4. `HasExplicitPointSetInput(inputs, @operator, "SrcPoints", "SrcPointsJson")` -- 检查是否有显式点集
5. `TryResolvePointSet(inputs, @operator, "SrcPoints", "SrcPointsJson", out parsedSrcPoints, out srcError)` -- 解析源点集
   - `TryParsePointCollection(rawInput, out points, out error)` -- 多格式解析
   - `TryParsePointArray(json, out points, out error)` -- JSON 数组解析
6. `TryResolvePointSet(inputs, @operator, "DstPoints", "DstPointsJson", out parsedDstPoints, out dstError)` -- 解析目标点集
7. `GetLegacyPoints(@operator, isSource: true/false)` -- 若无显式点集，回退到 16 个独立参数
8. `TryEnsureNonDegenerateQuadrilateral(srcPoints, "SrcPoints", out error)` -- 源点退化检测
   - `ComputeMaxTriangleArea(sample)` -- 计算最大三角形面积
9. `TryEnsureNonDegenerateQuadrilateral(dstPoints, "DstPoints", out error)` -- 目标点退化检测
10. `Cv2.GetPerspectiveTransform(srcPoints, dstPoints)` -- 求解 3x3 单应性矩阵
11. `TryValidatePerspectiveMatrix(perspectiveMatrix, out error)` -- 矩阵有效性验证
    - `Cv2.Determinant(matrix)` -- 检查行列式
12. `Cv2.WarpPerspective(src, dst, perspectiveMatrix, new Size(outputWidth, outputHeight), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black)` -- 执行透视变换
13. `CreateImageOutput(dst, additionalData)` -- 封装输出，附带 PointSetMode / PointCount

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `SrcPointsJson` | `string` | `""` | JSON 数组字符串 | 源四边形 4 个顶点的 JSON 表示。格式示例：`[[0,0],[100,0],[100,100],[0,100]]`。与 DstPointsJson 需同时提供。 |
| `DstPointsJson` | `string` | `""` | JSON 数组字符串 | 目标四边形 4 个顶点的 JSON 表示。格式示例：`[[0,0],[640,0],[640,480],[0,480]]`。与 SrcPointsJson 需同时提供。 |
| `SrcX1` | `double` | `0.0` | - | 源点 1 X 坐标（Legacy 模式）。 |
| `SrcY1` | `double` | `0.0` | - | 源点 1 Y 坐标（Legacy 模式）。 |
| `SrcX2` | `double` | `100.0` | - | 源点 2 X 坐标（Legacy 模式）。 |
| `SrcY2` | `double` | `0.0` | - | 源点 2 Y 坐标（Legacy 模式）。 |
| `SrcX3` | `double` | `100.0` | - | 源点 3 X 坐标（Legacy 模式）。 |
| `SrcY3` | `double` | `100.0` | - | 源点 3 Y 坐标（Legacy 模式）。 |
| `SrcX4` | `double` | `0.0` | - | 源点 4 X 坐标（Legacy 模式）。 |
| `SrcY4` | `double` | `100.0` | - | 源点 4 Y 坐标（Legacy 模式）。 |
| `DstX1` | `double` | `0.0` | - | 目标点 1 X 坐标（Legacy 模式）。 |
| `DstY1` | `double` | `0.0` | - | 目标点 1 Y 坐标（Legacy 模式）。 |
| `DstX2` | `double` | `640.0` | - | 目标点 2 X 坐标（Legacy 模式）。 |
| `DstY2` | `double` | `0.0` | - | 目标点 2 Y 坐标（Legacy 模式）。 |
| `DstX3` | `double` | `640.0` | - | 目标点 3 X 坐标（Legacy 模式）。 |
| `DstY3` | `double` | `480.0` | - | 目标点 3 Y 坐标（Legacy 模式）。 |
| `DstX4` | `double` | `0.0` | - | 目标点 4 X 坐标（Legacy 模式）。 |
| `DstY4` | `double` | `480.0` | - | 目标点 4 Y 坐标（Legacy 模式）。 |
| `OutputWidth` | `int` | `640` | [1, 8192] | 输出图像宽度（像素）。变换结果会缩放到该尺寸。 |
| `OutputHeight` | `int` | `480` | [1, 8192] | 输出图像高度（像素）。变换结果会缩放到该尺寸。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待变换的输入图像。 |
| `SrcPoints` | 源点集合 | `PointList` | No | 源四边形 4 个顶点。优先级高于 SrcPointsJson 和 16 个独立参数。 |
| `DstPoints` | 目标点集合 | `PointList` | No | 目标四边形 4 个顶点。优先级高于 DstPointsJson 和 16 个独立参数。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 透视变换后的输出图像，尺寸为 OutputWidth x OutputHeight。变换区域外填充黑色。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（像素）。 |
| `Height` | `Integer` | 输出图像高度（像素）。 |
| `PointSetMode` | `String` | 点输入模式：`"PointSetJsonOrInput"`（使用 JSON 或端口输入）或 `"Legacy16Params"`（使用 16 个独立参数）。 |
| `PointCount` | `Integer` | 实际使用的点数，固定为 4。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W_out * H_out)，其中 W_out / H_out 为输出图像尺寸。变换矩阵求解为 O(1)（固定 4 点）。 |
| 典型耗时 (Typical Latency) | 640x480 输出约 1-3ms；8192x8192 输出约 50-100ms。取决于输出尺寸而非输入尺寸。 |
| 内存特征 (Memory Profile) | 需要分配输出尺寸的 Mat，加上 3x3 变换矩阵和临时点数组。峰值内存主要由输出图像大小决定。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：倾斜拍摄的文档或标签校正，将不规则四边形区域变换为矩形以便 OCR 或模板匹配。
- **适合 (Suitable)**：工业视觉中的视角转换，例如将斜视拍摄的产品表面变换为正视图进行尺寸测量。
- **适合 (Suitable)**：多相机视角对齐，将不同角度拍摄的图像变换到同一参考平面进行拼接或比对。
- **适合 (Suitable)**：通过上游算子动态传入 SrcPoints/DstPoints 端口，实现自适应的感兴趣区域提取。
- **不适合 (Not Suitable)**：仅需旋转变换或缩放的简单场景，仿射变换（Affine Transform）计算更高效。
- **不适合 (Not Suitable)**：源点或目标点共线、重叠或构成退化四边形的情况，算子会返回明确错误。
- **不适合 (Not Suitable)**：需要亚像素精度的精密测量场景，线性插值（InterpolationFlags.Linear）会引入插值误差。

## 已知限制 / Known Limitations
1. 默认 SrcPoints 为 100x100 的正方形，DstPoints 为 640x480 的矩形，这是一个放大变换。若直接使用默认值不做任何配置，变换结果可能不符合预期。
2. 16 个独立参数（SrcX1-SrcY4, DstX1-DstY4）的默认值构成一个非退化四边形对，但实际业务场景中这些值通常需要替换。
3. 输出边界使用 `BorderTypes.Constant` + `Scalar.Black` 填充，无法自定义填充值或填充策略。
4. 插值方式固定为 `InterpolationFlags.Linear`，不支持最近邻或立方插值等其他方式。
5. JSON 点解析支持 `[x,y]` 数组和 `{x,y}` 对象两种格式，但不支持 `[[x1,y1],[x2,y2]]` 以外的嵌套结构。
6. 矩阵验证中行列式阈值为 `1e-12`，对于极端透视（如接近平行投影）可能误判为奇异。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充算法原理（单应性矩阵、齐次坐标）、实现策略（双模式点输入、退化检测、矩阵验证、多格式解析）、完整参数语义（20 个参数）、API 调用链、性能量化和使用场景分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
