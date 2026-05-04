# 区域腐蚀 / Region Erosion

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionErosionOperator` |
| 枚举值 (Enum) | `OperatorType.RegionErosion` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Erosion, Morphology, Shrink, RLE |
| 图标 (Icon) | region-erosion |

## 算法原理 / Algorithm Principle
腐蚀（Erosion）是形态学基本操作之一。给定输入区域 A 和结构元素 K，腐蚀定义为：

```
Erosion(A, K) = {p | p + k in A, for all k in K}
```

即一个点 p 保留当且仅当结构元素 K 的所有偏移量 k 对应的点 p+k 都在区域 A 内（K 完全包含在 A 中）。

直观理解：区域向内收缩，收缩幅度由结构元素的大小决定。效果包括：
- 去除边界噪声和小突起
- 分离近距离连接的区域
- 验证最小特征宽度

支持多迭代：每次迭代在前一次结果上再次腐蚀。当区域被完全腐蚀为空时，迭代提前终止。

> English: Erosion shrinks a region by keeping only points where the entire structuring element fits inside the region. It removes boundary noise and enforces minimum feature width.

## 实现策略 / Implementation Strategy
1. **结构元素构建**：通过 `MorphologyKernel(shape, width, height)` 构建离散结构元素，支持 Rectangle、Ellipse、Cross 三种形状。调用 `kernel.GetOffsets()` 获取偏移量列表。
2. **逐点包含测试**：对区域的每个游程中的每个点，检查所有核偏移量 `(dx, dy)` 对应的 `region.ContainsPoint(x + dx, y + dy)` 是否全部成立。
3. **连续区间优化**：找到第一个满足条件的点后，继续向右扫描直到条件不满足，一次性生成连续游程 `[startX, endX]`。
4. **合并相邻游程**：通过 `MergeAdjacentRuns()` 合并可能的相邻游程。
5. **多迭代支持**：通过 `Iterations` 参数控制迭代次数。每次迭代检查结果是否为空，为空则提前终止。

与 Halcon `erosion_region` 算子对标。

> English: Tests structuring element containment for each point using `ContainsPoint()`, with run-length optimization for consecutive interior points. Supports early termination when region becomes empty.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "KernelShape")` / `GetIntParam(@operator, "KernelWidth")` / `GetIntParam(@operator, "KernelHeight")` / `GetIntParam(@operator, "Iterations")` -- 读取参数
2. `TryGetInputRegion(inputs, "Region")` -- 获取输入区域
3. `new MorphologyKernel(shape, kernelWidth, kernelHeight)` -- 构建结构元素
4. `kernel.GetOffsets()` -- 获取核偏移量列表
5. `ErodeRegion(region, kernel, iterations)` -- 多迭代腐蚀入口
6. `ErodeOnce(current, kernel)` -- 单次腐蚀：遍历游程 -> 逐点 `ContainsPoint()` 全偏移量检查 -> 连续区间生成游程
7. `new Region(resultRuns).MergeAdjacentRuns()` -- 合并相邻游程
8. `CreateVisualization(img, region, erodedRegion)` / `CreateRegionVisualization(region, erodedRegion)` -- 生成可视化
9. `CreateImageOutput(visualization, { Region, OriginalArea, Area, AreaReduction, ReductionRatio, Iterations, Kernel, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelShape` | `enum` | `Rectangle` | `Rectangle` / `Ellipse` / `Cross` | 结构元素形状。Rectangle 为矩形核，Ellipse 为椭圆核，Cross 为十字核。 |
| `KernelWidth` | `int` | `3` | [1, 99] | 结构元素宽度（像素）。值越大，腐蚀效果越强，小区域越容易被完全消除。 |
| `KernelHeight` | `int` | `3` | [1, 99] | 结构元素高度（像素）。与 KernelWidth 独立控制，可实现非对称腐蚀。 |
| `Iterations` | `int` | `1` | [1, 100] | 腐蚀迭代次数。多次迭代等效于更大结构元素的单次腐蚀。区域变空时提前终止。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | 待腐蚀的输入区域。 |
| `Image` | Reference Image (Optional) | `Image` | No | 参考图像。提供时用于在原图上叠加可视化结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Eroded Region | `Any` | 腐蚀后的区域。 |
| `Image` | Visualization | `Image` | 可视化图：原始区域蓝色轮廓，腐蚀结果绿色填充。 |
| `Area` | Eroded Area | `Integer` | 腐蚀后的区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `OriginalArea` | `Integer` | 原始输入区域面积。 |
| `AreaReduction` | `Integer` | 面积减少量（OriginalArea - ErodedArea）。 |
| `ReductionRatio` | `Double` | 面积减少比率（AreaReduction / OriginalArea）。当原始面积为 0 时为 0。 |
| `Iterations` | `Integer` | 本次执行实际使用的迭代次数（若区域提前变空则小于设定值）。 |
| `Kernel` | `Object` | 本次执行使用的结构元素参数：`{ Shape, Width, Height }`。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I*P*K*log(Rrow))，其中 I 为迭代次数，P 为输入点数，K 为核偏移量数，Rrow 为行游程数 |
| 典型耗时 (Typical Latency) | 平均 0.359 ms，最大 1.585 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(P+K)，主要为结果游程列表和核偏移量列表 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：收缩前景区域，去除边界毛刺和小突起。
- **适合 (Suitable)**：去除二值化后的边界噪声。
- **适合 (Suitable)**：在测量前验证最小特征宽度（宽度小于核的特征会被消除）。
- **适合 (Suitable)**：预处理步骤，为后续膨胀（开运算）做准备。
- **不适合 (Not Suitable)**：亚像素级形态学或灰度形态学。
- **不适合 (Not Suitable)**：需要保留所有小特征的场景（大核会消除小区域）。

## 已知限制 / Known Limitations
1. 大结构元素可以完全消除小或薄区域，这是腐蚀的预期行为。
2. 结构元素形状为离散 Rectangle/Ellipse/Cross 光栅化，非解析连续几何。
3. `ContainsPoint()` 对每个点逐一检查所有核偏移量，大核时可能成为性能瓶颈。
4. 多迭代腐蚀的中间结果不输出，仅返回最终结果。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（腐蚀集合公式）、实现策略（ContainsPoint 包含测试 + 连续区间优化）、详细参数语义（4 个参数全覆盖）、多迭代提前终止说明、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
