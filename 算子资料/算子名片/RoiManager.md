# ROI管理器 / RoiManager

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RoiManagerOperator` |
| 枚举值 (Enum) | `OperatorType.RoiManager` |
| 分类 (Category) | 辅助 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子用于从输入图像中提取感兴趣区域（ROI），支持三种形状和两种操作模式：

**形状 (Shape)**：
1. **矩形 (Rectangle)**：由左上角 `(X, Y)` 和尺寸 `(Width, Height)` 定义。
2. **圆形 (Circle)**：由圆心 `(CenterX, CenterY)` 和半径 `Radius` 定义。裁剪时取外接矩形并应用圆形掩膜。
3. **多边形 (Polygon)**：由 JSON 格式的顶点数组定义（如 `[[10,10],[200,10],[200,200],[10,200]]`），至少需要 3 个顶点。

**操作 (Operation)**：
1. **裁剪 (Crop)**：提取 ROI 区域并输出为独立图像（矩形裁剪为矩形；圆形/多边形裁剪为外接矩形 + 掩膜，非 ROI 区域为黑色）。
2. **掩膜 (Mask)**：保留原图尺寸，非 ROI 区域置为黑色（通过 `BitwiseAnd` 应用掩膜）。

所有形状均同时输出一个二值掩膜（ROI 区域白色 255，其余黑色 0）。

> English: This operator extracts Regions of Interest (ROI) from input images in three shapes (Rectangle, Circle, Polygon) and two operation modes. Crop mode extracts the ROI region; Mask mode preserves original dimensions with non-ROI areas blacked out. All shapes output a binary mask alongside the ROI image.

## 实现策略 / Implementation Strategy
- **形状分发**：通过 `switch(shape)` 分发到 `ProcessRectangle`、`ProcessCircle`、`ProcessPolygon` 三个私有方法。
- **矩形裁剪**：直接 `new Mat(src, rect)` 提取 ROI 子矩阵，零拷贝引用。
- **圆形处理**：在全黑掩膜上 `Cv2.Circle` 填充白色圆形，裁剪模式取外接矩形后 `BitwiseAnd`，掩膜模式对全图 `BitwiseAnd`。
- **多边形处理**：JSON 顶点数组通过 `JsonSerializer.Deserialize<int[][]>` 解析，`Cv2.FillPoly` 填充掩膜。裁剪时计算多边形的 AABB（Axis-Aligned Bounding Box）作为裁剪区域。
- **边界保护**：矩形模式下 `width = Math.Min(width, src.Width - x)` 和 `height = Math.Min(height, src.Height - y)` 确保不越界。
- **掩膜生命周期**：掩膜 Mat 在 `finally` 块中 Dispose，输出时通过 `mask.Clone()` 创建独立副本。

> English: The implementation dispatches to shape-specific methods, uses zero-copy Mat ROI extraction for rectangles, circle/polygon masks via OpenCV drawing functions, JSON deserialization for polygon vertices, AABB computation for polygon cropping, boundary clamping, and proper Mat lifecycle management with finally-block disposal.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `GetStringParam(@operator, "Shape"/"Operation", ...)` -- 获取形状和操作模式
3. `GetIntParam(@operator, "X"/"Y"/"Width"/"Height"/"CenterX"/"CenterY"/"Radius", ...)` -- 获取几何参数
4. `GetStringParam(@operator, "PolygonPoints", ...)` -- 获取多边形顶点 JSON
5. `imageWrapper.GetMat()` -- 获取 Mat 引用
6. 边界检查：`width = Math.Min(width, src.Width - x)` 等
7. `new Mat(src.Size(), MatType.CV_8UC1, Scalar.All(0))` -- 创建掩膜
8. 根据形状分发：
   - **Rectangle**：`new Mat(src, rect)` + `Cv2.Rectangle(mask, rect, Scalar.All(255), -1)`
   - **Circle**：`Cv2.Circle(mask, center, radius, Scalar.All(255), -1)` + `Cv2.BitwiseAnd`
   - **Polygon**：`JsonSerializer.Deserialize<int[][]>(polygonPointsJson)` + `Cv2.FillPoly(mask, points, Scalar.All(255))` + `Cv2.BitwiseAnd`
9. `CreateImageOutput(resultImage, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Shape` | `enum` | `"Rectangle"` | Rectangle / Circle / Polygon | ROI 形状类型。 |
| `Operation` | `enum` | `"Crop"` | Crop / Mask | 操作模式。Crop 裁剪 ROI；Mask 保留原图尺寸并置黑非 ROI 区域。 |
| `X` | `int` | `0` | [0, +inf] | 矩形 ROI 左上角 X 坐标。仅 Shape=Rectangle 时生效。 |
| `Y` | `int` | `0` | [0, +inf] | 矩形 ROI 左上角 Y 坐标。仅 Shape=Rectangle 时生效。 |
| `Width` | `int` | `200` | [1, +inf] | 矩形 ROI 宽度。仅 Shape=Rectangle 时生效。 |
| `Height` | `int` | `200` | [1, +inf] | 矩形 ROI 高度。仅 Shape=Rectangle 时生效。 |
| `CenterX` | `int` | `100` | - | 圆形 ROI 圆心 X 坐标。仅 Shape=Circle 时生效。 |
| `CenterY` | `int` | `100` | - | 圆形 ROI 圆心 Y 坐标。仅 Shape=Circle 时生效。 |
| `Radius` | `int` | `50` | [1, +inf] | 圆形 ROI 半径。仅 Shape=Circle 时生效。 |
| `PolygonPoints` | `string` | `"[[10,10],[200,10],[200,200],[10,200]]"` | JSON 数组 | 多边形 ROI 顶点坐标，格式为 `[[x1,y1],[x2,y2],...]`，至少 3 个顶点。仅 Shape=Polygon 时生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 输入图像 | `Image` | Yes | 待提取 ROI 的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | ROI图像 | `Image` | 提取后的 ROI 图像。Crop 模式输出裁剪区域；Mask 模式输出原图尺寸掩膜应用结果。 |
| `Mask` | 掩膜 | `Image` | 二值掩膜图像（ROI 区域白色，其余黑色）。尺寸与原图相同。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Shape` | `String` | 本次使用的形状类型。 |
| `Operation` | `String` | 本次使用的操作模式。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | 矩形 Crop：`O(Wr*Hr)`，其中 `Wr`、`Hr` 为 ROI 尺寸。掩膜模式：`O(W*H)`，需对全图做 BitwiseAnd。多边形填充：`O(W*H)`，FillPoly 需扫描全图。 |
| 典型耗时 (Typical Latency) | 矩形 Crop（200x200 ROI from 1920x1080）：< 1ms。圆形/多边形 Mask（1920x1080）：1-3ms。 |
| 内存特征 (Memory Profile) | 峰值内存为掩膜大小（`W*H*1` 字节）+ 输出图像。矩形 Crop 模式下输出图像远小于原图。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：局部检测，从大图中裁剪出感兴趣区域后送入检测算子。
- **适合 (Suitable)**：掩膜预处理，生成二值掩膜供后续形态学或掩膜运算使用。
- **适合 (Suitable)**：圆形/多边形 ROI，处理非矩形的感兴趣区域（如圆形工件、不规则区域）。
- **适合 (Suitable)**：流程中的 ROI 选择节点，配合前端 UI 动态调整 ROI 参数。
- **不适合 (Not Suitable)**：多 ROI 同时提取（当前每次执行仅处理一个 ROI）。
- **不适合 (Not Suitable)**：旋转矩形 ROI（当前仅支持轴对齐矩形）。

## 已知限制 / Known Limitations
1. 每次执行仅支持一个 ROI，不支持多 ROI 批量提取。
2. 矩形 ROI 仅支持轴对齐（Axis-Aligned），不支持旋转矩形。
3. 多边形 ROI 的顶点通过 JSON 字符串传入，解析失败时静默回退到默认矩形，不会报错。
4. 圆形裁剪输出的是外接矩形区域（内含圆形掩膜），非 ROI 区域为黑色，下游可能需要额外掩膜处理。
5. 所有形状参数（X/Y/Width/Height/CenterX/CenterY/Radius/PolygonPoints）始终可用，但仅对应形状的参数会生效，可能造成配置困惑。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充三种形状的详细处理算法（矩形零拷贝、圆形外接矩形+掩膜、多边形 JSON 解析+AABB 裁剪）、边界保护、掩膜生命周期管理等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
