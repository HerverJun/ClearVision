# 矩形检测 / RectangleDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RectangleDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.RectangleDetection` |
| 分类 (Category) | 定位 |
| IconName | `rectangle` |
| Keywords | `rectangle`, `quadrilateral`, `box`, `locate` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**中文：**
该算子从图像中检测矩形/四边形目标，施加直角约束（区别于四边形查找算子）。核心流程：灰度转换 -> 高斯模糊 (5x5) -> Canny 边缘检测 (60,180) -> 形态学闭运算 (3x3 矩形核) -> `FindContours` 提取外层轮廓 -> `ApproxPolyDP` 多边形逼近 -> 筛选 4 顶点且凸的轮廓 -> `CornerSubPix` 亚像素精化 -> **直角验证**（4 个内角均在 90 +/- AngleTolerance 范围内）-> `MinAreaRect` 获取最小外接旋转矩形。直角验证通过向量点积计算每对相邻边的夹角 `cos = dot(v1,v2)/(|v1|*|v2|)`，任一内角偏离 90 度超过 `AngleTolerance` 则拒绝该轮廓。角度归一化确保长边对应 Width、短边对应 Height，角度范围 (-90, 90] 度。

**English:**
This operator detects rectangular/quadrilateral objects from image contours with right-angle constraints (unlike QuadrilateralFind). Core pipeline: grayscale -> Gaussian blur (5x5) -> Canny (60,180) -> morphological closing (3x3 rect) -> `FindContours` external -> `ApproxPolyDP` -> filter 4-vertex convex contours -> `CornerSubPix` sub-pixel refinement -> **right-angle validation** (all 4 interior angles within 90 +/- AngleTolerance) -> `MinAreaRect` for minimum bounding rotated rectangle. Right-angle validation uses vector dot products: `cos = dot(v1,v2)/(|v1|*|v2|)`; any angle deviating from 90 beyond `AngleTolerance` rejects the contour. Angle normalization ensures long side = Width, short side = Height, angle in (-90, 90].

## 实现策略 / Implementation Strategy
- **直角约束为核心差异：** 与 `QuadrilateralFindOperator` 的关键区别——本算子在 4 顶点凸轮廓筛选后，额外执行 `IsNearRightAngle` 验证，要求所有 4 个内角均接近 90 度。这使其专门适用于矩形/正方形目标，排除梯形、菱形等非矩形四边形。
- **亚像素角点精化 + 直角验证顺序：** 先 `RefineCorners` 精化角点，再用精化后的角点做直角验证。这保证了角度计算基于高精度坐标。
- **角度归一化：** `NormalizeRect` 将 `MinAreaRect` 的原始角度规范化：若 height > width 则交换并 angle += 90；然后将 angle 映射到 (-90, 90] 范围。输出 NormalizedAngle 表示长边相对于水平轴的倾斜角，LongSide/ShortSide 分别为长边和短边长度。
- **确定性顶点排序：** `OrderVertices` 与四边形查找算子相同的排序逻辑：极角排序 -> 逆时针 -> 最上方顶点起点。
- **面积自适应逼近：** `ApproxEpsilon <= 1.0` 时为周长比例，> 1.0 时为绝对像素值。
- **多矩形输出：** 所有检测到的矩形按面积降序排列；主输出端口（Center/Angle/Width/Height/NormalizedAngle/LongSide/ShortSide）取面积最大的矩形；`Rectangles` 端口输出全部矩形的详细信息列表。

## 核心 API 调用链 / Core API Call Chain
```
1. TryGetInputImage(inputs)                        -- 获取输入图像
2. GetIntParam("MinArea", 100, 0, 100_000_000)     -- 读取最小面积
3. GetIntParam("MaxArea", 10_000_000, 0, 100_000_000) -- 读取最大面积
4. GetDoubleParam("AngleTolerance", 15.0, 0, 90)   -- 读取直角容差
5. GetDoubleParam("ApproxEpsilon", 0.02, 0.0001, 1000) -- 读取逼近精度
6. Cv2.CvtColor(src, gray, BGR2GRAY)               -- 灰度转换
7. Cv2.GaussianBlur(gray, blurred, Size(5,5), 0)   -- 高斯降噪
8. Cv2.Canny(blurred, edges, 60, 180)              -- 边缘检测
9. Cv2.MorphologyEx(edges, closed, Close, 3x3)     -- 形态学闭运算
10. Cv2.FindContours(closed, External, ApproxSimple) -- 轮廓提取
11. [循环] Cv2.ContourArea -> 面积过滤
12. [循环] Cv2.ApproxPolyDP -> 4 顶点过滤
13. [循环] Cv2.IsContourConvex -> 凸性过滤
14. [循环] RefineCorners(gray, approx)
    └─ Cv2.CornerSubPix(gray, corners, Size(5,5), Size(-1,-1), TermCriteria(30,0.01))
15. [循环] IsNearRightAngle(refinedCorners, angleTolerance) -- 直角验证
    └─ 4 组向量点积 cos(theta) + 偏离 90 度检查
16. [循环] Cv2.MinAreaRect(refinedCorners) -> 旋转矩形
17. [循环] NormalizeRect(rect) -> 角度/长边/短边归一化
18. [循环] OrderVertices(refinedCorners) -> 顶点排序
19. rectangles.OrderByDescending(Area)
20. Cv2.Polylines(result, pts, green, 2) + Cv2.Circle(center, red)
21. CreateImageOutput(result, {Rectangles, Count, Center, Angle, Width, Height,
                              NormalizedAngle, LongSide, ShortSide})
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MinArea` | `int` | `100` | [0, 100000000] | 最小轮廓面积（像素^2）。低于此值的矩形被过滤。 |
| `MaxArea` | `int` | `10000000` | [0, 100000000] | 最大轮廓面积（像素^2）。高于此值的矩形被过滤。 |
| `AngleTolerance` | `double` | `15.0` | [0.0, 90.0] | 直角容差（度）。矩形每个内角必须在 [90-tolerance, 90+tolerance] 范围内。值越大允许的矩形越不规整。 |
| `ApproxEpsilon` | `double` | `0.02` | [0.0001, 1000.0] | 多边形逼近精度。<=1.0 时为周长比例（如 0.02 = 周长的 2%）；>1.0 时为绝对像素距离。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像，支持灰度和彩色。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 可视化结果图，矩形以绿色多边形绘制，中心以红点标记。 |
| `Rectangles` | Rectangles | `Any` | 全部检测到的矩形列表，每项含 CenterX/CenterY/Width/Height/Angle/NormalizedAngle/LongSide/ShortSide/Area/Points。 |
| `Count` | Count | `Integer` | 检测到的矩形数量。 |
| `Center` | Center | `Point` | 最大面积矩形的中心坐标。 |
| `Angle` | Angle | `Float` | 最大面积矩形的原始 MinAreaRect 角度。 |
| `Width` | Width | `Float` | 最大面积矩形的原始宽度（MinAreaRect 定义）。 |
| `Height` | Height | `Float` | 最大面积矩形的原始高度（MinAreaRect 定义）。 |
| `NormalizedAngle` | Normalized Angle | `Float` | 归一化角度，长边相对水平轴倾斜角，范围 (-90, 90] 度。 |
| `LongSide` | Long Side | `Float` | 长边长度（像素）。 |
| `ShortSide` | Short Side | `Float` | 短边长度（像素）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) 边缘检测与轮廓提取 + O(M) 多边形逼近与筛选 + O(K) CornerSubPix + 直角验证 + MinAreaRect，N 为像素数，M 为轮廓数，K 为候选四边形数。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像约 25-80ms（取决于轮廓数量和候选矩形数量）。 |
| 内存特征 (Memory Profile) | 需分配灰度图、模糊图、边缘图、闭运算图、结果图各一份 Mat；矩形结果列表随检测数量增长。 |

## 适用场景 / Use Cases
- **适合 (Suitable)：** PCB 板/芯片/屏幕等矩形工件定位；包装盒/标签检测；需要同时获取矩形位置、尺寸、角度和长短边信息的测量场景；与四边形查找互补使用（需严格直角约束时）。
- **不适合 (Not Suitable)：** 梯形/菱形/平行四边形等非矩形四边形检测（应使用 QuadrilateralFind）；严重透视变形导致内角偏离 90 度较大的场景；圆形或椭圆形目标检测。

## 已知限制 / Known Limitations
1. Canny 阈值 (60, 180) 和高斯核 (5x5) 为硬编码，对低对比度或高噪声图像可能需要预处理。
2. `IsNearRightAngle` 使用精化后的角点坐标计算角度，若 `CornerSubPix` 因边界跳过精化，角度精度可能降低。
3. `Angle` 端口输出的是 `MinAreaRect` 原始角度（OpenCV 定义），可能与用户直觉不同；建议使用 `NormalizedAngle`。
4. 仅输出面积最大矩形的详细位置信息（Center/Angle/Width/Height 等），其余矩形仅在 `Rectangles` 列表中。
5. `FindContours` 使用 `RetrievalModes.External`，嵌套矩形（如框中框）的内层矩形不会被检测。
6. 对于严重圆角的矩形（如某些芯片封装），`ApproxPolyDP` 可能产生多于 4 个顶点，导致漏检。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充直角验证算法（IsNearRightAngle 向量点积法）、角度归一化逻辑（NormalizeRect）、与四边形查找的差异对比；完善全部 10 个输出端口说明（含 NormalizedAngle/LongSide/ShortSide 及 Rectangles 列表结构）；增加适用场景与已知限制的源码级分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
