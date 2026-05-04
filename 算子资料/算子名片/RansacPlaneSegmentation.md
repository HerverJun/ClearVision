# RANSAC平面分割 / RansacPlaneSegmentation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RansacPlaneSegmentationOperator` |
| 枚举值 (Enum) | `OperatorType.RansacPlaneSegmentation` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 关键词 (Keywords) | PointCloud, RANSAC, Plane, Segmentation, 3D |
| 图标 (Icon) | plane |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
RANSAC 平面分割是一种基于**随机采样一致性**的平面拟合方法，能够在包含大量离群点的点云中稳健地估计平面模型。核心流程：

1. **随机采样假设**：每次迭代随机选取 3 个点，计算通过这 3 点的平面方程 `ax + by + cz + d = 0`（其中 `(a,b,c)` 为法向量）。
2. **内点计分**：遍历点云中所有点，计算每个点到假设平面的距离，距离小于 `DistanceThreshold` 的点计为内点（inlier）。
3. **最优模型选取**：保留内点数最多的平面假设作为当前最优模型。
4. **PCA 精修**：对最优模型的内点集做 PCA 分析（3x3 协方差矩阵的最小特征向量作为法向量），重新计算精确平面方程和内点集。

> English: RANSAC plane segmentation randomly samples 3 points per iteration to hypothesize a plane, scores by inlier count within `DistanceThreshold`, keeps the best model, then refines via PCA on inliers to compute the final plane coefficients.

## 实现策略 / Implementation Strategy
当前实现采用**两阶段候选评分**策略，在保持 RANSAC 鲁棒性的同时优化大规模点云的处理效率：

- **两阶段评分**：先用子样本粗筛候选平面，再对少量高分候选做全量精评，显著降低百万点场景的处理延迟。
- **PCA 精修**：使用 `OpenCvSharp.Cv2.Eigen` 对 3x3 协方差矩阵做特征分解，取最小特征向量作为最终法向量，比单纯 3 点拟合更精确。
- **确定性控制**：`RandomSeed = 0` 时使用确定性种子（基于输入数据），非零值为指定随机种子，保证结果可复现。
- **双阶段输出**：先执行 `Segment` 获取平面系数和内点索引，再执行 `ExtractInlierCloud` 提取内点点云，便于下游按需使用。
- **失败检测**：若最终内点数 < `MinInliers`，判定为 `NoPlaneFound` 失败，避免输出低质量平面。

与 Halcon 的 `fit_plane_ransac` 或 PCL 的 `SACSegmentation` 相比，本实现增加了两阶段候选评分优化和内点点云直接物化输出。

> English: Two-stage candidate scoring with PCA refinement using `Cv2.Eigen`. Supports deterministic seeding via `RandomSeed`. Dual-stage output: plane coefficients + inlier indices first, then materialized inlier point cloud.

## 核心 API 调用链 / Core API Call Chain
1. `RansacPlaneSegmentationOperator.ExecuteCoreAsync` -- 入口，获取输入点云与参数
2. `OperatorBase.RunCpuBoundWork(...)` -- CPU 密集计算调度
3. `RansacPlaneSegmentation.Segment(cloud, distanceThreshold, maxIterations, minInliers)` -- 核心 RANSAC 平面分割
4. `OperatorBase.RunCpuBoundWork(...)` -- 第二次调度
5. `RansacPlaneSegmentation.ExtractInlierCloud(cloud, result.Inliers)` -- 提取内点点云

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `DistanceThreshold` | `double` | `0.01` | `[1e-6, 1000]` | 内点距离阈值（与点云坐标同单位）。点到假设平面的距离小于此值则视为内点。值越小越严格，要求拟合越精确。典型值：测量噪声的 2~3 倍。 |
| `MaxIterations` | `int` | `1000` | `[1, 200000]` | RANSAC 最大迭代次数。越大越可能找到全局最优平面，但计算时间线性增长。对于内点比例较高的场景（如 >50%），较少迭代即可收敛。 |
| `MinInliers` | `int` | `100` | `[1, 10000000]` | 最小内点数门槛。最终内点数必须 >= 此值才视为成功找到平面。用于过滤噪声区域的虚假平面。 |
| `RandomSeed` | `int` | `0` | `[0, 2147483647]` | 随机种子。`0` 为确定性模式（基于输入数据推导种子），结果在同一输入下完全可复现；非零值为指定随机种子。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | `Point Cloud` | `Any` | Yes | 输入三维点云。点云不能为空。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `PlaneA` | `Plane A` | `Float` | 平面方程 `ax + by + cz + d = 0` 中的系数 a（法向量 X 分量）。法向量为单位向量。 |
| `PlaneB` | `Plane B` | `Float` | 平面方程系数 b（法向量 Y 分量）。 |
| `PlaneC` | `Plane C` | `Float` | 平面方程系数 c（法向量 Z 分量）。 |
| `PlaneD` | `Plane D` | `Float` | 平面方程系数 d（原点到平面的有符号距离 = d / ||(a,b,c)||）。 |
| `InlierCount` | `Inlier Count` | `Integer` | 内点总数（距离平面 < `DistanceThreshold` 的点数）。 |
| `InlierRatio` | `Inlier Ratio` | `Float` | 内点比例：`InlierCount / TotalPointCount`。可用于评估平面质量。 |
| `Inliers` | `Inliers` | `Any` | 内点索引数组 `int[]`，指向输入点云中的点索引。 |
| `InlierPointCloud` | `Inlier Point Cloud` | `Any` | 内点点云对象，从输入点云中提取的完整内点数据。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | O(n * MaxIterations)，每次迭代需遍历全部点计算距离。两阶段优化可将实际计算量降低至 O(n * MaxIterations * f)，f < 1。 |
| 典型耗时 | 阶段 2 专项验收（Release，100 万点，阈值 1.5mm，144 次迭代）核心分割 < 300ms。若同时输出 `InlierPointCloud`，总耗时会高于核心分割（含点云拷贝）。 |
| 内存特征 | O(n) 用于距离计算暂存和内点标记。额外输出 `InlierPointCloud` 时有内点点云的完整拷贝。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：从场景点云中提取主平面（桌面、地面、工装平面），作为后续聚类、配准、测量的基准。
- **适合 (Suitable)**：工业检测中移除背景平面，提取感兴趣的目标物体。
- **适合 (Suitable)**：配合 `EuclideanClusterExtraction` 使用：先剔除主平面，再对剩余点云做聚类分割。
- **不适合 (Not Suitable)**：未下采样的超大点云（百万级）直接使用时迭代遍历开销大，建议先体素降采样。
- **不适合 (Not Suitable)**：需要同时检测多个平面的场景，本算子仅做单平面提取，多平面需迭代剔除内点后重复调用。
- **不适合 (Not Suitable)**：曲面或非平面结构的拟合，RANSAC 平面模型无法捕获曲率信息。

## 已知限制 / Known Limitations
1. 仅做单平面提取。多平面场景需要外层循环：提取平面 -> 剔除内点 -> 重新调用，直到内点数不足。
2. 若同时物化 `InlierPointCloud`，算子总耗时会显著高于核心分割耗时（点云拷贝成本），非平面估计本身的瓶颈。
3. `DistanceThreshold` 的选择对结果影响极大：过小会导致内点不足，过大会将非平面点误判为内点。
4. 平面法向量方向不保证一致性（可能朝上或朝下），下游使用时需根据场景做方向校验。
5. 对于包含多个近似平行平面的场景（如多层货架），可能需要配合高度范围裁剪预处理。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 RANSAC 算法原理、两阶段候选评分策略、PCA 精修、RandomSeed 确定性控制、平面方程系数语义、性能验收数据 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
