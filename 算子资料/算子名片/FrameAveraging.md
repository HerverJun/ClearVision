# 帧平均 / Frame Averaging

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FrameAveragingOperator` |
| 枚举值 (Enum) | `OperatorType.FrameAveraging` |
| 分类 (Category) | 预处理 / 多帧降噪 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
帧平均是一种**时间域融合**技术（与空间域滤波不同），通过在时间轴上对同一像素位置进行统计融合来降低噪声：

- **Mean 模式**：对最近 N 帧的同一像素位置取算术平均。
  `O(x, y) = (1 / N) * sum_{t=1}^{N} I_t(x, y)`
  理论上，随机高斯噪声的标准差会随帧数增加按 `1 / sqrt(N)` 下降。

- **Median 模式**：对最近 N 帧的同一像素位置取中值。
  `O(x, y) = median(I_1(x, y), I_2(x, y), ..., I_N(x, y))`
  对脉冲噪声、偶发亮点、随机闪烁等离群值有更好的抑制效果。

需要特别注意：该算子是**有状态的**，默认**每次执行都返回当前缓存帧的融合结果**，即便缓存尚未积满配置的 `FrameCount`，也会输出"热启动阶段"的部分融合结果。

> English: Frame averaging performs temporal fusion across the latest N frames using either arithmetic mean or per-pixel temporal median, implemented as a stateful operator with internal frame caching.

## 实现策略 / Implementation Strategy
当前实现是一个**带内部缓存队列的状态型算子**，使用 `ConcurrentDictionary<Guid, FrameWindowState>` 按算子实例 ID 维护独立的帧队列：

- **帧队列管理**：在锁内维护 `Queue<Mat>` 缓存最近 N 帧。若新输入帧的 `Rows`/`Cols`/`Type` 与缓存中的参考帧不一致，会先清空历史帧再重新累计。
- **锁外计算**：队列更新完成后，复制出 `snapshot` 在锁外计算，减少锁持有时间。
- **状态清理**：`StateTtl` 为 30 分钟，`CleanupInterval` 为 5 分钟。超过 TTL 未访问的状态会被自动清理。
- **Mean 实现**：使用 `CV_32F`（或 `CV_32FC2`/`CV_32FC3`/`CV_32FC4`）累加图和 `Cv2.Accumulate`，最后 `ConvertTo(originalType, 1.0 / frameCount)`，避免 8 位整型直接求和溢出。
- **Median 实现**：使用快速选择算法（`SelectKthInPlace`，基于快速排序的 partition）逐像素找到中值，支持 `CV_8U`/`CV_16U`/`CV_32F`/`CV_64F` 位深。每帧被 `Reshape` 为单行后逐像素取中值，最后 `Reshape` 回原图尺寸。
- **IDisposable**：实现 `IDisposable` 接口，清理时释放所有缓存帧。

> English: The implementation maintains per-operator-ID rolling frame queues with thread-safe access, floating-point accumulation for mean, and quickselect-based per-pixel median for multiple bit depths.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像（注意：无端口名参数，使用无名重载）
2. `GetIntParam(@operator, "FrameCount", 8)` / `GetStringParam(@operator, "Mode", "Mean")` -- 读取参数
3. `ConcurrentDictionary.GetOrAdd(@operator.Id, ...)` -- 获取或创建帧状态
4. 在 `lock(state.SyncRoot)` 内：`state.Frames.Enqueue(src.Clone())`，超出 `frameCount` 时 `Dequeue` 旧帧
5. `state.Frames.Select(frame => new Mat(frame)).ToArray()` -- 创建 snapshot
6. **Mean 路径**：
   - `frame.ConvertTo(temp, accumType)` -- 转为浮点
   - `Cv2.Accumulate(temp, accum, noMask)` -- 累加
   - `accum.ConvertTo(result, originalType, 1.0 / frameCount)` -- 除以帧数并转回原类型
7. **Median 路径**：
   - `frame.Reshape(1, rows)` -- 展平为单行
   - `SelectKthInPlace(samples, medianIndex)` -- 快速选择中值
   - `resultFlat.Reshape(channels, rows)` -- 恢复原图形状
8. `TryCleanupStaleStates(nowUtc)` -- 定期清理过期状态
9. `CreateImageOutput(result, output)` -- 封装输出，附带实际帧数

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FrameCount` | `int` | `8` | `[1, 64]` | 时间窗口大小，即最多缓存多少帧参与融合。值越大，降噪更强，但启动更慢、拖影风险更高、内存占用也更大。 |
| `Mode` | `enum` | `Mean` | `Mean` / `Median` | 融合模式。`Mean` 适合随机高斯噪声，计算快速稳定；`Median` 更抗脉冲噪声和离群值，但计算开销更大。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | `Image` | `Image` | Yes | 连续输入的视频帧或时序图像。所有参与融合的帧必须尺寸和类型一致。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | `Image` | `Image` | 当前缓存窗口上的融合结果。热启动阶段结果会逐渐改善。 |
| `FrameCount` | `Frame Count` | `Integer` | 当前缓存中实际参与融合的帧数，不是配置参数原值。热启动阶段从 1 逐步增长到目标值。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `FrameCount` | `Integer` | 本次执行实际参与融合的帧数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `Mean`：`O(N * H * W * C)`，其中 `N = FrameCount`。`Median`：`O(H * W * C * N)`，使用快速选择算法（平均 O(N)，最坏 O(N^2)）。 |
| 典型耗时 (Typical Latency) | `Mean` 通常明显快于 `Median`，因为使用 OpenCV 向量化的 `Accumulate`。`Median` 的主要成本来自逐像素的快速选择和内存随机访问。 |
| 内存特征 (Memory Profile) | 需要维护最多 `FrameCount` 帧的缓存（`Queue<Mat>`），加上 `snapshot` 副本。`Median` 还需要展平视图和结果矩阵。`FrameCount=64` 时，1080p 3 通道图像约占用 64 * 6MB = 384MB 缓存。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：相机固定、目标基本静止、连续采图且主要问题是随机噪声的场景。
- **适合 (Suitable)**：需要提升信噪比、改善弱纹理可见性、抑制传感器噪声的预处理流程。
- **适合 (Suitable)**：`Median` 模式可用于抑制偶发亮点、火花、随机闪烁、热像素等时序离群值。
- **不适合 (Not Suitable)**：目标高速运动或位置变化明显的场景，会导致拖影、重影或轮廓模糊。
- **不适合 (Not Suitable)**：输入分辨率、通道数、位深频繁变化的流程，因为历史帧会被清空，累计效果无法稳定建立。
- **不适合 (Not Suitable)**：对单帧实时性非常敏感且内存预算很紧的任务，尤其不适合使用较大 `FrameCount` 的 `Median` 模式。

## 已知限制 / Known Limitations
1. 这是一个有状态算子，历史帧缓存在算子实例内部；如果同一实例被多个流程复用，需要明确管理生命周期与上下文隔离。
2. 在缓存未积满前，算子也会立即输出结果，因此前几帧的降噪效果会逐渐爬升，而不是一步到位。
3. 当 `FrameCount` 为偶数时，`Median` 当前实现取排序后的"上中位数"（`frames.Count / 2` 对应的索引），不是两个中间值的平均。
4. 所有帧必须尺寸和类型完全一致，否则内部会清空缓存重新开始。
5. `Median` 是**时间域中值**，并不会像 `MedianBlur` 那样平滑单帧空间噪点；它更适合跨帧离群值抑制。
6. 当前实现没有提供"窗口已满再输出"的开关，也没有时间戳或触发同步机制，默认按到达顺序滚动融合。
7. 状态 TTL 为 30 分钟，清理间隔为 5 分钟。长时间不使用的算子实例会自动释放缓存，但不会通知调用方。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：修正 Median 实现为快速选择算法（非 Cv2.Sort）、补充 ConcurrentDictionary 状态管理、说明 IDisposable 生命周期、修正调用链细节 |
| 1.0.1 | 2026-03-14 | 基于源码补充时间域融合原理、状态缓存行为、Median 向量化实现与限制说明 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |
