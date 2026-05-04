# 区域开运算 / Region Opening

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionOpeningOperator` |
| 枚举值 (Enum) | `OperatorType.RegionOpening` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | Region, Opening, Morphology, NoiseRemoval, Smooth |
| 图标 (Icon) | region-opening |

## 算法原理 / Algorithm Principle
开运算（Opening）是形态学基本操作之一，定义为先腐蚀后膨胀，使用相同的结构元素：

```
Opening(A, K) = Dilation(Erosion(A, K), K)
```

其中 K 为结构元素，A 为输入区域。

**腐蚀（Erosion）**：对每个候选点，检查结构元素 K 的所有偏移量对应的点是否都在输入区域内。只有当 K 完全包含在区域内时，该点才保留。效果是去除小突起和窄连接。

**膨胀（Dilation）**：对区域内的每个点，将结构元素 K 的所有偏移量应用到该点，将所有偏移后的点加入结果集。效果是恢复被腐蚀的大区域的形状。

开运算的净效果是去除区域中的小突起、孤立像素和窄连接，同时大致保持较大区域的形状和面积。它是一种"保形去噪"操作。

> English: Opening = Erosion followed by Dilation with the same structuring element. It removes small protrusions and isolated pixels while preserving larger region shapes.

## 实现策略 / Implementation Strategy
1. **结构元素构建**：通过 `MorphologyKernel(shape, width, height)` 构建离散结构元素，支持 Rectangle、Ellipse、Cross 三种形状。调用 `kernel.GetOffsets()` 获取偏移量列表。
2. **腐蚀实现**：对区域的每个游程中的每个点，检查 `region.ContainsPoint(x + dx, y + dy)` 是否对所有偏移量成立。找到连续满足条件的 X 区间后生成游程。通过 `MergeAdjacentRuns()` 合并。
3. **膨胀实现**：对腐蚀结果的每个游程中的每个点，将所有核偏移量 `(dx, dy)` 加入 `HashSet<(int x, int y)>` 去重，然后通过 `PointsToRuns()` 将点集转换回游程。
4. **单次运算**：当前实现执行一次腐蚀 + 一次膨胀。重复开运算需要在工作流中串联多次调用。

与 Halcon `opening_region` 算子对标。

> English: Builds a discrete structuring element, applies one erosion pass (containment test) followed by one dilation pass (HashSet-based point expansion), converting between RLE and point sets as needed.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "KernelShape")` / `GetIntParam(@operator, "KernelWidth")` / `GetIntParam(@operator, "KernelHeight")` -- 读取参数
2. `TryGetInputRegion(inputs, "Region")` -- 获取输入区域
3. `new MorphologyKernel(shape, kernelWidth, kernelHeight)` -- 构建结构元素
4. `kernel.GetOffsets()` -- 获取核偏移量列表
5. `Erode(region, kernel)` -- 腐蚀：遍历游程，`ContainsPoint()` 全偏移量检查，连续区间生成游程，`MergeAdjacentRuns()` 合并
6. `Dilate(eroded, kernel)` -- 膨胀：遍历游程，展开偏移点到 HashSet，`PointsToRuns()` 转回游程
7. `CreateVisualization(img, region, opened)` / `CreateRegionVisualization(region, opened)` -- 生成可视化
8. `CreateImageOutput(visualization, { Region, OriginalArea, Area, AreaChange, ProcessingTimeMs, Kernel })` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelShape` | `enum` | `Rectangle` | `Rectangle` / `Ellipse` / `Cross` | 结构元素形状。Rectangle 为矩形核，Ellipse 为椭圆核，Cross 为十字核。 |
| `KernelWidth` | `int` | `3` | [1, 99] | 结构元素宽度（像素）。值越大，去除小特征的能力越强。 |
| `KernelHeight` | `int` | `3` | [1, 99] | 结构元素高度（像素）。与 KernelWidth 独立控制，可实现非对称开运算。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | 待进行开运算的输入区域。 |
| `Image` | Reference Image (Optional) | `Image` | No | 参考图像。提供时用于在原图上叠加可视化结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Opened Region | `Any` | 开运算后的区域。 |
| `Image` | Visualization | `Image` | 可视化图：开运算结果绿色叠加。 |
| `Area` | Opened Area | `Integer` | 开运算后的区域面积（像素数）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `OriginalArea` | `Integer` | 原始输入区域面积。 |
| `AreaChange` | `Integer` | 面积变化量（OpenedArea - OriginalArea）。开运算通常导致面积减少或不变。 |
| `Kernel` | `Object` | 本次执行使用的结构元素参数：`{ Shape, Width, Height }`。 |
| `ProcessingTimeMs` | `Long` | 算子执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(P*K*log(Rrow) + P'*K)，其中 P 为输入点数，K 为核偏移量数，Rrow 为行游程数，P' 为腐蚀后点数 |
| 典型耗时 (Typical Latency) | 平均 0.437 ms，最大 3.141 ms（100 组合成测试用例） |
| 内存特征 (Memory Profile) | O(P+P'+K)，主要为腐蚀和膨胀阶段的中间数据 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：去除前景区域中的孤立像素和小突起噪声。
- **适合 (Suitable)**：平滑区域边界，去除毛刺。
- **适合 (Suitable)**：在测量前清理碎片化前景，保留较大结构。
- **适合 (Suitable)**：作为闭运算的互补操作，分别处理不同方向的噪声。
- **不适合 (Not Suitable)**：需要保留小于结构元素的微小缺陷的场景（开运算会消除这些特征）。
- **不适合 (Not Suitable)**：需要多次迭代开运算的场景（当前为单次腐蚀 + 膨胀，需工作流串联）。

## 已知限制 / Known Limitations
1. 当结构元素大于特征时，开运算会删除薄组件或窄桥，这是预期行为。
2. 当前执行单次腐蚀 + 膨胀对；重复开运算需要显式工作流串联。
3. 结构元素形状为离散 Rectangle/Ellipse/Cross 光栅化，非解析连续几何。
4. 腐蚀阶段使用 `ContainsPoint()` 逐点检查，大核时可能成为性能瓶颈。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充算法原理（开运算公式 = 腐蚀 + 膨胀）、实现策略（ContainsPoint 腐蚀 + HashSet 膨胀）、详细参数语义（KernelShape/Width/Height）、性能分析与限制说明 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
