# Blob 分析 / BlobDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `BlobDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.BlobAnalysis` |
| 分类 (Category) | 特征提取 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.1.0 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | 连通域, 缺陷区域, 斑点, 面积提取, 缺陷分析, Blob, Connected components |
| 图标 (Icon) | blob |

## 算法原理 / Algorithm Principle
Blob 检测（连通区域分析）将二值图像中互相连通的前景像素分组为独立区域（Blob），并提取每个区域的几何与灰度特征。

核心流程：
1. **灰度转换 + Otsu 自动二值化**：将输入图转灰度后，用 Otsu 阈值自动将图像分为前景/背景。若图像动态范围为零（全黑或全白），输出空结果。
2. **颜色方向处理**：若 `Color=Black`，对二值图取反，将黑色区域变为前景。
3. **HSV 颜色预过滤**（可选）：启用 `EnableColorFilter` 后，在 HSV 色彩空间按 `HueLow-HueHigh`、`SatLow-SatHigh`、`ValLow-ValHigh` 范围生成掩码，与二值图做 AND 操作。
4. **连通区域标记**：`Cv2.ConnectedComponentsWithStats()` 以 8 连通方式标记所有连通区域，输出标签图、统计信息（面积、外接矩形）和质心。
5. **特征计算**：对每个连通区域，提取轮廓后计算：面积、周长、圆度、凸度、矩形度、离心率、惯性比、欧拉数、孔洞数、灰度均值与标准差。
6. **特征过滤**：支持 `FeatureFilter` 表达式，用 `DataTable.Compute()` 对特征值做布尔表达式求值。
7. **可视化**：在原图（或 SourceImage）上绘制绿色轮廓和红色质心。

**关键特征定义**：
- **圆度 (Circularity)**：`4*pi*Area / Perimeter^2`，近似轮廓（epsilon=0.2% 周长）后计算以抑制光栅化锯齿。
- **凸度 (Convexity)**：`ContourArea / ConvexHullArea`。
- **矩形度 (Rectangularity)**：`ContourArea / BoundingRectArea`。
- **离心率 (Eccentricity)**：`sqrt(1 - lambda2/lambda1)`，其中 lambda1/lambda2 为二阶中心矩特征值。
- **惯性比 (InertiaRatio)**：`lambda2/lambda1`，越接近 1 越圆。

> English: Blob detection performs connected component analysis on a binarized image, then extracts geometric features (area, perimeter, circularity, convexity, rectangularity, eccentricity, inertia ratio, Euler number) and grayscale statistics for each blob. Feature filtering via expression is supported.

## 实现策略 / Implementation Strategy
- 使用 `Cv2.ConnectedComponentsWithStats()` 而非 `SimpleBlobDetector`，获得更完整的统计信息和轮廓数据。
- 对每个标签区域单独提取轮廓（`FindContours` + CComp 层次），取外层轮廓作为 Blob 边界。
- 圆度计算对光栅化锯齿做近似处理：当轮廓点 >= 12 时，先用 `ApproxPolyDP`（epsilon=0.2% 周长）简化轮廓再计算。
- `FeatureFilter` 使用 `DataTable.Compute()` 引擎，支持 `AND`/`OR`/`NOT` 逻辑运算和 `>`/`<`/`=` 比较运算，表达式中的特征名会被替换为实际数值。
- `SourceImage` 输入端口可选：提供时用于灰度统计和可视化底图；不提供时使用主输入图像。
- 低动态范围图像（min==max）直接输出空结果，避免全前景掩码。

> English: Uses `ConnectedComponentsWithStats` for comprehensive region statistics. Circularity computation includes polygonal approximation to suppress rasterization artifacts. Feature filter uses `DataTable.Compute()` expression engine.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取主输入图像
2. `TryGetInputImage(inputs, "SourceImage", out sourceWrapper)` -- 获取可选源图
3. `Cv2.CvtColor(src, gray, BGR2GRAY)` -- 灰度转换
4. `Cv2.MinMaxLoc(gray, minVal, maxVal)` -- 检查动态范围
5. `Cv2.Threshold(gray, binary, 0, 255, Binary|Otsu)` -- Otsu 自动二值化
6. `Cv2.BitwiseNot(binary, binary)` -- 黑色目标取反（Color=Black 时）
7. `ApplyColorFilter(src, hue/sat/val ranges)` -- HSV 颜色预过滤（可选）
8. `Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids, Connectivity8, CV_32S)` -- 连通区域标记
9. 对每个标签区域：
   - `Cv2.Compare(labelRoi, label, mask, EQ)` -- 提取单标签掩码
   - `Cv2.FindContours(mask, contours, hierarchy, CComp, ApproxSimple)` -- 提取轮廓
   - `Cv2.ContourArea` / `Cv2.ArcLength` / `Cv2.ConvexHull` / `Cv2.Moments` -- 计算特征
   - `TryEvaluateFeatureFilter(filter, featureValues, passed, error)` -- 特征过滤
10. `Cv2.DrawContours(resultImage, contour, ...)` + `Cv2.Circle(resultImage, center, ...)` -- 可视化
11. `CreateImageOutput(resultImage, { BlobCount, Blobs, BlobFeatures })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MinArea` | `int` | `100` | [0, +inf) | 最小 Blob 面积（像素）。低于此值的连通区域被过滤。 |
| `MaxArea` | `int` | `100000` | [0, +inf) | 最大 Blob 面积（像素）。高于此值的连通区域被过滤。 |
| `Color` | `enum` | `"White"` | `White` / `Black` | 目标颜色。White 检测白色前景；Black 检测黑色前景（二值图取反）。 |
| `MinCircularity` | `double` | `0.0` | [0.0, 1.0] | 最小圆度阈值。为 0 时不筛选。 |
| `MinConvexity` | `double` | `0.0` | [0.0, 1.0] | 最小凸度阈值。为 0 时不筛选。 |
| `MinInertiaRatio` | `double` | `0.0` | [0.0, 1.0] | 最小惯性比阈值。为 0 时不筛选。 |
| `MinRectangularity` | `double` | `0.0` | [0.0, 1.0] | 最小矩形度阈值。为 0 时不筛选。 |
| `MinEccentricity` | `double` | `0.0` | [0.0, 1.0] | 最小离心率阈值。为 0 时不筛选。 |
| `OutputDetailedFeatures` | `bool` | `false` | true / false | 是否在输出中包含每个 Blob 的详细特征字典。 |
| `FeatureFilter` | `string` | `""` | - | 特征过滤表达式。支持特征名（如 `Area > 500 AND Circularity > 0.8`）和 `AND`/`OR`/`NOT` 逻辑。 |
| `EnableColorFilter` | `bool` | `false` | true / false | 是否启用 HSV 颜色范围预过滤。 |
| `HueLow` | `int` | `0` | [0, 180] | HSV 色相下限。 |
| `HueHigh` | `int` | `180` | [0, 180] | HSV 色相上限。 |
| `SatLow` | `int` | `50` | [0, 255] | HSV 饱和度下限。 |
| `SatHigh` | `int` | `255` | [0, 255] | HSV 饱和度上限。 |
| `ValLow` | `int` | `50` | [0, 255] | HSV 明度下限。 |
| `ValHigh` | `int` | `255` | [0, 255] | HSV 明度上限。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 主输入图像，用于二值化和连通区域分析。 |
| `SourceImage` | Source Image | `Image` | No | 可选源图像，用于灰度统计和可视化底图。不提供时使用主输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 标记图像 | `Image` | 可视化结果图，标注了 Blob 轮廓（绿色）和质心（红色）。 |
| `Blobs` | Blob数据 | `Contour` | Blob 信息列表，每个 Blob 包含 Id、Area、ContourArea、Perimeter、Circularity、Convexity 等字段。 |
| `BlobFeatures` | Blob特征 | `Any` | 详细特征字典（OutputDetailedFeatures=true 时与 Blobs 相同内容）。 |
| `BlobCount` | Blob数量 | `Integer` | 检测到的 Blob 数量。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H) 灰度转换 + O(W*H) Otsu 二值化 + O(W*H) 连通区域标记 + O(N) 轮廓与特征计算（N 为连通区域数）。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像，100 个 Blob 约 15-50ms。Blob 数量多时轮廓提取和特征计算成为瓶颈。 |
| 内存特征 (Memory Profile) | 分配灰度图、二值图、标签图（CV_32S）、统计矩阵、质心矩阵、结果图等。峰值约为输入图像 6-10 倍（标签图为 4 字节/像素）。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：缺陷检测中的缺陷区域定位与分类（如划痕、气泡、异物）。
- **适合 (Suitable)**：粒子计数与尺寸分布分析（如粉末、颗粒、细胞）。
- **适合 (Suitable)**：需要按形状特征（圆度、凸度等）筛选目标的场景。
- **适合 (Suitable)**：FeatureFilter 表达式适合复杂的多条件组合筛选。
- **不适合 (Not Suitable)**：需要保留 Blob 间层次关系的场景（当前为扁平列表，无父子关系）。
- **不适合 (Not Suitable)**：重叠目标分离（连通区域分析无法处理粘连目标，应使用分水岭或距离变换）。
- **不适合 (Not Suitable)**：需要亚像素级轮廓精度的场景（轮廓为像素级）。

## 已知限制 / Known Limitations
1. 二值化使用 Otsu 全局阈值，对光照不均匀的图像可能效果不佳（未提供自适应阈值选项）。
2. 连通区域使用 8 连通，不支持 4 连通切换。
3. `FeatureFilter` 使用 `DataTable.Compute()` 引擎，表达式能力有限，不支持函数调用（如 `sqrt`、`abs`）。
4. HSV 颜色过滤要求输入为 3 通道彩色图；灰度图输入时颜色过滤被跳过。
5. 圆度计算的近似轮廓处理对非常小的轮廓（< 12 点）可能退化为直接使用原始周长。
6. 不支持 Blob 间的合并、分裂或时序跟踪。
7. 输出的轮廓数据为像素级坐标，不支持亚像素精度。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（连通区域标记流程、特征定义公式）、实现策略（CCStats 替代 SimpleBlobDetector、FeatureFilter 表达式引擎）、详细参数语义（17 个参数全部覆盖）、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
