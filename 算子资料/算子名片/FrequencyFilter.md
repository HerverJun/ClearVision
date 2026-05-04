# 频率滤波 / Frequency Filter

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FrequencyFilterOperator` |
| 枚举值 (Enum) | `OperatorType.FrequencyFilter` |
| 分类 (Category) | Frequency |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
频率滤波在频域中通过设计滤波器传递函数 H(f) 对频谱进行选择性衰减或保留。本算子支持四种 Butterworth 滤波器类型：

- **低通 (Lowpass)**：`H(f) = 1 / (1 + (f/f_c)^(2n))`，保留低频、衰减高频，用于去噪/平滑。
- **高通 (Highpass)**：`H(f) = (f/f_c)^(2n) / (1 + (f/f_c)^(2n))`，保留高频、衰减低频，用于边缘增强。
- **带通 (Bandpass)**：`H(f) = Highpass(f, f_low) * Lowpass(f, f_high)`，仅保留 `[f_low, f_high]` 频段。
- **带阻 (Bandstop/Notch)**：`H(f) = 1 - Bandpass(f, f_low, f_high)`，衰减指定频段、保留其余频率。

其中 `f_c` 为截止频率（归一化到 `[0, 0.5]`），`n` 为滤波器阶数（控制过渡带陡峭度）。滤波操作为频谱与掩码的逐元素乘法：`Filtered[k] = Spectrum[k] * H[k]`。

> English: The operator applies Butterworth lowpass/highpass/bandpass/bandstop filters to 1D or 2D complex spectra via element-wise multiplication with a frequency-domain mask. The filter order controls transition band steepness.

## 实现策略 / Implementation Strategy
当前实现兼容 1D 复数频谱和 2D 复数频谱两种输入：

**输入解析**：
- 若输入为 `Complex[]`，直接进入 1D 滤波路径。
- 若输入为 `ImageWrapper`，通过 `TryResolveComplexSpectrum` 验证其为 2 通道 `CV_32FC2` 复数矩阵，进入 2D 滤波路径。

**1D 滤波路径**：
- `CreateFilterMask1D` 按归一化频率 `f = |index/N|`（对称映射）逐点计算 Butterworth 响应。
- 滤波结果为 `filtered[i] = spectrum[i] * mask[i]`（复数乘法）。
- 可视化为 512x200 的掩码响应曲线图。

**2D 滤波路径**：
- `CreateFilterMask2D` 按归一化半径 `r = sqrt(fx^2 + fy^2)` 逐像素计算 Butterworth 响应。
- `ApplyComplexMask` 将单通道掩码 `Merge` 为双通道后与复数频谱逐元素相乘。
- 可视化为经象限平移的 Turbo 色图热力图。

截止频率被 `NormalizeCutoff` 约束到 `[1e-6, 0.5]`，确保数值稳定性。所有滤波器类型统一由 `EvaluateFilter` 分发，复用 `ButterworthLowpass` 和 `ButterworthHighpass` 两个基础函数。

> English: The implementation handles both 1D Complex[] and 2D complex Mat spectra. 1D uses per-sample Butterworth evaluation; 2D uses radius-based evaluation. Cutoff frequencies are clamped to [1e-6, 0.5] for numerical stability.

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("Spectrum")` -- 获取频谱输入
2. `GetString(inputs, "FilterType", "lowpass")` -- 解析滤波类型（lowpass/highpass/bandpass/bandstop）
3. `GetDouble(inputs, "CutoffLow", 0.1)` / `GetDouble(inputs, "CutoffHigh", 0.3)` -- 获取截止频率
4. `NormalizeCutoff(cutoff)` -- 约束到 `[1e-6, 0.5]`
5. **1D 路径**：`CreateFilterMask1D(type, N, cutoffLow, cutoffHigh, order)` -> `EvaluateFilter` -> `ButterworthLowpass` / `ButterworthHighpass` -> 逐点复数乘法
6. **2D 路径**：`TryResolveComplexSpectrum` -> `CreateFilterMask2D(type, size, cutoffLow, cutoffHigh, order)` -> `ApplyComplexMask`（`Cv2.Merge` + `Cv2.Multiply`）
7. `CreateMaskVisualization(mask)` -> `ShiftQuadrants` -> `Cv2.Normalize` -> `Cv2.ApplyColorMap(Turbo)`
8. `CreateImageOutput(visualization, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| （无 `[OperatorParam]` 参数） | - | - | - | 本算子通过输入端口传递参数，不使用 `[OperatorParam]` 属性。以下为输入端口参数说明。 |

### 输入端口参数详解 / Input Port Parameter Details
| 端口名 (Port) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FilterType` | `String` | `lowpass` | `lowpass` / `highpass` / `bandpass` / `bandstop`（及别名 `low` / `high` / `band` / `notch`） | 滤波器类型。低通去高频噪声，高通增强边缘，带通保留指定频段，带阻去除指定频段。 |
| `CutoffLow` | `Double` | `0.1` | `[1e-6, 0.5]`（超出范围自动钳位） | 低截止频率（归一化）。带通/带阻模式下为频段下界；低通/高通模式下为唯一截止频率。 |
| `CutoffHigh` | `Double` | `0.3` | `[1e-6, 0.5]`（超出范围自动钳位） | 高截止频率（归一化）。仅带通/带阻模式使用，为频段上界。低通/高通模式下被忽略。 |
| `Order` | `Integer` | `2` | `>= 1` | Butterworth 滤波器阶数。阶数越高，过渡带越陡峭（越接近理想滤波器），但可能引入振铃效应。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Spectrum` | Input Frequency Spectrum | `Any` | Yes | 1D 复数频谱（`Complex[]`）或 2D 复数频谱图（`ImageWrapper`，2 通道 `CV_32FC2`）。通常来自 `FFT1D` 算子的 `Spectrum` 输出。 |
| `FilterType` | Filter Type | `String` | Yes | 滤波器类型：`lowpass` / `highpass` / `bandpass` / `bandstop`。 |
| `CutoffLow` | Low Cutoff Frequency | `Float` | No | 低截止频率，归一化到 `[0, 0.5]`。默认 `0.1`。 |
| `CutoffHigh` | High Cutoff Frequency | `Float` | No | 高截止频率，归一化到 `[0, 0.5]`。默认 `0.3`。仅带通/带阻使用。 |
| `Order` | Filter Order | `Integer` | No | Butterworth 阶数，默认 `2`。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `FilteredSpectrum` | Filtered Spectrum | `Any` | 1D 路径输出 `Complex[]`；2D 路径输出 `ImageWrapper`（`CV_32FC2`）。可直接传给 `InverseFFT1D` 做逆变换。 |
| `FilterMask` | Filter Mask | `Any` | 1D 路径输出 `double[]`；2D 路径输出 `ImageWrapper`（`CV_32FC1`）。可用于检查滤波器频率响应。 |
| `Image` | Visualization | `Image` | 1D 路径：黄色掩码响应曲线。2D 路径：经象限平移的 Turbo 热力图。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `SpectrumKind` | `String` | `1DSignal` 或 `2DComplexImage`，标识处理路径。 |
| `IsShifted` | `Boolean` | 仅 2D 路径输出，当前固定为 `false`。 |
| `EffectiveCutoffLow` | `Double` | 实际使用的低截止频率（经钳位后）。 |
| `EffectiveCutoffHigh` | `Double` | 实际使用的高截止频率（经钳位后）。 |
| `WasClamped` | `Boolean` | 截止频率是否被钳位到有效范围。 |
| `ProcessingTimeMs` | `Int64` | 本次执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 1D：`O(N)`（逐点掩码乘法）；2D：`O(W*H)`（逐像素掩码计算 + 逐像素乘法）。掩码生成本身为 `O(N)` 或 `O(W*H)`。 |
| 典型耗时 (Typical Latency) | 1D 1024 点频谱约 `<= 1 ms`（实验室基线，2026-04-15）。2D 耗时与频谱分辨率成正比。 |
| 内存特征 (Memory Profile) | 1D 路径：`O(N)` 掩码 + `O(N)` 滤波结果。2D 路径：`O(W*H)` 掩码 Mat + 双通道合并 Mat + 滤波结果 Mat，峰值约为频谱图的 3-4 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：低通滤波去除高频噪声，如周期信号中的高频毛刺、图像频谱中的噪声频段。
- **适合 (Suitable)**：高通滤波增强边缘和细节，如突出图像中的纹理和轮廓信息。
- **适合 (Suitable)**：带通滤波保留目标频段，如从混合信号中提取特定频率成分。
- **适合 (Suitable)**：带阻滤波去除特定频率干扰，如消除周期性干涉条纹或电源工频干扰。
- **适合 (Suitable)**：实验室内频域滤波链路验证，配合 `FFT1D` + `InverseFFT1D` 构建完整的频域处理管道。
- **不适合 (Not Suitable)**：需要严格数学窗函数设计（如 Kaiser、Hamming 窗）的数字信号处理场景。
- **不适合 (Not Suitable)**：需要现场级自适应频谱建模的复杂场景，当前滤波参数为静态配置。
- **不适合 (Not Suitable)**：需要对实数频谱（幅度谱）直接滤波的场景，当前仅支持复数频谱输入。

## 已知限制 / Known Limitations
1. 截止频率为归一化值（`[0, 0.5]`），不直接对应物理频率单位（Hz）。用户需根据采样率自行换算。
2. 截止频率被硬钳位到 `[1e-6, 0.5]`，超出范围的值会被静默修正，不会报错。`WasClamped` 附加输出可用于检测此行为。
3. 1D 和 2D 路径的 `FilteredSpectrum` 和 `FilterMask` 输出类型不同（`Complex[]` / `double[]` vs `ImageWrapper`），下游算子需自行判断。
4. 当前不支持自定义滤波器形状（如 Chebyshev、Elliptic），仅实现 Butterworth 家族。
5. `ShiftQuadrants` 使用逐像素循环实现，在大频谱图上性能不如 OpenCV 原生操作。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充四种 Butterworth 滤波器的数学公式，区分 1D/2D 两条处理路径，细化参数语义（归一化频率、阶数含义），补充运行时附加输出与限制 |
| 1.0.0 | 2026-03-18 | 自动生成文档骨架 / Generated skeleton |
