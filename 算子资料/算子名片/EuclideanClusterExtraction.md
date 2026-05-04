# 欧氏聚类分割 / EuclideanClusterExtraction

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `EuclideanClusterExtractionOperator` |
| 枚举值 (Enum) | `OperatorType.EuclideanClusterExtraction` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 关键词 (Keywords) | PointCloud, Cluster, Segmentation, 3D |
| 图标 (Icon) | cluster |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
欧氏聚类分割基于**三维空间连通域分析**，将点云中空间距离相近的点归为同一聚类。核心思想如下：

1. **空间网格哈希**：以 `ClusterTolerance` 为边长构建三维空间网格哈希表，将每个点映射到对应的体素单元。
2. **邻域搜索**：对每个未访问的种子点，仅扫描其所在体素及周围 26 个相邻体素（共 27 个），查找距离小于 `ClusterTolerance` 的邻居点，避免 O(n^2) 暴力搜索。
3. **BFS/DFS 连通分量标记**：从种子点出发，通过广度优先搜索（BFS）逐步扩展连通域，将所有满足距离阈值的点标记为同一聚类。
4. **聚类过滤**：根据 `MinClusterSize` 和 `MaxClusterSize` 参数，过滤掉过大或过小的聚类分量。

> English: Euclidean cluster extraction builds 3D connected components using a spatial hash grid with cell size = `ClusterTolerance`. For each unvisited seed point, it scans 27 neighboring cells to find neighbors within the distance threshold, then performs BFS to grow the cluster. Clusters outside the min/max size range are discarded.

## 实现策略 / Implementation Strategy
当前实现采用**空间网格哈希 + BFS** 的组合策略，在保持算法简洁性的同时获得了接近线性的平均性能：

- **邻域查询优化**：使用网格哈希（cell size = `ClusterTolerance`），每个点只需检查 27 个相邻 cell，避免了 O(n^2) 的暴力距离计算。
- **双阶段输出**：算子同时调用 `Extract`（输出每聚类的索引数组 `List<int[]>`）和 `ExtractPointClouds`（输出每聚类的独立点云对象），便于下游按需选择索引或点云数据。
- **参数合法性校验**：`ValidateParameters` 在执行前检查 `MinClusterSize <= MaxClusterSize`，避免无效组合进入计算。

与 Halcon 的 `connection` 或 VisionPro 的聚类工具相比，本实现更侧重于 3D 点云场景的原生支持，无需中间投影或深度图转换。

> English: Uses spatial hash grid + BFS with dual-stage output (index arrays + materialized point clouds per cluster). Parameter validation prevents invalid Min/Max combinations before execution.

## 核心 API 调用链 / Core API Call Chain
1. `EuclideanClusterExtractionOperator.ExecuteCoreAsync` -- 入口，获取输入点云与参数
2. `OperatorBase.RunCpuBoundWork(...)` -- 将 CPU 密集计算调度到线程池
3. `EuclideanClusterExtraction.Extract(cloud, clusterTolerance, minClusterSize, maxClusterSize)` -- 提取聚类索引数组
4. `EuclideanClusterExtraction.ExtractPointClouds(cloud, clusterTolerance, minClusterSize, maxClusterSize)` -- 提取聚类独立点云

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ClusterTolerance` | `double` | `0.02` | `[1e-6, 1000]` | 连通距离阈值（与点云坐标同单位）。两点欧氏距离小于此值则视为连通。值越小聚类越精细，值越大越容易合并邻近物体。典型值：点云间距的 1.5~3 倍。 |
| `MinClusterSize` | `int` | `100` | `[1, 10000000]` | 最小聚类点数。点数少于此值的连通分量将被丢弃，用于过滤噪声碎片。 |
| `MaxClusterSize` | `int` | `1000000` | `[1, 10000000]` | 最大聚类点数。点数超过此值的连通分量将被丢弃，用于过滤异常大聚类（如背景或大面积平面残余）。约束：必须 >= `MinClusterSize`。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | Yes | 输入三维点云。点云不能为空。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Clusters` | `Clusters` | `Any` | `List<int[]>`：每个聚类包含的点索引数组列表。 |
| `ClusterCount` | `Cluster Count` | `Integer` | 检测到的有效聚类数量（过滤后）。 |
| `ClusterPointClouds` | `Cluster Point Clouds` | `Any` | `List<PointCloud>`：每个聚类的独立点云对象，便于下游单独处理。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 平均 O(n)，最坏情况 O(n * k)（k 为单体素内最大点数，均匀分布时 k 极小）。网格哈希使邻域查询接近常数时间。 |
| 典型耗时 (Typical Latency) | 与点云总点数和空间密度分布强相关。建议先进行体素降采样以控制百万级点云的处理耗时。 |
| 内存特征 (Memory Profile) | O(n) 用于访问标记数组和网格哈希桶。额外输出 `ClusterPointClouds` 时会有聚类点云的完整拷贝，总内存可达 O(n + sum(cluster_sizes))。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：从场景点云中分割多个空间分离的物体，为后续 PPF 匹配、测量、抓取规划提供候选区域。
- **适合 (Suitable)**：工业质检中分离传送带上多个工件的点云。
- **适合 (Suitable)**：机器人分拣场景中识别和定位多个独立目标。
- **不适合 (Not Suitable)**：点云密度极不均匀或对象紧密接触/重叠的场景，可能合并为单一聚类，需要配合其他分割策略（如 RANSAC 剔除平面后再聚类）。
- **不适合 (Not Suitable)**：需要基于表面形状或语义特征进行分割的场景，欧氏距离无法捕获几何语义。

## 已知限制 / Known Limitations
1. 单阈值距离连通无法处理"相交/接触"目标的分离。若多个物体在空间上接触，会被合并为同一聚类，需要额外的平面剔除或形状约束。
2. 网格哈希的 cell size 固定为 `ClusterTolerance`，在密度差异极大的区域可能出现过度合并或过度分裂。
3. `Extract` 和 `ExtractPointClouds` 内部各自独立执行一遍聚类计算，存在重复计算开销；若后续版本合并为单次计算可进一步优化。
4. 输出聚类中点的顺序取决于网格遍历顺序，不保证与输入点云的原始顺序一致。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充空间网格哈希 + BFS 算法原理、双阶段输出策略、参数语义、性能分析与限制说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
