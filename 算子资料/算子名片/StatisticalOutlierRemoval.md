# 统计滤波 / StatisticalOutlierRemoval

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `StatisticalOutlierRemovalOperator` |
| 枚举值 (Enum) | `OperatorType.StatisticalOutlierRemoval` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 关键词 (Keywords) | PointCloud, Filter, Outlier, SOR, 3D |
| 图标 (Icon) | filter |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**统计离群点去除（SOR）** 是一种基于邻域统计的点云噪声过滤方法，由 Rusu 等人提出。核心思想：正常点的邻域密度相对均匀，而离群点的邻域通常稀疏。算法流程：

1. **K 近邻距离统计**：对每个点计算其到 K 个最近邻点的平均距离 `mean_dist_i`。
2. **全局统计**：计算所有点 `mean_dist_i` 的全局均值 `mu` 和标准差 `sigma`。
3. **阈值过滤**：移除 `mean_dist_i > mu + StddevMul * sigma` 的点，保留邻域密度正常的点。

```
keep point i  if  mean_dist_i <= mu + StddevMul * sigma
```

该方法假设正常点的邻域距离服从近似正态分布，离群点表现为分布尾部的异常值。

> English: SOR computes each point's mean distance to its K nearest neighbors, then removes points whose mean distance exceeds `global_mean + StddevMul * global_std`. Normal points have uniform neighborhood density; outliers appear in the distribution tail.

## 实现策略 / Implementation Strategy
当前实现采用**暴力 KNN + 固定大小最大堆**策略：

1. **暴力 KNN**：对每个点遍历全部其他点计算距离，使用固定大小的最大堆（max-heap）维护 K 个最小距离。适合点数不大（<= 100k）的场景。
2. **非有限数处理**：对 NaN/Inf 距离跳过统计，相关点自然被剔除，避免污染全局均值/方差。
3. **属性保留**：过滤后保留输入点云的 `Colors` 和 `Normals`（若存在），输出为非 organized 点云。
4. **移除计数**：通过 `Math.Max(0, originalCount - filteredCount)` 输出移除点数，保证非负。

与 PCL 的 `StatisticalOutlierRemoval` 或 Open3D 的实现相比，本实现使用暴力搜索而非 KDTree，适用于中小规模点云；大规模场景建议先体素降采样。

> English: Brute-force KNN with a fixed-size max-heap per point to track the K smallest distances. Non-finite distances are skipped. Preserves Colors and Normals. Best for point clouds up to ~100k points.

## 核心 API 调用链 / Core API Call Chain
1. `StatisticalOutlierRemovalOperator.ExecuteCoreAsync` -- 入口，获取输入点云与参数
2. `OperatorBase.RunCpuBoundWork(...)` -- CPU 密集计算调度
3. `StatisticalOutlierRemoval.Filter(cloud, meanK, stddevMul)` -- 核心 SOR 过滤
4. （内部）暴力 KNN + 固定大小最大堆，计算每点 K 近邻平均距离
5. （内部）计算全局均值和标准差，阈值过滤离群点

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MeanK` | `int` | `50` | `[1, 500]` | 每个点参与统计的近邻数量（K 值）。K 越大统计越稳定，但计算量为 O(n * n * logK)（暴力搜索）。K 过小可能导致正常点被误判为离群点。典型值：20~100。 |
| `StddevMul` | `double` | `1.0` | `[0.0, 10.0]` | 标准差倍数阈值。值越大过滤越"宽松"，保留的点越多。`0` 表示仅保留均值处的点（极端严格），`3` 表示保留 99.7% 的正态分布点。典型值：1.0~2.0。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | Yes | 输入三维点云。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | 过滤后的点云，已移除统计离群点。保留 Colors 和 Normals（若输入包含）。 |
| `PointCount` | `Point Count` | `Integer` | 过滤后点云的总点数。 |
| `RemovedCount` | `Removed Count` | `Integer` | 被移除的离群点数量：`originalCount - filteredCount`。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | O(n^2 * logK)（暴力 KNN，每点遍历全部其他点 + 最大堆维护）。对于大规模点云性能瓶颈明显。 |
| 典型耗时 | 与点数强相关。10 万点级别可能需要数秒，百万级不建议直接使用。建议先体素降采样再做统计滤波。 |
| 内存特征 | O(n) 用于存储每点的 mean_dist 数组 + 输出点云拷贝。最大堆大小固定为 K，内存开销可忽略。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：点云含少量随机离群点（散点噪声），用于分割、匹配、配准前的预清洗。
- **适合 (Suitable)**：扫描设备产生的椒盐噪声或反射噪声的去除。
- **适合 (Suitable)**：配合 RANSAC 平面分割或欧氏聚类使用，先清洗再分割效果更佳。
- **不适合 (Not Suitable)**：超大点云（百万级）且未做下采样，当前暴力实现会非常慢。建议先 `VoxelDownsample` 或后续引入 KDTree 加速。
- **不适合 (Not Suitable)**：需要精确保留所有原始点的场景，SOR 会不可避免地移除部分边界点。
- **不适合 (Not Suitable)**：结构性噪声（如大面积错误点云），SOR 仅处理统计异常，无法识别结构性伪影。

## 已知限制 / Known Limitations
1. 当前为暴力 KNN 版本，时间复杂度 O(n^2 * logK)，点数过大时不建议直接使用。后续可替换为 KDTree 加速至 O(n * logn * logK)。
2. SOR 是统计阈值方法，可能会剔除少量边界点或稀疏区域点（属于算法特性，非缺陷）。
3. `StddevMul = 0` 时会极端严格，仅保留邻域距离恰好等于全局均值的点，实际场景中几乎无用。
4. 对于密度变化剧烈的点云（如近密远疏），全局统计可能不够准确，局部自适应版本效果更佳。
5. 不保证输出点的顺序与输入一致（过滤过程会重组点序）。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 SOR 算法数学原理、暴力 KNN + 最大堆实现策略、非有限数处理、参数语义、性能瓶颈分析与 KDTree 优化建议 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
