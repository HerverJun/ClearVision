# 角点检测 / CornerDetection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `CornerDetectionOperator` |
| 枚举值 (Enum) | `OperatorType.CornerDetection` |
| 分类 (Category) | 定位 |
| IconName | `corner` |
| Keywords | `corner`, `vertex`, `harris`, `shitomasi` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**中文：**
该算子检测图像中的关键角点并输出亚像素精度的点集。支持两种经典算法：Shi-Tomasi（默认）和 Harris。Shi-Tomasi 使用 `Cv2.GoodFeaturesToTrack` 计算每个像素邻域的自相关矩阵，取其较小特征值 lambda_min 作为角点响应值，响应超过 `QualityLevel * max(lambda_min)` 且满足 `MinDistance` 约束的像素被选为候选角点。Harris 方法同样基于自相关矩阵，但使用 `k=0.04` 的 Harris 响应函数 `det(M) - k*trace(M)^2`。检测后使用 `Cv2.CornerSubPix` 在 5x5 窗口内迭代优化至亚像素精度（终止条件：30 次迭代或 epsilon=0.01）。

**English:**
This operator detects key corner points in an image and outputs sub-pixel-precision point sets. It supports two classic algorithms: Shi-Tomasi (default) and Harris. Shi-Tomasi uses `Cv2.GoodFeaturesToTrack` to compute the auto-correlation matrix for each pixel's neighborhood, takes the smaller eigenvalue lambda_min as the corner response, and selects candidates exceeding `QualityLevel * max(lambda_min)` with `MinDistance` constraints. Harris uses the same matrix but applies the Harris response function `det(M) - k*trace(M)^2` with `k=0.04`. After detection, `Cv2.CornerSubPix` refines positions to sub-pixel accuracy in a 5x5 window (termination: 30 iterations or epsilon=0.01).

## 实现策略 / Implementation Strategy
- **双算法切换：** 通过 `Method` 参数在 Harris 和 Shi-Tomasi 之间切换，底层均调用 `Cv2.GoodFeaturesToTrack`，仅 `useHarris` 标志不同。
- **自动灰度转换：** 输入为彩色图像时自动 BGR2GRAY 转换，灰度图像直接使用。
- **亚像素精化：** 所有检测到的角点均通过 `CornerSubPix` 精化，搜索窗口 5x5，零区域 (-1,-1) 表示使用全邻域。
- **可视化标记：** 结果图使用红色十字标记（`DrawMarker`，`MarkerTypes.Cross`，大小 12，线宽 2）标注每个角点位置。
- **参数验证：** `ValidateParameters` 检查 Method 合法性、MaxCorners > 0、QualityLevel 范围 (0, 1]。
- 与 Halcon 的 `points_foerstner` / `points_harris` 类似，但本算子合并了检测与精化为单步操作。

## 核心 API 调用链 / Core API Call Chain
```
1. TryGetInputImage(inputs)                        -- 获取输入图像
2. GetStringParam("Method", "ShiTomasi")           -- 读取算法选择
3. GetIntParam("MaxCorners", 100, 1, 5000)         -- 读取最大角点数
4. GetDoubleParam("QualityLevel", 0.01, 1e-6, 1.0) -- 读取质量阈值
5. GetDoubleParam("MinDistance", 10.0, 0, 10000)   -- 读取最小间距
6. GetIntParam("BlockSize", 3, 2, 31)              -- 读取邻域块大小
7. Cv2.CvtColor(src, gray, BGR2GRAY)               -- 灰度转换 (若彩色)
8. Cv2.GoodFeaturesToTrack(gray, maxCorners, qualityLevel, minDistance,
                           noMask, blockSize, useHarris, 0.04)
9. Cv2.CornerSubPix(gray, corners, Size(5,5), Size(-1,-1),
                    TermCriteria(Eps|MaxIter, 30, 0.01))
10. Cv2.DrawMarker(result, center, Scalar(0,0,255), Cross, 12, 2)
11. CreateImageOutput(result, {Corners: points, Count: points.Count})
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Method` | `enum` | `"ShiTomasi"` | `Harris` / `ShiTomasi` | 角点检测算法。Shi-Tomasi 基于最小特征值，对 L 形/T 形角点更鲁棒；Harris 基于响应函数，对纹理丰富的区域更敏感。 |
| `MaxCorners` | `int` | `100` | [1, 5000] | 返回的最大角点数量。实际返回数可能少于此值（取决于图像内容）。 |
| `QualityLevel` | `double` | `0.01` | [1E-06, 1.0] | 角点质量阈值，相对于最佳角点响应值的比例。值越小检测越灵敏但噪声越多。 |
| `MinDistance` | `double` | `10.0` | [0.0, 10000.0] | 角点之间的最小欧氏距离（像素）。防止角点聚集。 |
| `BlockSize` | `int` | `3` | [2, 31] | 计算自相关矩阵的邻域块大小（奇数）。值越大对噪声越鲁棒但定位精度降低。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入待处理图像，支持灰度和彩色。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 可视化结果图，角点以红色十字标记。 |
| `Corners` | Corners | `PointList` | 亚像素精度的角点坐标列表 (`List<Position>`)。 |
| `Count` | Count | `Integer` | 检测到的角点数量。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N * BlockSize^2) 特征值计算，N 为像素数；CornerSubPix 精化 O(K * winSize^2 * iterations)，K 为角点数。 |
| 典型耗时 (Typical Latency) | 1920x1080 图像约 20-60ms（Shi-Tomasi, MaxCorners=100）；Harris 略快但精度稍低。 |
| 内存特征 (Memory Profile) | 需分配灰度图一份 Mat；角点数组 `Point2f[]` 和结果图各一份。内存占用与 MaxCorners 线性相关。 |

## 适用场景 / Use Cases
- **适合 (Suitable)：** 标定板角点检测；工件顶点定位；特征点匹配的预处理步骤；棋盘格/圆点阵列检测。
- **不适合 (Not Suitable)：** 纹理极少的平滑表面（无法产生足够角点响应）；高速实时场景（CornerSubPix 精化有额外开销）；需要亚像素精度低于 0.1 pixel 的极端场景。

## 已知限制 / Known Limitations
1. `BlockSize` 参数实际传入 `GoodFeaturesToTrack` 时作为 `blockSize` 使用，OpenCV 内部要求为奇数，但算子未强制校验奇偶性。
2. Harris 方法的 `k=0.04` 硬编码，无法通过参数调整。
3. `CornerSubPix` 的搜索窗口 5x5 和终止条件 30/0.01 均为硬编码，不可配置。
4. 对于大量角点场景（MaxCorners > 1000），`CornerSubPix` 精化阶段可能成为性能瓶颈。
5. 输入图像为空或通道数异常时返回 Failure，但不区分具体错误原因。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 Harris/Shi-Tomasi 双算法原理（自相关矩阵、特征值、响应函数）、CornerSubPix 亚像素精化细节、硬编码参数说明；完善参数语义描述；增加适用场景与已知限制的源码级分析 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
