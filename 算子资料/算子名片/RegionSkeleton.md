# 区域骨架 / Region Skeleton

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionSkeletonOperator` |
| 枚举值 (Enum) | `OperatorType.RegionSkeleton` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Skeleton, Thinning, ZhangSuen, Topology |
| 图标 (Icon) | region-skeleton |

## 算法原理 / Algorithm Principle
骨架化（Skeletonization）将二值区域细化为单像素宽的骨架线，同时保持区域的拓扑结构（连通性、端点、分支点）不变。

本算子采用 **Zhang-Suen 并行细化算法**，该算法通过迭代删除边界像素实现细化。每次迭代分为两个子步骤（Step 1 和 Step 2），每个子步骤中：

对每个前景像素 P，计算其 8 邻域（按顺时针编号 P1-P8，P 为 P0）：
- **B** = P1~P8 中前景像素的数量（连通度）
- **A** = P1->P2->...->P8->P1 中 0->1 的转换次数（拓扑度）

**Step 1 删除条件**：
```
2 <= B <= 6  AND  A == 1  AND  P1*P3*P5 == 0  AND  P3*P5*P7 == 0
```

**Step 2 删除条件**：
```
2 <= B <= 6  AND  A == 1  AND  P1*P3*P7 == 0  AND  P1*P5*P7 == 0
```

两个步骤交替执行直到没有像素被删除，或达到最大迭代次数。

细化完成后，分析骨架的拓扑特征：
- **端点（Endpoint）**：8 邻域中恰好有 1 个前景像素的骨架点
- **分支点（Branchpoint）**：8 邻域中有 3 个或以上前景像素的骨架点

> English: Applies Zhang-Suen parallel thinning to produce a single-pixel-wide skeleton preserving topology. Analyzes endpoints (1 neighbor) and branchpoints (3+ neighbors) via 8-neighborhood diagnostics.

## 实现策略 / Implementation Strategy
1. **区域转二值图**：通过 `region.ToMat()` 将区域转换为二值 Mat，再通过 `CreatePaddedBinary()` 在四周填充 1 像素的零边框（防止边界越界）。
2. **Zhang-Suen 迭代细化**：在填充后的二值图上执行迭代细化。每个迭代包含两个标记-删除子步骤。使用 `bool[,] markers` 数组标记待删除像素，避免边标记边删除导致的传播错误。
3. **坐标回映射**：细化结果通过 `Region.FromMat()` 转换为区域，再通过 `Translate(originalBounds.X - 1, originalBounds.Y - 1)` 回映射到原始坐标系（减去填充偏移）。
4. **骨架分析**：`AnalyzeSkeleton()` 在细化后的 Mat 上扫描所有前景像素，统计 8 邻域中前景邻居数，分类为端点和分支点。坐标同样回映射到原始坐标系。
5. **连通性**：固定使用 8 连通（`ConnectivityType.EightConnected`）。
6. **PreserveTopology 参数**：当前实现始终使用 Zhang-Suen 算法，`PreserveTopology` 参数记录在输出元数据中但不影响执行路径。

与 Halcon `skeleton` 算子对标。

> English: Converts region to padded binary Mat, applies iterative Zhang-Suen thinning with marker-based deletion, translates skeleton back to original coordinates, and classifies endpoints/branchpoints.

## 核心 API 调用链 / Core API Call Chain
1. `GetIntParam(@operator, "MaxIterations")` / `GetBoolParam(@operator, "PreserveTopology")` -- 读取参数
2. `TryGetInputRegion(inputs, "Region")` -- 获取输入区域
3. `region.ToMat()` -- 区域转二值 Mat
4. `CreatePaddedBinary(binaryMat)` -- 四周填充 1 像素零边框
5. `ZhangSuenThinning(paddedBinaryMat, maxIterations)` -- Zhang-Suen 迭代细化
   - `GetNeighbors(src, x, y)` -- 获取 8 邻域像素值
   - `CountTransitions(p)` -- 计算 0->1 转换次数
   - 标记-删除两步交替执行
6. `Region.FromMat(skeletonMat).Translate(dx, dy)` -- 细化结果转区域并回映射坐标
7. `AnalyzeSkeleton(skeletonMat)` -- 分析端点和分支点
8. `TranslatePoints(points, dx, dy)` -- 端点/分支点坐标回映射
9. `CreateVisualization(img, region, skeletonRegion, endPoints, branchPoints)` -- 生成可视化（骨架青色，端点红色，分支点蓝色）
10. `CreateImageOutput(visualization, { Region, SkeletonLength, EndPoints, BranchPoints, OriginalArea, ReductionRatio, Algorithm, Connectivity, PreserveTopology, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MaxIterations` | `int` | `100` | [1, 1000] | Zhang-Suen 细化的最大迭代次数。复杂形状可能需要更多迭代。达到上限后停止细化，骨架可能不完整。 |
| `PreserveTopology` | `bool` | `true` | true / false | 是否保持拓扑结构。当前实现始终使用 Zhang-Suen 算法（本身保持拓扑），此参数记录在输出元数据中。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | 待骨架化的输入区域。 |
| `Image` | Reference Image (Optional) | `Image` | No | 参考图像。提供时用于在原图上叠加可视化结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Skeleton Region | `Any` | 骨架区域（单像素宽）。 |
| `Image` | Visualization | `Image` | 可视化图：骨架青色叠加，端点红色圆点，分支点蓝色圆点。 |
| `SkeletonLength` | Skeleton Length | `Integer` | 骨架长度（骨架区域的像素数）。 |
| `BranchPoints` | Branch Point Count | `Integer` | 分支点数量（8 邻域中有 >=3 个前景邻居的骨架点）。 |
| `EndPoints` | End Point Count | `Integer` | 端点数量（8 邻域中恰好有 1 个前景邻居的骨架点）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `OriginalArea` | `Integer` | 原始输入区域面积。 |
| `ReductionRatio` | `Double` | 面积缩减率：`1 - SkeletonLength / OriginalArea`。当原始面积为 0 时为 0。 |
| `Algorithm` | `String` | 使用的细化算法名称（固定为 `"Zhang-Suen"`）。 |
| `Connectivity` | `Integer` | 连通性类型（固定为 `8`，即 8 连通）。 |
| `PreserveTopology` | `Boolean` | 是否保持拓扑（记录参数值）。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I*W*H)，其中 I 为实际迭代次数，W*H 为填充后二值图尺寸 |
| 典型耗时 (Typical Latency) | 平均 1.438 ms，最大 18.477 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(W*H)，主要为填充后的二值 Mat 和 markers 数组 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：提取像素级骨架用于拓扑检查（连通性、端点、分支点分析）。
- **适合 (Suitable)**：中心线类诊断，如血管、裂缝、线路的粗略中心线提取。
- **适合 (Suitable)**：粗略的分支/端点计数，用于形态分类或结构分析。
- **适合 (Suitable)**：区域形状的紧凑表示，减少数据量同时保持拓扑。
- **不适合 (Not Suitable)**：亚像素级中心线提取（结果为像素级精度）。
- **不适合 (Not Suitable)**：计量级中轴线拟合（需要更精确的中轴变换算法）。
- **不适合 (Not Suitable)**：需要超出 Zhang-Suen 规则的拓扑保证的场景。

## 已知限制 / Known Limitations
1. 端点和分支点计数基于离散 8 邻域诊断，在粗连接处附近可能过度计数。
2. `PreserveTopology` 参数记录在输出元数据中，但执行路径始终使用 Zhang-Suen 算法，无法切换到其他细化算法。
3. Zhang-Suen 算法在极端复杂的形状（如大量细分支）可能需要较多迭代才能收敛，`MaxIterations` 不足时骨架不完整。
4. 填充边框为 1 像素，若区域触及图像边界，填充后不会越界，但骨架在边界处的行为可能略有不同。
5. `GetNeighbors()` 和 `CountTransitions()` 使用 `At<byte>()` 逐像素访问，非 SIMD 优化，大图时可能成为瓶颈。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（Zhang-Suen Step 1/Step 2 条件公式、端点/分支点定义）、实现策略（填充边框 + 标记删除 + 坐标回映射）、详细参数语义、骨架拓扑分析说明、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
