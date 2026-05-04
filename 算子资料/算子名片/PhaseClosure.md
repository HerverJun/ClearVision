# 相位闭合 / Phase Closure

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `PhaseClosureOperator` |
| 枚举值 (Enum) | `OperatorType.PhaseClosure` |
| 分类 (Category) | 测量 / Measurement |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子对包裹相位图（wrapped phase map）进行解缠绕（unwrapping），恢复被 `2pi` 折叠的连续相位。

**相位包裹模型**：干涉测量中，相位被限制在 `(-pi, pi]` 区间，连续相位 `phi(x,y)` 被映射为：
`wrapped(x,y) = atan2(sin(phi), cos(phi))`

**解缠绕目标**：从包裹相位恢复 `unwrapped(x,y) = wrapped(x,y) + 2*pi*k(x,y)`，其中 `k` 为整数跳数。

**三种解缠绕方法：**

1. **Itoh**：逐行再逐列顺序扫描，相邻像素相位差通过 `WrapToPi` 映射到 `(-pi, pi]` 后累加。
   `delta = WrapToPi(wrapped[i] - wrapped[i-1])`
   `unwrapped[i] = unwrapped[i-1] + delta`
   复杂度 `O(W*H)`，速度快但对噪声敏感。

2. **Quality-guided**：基于质量图的优先队列遍历。从最高质量种子点开始，按质量降序展开邻域，始终优先处理可靠区域。
   质量图由 Sobel 梯度幅值构建：`Q(x,y) = 1 / (1 + |grad|)`。
   复杂度 `O(W*H*log(W*H))`。

3. **Flood-fill**：从 `(0,0)` 种子点 BFS 遍历，按队列顺序（无优先级）展开邻域。
   复杂度 `O(W*H)`。

**断点检测**：相邻像素包裹相位差超过 `0.9*pi` 时标记为断点。

**物理位移转换**：若提供波长 `lambda`，输出相位转为物理位移 `d = unwrapped * lambda / (2*pi)`。

**质量度量**：对解缠后相位做 Sobel 梯度，`quality = 1 / (1 + stddev(|grad|))`，值越高表示相位越平滑。

> English: The operator unwraps a 2pi-wrapped phase map using Itoh sequential, quality-guided priority, or flood-fill traversal, detects phase discontinuities, and optionally converts to physical displacement given a wavelength.

## 实现策略 / Implementation Strategy
- 输入相位通过 `PrepareWrappedPhaseInput` 统一转为 `CV_32FC1` 包裹相位，支持 8U/16U/32F/64F 多种位深自动归一化。
- Itoh 模式为行优先 + 列优先两遍扫描，实现简单且速度快。
- Quality-guided 使用 `PriorityQueue<Point, float>` 按质量降序展开，种子点选自质量图全局最大值处。
- Flood-fill 使用同一队列结构但不按质量排序（优先级恒为 0），适合质量分布均匀的场景。
- `FillRemainingIslands` 处理主遍历未覆盖的孤立区域，确保输出无 NaN 残留。
- 可视化输出三联图：包裹相位、解缠相位、断点叠加图，使用 Jet 色彩映射。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "PhaseImage")` -- 获取包裹相位图
2. `GetDouble(inputs, "Wavelength", 0.0)` / `GetString(inputs, "UnwrapMethod", "itoh")` -- 读取参数
3. `PrepareWrappedPhaseInput(sourcePhase)` -- 统一转为 CV_32F 包裹相位
4. `TryGetOptionalQualityMap(inputs)` -- 获取可选质量图
5. 解缠绕（按 method 分支）：
   - `ItohUnwrap(wrapped)` -- 行列顺序扫描
   - `QualityGuidedUnwrap(wrapped, qualityMap)` -- 优先队列遍历
   - `FloodFillUnwrap(wrapped)` -- BFS 遍历
6. `ConvertPhaseToPhysicalDisplacement(unwrapped, wavelength)` -- 可选物理位移转换
7. `DetectDiscontinuities(wrapped)` -- 断点检测
8. `CreateVisualization(wrapped, scaledPhase, discontinuities)` -- 三联可视化
9. `CreateImageOutput(visualization, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| *(无 [OperatorParam] 参数)* | | | | 所有配置通过输入端口传入。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PhaseImage` | `Wrapped Phase Image` | `Image` | Yes | 包裹相位图，支持 8U/16U/32F/64F，多通道会自动转灰度。 |
| `Wavelength` | `Wavelength (nm)` | `Float` | No | 波长（纳米），用于将相位转为物理位移。默认 0 表示不转换。 |
| `UnwrapMethod` | `Unwrapping Method` | `String` | No | 解缠绕方法：`itoh`（默认）、`quality`、`floodfill`。 |
| `QualityMap` | `Quality Map (optional)` | `Image` | No | 外部质量图，仅 quality 模式使用。需与 PhaseImage 尺寸一致。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `UnwrappedPhase` | `Unwrapped Phase` | `Image` | 解缠后的连续相位图（CV_32F），若提供波长则为物理位移。 |
| `Discontinuities` | `Phase Discontinuities` | `Image` | 断点二值图（CV_8U），白色标记相位跳变位置。 |
| `Quality` | `Quality Metric` | `Float` | 解缠质量度量，`[0, 1]`，越高越好。 |
| `Image` | `Visualization` | `Image` | 三联可视化：包裹相位 + 解缠相位 + 断点叠加。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Wavelength` | `Double` | 本次执行使用的波长。 |
| `Method` | `String` | 本次执行实际使用的解缠绕方法。 |
| `ProcessingTimeMs` | `Long` | 执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | Itoh/Flood-fill: `O(W*H)`；Quality-guided: `O(W*H*log(W*H))`。 |
| 典型耗时 (Typical Latency) | 平均 1.429 ms，最大 4.590 ms（22 组合成测试用例）。 |
| 内存特征 (Memory Profile) | `O(W*H)`，包含包裹相位、解缠相位、质量图、visited 数组、可视化图等，峰值约为输入的 5-6 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：相邻像素相位步进在 `(-pi, pi]` 假设内的平滑包裹相位图。
- **适合 (Suitable)**：干涉测量检测场景，需要断点图和质量度量配合解缠相位。
- **适合 (Suitable)**：提供外部质量图时，quality 模式可优先在可靠区域展开，抗噪性更强。
- **不适合 (Not Suitable)**：严重噪声的相位图，未做掩码或预处理时解缠可能完全失败。
- **不适合 (Not Suitable)**：拓扑复杂、含大量残差的相位场，需要分支切割等高级算法。

## 已知限制 / Known Limitations
1. quality 模式在未提供外部质量图时，使用 Sobel 梯度派生的局部质量图，质量精度有限。
2. 质量度量是稳定性启发值，非经过校准的计量不确定度。
3. Itoh 模式假设相位沿行列方向连续，对噪声敏感，断点多时可能传播误差。
4. 相位输入归一化到 `(-pi, pi]` 由 `atan2(sin, cos)` 实现，精度受浮点误差影响。
5. 可视化使用 Jet colormap，可能在某些显示器上产生视觉误导。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写至金标准 |
| 1.0.1 | 2026-03-14 | 基于 AlgorithmInfo 补充算法细节与性能数据 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
