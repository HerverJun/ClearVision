# 区域闭运算 / Region Closing

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionClosingOperator` |
| 枚举值 (Enum) | `OperatorType.RegionClosing` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Closing, Morphology, HoleFilling, Connect |
| 图标 (Icon) | region-closing |

## 算法原理 / Algorithm Principle
闭运算（Closing）是形态学基本操作之一，定义为先膨胀后腐蚀，使用相同的结构元素（Structuring Element）：

```
Closing(A) = Erosion(Dilation(A, K), K)
```

其中 K 为结构元素，A 为输入区域。

**膨胀（Dilation）**：对于区域内的每个点，将结构元素 K 的所有偏移量应用到该点，将所有偏移后的点加入结果集。效果是区域向外扩展，填充小孔洞，连接近距离区域。

**腐蚀（Erosion）**：对于每个候选点，检查结构元素 K 的所有偏移量对应的点是否都在输入区域内。只有当 K 完全包含在区域内时，该点才保留在结果中。效果是区域向内收缩，去除边界噪声。

闭运算的净效果是填充区域内的小孔洞和窄缝隙，同时大致保持区域的整体形状和面积。

> English: Closing = Dilation followed by Erosion with the same structuring element. It fills small holes and connects nearby components while approximately preserving overall shape.

## 实现策略 / Implementation Strategy
1. **结构元素构建**：通过 `MorphologyKernel(shape, width, height)` 构建离散结构元素，支持 Rectangle、Ellipse、Cross 三种形状。调用 `kernel.GetOffsets()` 获取偏移量列表。
2. **膨胀实现**：对区域的每个游程中的每个点，将所有核偏移量 `(dx, dy)` 加入 `HashSet<(int x, int y)>` 去重，然后通过 `PointsToRuns()` 将点集转换回游程编码。
3. **腐蚀实现**：对膨胀结果的每个游程中的每个点，检查 `region.ContainsPoint(x + dx, y + dy)` 是否对所有偏移量成立。找到连续满足条件的 X 区间后生成游程。通过 `MergeAdjacentRuns()` 合并相邻游程。
4. **单次运算**：当前实现执行一次膨胀 + 一次腐蚀。重复闭运算需要在工作流中串联多次调用。

与 Halcon `closing_region` 算子对标。

> English: Builds a discrete structuring element, applies one dilation pass (HashSet-based point expansion) followed by one erosion pass (containment test), converting between RLE and point sets as needed.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "KernelShape")` / `GetIntParam(@operator, "KernelWidth")` / `GetIntParam(@operator, "KernelHeight")` -- 读取参数
2. `TryGetInputRegion(inputs, "Region")` -- 获取输入区域
3. `new MorphologyKernel(shape, kernelWidth, kernelHeight)` -- 构建结构元素
4. `kernel.GetOffsets()` -- 获取核偏移量列表
5. `Dilate(region, kernel)` -- 膨胀：遍历游程，展开偏移点到 HashSet，`PointsToRuns()` 转回游程
6. `Erode(dilated, kernel)` -- 腐蚀：遍历游程，`ContainsPoint()` 全偏移量检查，连续区间生成游程
7. `new Region(resultRuns).MergeAdjacentRuns()` -- 合并相邻游程
8. `CreateVisualization(img, region, closed)` / `CreateRegionVisualization(region, closed)` -- 生成可视化
9. `CreateImageOutput(visualization, { Region, OriginalArea, Area, AreaChange, Kernel, ProcessingTimeMs })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelShape` | `enum` | `Rectangle` | `Rectangle` / `Ellipse` / `Cross` | 结构元素形状。Rectangle 为矩形核，Ellipse 为椭圆核，Cross 为十字核。 |
| `KernelWidth` | `int` | `3` | [1, 99] | 结构元素宽度（像素）。必须为正奇数效果最佳，偶数亦可接受。值越大，填充孔洞的能力越强。 |
| `KernelHeight` | `int` | `3` | [1, 99] | 结构元素高度（像素）。与 KernelWidth 独立控制，可实现非对称闭运算。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | 待进行闭运算的输入区域。 |
| `Image` | Reference Image (Optional) | `Image` | No | 参考图像。提供时用于在原图上叠加可视化结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Closed Region | `Any` | 闭运算后的区域。 |
| `Image` | Visualization | `Image` | 可视化图：原始区域蓝色叠加，闭运算结果绿色叠加。 |
| `Area` | Closed Area | `Integer` | 闭运算后的区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `OriginalArea` | `Integer` | 原始输入区域面积。 |
| `AreaChange` | `Integer` | 面积变化量（ClosedArea - OriginalArea）。闭运算通常导致面积增加或不变。 |
| `Kernel` | `Object` | 本次执行使用的结构元素参数：`{ Shape, Width, Height }`。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P*K + P'*K*log(Rrow))，其中 P 为输入点数，K 为核偏移量数，P' 为膨胀后点数，Rrow 为行游程数 |
| 典型耗时 (Typical Latency) | 平均 0.963 ms，最大 32.974 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(P+P'+K)，主要为膨胀阶段的 HashSet 点集和核偏移量列表 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：填充前景区域内的小孔洞（如二值化后产生的空洞）。
- **适合 (Suitable)**：连接近距离的断裂区域，使其成为一个连通区域。
- **适合 (Suitable)**：在测量前稳定碎片化前景，减少噪声干扰。
- **不适合 (Not Suitable)**：需要保持相邻组件严格分离的场景（当间隙在核作用范围内时会被桥接）。
- **不适合 (Not Suitable)**：需要多次迭代闭运算的场景（当前为单次膨胀 + 腐蚀，需工作流串联）。

## 已知限制 / Known Limitations
1. 当间隙在结构元素作用范围内时，闭运算可能桥接相邻组件。
2. 当前执行单次膨胀 + 腐蚀对；重复闭运算需要显式工作流串联。
3. 结构元素形状为离散 Rectangle/Ellipse/Cross 光栅化，非解析连续几何。
4. 腐蚀阶段对每个点逐一检查所有核偏移量的 `ContainsPoint()`，大核时可能成为性能瓶颈。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（膨胀-腐蚀公式）、实现策略（HashSet 点膨胀 + ContainsPoint 腐蚀）、详细参数语义（KernelShape/Width/Height）、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
