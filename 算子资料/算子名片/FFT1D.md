# 一维FFT / FFT 1D

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `FFT1DOperator` |
| 枚举值 (Enum) | `OperatorType.FFT1D` |
| 分类 (Category) | Frequency |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
一维快速傅里叶变换（FFT）将时域/空域信号转换为频域表示。对于长度为 N 的离散信号 x[n]，其离散傅里叶变换定义为：

```
X[k] = sum_{n=0}^{N-1} x[n] * e^{-j*2*pi*k*n/N},  k = 0, 1, ..., N-1
```

输出为复数频谱 `X[k]`，可分解为：
- **幅度谱** `|X[k]| = sqrt(Re^2 + Im^2)`：各频率分量的能量强度。
- **相位谱** `phase(X[k]) = atan2(Im, Re)`：各频率分量的初始相位偏移。

对于图像输入，算子将图像转为单通道浮点矩阵后执行 2D DFT（`Cv2.Dft`），输出完整的复数频谱、幅度谱和相位谱，并生成经对数缩放和象限平移的可视化热力图。

> English: The operator performs 1D FFT on numeric arrays or 2D DFT on images. For 1D signals it computes the full complex spectrum via OpenCV DFT and emits magnitude/phase arrays. For images it computes a 2D complex spectrum and produces a log-scaled, quadrant-shifted Jet colormap visualization.

## 实现策略 / Implementation Strategy
当前实现根据输入类型分两条路径处理：

**1D 信号路径**：
- 通过 `TryConvertToSignal` 将 `double[]`、`float[]`、`int[]` 统一转为 `double[]`。
- 将实数信号包装为 `Vec2f`（虚部为 0）构造 `CV_32FC2` 的单列 `Mat`。
- 调用 `Cv2.Dft(src, dst, DftFlags.ComplexOutput)` 得到复数频谱。
- 逐元素提取 `Complex.Magnitude` 和 `Complex.Phase`，并生成幅度/相位双曲线的可视化图。

**2D 图像路径**：
- 通过 `ConvertToSingleChannelFloat` 将多通道图像转灰度、统一为 `CV_32FC1`。
- 调用 `Cv2.Dft` 得到 2 通道复数频谱 `CV_32FC2`。
- 分离通道后用 `Cv2.Magnitude` / `Cv2.Phase` 计算幅度谱和相位谱。
- 可视化时先取对数幅度（`log(1 + magnitude)`），再通过 `ShiftQuadrants` 将零频移到中心，最后归一化到 `[0,255]` 并应用 Jet 色图。

与 Halcon 的 `fft_generic` 相比，本算子当前不支持指定变换轴（Axis 参数为遗留占位），且 2D 图像路径不执行行/列方向的独立 1D FFT。

> English: The implementation branches on input type -- 1D arrays go through a direct OpenCV DFT on a single-column complex Mat, while images are converted to single-channel float and processed with 2D DFT. Visualization uses log-magnitude with quadrant shifting and Jet colormap.

## 核心 API 调用链 / Core API Call Chain
1. `TryConvertToSignal(input)` -- 尝试将输入转为 `double[]`（支持 `double[]` / `float[]` / `int[]`）
2. **1D 路径**：`FFT(signal)` -> `new Mat(N, 1, CV_32FC2, complexData)` -> `Cv2.Dft(src, dst, DftFlags.ComplexOutput)` -> 逐元素提取 `Complex`
3. **2D 路径**：`ConvertToSingleChannelFloat(image)` -> 灰度 + `ConvertTo(CV_32FC1)` -> `Cv2.Dft(singleChannelFloat, complexSpectrum, DftFlags.ComplexOutput)`
4. `CreateMagnitudeSpectrum(complexSpectrum, logScale: false)` -> `Cv2.Split` + `Cv2.Magnitude`
5. `CreatePhaseSpectrum(complexSpectrum)` -> `Cv2.Split` + `Cv2.Phase`
6. `CreateSpectrumVisualization2D(complexSpectrum)` -> 对数幅度 -> `ShiftQuadrants` -> `Cv2.Normalize` -> `Cv2.ApplyColorMap(Jet)`
7. `CreateImageOutput(visualization, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| （无用户可配参数） | - | - | - | 本算子不通过 `[OperatorParam]` 暴露参数。`Axis` 输入端口为遗留参数，当前实现未使用。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Input` | Input Signal or Image | `Any` | Yes | 1D 数值数组（`double[]` / `float[]` / `int[]`）或图像。算子会自动判断输入类型并选择对应变换路径。 |
| `Axis` | Transform Axis | `Integer` | No | 遗留参数，用于信号/图像 profile 的变换轴选择。当前实现未使用此参数。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Spectrum` | Frequency Spectrum | `Any` | 1D 路径输出 `Complex[]`；2D 路径输出包含复数频谱的 `ImageWrapper`（`CV_32FC2`）。 |
| `Magnitude` | Magnitude Spectrum | `Any` | 1D 路径输出 `double[]`（各频率幅度值）；2D 路径输出幅度谱 `ImageWrapper`（`CV_32FC1`）。 |
| `Phase` | Phase Spectrum | `Any` | 1D 路径输出 `double[]`（各频率相位，弧度）；2D 路径输出相位谱 `ImageWrapper`（`CV_32FC1`）。 |
| `Image` | Visualization | `Image` | 1D 路径：绿线幅度 + 紫线相位的双曲线图。2D 路径：经对数缩放和象限平移的 Jet 热力图。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `TransformKind` | `String` | `1DSignal` 或 `2DImage`，标识实际执行的变换路径。 |
| `IsShifted` | `Boolean` | 仅 2D 路径输出，当前固定为 `false`（频谱未预平移）。 |
| `ProcessingTimeMs` | `Int64` | 本次执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 1D 信号：`O(N log N)`；2D 图像：`O(W * H * log(W*H))`。核心 DFT 由 OpenCV 本地 C++ 实现完成。 |
| 典型耗时 (Typical Latency) | 1D 1024 点信号约 `<= 4 ms`（实验室基线，2026-04-15）。2D 图像耗时与分辨率成正比。 |
| 内存特征 (Memory Profile) | 1D 路径：`O(N)` 复数数组。2D 路径：输入灰度副本 + 复数频谱 + 幅度/相位/可视化 Mat，峰值约为输入图像的 5-6 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：周期信号的频谱分析，如振动信号、音频波形的频率成分识别。
- **适合 (Suitable)**：频域滤波链路的前置步骤，为 `FrequencyFilter` 算子提供频谱输入。
- **适合 (Suitable)**：图像频域特征的可视化检查，如周期性纹理、干涉条纹的频率分析。
- **适合 (Suitable)**：实验室内 1D 信号的快速 FFT 验算。
- **不适合 (Not Suitable)**：需要按指定轴（行/列）独立做 1D FFT 的图像处理场景，当前 Axis 参数未实现。
- **不适合 (Not Suitable)**：需要严格实时硬约束的长链路场景，2D FFT 在大图像上有明显延迟。
- **不适合 (Not Suitable)**：需要逆变换恢复时域信号的场景，应使用 `InverseFFT1D` 算子。

## 已知限制 / Known Limitations
1. `Axis` 输入端口为遗留参数，当前实现未使用。图像输入始终执行完整 2D DFT，不支持按行/列独立做 1D FFT。
2. 2D 图像路径的幅度谱可视化默认不使用对数缩放（`logScale: false`），低频分量可能被高频分量掩盖。可视化图单独使用对数缩放。
3. `ShiftQuadrants` 使用逐像素循环实现（非 OpenCV 原生象限交换），在大图像上性能不如 `Cv2.Dft` 内置的 `DftFlags.Inverse` 平移。
4. 多通道图像统一转灰度处理，不支持对各通道分别做 FFT。
5. 输出的 `Spectrum` 端口在 1D/2D 路径下类型不同（`Complex[]` vs `ImageWrapper`），下游算子需自行判断。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：区分 1D 信号与 2D 图像两条变换路径，修正算法原理（补充 DFT 公式）、实现策略、调用链与参数说明，补充运行时附加输出与限制 |
| 1.0.0 | 2026-03-18 | 自动生成文档骨架 / Generated skeleton |
