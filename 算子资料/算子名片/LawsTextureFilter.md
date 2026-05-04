# Laws纹理滤波 / Laws Texture Filter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LawsTextureFilterOperator` |
| 枚举值 (Enum) | `OperatorType.LawsTextureFilter` |
| 分类 (Category) | Texture |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
Laws 纹理滤波是一种基于一维卷积核对的纹理能量测量方法，由 Kenneth Laws 于 1980 年提出。其核心思想是：使用一组精心设计的 5-tap 一维滤波器（Level、Edge、Spot、Wave、Ripple），通过两个滤波器的外积构成 5x5 二维卷积核，对图像进行滤波后计算局部能量，从而捕获不同尺度和方向的纹理特征。

五个基础 5-tap 滤波器定义为：
- **L5** = `[1, 4, 6, 4, 1]`：Level（平滑/低通）
- **E5** = `[-1, -2, 0, 2, 1]`：Edge（边缘检测）
- **S5** = `[-1, 0, 2, 0, -1]`：Spot（点检测）
- **W5** = `[-1, 2, 0, -2, 1]`：Wave（波浪检测）
- **R5** = `[1, -4, 6, -4, 1]`：Ripple（涟漪检测）

组合命名规则为 `{行滤波器}5{列滤波器}5`，如 `E5L5` 表示行方向用 E5、列方向用 L5 构成的 5x5 核。

滤波后的局部能量通过在窗口内计算均方响应获得：`Energy(x,y) = mean(FilterResponse^2)`，窗口大小由 `EnergyWindowSize` 控制。

> English: Laws texture filtering applies 5x5 separable kernels formed by outer products of 5-tap 1D filters (L/E/S/W/R) to the image, then computes local energy as the mean squared filter response within a sliding window.

## 实现策略 / Implementation Strategy
当前实现通过 `LawsTextureFilter` 静态工具类完成全部计算：

1. **输入验证**：通过 `TryGetInputImage` 获取图像，验证非空。
2. **参数读取**：从 `[OperatorParam]` 读取核组合、局部均值减除、窗口大小和边界类型。
3. **核组合验证**：`IsValidKernelCombo` 检查格式为 `{L/E/S/W/R}5{L/E/S/W/R}5`（4 字符，第 2/4 位为 `'5'`，第 1/3 位为合法核代码）。
4. **边界类型映射**：将整数参数映射为 `BorderTypes`（1=Replicate, 2=Reflect, 4=Default）。
5. **滤波执行**：`LawsTextureFilter.Apply` 将图像转为归一化灰度浮点数据，可选地减去局部均值光照（`subtractLocalMean`），然后应用配置的 5x5 可分离 Laws 核对。
6. **能量计算**：`LawsTextureFilter.ComputeEnergy` 在指定窗口内计算滤波响应的均方值。
7. **均值能量**：`Cv2.Mean(energy).Val0` 计算整幅能量图的平均值，作为全局纹理能量指标。

与 Halcon 的 `texture_laws` 相比，本算子将滤波和能量计算合并为单次调用，且支持局部均值减除来补偿光照不均。

> English: The implementation validates the kernel combo format, delegates to LawsTextureFilter.Apply for separable 5x5 convolution with optional local mean subtraction, then computes local energy via LawsTextureFilter.ComputeEnergy.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像
2. `GetStringParam(@operator, "KernelCombo", "E5E5")` -- 读取核组合
3. `GetBoolParam(@operator, "SubtractLocalMean", true)` -- 是否减除局部均值
4. `GetIntParam(@operator, "LocalMeanWindowSize", 15)` / `GetIntParam(@operator, "EnergyWindowSize", 15)` -- 读取窗口大小
5. `GetIntParam(@operator, "BorderType", 1)` -> 映射为 `BorderTypes` -- 读取边界类型
6. `LawsTextureFilter.Apply(src, kernelCombo, subtractLocalMean, localMeanWindowSize, borderType)` -- 核心滤波：灰度归一化 -> 局部均值减除 -> 5x5 可分离卷积
7. `LawsTextureFilter.ComputeEnergy(filtered, windowSize, borderType)` -- 局部能量计算：均方响应
8. `Cv2.Mean(energy).Val0` -- 全局均值能量
9. `OperatorExecutionOutput.Success(output)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `KernelCombo` | `string` | `E5E5` | 格式为 `{L/E/S/W/R}5{L/E/S/W/R}5` | Laws 滤波核组合。前两位为行方向滤波器，后两位为列方向滤波器。常用组合：`E5E5`（边缘能量）、`E5L5`（边缘平滑）、`S5S5`（点检测）、`W5W5`（波浪检测）、`R5R5`（涟漪检测）。 |
| `SubtractLocalMean` | `bool` | `true` | - | 是否在滤波前减除局部均值光照。为 `true` 时可补偿缓慢变化的光照不均，使纹理能量更准确地反映纹理而非亮度变化。 |
| `LocalMeanWindowSize` | `int` | `15` | `[3, 101]`，必须为奇数 | 局部均值减除的窗口大小。值越大，减除的光照变化尺度越大。仅在 `SubtractLocalMean` 为 `true` 时生效。 |
| `EnergyWindowSize` | `int` | `15` | `[3, 101]`，必须为奇数 | 局部能量计算的窗口大小。值越大，能量图越平滑、对局部细节越不敏感；值越小，对局部纹理变化越敏感。 |
| `BorderType` | `enum` | `1`（Replicate） | `1` = Replicate, `2` = Reflect, `4` = Default | 边界填充方式。`Replicate` 用边缘像素值填充，`Reflect` 镜像填充，`Default` 使用 OpenCV 默认方式（通常为 `Reflect101`）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入图像。会在 `LawsTextureFilter.Apply` 内部转为归一化灰度浮点数据处理。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `FilteredImage` | Filtered Image | `Image` | Laws 核滤波后的响应图像。反映指定核组合在各像素处的滤波输出。 |
| `EnergyImage` | Energy Image | `Image` | 局部能量图。每个像素值为 `EnergyWindowSize` 窗口内滤波响应的均方值，反映局部纹理强度。 |
| `MeanEnergy` | Mean Energy | `Float` | 整幅能量图的全局均值。可作为单一标量纹理指标用于阈值判定或比较。 |

### 运行时附加输出 / Runtime Additional Outputs
（无额外运行时附加输出。所有结果均通过输出端口返回。）

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(W*H*(K^2 + M^2 + E^2))`，其中 `K=5`（Laws 核大小），`M=LocalMeanWindowSize`，`E=EnergyWindowSize`。滤波为 `O(W*H*K^2)`，局部均值和能量计算各为 `O(W*H*M^2)` 和 `O(W*H*E^2)`。 |
| 典型耗时 (Typical Latency) | 无专用基准数据；由纹理单元测试和集成测试覆盖。 |
| 内存特征 (Memory Profile) | `O(W*H)`，主要为滤波结果图和能量图各一张。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：材料、表面或缺陷预筛中的局部纹理能量高亮，如金属表面划痕、纺织品纹理异常检测。
- **适合 (Suitable)**：对比固定 Laws 核组合（如 E5E5、E5L5、S5S5、W5W5、R5R5）的响应差异，选择最佳纹理描述子。
- **适合 (Suitable)**：光照不均匀场景下的纹理分析，通过局部均值减除补偿亮度漂移。
- **适合 (Suitable)**：作为纹理分类器的特征提取前置步骤，将能量图或均值能量作为输入特征。
- **不适合 (Not Suitable)**：语义纹理分类，本算子仅输出滤波响应和能量，不执行分类判定，需要下游阈值或模型。
- **不适合 (Not Suitable)**：光照漂移无法通过局部均值减除校正的场景，如强烈的阴影边界或高动态范围图像。
- **不适合 (Not Suitable)**：需要自定义非 Laws 核的滤波场景，当前核组合仅支持 L/E/S/W/R 五种标准 5-tap 滤波器。

## 已知限制 / Known Limitations
1. 核组合必须使用经典的 L/E/S/W/R 五种 5-tap Laws 滤波器代码。不支持自定义滤波器系数或非 5-tap 核。
2. 输出能量取决于所选窗口大小，不同采集环境下的能量值不可直接比较。跨场景比较时需固定窗口大小和核组合。
3. `LocalMeanWindowSize` 和 `EnergyWindowSize` 的范围为 `[3, 101]`，且必须为奇数。当前实现未自动修正偶数输入（与 `AdaptiveThreshold` 的 `BlockSize` 行为不同）。
4. `BorderType` 参数映射有限，仅支持 Replicate(1)、Reflect(2)、Default(4) 三种，不支持 Wrap 等其他边界模式。
5. Laws 核为固定系数，无法通过参数调整滤波器的频率响应特性。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 Laws 五种 5-tap 滤波器的数学定义和核组合命名规则，细化实现策略（可分离卷积 + 局部均值减除 + 能量计算），补充参数语义与限制 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
