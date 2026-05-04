# 图像旋转 / ImageRotate

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageRotateOperator` |
| 枚举值 (Enum) | `OperatorType.ImageRotate` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子基于 2D 仿射变换实现图像旋转，流程如下：

1. **确定旋转中心**：若 `CenterX`/`CenterY` 为默认值 `-1`，则使用图像中心 `(Width/2, Height/2)`。
2. **构建旋转矩阵**：通过 `Cv2.GetRotationMatrix2D(center, angle, scale)` 生成 2x3 仿射变换矩阵。
   - `angle` 为旋转角度（正值逆时针，负值顺时针）。
   - `scale` 为附加缩放因子，旋转同时缩放图像。
3. **自动调整输出尺寸**（`AutoResize=true`）：
   ```
   newWidth  = |cos(angle)| * srcW + |sin(angle)| * srcH
   newHeight = |sin(angle)| * srcW + |cos(angle)| * srcH
   ```
   同时修改旋转矩阵的平移分量，使旋转后图像居中显示。
4. **执行仿射变换**：`Cv2.WarpAffine(src, dst, rotationMatrix, dstSize, InterpolationFlags.Linear, BorderTypes.Constant, Scalar(0,0,0))`。
   - 使用双线性插值，边界填充黑色（0,0,0）。

> English: The operator performs affine rotation using `Cv2.GetRotationMatrix2D` and `Cv2.WarpAffine`. When AutoResize is enabled, output dimensions are computed from the rotated bounding box to prevent clipping.

## 实现策略 / Implementation Strategy
- 使用 `Cv2.GetRotationMatrix2D` + `Cv2.WarpAffine` 的标准仿射流程，而非 `Cv2.Transpose` + `Cv2.Flip`（后者仅支持 90 度倍数旋转）。
- `AutoResize` 通过计算旋转后包围盒尺寸并修改变换矩阵平移分量实现，避免裁剪。
- 当 `AutoResize=false` 时，输出尺寸与输入相同，超出画布的部分被黑色填充。
- 旋转中心默认为图像几何中心，也支持手动指定，适合围绕特定锚点旋转的场景。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)`
2. `GetDoubleParam(@operator, "Angle", 0.0, -360, 360)`
3. `GetIntParam(@operator, "CenterX", -1)` / `GetIntParam("CenterY", -1)`
4. `GetDoubleParam(@operator, "Scale", 1.0, 0.1, 10.0)`
5. `GetBoolParam(@operator, "AutoResize", true)`
6. `imageWrapper.GetMat()`
7. `new Point2f(centerX, centerY)` — 旋转中心
8. `Cv2.GetRotationMatrix2D(center, angle, scale)` — 2x3 仿射矩阵
9. **AutoResize=true**：计算 `newWidth`/`newHeight`，修改矩阵平移分量 `At<double>(0,2)` / `At<double>(1,2)`
10. `Cv2.WarpAffine(src, dst, rotationMatrix, dstSize, InterpolationFlags.Linear, BorderTypes.Constant, Scalar(0,0,0))`
11. `CreateImageOutput(dst, additionalData)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Angle` | `double` | `0.0` | [-360, 360] | 旋转角度（度）。正值逆时针，负值顺时针。 |
| `CenterX` | `int` | `-1` | [-1, +inf) | 旋转中心 X 坐标。`-1` 表示使用图像中心。 |
| `CenterY` | `int` | `-1` | [-1, +inf) | 旋转中心 Y 坐标。`-1` 表示使用图像中心。 |
| `Scale` | `double` | `1.0` | [0.1, 10.0] | 旋转时附加的缩放因子。1.0 表示不缩放。 |
| `AutoResize` | `bool` | `true` | `true` / `false` | 是否自动调整输出尺寸以容纳完整旋转后图像。关闭时超出部分被裁剪。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待旋转的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 旋转后的输出图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Angle` | `Double` | 实际使用的旋转角度。 |
| `Scale` | `Double` | 实际使用的缩放因子。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(dstW x dstH)，线性于输出图像像素总数。 |
| 典型耗时 (Typical Latency) | 1080p 旋转 90 度约 3-8 ms（AutoResize=true 时输出尺寸可能变化）。 |
| 内存特征 (Memory Profile) | 额外分配一幅输出尺寸的 Mat 和一个 2x3 旋转矩阵 Mat。AutoResize=true 时输出尺寸可能大于输入。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：校正相机安装倾斜导致的图像偏转。
- **适合 (Suitable)**：将竖拍图像旋转为横拍以适配检测流程。
- **适合 (Suitable)**：围绕特定锚点（如标记点）旋转图像进行配准。
- **不适合 (Not Suitable)**：仅需 90/180/270 度精确旋转的场景，使用 `Cv2.Transpose` + `Cv2.Flip` 更高效。
- **不适合 (Not Suitable)**：需要透视变换（如梯形校正）的场景，应使用 `Cv2.WarpPerspective`。

## 已知限制 / Known Limitations
1. 边界填充固定为黑色（Scalar(0,0,0)），不可配置。
2. 插值方法固定为 `InterpolationFlags.Linear`，不可切换为其他插值方式。
3. AutoResize=true 时输出尺寸由旋转角度决定，45 度旋转时输出面积最大（约为原始的 1.414 倍），可能显著增加内存消耗。
4. 非 90 度倍数旋转会因插值引入轻微模糊，多次旋转可能导致累积质量损失。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充仿射变换原理、AutoResize 包围盒计算、矩阵平移分量修改、API 调用链 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
