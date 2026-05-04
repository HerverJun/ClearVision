# PPF表面匹配 / PPFMatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PPFMatchOperator` |
| 枚举值 (Enum) | `OperatorType.PPFMatch` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.4 |
| 关键词 (Keywords) | PointCloud, PPF, Match, Pose, 3D |
| 图标 (Icon) | match3d |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
PPF 表面匹配是一种基于**点对特征投票**的三维粗配准方法，用于估计模型点云到场景点云的刚体变换（旋转 + 平移）。核心流程：

1. **模型哈希表构建**：对模型点云的 PPF 特征进行量化并存入哈希表，每个哈希桶记录具有相似 PPF 的参考点集合。
2. **场景采样与投票**：从场景点云中按步长采样参考点，计算其 PPF 特征并在模型哈希表中查找匹配，通过投票确定模型参考点与场景参考点的对应关系。
3. **RANSAC 刚体变换估计**：基于对应关系，通过 RANSAC 迭代采样最小点集（3 点），估计 4x4 刚体变换矩阵（旋转 + 平移），以 RMS 误差和内点数评分。
4. **验证与消歧**：对候选变换进行法向一致性验证（`NormalConsistency`）、稳定性评分（`StabilityScore`）和歧义检测（`AmbiguityScore`），输出最终匹配结果。

> English: PPF surface matching uses point-pair feature voting for 3D coarse registration. It builds a quantized PPF hash table on the model, samples scene reference points for voting, estimates rigid transforms via RANSAC, and verifies candidates by normal consistency, stability scoring, and ambiguity detection.

## 实现策略 / Implementation Strategy
当前实现采用**简化 PPF 粗配准**流水线，面向工业可用的轻量方案：

- **哈希量化**：PPF 特征按 `DistanceStep` 和 `AngleStepRad` 进行离散化，构建紧凑的哈希表，降低内存占用。
- **采样控制**：`NumSamples` 控制场景参考点采样数量，`ModelRefStride` 控制模型参考点的步长采样，平衡精度与速度。
- **RANSAC 鲁棒估计**：`RansacIterations` 次迭代中随机采样对应关系，估计刚体变换并以 `InlierThreshold` 阈值统计内点。
- **多层验证**：
  - 内点数 >= `MinInliers` 为基本门槛
  - `StabilityScore` 评估变换的几何稳定性
  - `NormalConsistency` 评估模型与场景法向的一致性
  - `AmbiguityScore` 检测是否存在多个等价解（对称性歧义）
- **确定性控制**：`Seed` 参数控制随机种子，`Seed >= 0` 时结果可复现，`Seed = -1` 为非确定性随机采样。

与 Halcon 的 `surface_matching` 相比，本实现更侧重于粗配准场景，未覆盖完整的 ICP 精修路径，需配合后续精配准算子。

> English: Lightweight PPF coarse registration with quantized hash table, RANSAC rigid transform estimation, and multi-layer verification (inlier count, stability, normal consistency, ambiguity). Supports deterministic seeding via `Seed` parameter.

## 核心 API 调用链 / Core API Call Chain
1. `PPFMatchOperator.ExecuteCoreAsync` -- 入口，获取模型/场景点云与参数
2. `OperatorBase.RunCpuBoundWork(...)` -- CPU 密集计算调度
3. `PPFMatcher.Match(model, scene, normalRadius, featureRadius, distanceStep, angleStepRad, numSamples, modelRefStride, ransacIterations, inlierThreshold, minInliers)` -- 核心匹配
4. （内部）`EnsureNormals(...)` -- 确保模型和场景点云包含法向量
5. （内部）`BuildModelHash(...)` -- 构建模型 PPF 量化哈希表
6. （内部）`BuildSceneCorrespondences(...)` -- 场景采样与投票
7. （内部）`RansacRigidTransform(...)` -- RANSAC 刚体变换估计
8. （内部）`RefineTransform(...)` -- 变换精修

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `NormalRadius` | `double` | `0.03` | `[1e-6, 1000]` | 法向估计的邻域半径。模型和场景点云共用此参数进行法向量计算。典型值为点云分辨率的 2~5 倍。 |
| `FeatureRadius` | `double` | `0.08` | `[1e-6, 1000]` | PPF 特征计算的邻域半径。决定点对的空间覆盖范围。通常大于 `NormalRadius` 以捕获更宏观的几何关系。 |
| `NumSamples` | `int` | `120` | `[10, 5000]` | 场景参考点的采样数量。越大匹配越鲁棒，但计算时间线性增长。 |
| `ModelRefStride` | `int` | `3` | `[1, 50]` | 模型参考点的步长采样间隔。`1` 表示使用全部模型点，`3` 表示每 3 个点取 1 个。增大可加速哈希构建但降低模型细节覆盖。 |
| `Seed` | `int` | `123` | `[-1, 2147483647]` | 随机种子。`>= 0` 时结果确定性可复现；`-1` 为非确定性随机采样。生产环境建议固定种子以保证可复现性。 |
| `RansacIterations` | `int` | `800` | `[50, 100000]` | RANSAC 迭代次数。越大越可能找到全局最优变换，但计算时间线性增长。 |
| `InlierThreshold` | `double` | `0.005` | `[1e-6, 1000]` | 内点判定的距离阈值（与点云坐标同单位）。变换后模型点与场景点距离小于此值则视为内点。 |
| `MinInliers` | `int` | `80` | `[3, 1000000]` | 最小内点数门槛。匹配结果的内点数必须达到此值才可能通过验证。 |
| `DistanceStep` | `double` | `0.01` | `[1e-6, 1000]` | PPF 哈希表中距离维度的量化步长。越小精度越高但哈希表越大。 |
| `AngleStepDeg` | `double` | `5.0` | `[0.1, 90.0]` | PPF 哈希表中角度维度的量化步长（度）。内部转换为弧度：`angleStepRad = angleStepDeg * PI / 180`。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `ModelPointCloud` | `Model Point Cloud` | `Any` | Yes | 模型点云（待匹配的 CAD 模型或参考点云）。不能为空。 |
| `ScenePointCloud` | `Scene Point Cloud` | `Any` | Yes | 场景点云（实际采集的工件或环境点云）。不能为空。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `IsMatch` | `Is Match` | `Boolean` | 综合验证结果：`true` 表示匹配成功且通过所有验证（内点数、稳定性、法向一致性、无歧义）。 |
| `IsMatched` | `Is Matched` | `Boolean` | 原始匹配标志（不含多层验证），表示 RANSAC 是否找到了满足内点门槛的变换。 |
| `Score` | `Score` | `Float` | 匹配得分：验证通过时为 `InlierCount / ModelPointCount`，验证失败时为 `0.0`。 |
| `MatchCount` | `Match Count` | `Integer` | 匹配计数：验证通过时为 `1`，否则为 `0`。 |
| `Method` | `Method` | `String` | 匹配方法标识，固定为 `"PPF-CoarsePose"`。 |
| `FailureReason` | `Failure Reason` | `String` | 匹配失败原因：`""`（成功）、`"Ambiguous coarse pose solution."`、`"PPF coarse pose stability verification failed."`、`"PPF coarse pose normal-consistency verification failed."`、`"PPF coarse pose verification failed."`。 |
| `VerificationPassed` | `Verification Passed` | `Boolean` | 验证是否通过（`IsMatched && !IsAmbiguous && failureReason` 为空）。 |
| `AmbiguityDetected` | `Ambiguity Detected` | `Boolean` | 是否检测到对称性歧义（存在多个等价变换解）。 |
| `AmbiguityScore` | `Ambiguity Score` | `Float` | 歧义评分。值越高表示候选解之间的区分度越低。 |
| `StabilityScore` | `Stability Score` | `Float` | 变换稳定性评分。低于 `PPFMatcher.MinimumRecommendedStabilityScore` 则判定为不稳定。 |
| `NormalConsistency` | `Normal Consistency` | `Float` | 法向一致性评分。低于 `PPFMatcher.MinimumRecommendedNormalConsistency` 则判定为法向不一致。 |
| `TransformMatrix` | `Transform Matrix` | `Any` | `Matrix4x4`：模型到场景的刚体变换矩阵（旋转 + 平移）。 |
| `InlierCount` | `Inlier Count` | `Integer` | 变换后的内点数量。 |
| `InlierRatio` | `Inlier Ratio` | `Float` | 内点比例：`InlierCount / ModelPointCount`。 |
| `CorrespondenceCount` | `Correspondence Count` | `Integer` | 投票阶段找到的候选对应关系数量。 |
| `RmsError` | `RMS Error` | `Float` | 内点的均方根误差（变换后模型点与场景对应点的距离）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 | 主要由模型哈希构建 O(m)、场景采样 O(NumSamples * k) 和 RANSAC 迭代 O(RansacIterations) 组成，近似随候选对应数线性增长。 |
| 典型耗时 | 阶段 2 专项验收（Release，4500 点模型 + 4500 点场景）P50 = 1786.35ms。大规模点云建议先体素降采样。 |
| 内存特征 | 主要来自 PPF 哈希表（受 `DistanceStep`/`AngleStepDeg` 量化精度影响）、法向量估计缓存和候选对应缓存。`NumSamples` 和 `ModelRefStride` 对内存影响明显。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：刚体 3D 目标配准，如工件在料框中的粗定位，为后续 ICP 精配准提供初始变换。
- **适合 (Suitable)**：点云姿态恢复、6DoF 位姿估计的粗配准阶段。
- **适合 (Suitable)**：CAD 模型与扫描点云的对齐验证。
- **不适合 (Not Suitable)**：大面积对称体（如圆柱、球体），PPF 特征在对称结构上缺乏区分度，会导致歧义检测触发。
- **不适合 (Not Suitable)**：严重遮挡场景（模型可见部分 < 50%），投票信号不足可能导致匹配失败。
- **不适合 (Not Suitable)**：非刚体或可变形物体的匹配，PPF 假设刚体变换。

## 已知限制 / Known Limitations
1. 当前实现为"足够工业可用的轻量 PPF"，未覆盖大规模遮挡、强对称体和复杂噪声场景的完整投票优化。
2. 性能和稳定性高度依赖 `NormalRadius`、`FeatureRadius`、`NumSamples`、`ModelRefStride` 等参数组合，建议在目标工件上做一次标定式调参。
3. 输出的 `TransformMatrix` 为粗配准结果，典型平移误差在数毫米级别，通常需要后续 ICP 精配准。
4. `IsMatch`（综合验证）与 `IsMatched`（原始匹配）存在语义差异：`IsMatched` 仅表示 RANSAC 找到了满足内点门槛的解，`IsMatch` 还需通过稳定性、法向一致性和歧义检测。
5. 法向量估计的质量直接影响匹配精度；输入点云若已包含高质量法向量，应确保 `NormalRadius` 不会覆盖法向量已有的平滑特性。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 PPF 投票匹配算法原理、哈希量化策略、多层验证机制、全部 16 个输出端口说明、Seed 确定性控制、FailureReason 分级 |
| 1.0.0 | 2026-03-17 | 自动生成文档骨架 / Generated skeleton |
