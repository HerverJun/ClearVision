# 区域膨胀 / Region Dilation

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionDilationOperator` |
| 枚举值 (Enum) | `OperatorType.RegionDilation` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Dilation, Morphology, Expand, Grow, RLE |
| 图标 (Icon) | region-dilation |

## 算法原理 / Algorithm Principle
膨胀（Dilation）是形态学基本操作之一。给定输入区域 A 和结构元素 K，膨胀定义为：

```
Dilation(A, K) = {p + k | p in A, k in K}
```

即对区域 A 中的每个点 p，将结构元素 K 的所有偏移量 k 加到 p 上，所有结果点的并集即为膨胀结果。

直观理解：区域向所有方向扩展，扩展幅度由结构元素的大小决定。效果包括：
- 填充区域内的小孔洞
- 连接近距离的断裂区域
- 平滑区域边界

支持多迭代：每次迭代在前一次结果上再次膨胀，等效于使用更大的结构元素。

> English: Dilation expands every point in region A by all offsets in structuring element K. The result is the union of all translated copies of K centered at each point of A.

## 实现策略 / Implementation Strategy
1. **结构元素构建**：通过 `MorphologyKernel(shape, width, height)` 构建离散结构元素，支持 Rectangle、Ellipse、Cross 三种形状。调用 `kernel.GetOffsets()` 获取偏移量列表。
2. **点集膨胀**：对区域的每个游程中的每个点，将所有核偏移量 `(dx, dy)` 加入 `HashSet<(int x, int y)>` 自动去重。
3. **点集转游程**：通过 `PointsToRuns()` 方法将 HashSet 中的点按 Y 分组、X 排序，合并连续 X 坐标为游程。
4. **多迭代支持**：通过 `Iterations` 参数控制迭代次数，每次迭代在前一次结果上重复膨胀。迭代间自动转换为 Region 对象。

与 Halcon `dilation_region` 算子对标。

> English: Uses HashSet-based point expansion with structuring element offsets, then converts back to RLE runs. Supports multiple iterations.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "KernelShape")` / `GetIntParam(@operator, "KernelWidth")` / `GetIntParam(@operator, "KernelHeight")` / `GetIntParam(@operator, "Iterations")` -- 读取参数
2. `TryGetInputRegion(inputs, "Region")` -- 获取输入区域
3. `new MorphologyKernel(shape, kernelWidth, kernelHeight)` -- 构建结构元素
4. `kernel.GetOffsets()` -- 获取核偏移量列表
5. `DilateRegion(region, kernel, iterations)` -- 多迭代膨胀入口
6. `DilateOnce(current, kernel)` -- 单次膨胀：遍历游程 -> 展开偏移点到 HashSet -> `PointsToRuns()` 转回游程
7. `CreateVisualization(img, region, dilatedRegion)` / `CreateRegionVisualization(region, dilatedRegion)` -- 生成可视化
8. `CreateImageOutput(visualization, { Region, OriginalArea, Area, AreaIncrease, IncreaseRatio, Iterations, Kernel, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelShape` | `enum` | `Rectangle` | `Rectangle` / `Ellipse` / `Cross` | 结构元素形状。Rectangle 为矩形核，Ellipse 为椭圆核，Cross 为十字核。 |
| `KernelWidth` | `int` | `3` | [1, 99] | 结构元素宽度（像素）。值越大，膨胀效果越强。 |
| `KernelHeight` | `int` | `3` | [1, 99] | 结构元素高度（像素）。与 KernelWidth 独立控制，可实现非对称膨胀。 |
| `Iterations` | `int` | `1` | [1, 100] | 膨胀迭代次数。多次迭代等效于更大结构元素的单次膨胀。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | 待膨胀的输入区域。 |
| `Image` | Reference Image (Optional) | `Image` | No | 参考图像。提供时用于在原图上叠加可视化结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Dilated Region | `Any` | 膨胀后的区域。 |
| `Image` | Visualization | `Image` | 可视化图：原始区域绿色轮廓，膨胀结果红色半透明。 |
| `Area` | Dilated Area | `Integer` | 膨胀后的区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `OriginalArea` | `Integer` | 原始输入区域面积。 |
| `AreaIncrease` | `Integer` | 面积增加量（DilatedArea - OriginalArea）。 |
| `IncreaseRatio` | `Double` | 面积增加比率（AreaIncrease / OriginalArea）。当原始面积为 0 时为 0。 |
| `Iterations` | `Integer` | 本次执行实际使用的迭代次数。 |
| `Kernel` | `Object` | 本次执行使用的结构元素参数：`{ Shape, Width, Height }`。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I*P*K + P'*log(P'))，其中 I 为迭代次数，P 为输入点数，K 为核偏移量数，P' 为膨胀后点数 |
| 典型耗时 (Typical Latency) | 平均 0.536 ms，最大 6.379 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(P'+K)，主要为 HashSet 点集和核偏移量列表 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：扩展前景掩码，增加检测区域的安全裕度。
- **适合 (Suitable)**：在布尔运算前填充小间隙，使断裂区域连通。
- **适合 (Suitable)**：为 ROI 添加像素级容差，确保不遗漏边界目标。
- **适合 (Suitable)**：预处理步骤，为后续腐蚀（开/闭运算）做准备。
- **不适合 (Not Suitable)**：需要自动裁剪到原始图像范围的场景（膨胀可能产生超出图像域的坐标）。
- **不适合 (Not Suitable)**：亚像素级形态学或灰度形态学。

## 已知限制 / Known Limitations
1. 膨胀设计上可以产生超出原始区域或图像域的坐标，下游若需要裁剪需显式添加。
2. 结构元素形状为离散 Rectangle/Ellipse/Cross 光栅化，非解析连续几何。
3. 多迭代膨胀的中间结果不输出，仅返回最终结果。
4. HashSet 点集方式在大面积区域 + 大核时内存消耗较高。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（膨胀集合公式）、实现策略（HashSet 点膨胀 + PointsToRuns 转换）、详细参数语义（4 个参数全覆盖）、多迭代支持说明、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
