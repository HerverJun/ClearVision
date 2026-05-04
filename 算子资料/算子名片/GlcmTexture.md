# GLCM纹理特征 / GLCM Texture Features

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `GlcmTextureOperator` |
| 枚举值 (Enum) | `OperatorType.GlcmTexture` |
| 分类 (Category) | Texture |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
灰度共生矩阵（Gray-Level Co-occurrence Matrix, GLCM）是一种经典的纹理统计分析方法，由 Haralick 等人于 1973 年提出。其核心思想是：统计图像中灰度值为 `i` 的像素与灰度值为 `j` 的像素在指定方向和距离上同时出现的概率，构建一个 `L x L` 的共现矩阵 `P(i, j)`，其中 `L` 为量化灰度级数。

本算子从 GLCM 中提取五个 Haralick 纹理特征：

- **Contrast（对比度）**：`sum_{i,j} (i-j)^2 * P(i,j)`，衡量灰度差异的强度，值越大表示纹理越粗糙。
- **Correlation（相关性）**：衡量灰度线性依赖关系，值接近 1 表示强正相关。
- **Energy（能量/角二阶矩）**：`sum_{i,j} P(i,j)^2`，衡量灰度分布的均匀性，值越大表示纹理越规则。
- **Homogeneity（同质性/逆差矩）**：`sum_{i,j} P(i,j) / (1 + |i-j|)`，衡量局部灰度均匀性。
- **Entropy（熵）**：`-sum_{i,j} P(i,j) * log(P(i,j))`，衡量灰度分布的随机性，值越大表示纹理越复杂。

> English: The operator computes a quantized Gray-Level Co-occurrence Matrix for configured directions (0/45/90/135 degrees) and extracts five Haralick texture features: Contrast, Correlation, Energy, Homogeneity, and Entropy. Features are averaged across all directions and also reported per-direction.

## 实现策略 / Implementation Strategy
当前实现通过 `GlcmTexture.Compute` 核心方法完成全部计算：

1. **ROI 裁剪**：通过 `ResolveRoi` 根据 `RoiX/RoiY/RoiW/RoiH` 参数从输入图像中提取感兴趣区域。若宽/高为 0，则自动扩展到图像边界。
2. **方向解析**：`ParseDirections` 将逗号分隔的角度字符串（如 `"0,45,90,135"`）解析为 `GlcmDirection` 列表。当前仅支持 0、45、90、135 度四个标准方向，其他角度会抛出异常。
3. **GLCM 计算**：`GlcmTexture.Compute` 接收 ROI 灰度矩阵，按配置的量化级数、距离、方向和对称性参数构建共现矩阵，并提取纹理特征。
4. **结果聚合**：输出所有方向的均值特征（`mean`）和按方向分离的特征字典（`perDirection`）。

与 Halcon 的 `gen_cooc_matrix` + `get_gray_features` 组合相比，本算子将矩阵构建和特征提取合并为单次调用，减少了中间数据暴露。

> English: The implementation resolves ROI, parses direction angles, delegates to GlcmTexture.Compute for GLCM construction and feature extraction, and returns both averaged and per-direction Haralick features.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs)` -- 获取输入图像
2. `GetIntParam(@operator, "Levels", 16)` / `GetIntParam(@operator, "Distance", 1)` / `GetStringParam(@operator, "DirectionsDeg")` / `GetBoolParam(@operator, "Symmetric")` / `GetBoolParam(@operator, "Normalize")` -- 读取参数
3. `ResolveRoi(@operator, src.Width, src.Height)` -- 计算 ROI 矩形
4. `ParseDirections(directionsDeg)` -- 解析方向角度为 `GlcmDirection` 列表
5. `new Mat(src, roi)` -- 裁剪 ROI
6. `GlcmTexture.Compute(roiMat, levels, distance, directions, symmetric, normalize)` -- 核心 GLCM 计算
7. `perDirection.ToDictionary(...)` -- 按方向整理特征输出
8. `OperatorExecutionOutput.Success(output)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Levels` | `int` | `16` | `[2, 256]` | 灰度量化级数。将原始灰度范围压缩到 L 个离散级别以构建 GLCM。值越小，计算越快但细节丢失越多；值越大，矩阵越稀疏。 |
| `Distance` | `int` | `1` | `[1, 64]` | 像素对的空间距离（像素数）。距离为 1 表示相邻像素，距离越大捕获的纹理尺度越大。 |
| `DirectionsDeg` | `string` | `0,45,90,135` | 逗号分隔的角度值，仅支持 `0,45,90,135` | GLCM 的扫描方向。四个方向覆盖水平、右上对角、垂直、左上对角。可配置为子集如 `"0,90"` 以加速计算。 |
| `Symmetric` | `bool` | `true` | - | 是否对称化 GLCM。为 `true` 时 `P(i,j) = P(j,i)`，矩阵对称，统计更稳定。 |
| `Normalize` | `bool` | `true` | - | 是否归一化 GLCM。为 `true` 时矩阵元素为概率值（总和为 1），否则为原始计数。 |
| `RoiX` | `int` | `0` | `>= 0` | ROI 左上角 X 坐标。默认 `0`（图像左边缘）。 |
| `RoiY` | `int` | `0` | `>= 0` | ROI 左上角 Y 坐标。默认 `0`（图像上边缘）。 |
| `RoiW` | `int` | `0` | `>= 0` | ROI 宽度。默认 `0`（自动扩展到图像右边缘）。 |
| `RoiH` | `int` | `0` | `>= 0` | ROI 高度。默认 `0`（自动扩展到图像下边缘）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | Image | `Image` | Yes | 输入图像。多通道图像会在 `GlcmTexture.Compute` 内部转为灰度处理。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Contrast` | Contrast | `Float` | 所有方向的平均对比度。值越大表示纹理越粗糙、灰度差异越大。 |
| `Correlation` | Correlation | `Float` | 所有方向的平均相关性。值接近 1 表示灰度线性依赖强。 |
| `Energy` | Energy | `Float` | 所有方向的平均能量。值越大表示纹理越规则、灰度分布越均匀。 |
| `Homogeneity` | Homogeneity | `Float` | 所有方向的平均同质性。值越大表示局部灰度越均匀。 |
| `Entropy` | Entropy | `Float` | 所有方向的平均熵。值越大表示纹理越复杂、灰度分布越随机。 |
| `PerDirection` | Per Direction Features | `Any` | 按方向分离的特征字典。键为角度字符串（如 `"0"`、`"45"`），值为包含五个特征的子字典。 |

### 运行时附加输出 / Runtime Additional Outputs
（无额外运行时附加输出。所有特征值均通过输出端口返回。）

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(D * (W*H + L^2))`，其中 `D` 为方向数，`W*H` 为 ROI 像素数，`L` 为量化级数。ROI 扫描为 `O(W*H)`，特征提取为 `O(L^2)`。 |
| 典型耗时 (Typical Latency) | 无专用基准数据；由纹理单元测试和集成测试覆盖。 |
| 内存特征 (Memory Profile) | `O(L^2)`，主要为 GLCM 矩阵存储。四个方向各一张 `L x L` 矩阵，但通常逐方向计算后释放。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：纹理检测场景，其中对比度、能量、同质性、熵和相关性是有意义的摘要特征，如金属表面、纺织品、纸张的纹理质量评估。
- **适合 (Suitable)**：基于 ROI 的材料或表面比较，在固定量化级数和方向设置下对比不同区域的纹理差异。
- **适合 (Suitable)**：缺陷检测前的纹理特征提取，将 GLCM 特征作为分类器的输入特征向量。
- **不适合 (Not Suitable)**：旋转不变的纹理分类，当前方向固定为 0/45/90/135 度，不支持任意角度或旋转不变特征。
- **不适合 (Not Suitable)**：大图像配合高量化级数的实时场景，GLCM 矩阵构建和特征提取的延迟可能不满足严格的时间约束。
- **不适合 (Not Suitable)**：需要直接输出纹理缺陷分类结果的场景，本算子仅输出统计特征，不执行分类判定。

## 已知限制 / Known Limitations
1. 支持的方向目前仅限于 0、45、90、135 度四个标准方向。传入其他角度（如 30、60）会抛出 `ArgumentOutOfRangeException`。
2. 算子仅输出统计纹理特征，不执行纹理缺陷分类。下游需要额外的分类器或阈值判定逻辑。
3. `DirectionsDeg` 参数解析失败时（如非数字字符）会抛出格式异常，而非返回友好的验证错误。
4. ROI 参数的 `RoiW`/`RoiH` 为 0 时自动扩展到图像边界，但若 `RoiX`/`RoiY` 超出图像范围，`ResolveRoi` 会将宽高钳位到 0，导致 ROI 无效并返回失败。
5. 高量化级数（如 256）配合大 ROI 时，GLCM 矩阵可能非常稀疏，导致熵和能量等特征的统计意义下降。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 GLCM 五个 Haralick 特征的数学公式，细化参数语义（Levels/Distance/DirectionsDeg），补充 ROI 解析逻辑、方向解析限制与适用场景 |
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |
