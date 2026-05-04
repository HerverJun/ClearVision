# 一维逆FFT / Inverse FFT 1D

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `InverseFFT1DOperator` |
| 枚举值 (Enum) | `OperatorType.InverseFFT1D` |
| 分类 (Category) | Frequency |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
逆快速傅里叶变换（IFFT）将频域复数频谱还原为时域/空域信号。对于长度为 N 的频谱 X[k]，其逆离散傅里叶变换定义为：

```
x[n] = (1/N) * sum_{k=0}^{N-1} X[k] * e^{j*2*pi*k*n/N},  n = 0, 1, ..., N-1
```

与正向 FFT 的区别在于：(1) 指数项符号为正 `+j`；(2) 结果除以 N 做归一化（本实现通过 `DftFlags.Scale` 自动完成）。

对于 2D 复数频谱图，逆 DFT 将其还原为实数图像，输出实部（重建信号）、虚部（理论上应接近零）和可视化灰度图。

> English: The operator performs inverse FFT on 1D complex spectra or 2D complex spectrum images. It uses OpenCV DFT with `Inverse | Scale | ComplexOutput` flags to reconstruct the time-domain signal with proper amplitude normalization.

## 实现策略 / Implementation Strategy
当前实现根据输入类型分两条路径处理：

**1D 频谱路径**：
- 输入为 `Complex[]`，通过 `IFFTComplex` 执行逆变换。
- 将复数数组包装为 `Vec2f` 构造 `CV_32FC2` 的单列 `Mat`。
- 调用 `Cv2.Dft(src, dst, DftFlags.Inverse | DftFlags.Scale | DftFlags.ComplexOutput)`，`Scale` 标志确保幅值回归到原始量级。
- 逐元素提取 `Complex.Real` 和 `Complex.Imaginary`，生成实部/虚部分离的 `double[]`。
- 可视化为绿线时域信号图（带中心基线）。
- 支持通过 `OutputSize` 截断输出长度（取频谱前 N 个分量做逆变换）。

**2D 频谱路径**：
- 通过 `TryResolveComplexSpectrum` 验证输入为 2 通道 `CV_32FC2` 复数矩阵。
- 调用 `Cv2.Dft` 同样使用 `Inverse | Scale | ComplexOutput` 标志。
- `Cv2.Split` 分离实部和虚部通道。
- 实部作为重建信号输出，虚部理论上应接近零（可作为精度验证依据）。
- 可视化为经 `Cv2.Normalize` + Bone 色图的灰度热力图。

> English: The implementation branches on input type -- 1D Complex[] goes through a single-column inverse DFT, while 2D complex Mats are inverse-transformed and split into real/imaginary channels. Both paths use DftFlags.Scale for amplitude normalization.

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("Spectrum")` -- 获取频谱输入
2. `GetInt(inputs, "OutputSize", 0)` -- 获取可选输出长度
3. **1D 路径**：`IFFTComplex(spectrum)` -> `new Mat(N, 1, CV_32FC2, data)` -> `Cv2.Dft(src, dst, DftFlags.Inverse | DftFlags.Scale | DftFlags.ComplexOutput)` -> 逐元素提取 `Real` / `Imaginary`
4. `CreateSignalVisualization(realSignal)` -- 绘制时域信号曲线
5. **2D 路径**：`TryResolveComplexSpectrum` -> `Cv2.Dft(complexSpectrum, inverseComplex, DftFlags.Inverse | DftFlags.Scale | DftFlags.ComplexOutput)` -> `Cv2.Split(inverseComplex, channels)`
6. `CreateImageVisualization(realMat)` -> `Cv2.Normalize` -> `Cv2.ApplyColorMap(Bone)`
7. `CreateImageOutput(visualization, additionalData)` -- 封装输出

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| （无 `[OperatorParam]` 参数） | - | - | - | 本算子通过输入端口传递参数，不使用 `[OperatorParam]` 属性。 |

### 输入端口参数详解 / Input Port Parameter Details
| 端口名 (Port) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `OutputSize` | `Integer` | `0`（使用全部频谱） | `>= 0` | 截断输出信号长度。`0` 表示使用全部频谱分量；正值 N 表示取频谱前 N 个分量做逆变换，输出信号长度为 N。仅对 1D 路径有效。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Spectrum` | Input Frequency Spectrum | `Any` | Yes | 1D 复数频谱（`Complex[]`）或 2D 复数频谱图（`ImageWrapper`，2 通道 `CV_32FC2`）。通常来自 `FFT1D` 或 `FrequencyFilter` 算子。 |
| `OutputSize` | Desired Output Size | `Integer` | No | 截断输出长度。默认 `0`（不截断）。仅对 1D 频谱路径有效。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Signal` | Reconstructed Signal | `Any` | 1D 路径输出 `double[]`（实部时域信号）；2D 路径输出 `ImageWrapper`（实部矩阵）。 |
| `Real` | Real Part | `Any` | 1D 路径输出 `double[]`；2D 路径输出 `ImageWrapper`。与 `Signal` 相同，为逆变换的实部。 |
| `Imaginary` | Imaginary Part | `Any` | 1D 路径输出 `double[]`；2D 路径输出 `ImageWrapper`。理论上应接近零，可用于精度验证。 |
| `Image` | Visualization | `Image` | 1D 路径：绿线时域信号曲线（带中心基线和采样点标注）。2D 路径：Bone 色图灰度热力图。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `SignalLength` | `Int32` | 重建信号的有效长度。1D 路径为数组元素数；2D 路径为 `Rows * Cols`。 |
| `ProcessingTimeMs` | `Int64` | 本次执行耗时（毫秒）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 1D：`O(N log N)`；2D：`O(W * H * log(W*H))`。核心逆 DFT 由 OpenCV 本地 C++ 实现完成。 |
| 典型耗时 (Typical Latency) | 1D 1024 点频谱约 `<= 4 ms`（实验室基线，2026-04-15）。2D 耗时与频谱分辨率成正比。 |
| 内存特征 (Memory Profile) | 1D 路径：`O(N)` 复数数组 + `O(N)` 实部/虚部数组。2D 路径：逆变换结果 + 3 个分离通道 + 可视化 Mat，峰值约为频谱图的 4-5 倍。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：FFT 频域链路的回放步骤，将滤波后的频谱还原为时域信号。
- **适合 (Suitable)**：频域滤波（`FrequencyFilter`）后的时域重建，验证滤波效果。
- **适合 (Suitable)**：实验室内 FFT -> 滤波 -> IFFT 完整链路的精度回归验证。
- **适合 (Suitable)**：2D 频谱的图像重建，如频域处理后的图像恢复。
- **不适合 (Not Suitable)**：需要复杂窗口补偿或频谱加权的高精度信号重建场景。
- **不适合 (Not Suitable)**：输入为幅度谱（单通道实数）的场景，逆变换需要完整的复数频谱（实部+虚部）。
- **不适合 (Not Suitable)**：需要实时硬约束的长链路场景。

## 已知限制 / Known Limitations
1. 图像频谱输入必须为 2 通道复数矩阵（`CV_32FC2`）。单通道幅度图不能直接做逆变换，因为相位信息已丢失。
2. `OutputSize` 截断仅对 1D 频谱路径有效，2D 路径忽略此参数。
3. `DftFlags.Scale` 标志确保幅度归一化，但如果上游 FFT 未使用标准 DFT 定义，重建信号的绝对幅值可能与原始信号存在缩放差异。
4. 1D 和 2D 路径的输出类型不同（`double[]` vs `ImageWrapper`），下游算子需自行判断。
5. 虚部输出在理论上应接近零，但由于浮点精度限制，实际值可能不严格为零。不建议将虚部作为业务判定依据。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充 IFFT 数学公式，区分 1D/2D 两条重建路径，修正调用链（强调 DftFlags.Scale 归一化），细化输出端口类型差异与限制 |
| 1.0.0 | 2026-03-18 | 自动生成文档骨架 / Generated skeleton |
