# 边缘对缺陷 / EdgePairDefect

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `EdgePairDefectOperator` |
| 枚举值 (Enum) | `OperatorType.EdgePairDefect` |
| 分类 (Category) | AI检测 |
| 显示名 (DisplayName) | 边缘对缺陷 |
| 图标 (Icon) | `edge-pair-defect` |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 关键词 (Keywords) | `edge pair`, `notch`, `bump`, `deviation` |

## 算法原理 / Algorithm Principle

**中文：** 该算子检查一对边缘之间的间距是否偏离期望宽度。算法核心流程：

1. **线对获取**：优先使用上游传入的 `Line1` 和 `Line2`（支持 `LineData`、字典和历史 hashtable 格式解析）；未提供时，通过 Canny 或 Sobel 构建边缘图，从 HoughLinesP 候选线中选择角度相近（< 8 度）、间距接近期望宽度的一对最优边缘线。
2. **采样检查**：沿第一条线按 `NumSamples` 等距采样，在每个采样点沿法线方向在局部搜索半径内寻找对应边缘点，计算实际宽度与 `ExpectedWidth` 的偏差。
3. **缺陷统计**：`|deviation| > Tolerance` 的连续采样段计为一个缺陷段，输出 `DefectCount`（缺陷段数）、`MaxDeviation`（最大绝对偏差）和 `Deviations`（每点偏差数组）。

**English:** This operator checks whether the spacing between a pair of edges deviates from an expected width. The core algorithm:

1. **Line pair acquisition**: Prioritizes upstream-provided `Line1` and `Line2` (supports `LineData`, dictionary, and legacy hashtable parsing); when absent, builds an edge map via Canny or Sobel, then selects the best parallel pair from HoughLinesP candidates based on angle similarity (< 8 degrees) and proximity to expected width.
2. **Sampling inspection**: Equally spaced samples along Line1 (`NumSamples` points), searching for corresponding edge points along the normal direction within a local radius, computing width deviation from `ExpectedWidth`.
3. **Defect statistics**: Consecutive sampling segments where `|deviation| > Tolerance` are counted as defect segments, outputting `DefectCount` (segment count), `MaxDeviation` (maximum absolute deviation), and `Deviations` (per-point deviation array).

## 实现策略 / Implementation Strategy

**中文：** 源码中的关键实现策略：

1. **自动找线**：`TryDetectLinesFromImage` 使用 HoughLinesP（阈值 80，最小线长 60，最大间隙 10），取最长 30 条候选线，对所有线对按角度差、间距偏差和线长综合评分，选最优对。评分公式：`score = lenA + lenB - widthPenalty + withinToleranceBonus`。
2. **局部边缘搜索**：`TryFindLocalEdgePoint` 从预测点沿法线方向双向扩展，在 3x3 邻域内统计边缘命中点，返回质心作为精确边缘点。搜索半径为 `max(4, ceil(max(tolerance*2, expectedWidth*0.25)))`。
3. **边缘图构建**：`BuildEdgeMap` 支持 Canny（阈值 60/160）和 Sobel（双向梯度 + 二值化阈值 60）两种方法。
4. **输入解析**：`TryParseLine` 支持 `LineData` 对象、`IDictionary<string, object>`（含 `StartX/StartY/EndX/EndY` 或 `X1/Y1/X2/Y2` 字段）和 `IDictionary` 历史格式。
5. **方向校正**：自动判断法线方向是否指向第二条线（通过中点投影），确保搜索方向正确。
6. **退化线检测**：当第一条线长度接近 0（< 1e-9）时直接失败。

**English:** Key implementation strategies:

1. **Auto line detection**: `TryDetectLinesFromImage` uses HoughLinesP (threshold 80, min line length 60, max gap 10), takes the 30 longest candidates, and scores all pairs by angle difference, width deviation, and line length. Score formula: `score = lenA + lenB - widthPenalty + withinToleranceBonus`.
2. **Local edge search**: `TryFindLocalEdgePoint` extends bidirectionally from the predicted point along the normal, counting edge hits in a 3x3 neighborhood and returning the centroid as the precise edge point. Search radius: `max(4, ceil(max(tolerance*2, expectedWidth*0.25)))`.
3. **Edge map construction**: `BuildEdgeMap` supports Canny (thresholds 60/160) and Sobel (bidirectional gradient + binary threshold 60).
4. **Input parsing**: `TryParseLine` supports `LineData` objects, `IDictionary<string, object>` (with `StartX/StartY/EndX/EndY` or `X1/Y1/X2/Y2` keys), and `IDictionary` legacy format.
5. **Direction correction**: Automatically determines whether the normal direction points toward Line2 (via midpoint projection), ensuring correct search direction.
6. **Degenerate line detection**: Fails immediately when Line1 length approaches 0 (< 1e-9).

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像
2. `GetStringParam / GetDoubleParam / GetIntParam` -- 读取参数
3. `TryResolveLines(src, inputs, edgeMethod, ...)` -- 解析或自动检测线对
   - `TryParseLine(line1Obj, ...)` -- 解析上游线输入
   - `TryDetectLinesFromImage(src, edgeMethod, ...)` -- 自动找线
     - `BuildEdgeMap(src, edgeMethod)` -- 构建边缘图
     - `Cv2.HoughLinesP(...)` -- 霍夫线检测
     - 角度/间距/线长综合评分选最优对
4. `BuildEdgeMap(src, edgeMethod)` -- 构建边缘图（用于局部搜索）
5. `TryFindLocalEdgePoint(edgeMap, predicted, normal, radius, ...)` -- 局部边缘点搜索
6. 采样宽度偏差并统计缺陷段（连续超容差段计数）
7. `Cv2.Line / Cv2.Circle / Cv2.PutText(...)` -- 绘制结果
8. `CreateImageOutput(result, output)` -- 构建输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ExpectedWidth` | `double` | `20.0` | `[0.0, 100000.0]` | 期望边缘间距（像素单位）。 |
| `Tolerance` | `double` | `2.0` | `[0.0, 100000.0]` | 允许宽度偏差。超过即计入缺陷段。 |
| `NumSamples` | `int` | `100` | `[5, 5000]` | 沿边缘采样的点数。 |
| `EdgeMethod` | `enum` | `Canny` | `Canny` / `Sobel` | 自动找线和局部边缘搜索使用的边缘检测方法。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待检测图像。 |
| `Line1` | Line 1 | `LineData` | No | 第一条边缘线。提供后优先使用，跳过自动找线。 |
| `Line2` | Line 2 | `LineData` | No | 第二条边缘线。提供后优先使用。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 叠加边缘线（绿/蓝）、缺陷点（红色）和统计文字的结果图。 |
| `DefectCount` | Defect Count | `Integer` | 超出容差的连续缺陷段数量。 |
| `MaxDeviation` | Max Deviation | `Float` | 最大绝对宽度偏差。 |
| `Deviations` | Deviations | `Any` | 每个采样点的宽度偏差数组（`List<double>`）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 边缘图构建约为 `O(W * H)`；采样检查约为 `O(NumSamples * localSearchRadius * 9)`（3x3 邻域）；自动找线还会叠加候选线筛选成本 `O(min(30, N)^2)`。 |
| 典型耗时 (Typical Latency) | `EdgePairDefect_contract_baseline.md` 记录 27/27 passed，总运行约 154 ms，合成契约平均耗时受自动找线和首轮 OpenCV 初始化影响。 |
| 内存特征 (Memory Profile) | 主要包含边缘图（单通道 uint8）、结果图（3 通道）、候选线集合（最多 30 条）、采样偏差数组和输出封装。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：边缘对间距、槽宽、胶路宽度、切边毛刺或局部收窄/外扩的规则检测。
- **适合 (Suitable)**：上游已有稳定线定位结果，且需要输出可解释的宽度偏差数组。
- **适合 (Suitable)**：需要自动从图像中检测边缘对的场景（自动找线模式）。
- **不适合 (Not Suitable)**：边缘弱、遮挡严重、线对不近似平行、或需要亚像素计量闭环的场景。
- **不适合 (Not Suitable)**：强反光、纹理噪声或边缘断裂严重的现场环境（自动找线稳定性下降）。

## 已知限制 / Known Limitations
1. 自动找线依赖边缘清晰度和候选线筛选，现场强反光、纹理噪声或边缘断裂会降低稳定性。
2. `DefectCount` 统计的是超容差连续采样段，不等同于真实物理缺陷个数。
3. 法线方向搜索使用 3x3 邻域质心定位，精度约为亚像素级但非严格亚像素拟合。
4. 当前 baseline 是合成契约证据，不声明真实零件、真实光学和现场节拍下的缺陷准确率。
5. 自动找线的角度容差固定为 8 度，不可通过参数调节。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增自动找线评分公式、局部边缘搜索算法、输入格式解析详情、退化线检测；统一五列参数表；补全英文算法原理 |
| 1.0.3 | 2026-04-28 | 回写 27/27 contract baseline、真实边缘对算法、失败契约和限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
