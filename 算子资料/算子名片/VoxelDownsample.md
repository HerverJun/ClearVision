# 体素下采样 / VoxelDownsample

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VoxelDownsampleOperator` |
| 枚举值 (Enum) | `OperatorType.VoxelDownsample` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 关键词 (Keywords) | PointCloud, Voxel, Downsample, 3D |
| 图标 (Icon) | voxel |
| 作者 (Author) | 蘅芜君 |
| 依赖 (Dependencies) | OpenCvSharp, Acme.Product.Infrastructure.PointCloud |

## 算法原理 / Algorithm Principle
**体素网格降采样**是一种基于三维空间均匀分桶的点云简化方法。核心思想：

1. **空间分桶**：以 `LeafSize` 为边长将三维空间划分为均匀的体素网格（voxel grid），将每个点映射到其所属的体素单元。
2. **质心计算**：对每个非空体素，计算其中所有点的坐标质心（centroid），作为该体素的代表点。
3. **可选属性聚合**：若点云包含颜色（Colors）或法向量（Normals），会对同一体素内的属性做均值聚合，并对聚合后的法向量做归一化。
4. **输出**：每个非空体素输出一个代表点，实现点云简化。

```
output_point = centroid(all points in voxel)
```

该方法保证了点云的空间均匀性，不会出现局部过度稀疏或过度密集的问题。

> English: Voxel grid downsampling bins each point into a uniform 3D grid of size `LeafSize`, computes the centroid per occupied voxel, and optionally aggregates colors and normals. Each occupied voxel emits one representative point.

## 实现策略 / Implementation Strategy
当前实现封装 `VoxelGridFilter`，采用**字典分桶 + 质心累积**策略：

1. **体素键计算**：对每个点的 (x, y, z) 坐标除以 `LeafSize` 并取整，生成 `VoxelKey` 作为字典的 key。
2. **字典累积**：使用 `Dictionary<VoxelKey, accumulator>` 累积每个体素内的点坐标、颜色和法向量。
3. **质心输出**：遍历字典，对每个非空体素计算质心坐标，输出降采样后的点云。
4. **属性保留**：颜色做均值聚合，法向量做均值聚合后重新归一化。

与 PCL 的 `VoxelGrid` 或 Open3D 的实现相比，本实现通过 `RunCpuBoundWork` 调度到线程池，不阻塞调用线程。

> English: Dictionary-based binning with centroid accumulation. VoxelKey is computed from floor(coord / LeafSize). Colors and normals are averaged per voxel; normals are renormalized after aggregation.

## 核心 API 调用链 / Core API Call Chain
1. `VoxelDownsampleOperator.ExecuteCoreAsync` -- 入口，获取输入点云与参数
2. `OperatorBase.RunCpuBoundWork(...)` -- CPU 密集计算调度
3. `VoxelGridFilter.Downsample(cloud, leafSize)` -- 核心体素降采样
4. （内部）`VoxelKey` 字典分桶 + 质心/颜色/法向量累积

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `LeafSize` | `double` | `0.01` | `[1e-6, 10000]` | 体素边长（与点云坐标同单位）。值越大降采样越激进，输出点越少；值越小越接近原始点云密度。典型值：点云分辨率的 1~3 倍。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | Yes | 输入三维点云。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | 降采样后的点云。每个非空体素输出一个质心代表点。颜色和法向量（若存在）已做均值聚合。 |
| `PointCount` | `Point Count` | `Integer` | 降采样后的点数（即非空体素数量）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | O(N)，单次遍历全部点进行分桶，字典操作为均摊 O(1)。 |
| 典型耗时 | 线性于输入点数，通常为点云预处理流水线中最快的步骤之一。无专用性能基准，由点云单元测试和流程测试覆盖。 |
| 内存特征 | O(V)，V 为非空体素数量。每个体素需存储累积的坐标和（可选的）颜色/法向量。V 远小于 N 时内存优势明显。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：在配准（PPF 匹配、ICP）、聚类分割或表面检测前降低点云密度，加速后续处理。
- **适合 (Suitable)**：保持近似几何形状的同时，通过质心平均降低单点噪声。
- **适合 (Suitable)**：需要均匀空间采样的场景，体素降采样保证空间分布的均匀性。
- **不适合 (Not Suitable)**：需要保留每个原始点或 organized 点云拓扑结构的应用，体素降采样会破坏原始点序。
- **不适合 (Not Suitable)**：`LeafSize` 极小导致几乎每个体素仅含一个点，降采样效果微乎其微，徒增计算开销。
- **不适合 (Not Suitable)**：需要保留尖锐特征（如棱边、角点）的场景，质心平均会模糊这些几何特征。

## 已知限制 / Known Limitations
1. 输出始终为非 organized 点云，即使输入为 organized 点云（如深度图展开），原始的行列拓扑信息会丢失。
2. 体素代表点为质心而非最近原始采样点，因此精确的原始点身份信息不保留。对于需要精确点对应的场景（如特征匹配），需使用其他采样策略。
3. `LeafSize` 的选择需要根据点云密度手动标定，缺乏自适应机制。密度过高的区域可能需要更小的 `LeafSize`。
4. 颜色和法向量的均值聚合在体素内属性变化剧烈时可能产生"模糊"效果。
5. 对于非均匀密度的点云，固定 `LeafSize` 可能导致稀疏区域过度简化、密集区域简化不足。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充体素网格分桶算法原理、字典累积实现策略、颜色/法向量聚合细节、LeafSize 参数语义、organized 点云限制 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
