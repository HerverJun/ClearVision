# 轮廓检测 / FindContours

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FindContoursOperator` |
| 枚举值 (Enum) | `OperatorType.ContourDetection` |
| 分类 (Category) | 特征提取 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | 轮廓, 边界, 形状, 多边形, 边缘点, Contour, Shape, Boundary |
| 图标 (Icon) | contour |

## 算法原理 / Algorithm Principle
轮廓检测从二值图像中提取前景区域的边界点集（轮廓），并输出轮廓的层次关系。这是机器视觉中形状分析、尺寸测量和目标定位的基础操作。

核心流程：
1. **灰度转换**：输入图像转为单通道灰度图。
2. **二值化**：根据 `ThresholdMode` 选择阈值策略将灰度图转为二值图。
3. **轮廓提取**：`Cv2.FindContours()` 从二值图中提取轮廓点集和层次关系。
4. **轮廓筛选**：按面积范围 `[MinArea, MaxArea]` 过滤轮廓。
5. **可视化**：在原图上绘制绿色轮廓、红色质心和蓝色编号。

**二值化模式**：
- **Manual**：手动指定阈值 `Threshold` 和最大值 `MaxValue`，调用 `Cv2.Threshold()`。
- **Otsu**：自动计算最优阈值，调用 `Cv2.Threshold(..., Otsu)`。
- **AdaptiveMean**：自适应均值阈值，对每个像素用邻域均值减去 `AdaptiveC` 作为阈值。
- **AdaptiveGaussian**：自适应高斯阈值，对每个像素用邻域高斯加权均值减去 `AdaptiveC` 作为阈值。
- **InputBinary**：假设输入已是二值图，仅做格式化阈值处理。

**检索模式**：
- **External**：只提取最外层轮廓。
- **List**：提取所有轮廓，不建立层次关系。
- **Tree**：提取所有轮廓并建立完整的父子层次关系。

**近似方法**：
- **Simple**：压缩水平、垂直和对角线方向的连续点，只保留端点。
- **None**：保留轮廓上所有点。

> English: The operator extracts contour point sets and hierarchy from a binarized image. Five thresholding modes are supported (Manual, Otsu, AdaptiveMean, AdaptiveGaussian, InputBinary). Contour retrieval modes include External, List, and Tree. Area-based filtering is applied post-extraction.

## 实现策略 / Implementation Strategy
- 灰度转换使用 `OperatorImageDepthHelper.EnsureSingleChannelGray()`，自动处理多通道输入。
- 自适应阈值的 `blockSize` 自动保证为奇数（偶数时 +1），最小值 3。
- `InputBinary` 模式跳过实际阈值计算，直接对灰度图做 `Threshold(gray, binary, 0, maxValue, threshType)` 格式化。
- 轮廓筛选使用 LINQ 链式操作：先计算面积，再按 `[MinArea, MaxArea]` 范围过滤。
- 层次关系通过 `RemapHierarchy()` 重新映射：过滤后的轮廓索引与原始索引不同，需要将 `Next/Previous/Child/Parent` 指针映射到新的索引空间，被过滤掉的索引映射为 -1。
- 输出的轮廓数据以 `Position` 列表格式提供，方便下游算子（如拟合、测量）直接使用。

> English: Adaptive threshold block size is auto-adjusted to odd values. Hierarchy indices are remapped after area filtering to maintain correct parent-child relationships in the output.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", out imageWrapper)` -- 获取输入图像
2. `OperatorImageDepthHelper.EnsureSingleChannelGray(src)` -- 灰度转换
3. `GetStringParam(@operator, "ThresholdMode", "Manual")` -- 读取阈值模式
4. `ApplyThreshold(gray, binary, ...)` -- 二值化（根据模式选择策略）
   - Manual：`Cv2.Threshold(gray, binary, threshold, maxValue, threshType)`
   - Otsu：`Cv2.Threshold(gray, binary, 0, maxValue, threshType | Otsu)`
   - AdaptiveMean/Gaussian：`Cv2.AdaptiveThreshold(gray, binary, maxValue, adaptiveMethod, threshType, blockSize, adaptiveC)`
   - InputBinary：`Cv2.Threshold(gray, binary, 0, maxValue, threshType)`
5. `Cv2.FindContours(binary, contours, hierarchy, retrievalMode, contourApprox)` -- 轮廓提取
6. 面积筛选：`Cv2.ContourArea(contour)` + LINQ Where 过滤
7. `RemapHierarchy(hierarchy[index], originalToFiltered)` -- 层次关系索引重映射
8. 可视化：`Cv2.DrawContours()` + `Cv2.Moments()` 质心 + `Cv2.Circle()` + `Cv2.PutText()` 编号
9. 构建轮廓摘要（Id, Area, Perimeter, X, Y, Width, Height, PointCount）
10. `CreateImageOutput(resultImg, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"External"` | `External` / `List` / `Tree` | 轮廓检索模式。External 只取最外层；List 取全部无层次；Tree 取全部带层次。 |
| `Method` | `enum` | `"Simple"` | `Simple` / `None` | 轮廓近似方法。Simple 压缩冗余点只保留端点；None 保留所有轮廓点。 |
| `MinArea` | `int` | `100` | [0, +inf) | 最小轮廓面积过滤阈值（像素）。 |
| `MaxArea` | `int` | `100000` | [0, +inf) | 最大轮廓面积过滤阈值（像素）。 |
| `Threshold` | `double` | `127.0` | [0, 255] | 手动二值化阈值。仅 Manual 模式生效。 |
| `MaxValue` | `double` | `255.0` | [0, 255] | 二值化最大值（前景像素值）。 |
| `ThresholdType` | `enum` | `"Binary"` | `Binary` / `BinaryInv` | 阈值类型。Binary：高于阈值为前景；BinaryInv：低于阈值为前景。 |
| `DrawContours` | `bool` | `true` | true / false | 是否在结果图上绘制轮廓、质心和编号。 |
| `ThresholdMode` | `enum` | `"Manual"` | `Manual` / `Otsu` / `AdaptiveMean` / `AdaptiveGaussian` / `InputBinary` | 二值化模式。 |
| `AdaptiveBlockSize` | `int` | `31` | [3, 301] | 自适应阈值的邻域块大小（奇数）。仅 AdaptiveMean/AdaptiveGaussian 模式生效。 |
| `AdaptiveC` | `double` | `2.0` | [-255.0, 255.0] | 自适应阈值的偏移常数。仅 AdaptiveMean/AdaptiveGaussian 模式生效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待检测的输入图像（支持彩色和灰度）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 结果图像 | `Image` | 可视化结果图，标注了轮廓（绿色）、质心（红色）和编号（蓝色）。 |
| `Contours` | 轮廓数据 | `Contour` | 过滤后的轮廓列表，每个轮廓为 `Position` 点集。 |
| `ContourCount` | 轮廓数量 | `Integer` | 过滤后的轮廓数量。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(W*H) 灰度转换 + O(W*H) 二值化 + O(W*H) 轮廓提取 + O(N) 面积筛选与可视化（N 为轮廓数）。自适应阈值额外 O(W*H*blockSize)。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像，Manual 阈值约 5-20ms；Otsu 约 5-15ms；AdaptiveGaussian 约 15-40ms。 |
| 内存特征 (Memory Profile) | 分配灰度图、二值图、结果图。轮廓数据以 Point 数组存储。峰值约为输入图像 3-5 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：形状检测与分类（如圆、矩形、多边形识别）。
- **适合 (Suitable)**：尺寸测量前的轮廓提取（配合轮廓测量、几何拟合算子）。
- **适合 (Suitable)**：缺陷检测中的缺陷边界提取。
- **适合 (Suitable)**：Otsu 自动阈值适合光照均匀的场景；AdaptiveGaussian 适合光照不均匀的场景。
- **适合 (Suitable)**：InputBinary 模式适合上游已做二值化处理的流水线。
- **不适合 (Not Suitable)**：需要亚像素级轮廓精度的场景（轮廓为像素级）。
- **不适合 (Not Suitable)**：重叠目标的分离（应使用分水岭或距离变换预处理）。
- **不适合 (Not Suitable)**：实时性要求极高的场景（自适应阈值计算开销较大）。

## 已知限制 / Known Limitations
1. 轮廓为像素级坐标，不支持亚像素精度。
2. `Tree` 模式输出完整层次关系，但面积过滤后层次关系经过重映射，部分父子关系可能变为 -1。
3. 自适应阈值的 `blockSize` 上限 301，对超大图像的局部自适应可能不够。
4. `InputBinary` 模式仍会对灰度图做 `Threshold` 格式化，若输入非严格 0/255 二值图可能产生意外结果。
5. 不支持多边形近似（`ApproxPolyDP`）输出，只支持 Simple/None 两种近似方法。
6. 轮廓筛选仅基于面积，不支持按周长、圆度等几何特征过滤（需配合 BlobDetection 算子）。
7. `Flooded` 检索模式在代码中存在映射但未在参数枚举中暴露。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 金牌质量重写：补充完整算法原理（5 种阈值模式、3 种检索模式）、实现策略（层次重映射逻辑）、详细参数语义（11 个参数全部覆盖）、性能分析与限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
