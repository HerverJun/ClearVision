# 边缘对缺陷 / EdgePairDefect

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `EdgePairDefectOperator` |
| 枚举值 (Enum) | `OperatorType.EdgePairDefect` |
| 分类 (Category) | AI检测 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子检查一对边缘之间的间距是否偏离期望宽度。它可以直接使用上游传入的 `Line1/Line2`，也可以从图像边缘图中自动寻找一对接近期望宽度的平行线，然后沿线段采样并统计宽度偏差。

> English: Checks edge-pair spacing deviations against an expected width.

## 实现策略 / Implementation Strategy
- 优先解析 `Line1` 和 `Line2`，支持 `LineData`、字典和历史 hashtable 格式。
- 未提供线输入时，通过 `Canny` 或 `Sobel` 构建边缘图，并从候选线中选择角度相近、间距接近期望宽度的一对边缘。
- 按 `NumSamples` 沿第一条线采样，沿法线方向在局部半径内寻找对应边缘点，计算实际宽度与 `ExpectedWidth` 的偏差。
- `abs(deviation) > Tolerance` 的连续采样段计为缺陷段，并输出 `DefectCount`、`MaxDeviation` 和完整 `Deviations`。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(...)`
2. `TryResolveLines(...)`
3. `TryParseLine(...)` 或 `TryDetectLinesFromImage(...)`
4. `BuildEdgeMap(...)`
5. `TryFindLocalEdgePoint(...)`
6. 采样宽度偏差并统计缺陷段
7. `CreateImageOutput(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ExpectedWidth` | `double` | `20.0` | `[0.0, 100000.0]` | 期望边缘间距。 |
| `Tolerance` | `double` | `2.0` | `[0.0, 100000.0]` | 允许宽度偏差；超过即计入缺陷段。 |
| `NumSamples` | `int` | `100` | `[5, 5000]` | 沿边缘采样的点数。 |
| `EdgeMethod` | `enum` | `Canny` | `Canny/Sobel` | 自动找线和局部边缘搜索使用的边缘方法。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 待检测图像。 |
| `Line1` | Line 1 | `LineData` | No | 第一条边缘线；提供后优先使用。 |
| `Line2` | Line 2 | `LineData` | No | 第二条边缘线；提供后优先使用。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | Image | `Image` | 叠加边缘线、缺陷点和统计文字的结果图。 |
| `DefectCount` | Defect Count | `Integer` | 超出容差的连续缺陷段数量。 |
| `MaxDeviation` | Max Deviation | `Float` | 最大绝对宽度偏差。 |
| `Deviations` | Deviations | `Any` | 每个采样点的宽度偏差数组。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 边缘图构建约为 `O(W * H)`；采样检查约为 `O(NumSamples * localSearchRadius)`；自动找线还会叠加候选线筛选成本。 |
| 典型耗时 (Typical Latency) | `EdgePairDefect_contract_baseline.md` 记录 27/27 passed，总运行约 154 ms，合成契约平均耗时受自动找线和首轮 OpenCV 初始化影响。 |
| 内存特征 (Memory Profile) | 主要包含边缘图、结果图、候选线集合、采样偏差数组和输出封装。 |

## 证据与失败契约 / Evidence & Failure Contracts
- Contract baseline：`quality/evals/reports/EdgePairDefect_contract_baseline.md`，27/27 passed。
- 覆盖范围：提供线几何、自动找线、Canny/Sobel、容差边界、采样数量、输出图契约、字典/hashtable 线输入、参数校验和失败路径。
- 失败契约包括缺失图像、空图像、无法解析/自动检测有效线对、退化线、负 `ExpectedWidth`、负 `Tolerance` 和非法 `EdgeMethod`。

## 适用场景 / Use Cases
- 适合：边缘对间距、槽宽、胶路宽度、切边毛刺或局部收窄/外扩的规则检测。
- 适合：上游已有稳定线定位结果，且需要输出可解释的宽度偏差数组。
- 不适合：边缘弱、遮挡严重、线对不近似平行、或需要亚像素计量闭环的场景。

## 已知限制 / Known Limitations
1. 自动找线依赖边缘清晰度和候选线筛选，现场强反光、纹理噪声或边缘断裂会降低稳定性。
2. `DefectCount` 统计的是超容差连续采样段，不等同于真实物理缺陷个数。
3. 当前 baseline 是合成契约证据，不声明真实零件、真实光学和现场节拍下的缺陷准确率。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.3 | 2026-04-28 | 回写 27/27 contract baseline、真实边缘对算法、失败契约和限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
