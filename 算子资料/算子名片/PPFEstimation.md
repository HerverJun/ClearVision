# PPF点对特征 / PPFEstimation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PPFEstimationOperator` |
| 枚举值 (Enum) | `OperatorType.PPFEstimation` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 关键词 (Keywords) | PointCloud, PPF, Feature, 3D |
| 图标 (Icon) | ppf |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
**PPF（Point Pair Feature，点对特征）** 是一种用于三维物体识别的局部几何描述子，由 Drost 等人（2010）提出。对于点云中任意两点 p1 和 p2，其 PPF 为一个 4 维向量：

```
F(p1, p2) = (||d||, ∠(n1, d), ∠(n2, d), ∠(n1, n2))
```

其中：
- `d = p2 - p1` 为两点间的位移向量
- `||d||` 为欧氏距离
- `n1, n2` 分别为两点的法向量
- `∠(n1, d)` 为法向量 n1 与连线方向 d 的夹角
- `∠(n2, d)` 为法向量 n2 与连线方向 d 的夹角
- `∠(n1, n2)` 为两法向量之间的夹角

本算子为每个参考点计算其与 `FeatureRadius` 邻域内所有邻居点的 PPF 列表，构建 **per-point PPF map**，作为后续 PPF 匹配（W8-2）的模型特征输入。

> English: PPF is a 4D descriptor for point pairs: (distance, angle(n1,d), angle(n2,d), angle(n1,n2)). This operator builds a per-point feature map by computing PPF features between each reference point and its neighbors within `FeatureRadius`.

## 实现策略 / Implementation Strategy
当前实现分为两个阶段，均通过 `RunCpuBoundWork` 调度到线程池执行：

1. **法向估计阶段**：
   - 若输入点云已有法向量且 `UseExistingNormals=true`，直接复用。
   - 否则通过 PCA 估计法向量：对每个点的 `NormalRadius` 邻域构建协方差矩阵，取最小特征向量作为法向量。
   - 邻域搜索使用空间哈希网格（cell size = radius），每点仅扫描 27 个相邻 cell。

2. **PPF 特征计算阶段**：
   - 对每个参考点，在 `FeatureRadius` 邻域内查找邻居点。
   - 对每对 (参考点, 邻居点) 计算 4 维 PPF 向量。
   - 角度计算使用 `acos(dot(n1, n2))`（dot 值 clamp 到 [-1,1]），保留有符号角度信息。
   - 输出 `Dictionary<int, List<PPFFeature>>`，key 为参考点索引。

法向估计的邻域半径 `NormalRadius` 和 PPF 计算的邻域半径 `FeatureRadius` 独立配置，允许对法向平滑度和特征覆盖范围分别调优。

> English: Two-phase pipeline: (1) PCA-based normal estimation via spatial hash grid, (2) per-point PPF feature computation within FeatureRadius. Normals are reused when available and `UseExistingNormals=true`.

## 核心 API 调用链 / Core API Call Chain
1. `PPFEstimationOperator.ExecuteCoreAsync` -- 入口，获取输入点云与参数
2. `OperatorBase.RunCpuBoundWork(...)` -- CPU 密集计算调度
3. `PPFEstimation.ComputePointCloudWithNormals(cloud, normalRadius, useExistingNormals)` -- 法向估计（若需要）
4. `PPFEstimation.ComputeModel(withNormals, normalRadius, featureRadius, useExistingNormals: true)` -- 构建 per-point PPF map
5. （内部）`NormalEstimation.Estimate(...)` -- PCA 法向量计算

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `NormalRadius` | `double` | `0.03` | `[1e-6, 1000]` | 法向估计的邻域半径（与点云坐标同单位）。越大法向越平滑，但会丢失细节；越小对噪声越敏感。典型值为点云分辨率的 2~5 倍。 |
| `FeatureRadius` | `double` | `0.05` | `[1e-6, 1000]` | PPF 特征计算的邻域半径。决定了每个参考点与多少邻居点构成点对。越大特征覆盖范围越广，但计算量和内存开销也越大。 |
| `UseExistingNormals` | `bool` | `true` | -- | 若输入点云已包含法向量数据，设为 `true` 可跳过法向估计阶段直接复用，节省计算时间。设为 `false` 则强制重新估计法向量。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | Yes | 输入三维点云。点云不能为空。若 `UseExistingNormals=true`，需已包含法向量。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `PPFMap` | `PPF Map` | `Any` | `Dictionary<int, List<PPFFeature>>`：per-point PPF 特征映射，key 为参考点索引，value 为该点与邻居点的 PPF 特征列表。 |
| `PointCloudWithNormals` | `Point Cloud With Normals` | `Any` | 包含法向量的点云副本（无论输入是否已有法向量，输出均保证包含法向）。 |
| `PointCount` | `Point Count` | `Integer` | 输出点云的总点数（与输入一致，仅在法向重估计后可能因 NaN 过滤而略有变化）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | O(n * k)，其中 k 为 `FeatureRadius` 邻域内的平均邻居数。法向估计阶段同样为 O(n * k_n)，k_n 为 `NormalRadius` 邻域内平均邻居数。 |
| 典型耗时 | 与点云总点数和密度强相关。`FeatureRadius` 越大，邻居数越多，耗时接近线性增长。建议先体素降采样以控制百万级点云的处理时间。 |
| 内存特征 | 主要开销为法向量数组 O(3n) 和 per-point PPF map。PPF map 的总大小为 O(n * k)，k 较大时内存可能成为瓶颈。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：为后续 PPF 匹配算子（PPFMatch）构建模型特征，是 PPF 粗配准流水线的第一步。
- **适合 (Suitable)**：比较两个点云局部几何一致性的场景，如点云配准质量评估。
- **适合 (Suitable)**：需要提取点对几何描述子的自定义 3D 分析流水线。
- **不适合 (Not Suitable)**：超大点云且 `FeatureRadius` 过大导致邻居数爆炸的场景，内存和计算时间都会急剧增长，需要配合体素降采样。
- **不适合 (Not Suitable)**：无法获取可靠法向量的点云（如极稀疏或噪声极大的数据），PPF 依赖法向量计算角度。

## 已知限制 / Known Limitations
1. PPF map 以 `Dictionary<int, List<PPFFeature>>` 存储，每个点的特征列表长度不固定，内存开销可能很大。匹配阶段通常会通过离散化哈希表或随机采样降低规模。
2. 本算子仅完成特征计算，不执行匹配投票与位姿估计，需要配合 PPFMatch 算子完成完整的 PPF 匹配流程。
3. 法向量存在二义性（sign ambiguity）：PCA 估计的法向方向不确定。本算子不做法向一致性翻转，该问题需在匹配阶段通过投票或显式处理消解。
4. `NormalRadius` 和 `FeatureRadius` 的选择高度依赖点云密度和分辨率，缺乏自适应机制，需要用户根据实际数据标定。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 PPF 4 维描述子数学定义、两阶段实现策略、法向估计细节、参数语义、内存分析与法向二义性说明 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
