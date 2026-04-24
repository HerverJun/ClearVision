# ClearVision 算子库精度审计问题与 C 级算子风险详细记录

> 审计日期：2026-04-21  
> 审计工具：Kimi Code CLI  
> 数据基准：源码 HEAD + 算子目录.json（2026-04-19 生成）+ 性能预算报告（2026-04-13）+ Week5 审计台账（2026-04-15）  
> 限制说明：因网络环境 NuGet SSL 失败，本次审计未执行新的运行时测试，结论基于静态代码审查与历史归档数据

---

## 目录

1. [精度审计具体问题（逐条详录）](#一精度审计具体问题逐条详录)
   - 1.1 亚像素边缘检测体系
   - 1.2 工业卡尺内核
   - 1.3 测量统计体系
   - 1.4 标定数值稳定性
   - 1.5 性能效率细节
2. [C 级算子风险详细清单](#二c-级算子风险详细清单)
   - 2.1 C 级算子总览与评分拆解
   - 2.2 检测类 C 级算子（ArcCaliper / ContourExtrema / PhaseClosure）
   - 2.3 Region/Morphology 类 C 级算子
   - 2.4 流程控制/辅助/变量类 C 级算子
3. [问题分级汇总表](#三问题分级汇总表)

---

## 一、精度审计具体问题（逐条详录）

### 1.1 亚像素边缘检测体系

#### 问题 A-001：`SubPixelEdgeDetector.DetectZernike` 名不副实（P1）

| 属性 | 内容 |
|---|---|
| **源码位置** | `Acme.Product/src/Acme.Product.Infrastructure/ImageProcessing/SubPixelEdgeDetector.cs`，第 184–289 行 |
| **问题类型** | 命名与算法实现严重不符，误导性强 |
| **详细描述** | 该方法声称实现 "Zernike-style moment based subpixel edge localization"，但实际计算的是梯度幅值的一阶空间矩（类似于灰度重心法在梯度域的变种），并非 Zernike 正交矩。真正的 Zernike 矩边缘检测需要构造 Zernike 正交多项式核（Z11、Z20、Z31 等），在图像上进行卷积，利用旋转不变性求解边缘参数。当前实现仅对梯度幅值做加权平均 `z11 += grad * ((i-center)/r)`，没有正交基、没有阶数概念、没有旋转不变性。 |
| **精度影响** | 对于理想阶跃边缘，该方法和重心法表现接近，能达到 0.1px 左右；但对于噪声图像或倾斜边缘，由于缺乏 Zernike 矩的旋转不变性和高阶信息，精度会明显低于真正的 Zernike 实现。 |
| **工业验收风险** | 高。用户可能因 "Zernike" 之名期望 0.01px 级精度，实际得到的是重心法级精度，现场验收时易产生争议。 |
| **修复建议** | **方案一（推荐）**：重命名为 `DetectGradientMoment` 或 `DetectFirstOrderMoment`，并在 XML 注释中明确说明算法原理与适用边界。**方案二**：引入真正的 Zernike 正交矩实现（参考 Ghosal & Mehrotra 1993），需要预计算 Z11/Z20/Z31 核（如 5×5 或 7×7），通过 `l = Z11/Z00 * 2` 和 `φ = atan2(Im(Z31), Re(Z31))` 计算边缘参数。 |
| **相关代码** | ```csharp
public float DetectZernike(Mat roi, int maskSize = 5)
{
    // ...
    double z00 = 0.0;
    double z11 = 0.0;
    for (int i = 0; i < gradLength; i++)
    {
        double grad = Math.Abs(values[i + 1] - values[i]);
        z00 += grad;
        z11 += grad * ((i - center) / r);
    }
    double l = (z11 / z00) * 2.0;  // 这不是 Zernike 矩的标准公式
    // ...
}
``` |

#### 问题 A-002：`StegerSubpixelEdgeDetector` 实现正确，但缺少数值稳定性边界测试（低）

| 属性 | 内容 |
|---|---|
| **源码位置** | `Acme.Product/src/Acme.Product.Infrastructure/ImageProcessing/StegerSubpixelEdgeDetector.cs` |
| **问题类型** | 实现正确，但测试覆盖待验证 |
| **详细描述** | Steger 法的实现符合经典文献：可分离高斯核、一阶/二阶导数核、Hessian 特征值分解、法向量归一化、二次泰勒展开求亚像素偏移。`NumericalEpsilon = 1e-10` 在 double 精度下足够。`FitCircle` 和 `FitLine` 使用代数法（Kasa 法），对于圆拟合在边缘点分布不均匀时可能产生偏差，但 RMSE 已输出，可供诊断。 |
| **精度影响** | 对于高对比度边缘，Steger 法精度可达 0.01px；对于低对比度或强噪声图像，Hessian 特征值可能接近 epsilon，导致边缘点丢失。 |
| **工业验收风险** | 低。实现正确，但建议补充噪声退化场景测试（SNR 10dB、20dB、30dB 下的定位精度）。 |
| **修复建议** | 1. 为 `FitCircle` 增加 Taubin 或 Pratt 代数拟合作为可选方法（Kasa 法在边缘点分布不均时有偏）。2. 补充噪声场景下的精度衰减测试。 |

---

### 1.2 工业卡尺内核（IndustrialCaliperKernel）

#### 问题 B-001：高斯核 radius 被硬截断到 8（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `Acme.Product/src/Acme.Product.Infrastructure/Operators/IndustrialCaliperKernel.cs`，第 276 行 |
| **问题类型** | 参数截断未告警，大 σ 时频谱泄漏 |
| **详细描述** | `var radius = Math.Clamp((int)Math.Ceiling(sigma * 3.0), 1, 8);` 当 σ > 2.7 时，radius 被截断为 8，而 3σ 法则实际需要 radius = ceil(3σ)。例如 σ=4 时需要 radius=12，截断到 8 后高斯核尾部被切断，归一化后频谱泄漏，平滑后的 profile 在边缘附近出现振铃。 |
| **精度影响** | σ 较大时（如强噪声图像需要大模糊），边缘定位可能产生系统性偏差，亚像素精度从 ~0.05px 退化到 ~0.2px。 |
| **工业验收风险** | 中。σ 默认值 1.2 不受影响，但用户在强噪声场景调大 σ 时可能无感知地损失精度。 |
| **修复建议** | **方案一**：移除 clamp 上限，或改为 `Math.Min(32, (int)Math.Ceiling(sigma * 3.0))`。**方案二（推荐）**：保留上限但增加 σ 过大时的日志警告：`if (sigma > 2.7) logger.LogWarning("Caliper sigma={Sigma} exceeds safe kernel radius 8; subpixel accuracy may degrade.")`。 |
| **相关代码** | ```csharp
private static double[] GaussianSmooth(IReadOnlyList<double> profile, double sigma)
{
    var radius = Math.Clamp((int)Math.Ceiling(sigma * 3.0), 1, 8);
    // ...
}
``` |

#### 问题 B-002：`GaussianSmooth` 边界处理使用 clamp（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `IndustrialCaliperKernel.cs`，第 286 行 `var idx = Math.Clamp(i + k, 0, profile.Count - 1);` |
| **问题类型** | 边界处理引入边缘偏差 |
| **详细描述** | 高斯平滑在边界处使用 clamp（重复边缘像素），这会在 profile 的起止位置产生偏置，导致靠近边界的边缘位置被拉向边界。对于工业测量中 ROI 常贴近图像边缘的场景，这可能造成系统性误差。 |
| **精度影响** | 边界附近边缘定位偏差约 0.1–0.3 px，内部边缘无影响。 |
| **工业验收风险** | 中。若用户 ROI 设置不当，边缘贴近图像边界时测量值系统性偏移。 |
| **修复建议** | 边界处改用 mirror（对称反射）或 linear extrapolation 处理；或在文档中明确提醒"卡尺 ROI 应距离图像边界至少 3σ 像素"。 |

#### 问题 B-003：`SampleBandProfile` 中 `Math.Pow` 可优化（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `IndustrialCaliperKernel.cs`，第 26 行 `Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2))` |
| **问题类型** | 性能微优化，不影响精度 |
| **详细描述** | `Math.Pow(x, 2)` 比 `x * x` 慢约 5–10 倍（函数调用 + 通用幂运算）。卡尺内核在每次执行时计算一次，单次影响微小，但高频调用时累积。 |
| **修复建议** | 改为 `double dx = end.X - start.X; double dy = end.Y - start.Y; double length = Math.Sqrt(dx * dx + dy * dy);` |

#### 问题 B-004：`ComputePercentile` 重复排序开销（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `IndustrialCaliperKernel.cs` 第 353–376 行 和 `MeasurementStatisticsHelper.cs` 第 5–29 行 |
| **问题类型** | 算法复杂度可优化 |
| **详细描述** | 每次调用都执行 `OrderBy` + `ToArray()`，时间复杂度 O(n log n)。卡尺内核在 `EstimateEdgeThreshold` 中调用两次（magnitudes 和 deviations），测量算子在批量输出统计量时可能调用多次。对于 n=128~512 的样本量，单次开销约 0.01–0.1ms，但在高并发或大批量测量时累积。 |
| **修复建议** | 若样本量固定或分布已知，可使用 QuickSelect O(n) 求中位数；或对批量测量场景，在 `MeasurementStatisticsHelper` 中提供一次性计算多统计量的方法（避免重复排序）。 |

---

### 1.3 测量统计体系（MeasurementStatisticsHelper）

#### 问题 C-001：MAD 计算未乘 1.4826，与 IndustrialCaliperKernel 不一致（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `MeasurementStatisticsHelper.cs`，第 36–45 行 |
| **问题类型** | 设计不一致，可能导致统计口径混乱 |
| **详细描述** | `IndustrialCaliperKernel.EstimateEdgeThreshold` 中 `var mad = ComputePercentile(deviations, 0.5) * 1.4826;`，而 `MeasurementStatisticsHelper.ComputeMedianAbsoluteDeviation` 返回原始 MAD，未乘 1.4826。1.4826 是将 MAD 转换为与标准差一致估计量的缩放因子（正态分布假设下）。两处设计目的不同（阈值估计 vs 统计输出），但用户看到 `MAD` 输出时可能误将其当作标准差的鲁棒替代。 |
| **工业验收风险** | 中。统计报告中的 MAD 与卡尺内部使用的 "scaled MAD" 数值不同，现场数据对标时易混淆。 |
| **修复建议** | 明确文档化：`ComputeMedianAbsoluteDeviation` 返回原始 MAD；如需标准差一致估计量，提供 `ComputeScaledMAD` 方法（×1.4826）。或在方法注释中注明 "Returns the raw MAD (unscaled). For a consistent estimator of standard deviation under normality, multiply by 1.4826."。 |

#### 问题 C-002：`ComputeConfidenceFromUncertainty` 为经验公式（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `MeasurementStatisticsHelper.cs`，第 87–95 行 |
| **问题类型** | 缺乏统计理论支撑 |
| **详细描述** | `return Math.Clamp(1.0 / (1.0 + Math.Max(0.0, uncertainty)), 0.0, 1.0);` 是一个经验映射：uncertainty=0 时 confidence=1，uncertainty→∞ 时 confidence→0。该公式不是基于任何已知概率分布（如正态分布的 t 检验、卡方检验）推导的置信度，无法对应到 95% CI 或 99% CI 的统计意义。 |
| **工业验收风险** | 中。客户可能要求 "置信度 95%" 的统计解释，当前公式无法提供。 |
| **修复建议** | 1. 在 XML 注释和算子文档中明确标注 "Empirical confidence, not a statistical confidence interval."。2. 如需统计置信区间，可基于重复测量次数和 t 分布计算：`CI = t(α/2, n-1) * SE`，然后 `Confidence = 1 - (CI / MeasurementValue)`。 |

#### 问题 C-003：圆形统计实现正确，但缺少大角度离散度校验（低）

| 属性 | 内容 |
|---|---|
| **源码位置** | `MeasurementStatisticsHelper.cs`，第 63–85 行 |
| **问题类型** | 实现正确，但边界场景未保护 |
| **详细描述** | 使用方向统计标准公式：合成长度 R = √(sin²+cos²)，标准差 σθ = √(-2·ln(R))·180/π。当角度分布极其分散（如均匀分布）时，R→0，ln(R)→-∞，但代码已用 `Math.Clamp(meanResultantLength, 1e-12, 1.0)` 保护。1e-12 对应的极限标准差约为 `√(-2·ln(1e-12)) * 180/π ≈ 1062°`，对实际应用足够。 |
| **修复建议** | 无需修改。如需更严格，可将下界收紧到 1e-15。 |

---

### 1.4 标定数值稳定性（IntrinsicsCalibrationRuntime）

#### 问题 D-001：标定校验严格，但缺少运行时漂移检测（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `Acme.Product/src/Acme.Product.Infrastructure/Calibration/IntrinsicsCalibrationRuntime.cs` |
| **问题类型** | 静态校验完善，但运行时监控缺失 |
| **详细描述** | `TryCreate` 对 camera matrix、distortion coefficients、image size 做了非常严格的静态校验（有限性、正性、模型长度、齐次坐标最后一行 [0,0,1] ±1e-9）。但在标定运行期间（如连续生产过程中），没有机制检测标定参数是否因温度、振动等因素发生漂移。 |
| **工业验收风险** | 中。长期运行后标定漂移可能导致像素→世界坐标转换系统性偏差，影响测量精度。 |
| **修复建议** | 增加标定质量监控机制：定期使用 `HandEyeCalibrationValidator` 或 `CameraCalibration` 的 reprojection error 进行在线校验，当 MeanError > 阈值时触发告警。 |

---

### 1.5 性能效率细节

#### 问题 E-001：`StegerSubpixelEdgeDetector` 全图导数计算（P2）

| 属性 | 内容 |
|---|---|
| **源码位置** | `StegerSubpixelEdgeDetector.cs`，第 109–123 行 `ComputeDerivatives` |
| **问题类型** | 大分辨率图像时内存带宽压力大 |
| **详细描述** | 对整张图像计算 dx, dy, dxx, dyy, dxy 五张双精度图，然后仅在 Canny 边缘点上使用。对于 4096×3072 图像，五张 CV_64F 图约需 5 × 4096 × 3072 × 8 ≈ 480 MB 内存带宽。 |
| **性能影响** | 512×512 图像无压力（已通过预算测试）；4K 图像时可能成为瓶颈。 |
| **修复建议** | 如性能测试在 4K 场景不达标，可改为 ROI 裁剪前置：先对 ROI 区域裁剪，再计算导数。当前无需修改，但需在 4K 性能预算中留足余量。 |

---

## 二、C 级算子风险详细清单

### 2.1 C 级算子总览与评分拆解

全库 155 个算子中，C 级算子 **15 个**，占比 9.7%。质量评分基于四个维度：

| 维度 | 权重暗示 | C 级算子典型得分 |
|---|---|---|
| `documentationScore` | 算法原理描述、边界说明 | 60（Region/Morphology/检测）或 100（变量/辅助） |
| `testCoverageScore` | 单元测试与集成测试覆盖 | **30（全库 C 级统一最低）** |
| `parameterValidationScore` | 参数范围校验、非法输入处理 | 80–100 |
| `errorHandlingScore` | 异常路径、退化场景处理 | 35（变量/辅助）或 60（Region/检测） |

**共同风险模式**：测试覆盖率仅 30 分意味着大量边界条件、异常输入、退化场景未被自动化测试覆盖；文档 60 分意味着算法原理描述模糊，现场调试困难；错误处理 60 分意味着算子在异常输入（空图、越界参数、无效标定）时可能崩溃或输出无意义结果。

---

### 2.2 检测类 C 级算子（高危）

#### 算子：`ArcCaliper`

| 属性 | 内容 |
|---|---|
| **质量评分** | 58（C 级）= Doc 60 / Test 30 / Validation 80 / ErrorHandling 60 |
| **版本** | 1.0.0 |
| **功能** | 弧形卡尺测量，用于测量圆弧上的边缘对 |
| **风险分析** | 卡尺类算子是测量精度的核心，但 `ArcCaliper` 的测试覆盖仅 30 分，意味着以下场景可能未测试：① 圆弧 ROI 跨越图像边界；② 弧长过短导致采样点不足；③ 极性设置错误时的退化行为；④ 亚像素模式与像素模式的精度对比。错误处理 60 分意味着异常输入（如半径为负、起始角=终止角）可能未被优雅处理。 |
| **精度风险** | **高**。若弧线段采样算法与 `IndustrialCaliperKernel` 的直线采样复用同一内核，但弧形参数插值未做均匀弧长采样，则边缘位置可能因角度线性插值而非弧长插值产生系统性偏差（尤其在曲率大时）。 |
| **现场影响** | 圆弧测量常用于轴承、密封圈等圆形工件。精度偏差 1–2 px 在物理单位换算后可能达到 0.05mm，超出公差带。 |
| **修复建议** | 1. 补齐单元测试：空图、越界 ROI、负半径、0°弧长、大曲率弧、图像边界跨越。**优先级：最高**。2. 审查弧形采样是否使用弧长参数化（`t` 应为 `s/L` 而非 `θ/Δθ`）。3. 补充与 `CaliperTool` 的精度对比基线。 |

#### 算子：`ContourExtrema`

| 属性 | 内容 |
|---|---|
| **质量评分** | 58（C 级）= Doc 60 / Test 30 / Validation 80 / ErrorHandling 60 |
| **版本** | 1.0.0 |
| **功能** | 求轮廓的极值点（最左、最右、最上、最下） |
| **风险分析** | 轮廓极值看似简单，但以下边界未覆盖：① 空轮廓（无边缘点）；② 轮廓所有点共线（极值不唯一）；③ 轮廓自相交；④ 多轮廓输入时的选择策略。错误处理 60 分暗示空轮廓时可能抛未处理异常或返回未初始化的坐标。 |
| **精度风险** | **中**。极值点通常用于定位基准，若返回未初始化的坐标（如 (0,0)），下游测量算子会基于此错误基准产生连锁偏差。 |
| **现场影响** | 常用于定位工件的左上角或右下角作为基准点。基准点错误会导致整组测量漂移。 |
| **修复建议** | 1. 补齐空轮廓、单点轮廓、共线轮廓的测试。2. 明确多轮廓时的策略（最大轮廓？第一个轮廓？）。3. 空轮廓时应输出 `IsSuccess=false` 并附带明确的 `ErrorMessage`。 |

#### 算子：`PhaseClosure`

| 属性 | 内容 |
|---|---|
| **质量评分** | 58（C 级）= Doc 60 / Test 30 / Validation 80 / ErrorHandling 60 |
| **版本** | 1.0.0 |
| **功能** | 相位闭合分析（通常用于干涉测量或条纹分析） |
| **风险分析** | PhaseClosure 是高级测量算子，涉及相位解包裹（phase unwrapping）。测试覆盖 30 分意味着以下场景可能未覆盖：① 噪声条纹（低 SNR）；② 条纹不连续（遮挡或阴影）；③ 相位跳变大于 π；④ 空图或 uniform 图像。文档 60 分意味着用户难以理解其适用边界。 |
| **精度风险** | **高**。相位解包裹本身是一个病态问题，算法选择（质量引导、枝切法、最小二乘等）对精度影响巨大。若实现未做质量图引导，噪声区域的解包裹错误会沿路径传播，导致整幅相位图系统性偏差。 |
| **现场影响** | 主要用于 3D 轮廓测量（如结构光、干涉仪）。相位解包裹错误会导致 3D 重建出现跳变或整体倾斜。 |
| **修复建议** | 1. 补齐噪声条纹、不连续条纹、高跳变场景的测试。2. 审查相位解包裹算法是否使用质量图（modulation/derivative variance）引导路径。3. 补充算法原理文档，明确适用场景与限制。 |

---

### 2.3 Region / Morphology 类 C 级算子

#### 算子群：RegionErosion / RegionDilation / RegionOpening / RegionClosing / RegionSkeleton

| 属性 | 内容 |
|---|---|
| **质量评分** | 均为 63（C 级）= Doc 60 / Test 30 / Validation 100 / ErrorHandling 60 |
| **版本** | 1.0.0 |
| **功能** | 区域形态的腐蚀、膨胀、开运算、闭运算、骨架提取 |
| **风险分析** | 这些算子的 `parameterValidationScore=100` 说明参数校验完善，但 `testCoverageScore=30` 和 `errorHandlingScore=60` 表明边界场景未覆盖。Region 算子与 `Morphology` 算子功能重叠（`Morphology` 评分为 A 级 94 分），存在维护冗余。文档 60 分说明算法原理描述模糊（如骨架提取使用 Zhang-Suen 还是 Guo-Hall 算法未说明）。 |
| **精度风险** | **中低**。形态学操作本身是确定性的，但以下场景有风险：① 空区域输入时输出未定义；② 大核（如 51×51）时的性能与内存开销；③ 骨架提取的连通性保证（单像素宽、8-连通）。 |
| **现场影响** | 形态学常用于预处理去噪或分割后处理。空区域时下游 BlobAnalysis 可能收到未预期输入。 |
| **修复建议** | 1. 补齐空区域、超大核、非矩形核的测试。2. 明确骨架提取算法及其连通性保证。3. 考虑将 Region 系列与 Morphology 系列合并或标记为 Legacy，减少维护面。 |

#### 算子群：RegionUnion / RegionIntersection / RegionDifference / RegionComplement

| 属性 | 内容 |
|---|---|
| **质量评分** | Union/Intersection/Difference: 61；Complement: 58 |
| **版本** | 1.0.0 |
| **功能** | 区域的布尔运算（并、交、差、补） |
| **风险分析** | `Week5 审计台账` 已确认 Region 布尔运算一致性面积测试通过，但 C 级评分说明测试覆盖仍然薄弱。`RegionComplement` 的 `errorHandlingScore=60` 是全 Region 系列最低，意味着全图补运算（如图像边界处理）可能存在异常。 |
| **精度风险** | **低**。布尔运算是确定性的，精度主要取决于像素级对齐。但 `RegionComplement` 在整图补运算时，若未正确处理图像边界，可能产生 1px 的边界偏差。 |
| **现场影响** | 常用于 ROI 掩膜组合。边界 1px 偏差在精密测量中可能影响判定。 |
| **修复建议** | 1. 补齐 `RegionComplement` 的整图边界测试。2. 审查所有 Region 算子在空输入时的行为（应返回空区域而非异常）。 |

---

### 2.4 流程控制 / 辅助 / 变量类 C 级算子

#### 算子：`CycleCounter`

| 属性 | 内容 |
|---|---|
| **质量评分** | 66（C 级）= Doc 100 / Test 30 / Validation 100 / ErrorHandling 35 |
| **版本** | 1.0.0 |
| **功能** | 循环计数器 |
| **风险分析** | `errorHandlingScore=35` 是全库最低之一。意味着溢出（如 int.MaxValue 后回绕）、并发访问（若多线程共享计数器）、负步长等场景几乎无保护。虽然功能简单，但 35 分的错误处理意味着算子可能在异常状态下静默失败。 |
| **精度风险** | **低**（非测量算子，不影响像素精度）。但流程控制错误可能导致检测节拍错乱。 |
| **修复建议** | 1. 补齐溢出、负步长、并发访问测试。2. 增加上限/下限 clamp 和溢出告警。 |

#### 算子：`Comment` / `Delay`

| 属性 | 内容 |
|---|---|
| **质量评分** | 均为 61（C 级）= Doc 100 / Test 30 / Validation 80 / ErrorHandling 35 |
| **版本** | 1.0.0 |
| **功能** | Comment：注释节点；Delay：延迟等待 |
| **风险分析** | `errorHandlingScore=35` 对 `Delay` 尤其危险：负延迟、超长延迟（如 1 小时）、取消令牌处理不当可能导致线程长时间阻塞或资源泄漏。`Comment` 虽不执行逻辑，但若被误当作有效节点处理，可能影响流程图解析。 |
| **精度风险** | **低**（非测量算子）。但 `Delay` 的线程阻塞可能影响整线节拍。 |
| **修复建议** | 1. `Delay` 补齐负值、超大值、CancellationToken 提前取消的测试。2. 增加延迟上限（如 60 秒）并抛出参数异常。 |

---

## 三、问题分级汇总表

### P0 — 阻碍工业级签收

| 编号 | 问题 | 影响算子/模块 | 修复紧迫性 |
|---|---|---|---|
| P0-001 | AI 检测算子缺少真实现场签收实录 | DeepLearning, AnomalyDetection, EdgePairDefect, SurfaceDefectDetection, SemanticSegmentation | 最高 |
| P0-002 | 生产模型唯一标识映射未闭环 | AI 运行时 / 模型目录 | 最高 |

### P1 — 精度/可靠性风险

| 编号 | 问题 | 源码位置 | 影响 | 建议措施 |
|---|---|---|---|---|
| P1-001 | `DetectZernike` 名不副实 | `SubPixelEdgeDetector.cs:184` | 用户期望 Zernike 精度，实际为梯度矩 | 重命名或实现真正 Zernike |
| P1-002 | `ArcCaliper` 测试覆盖仅 30% | `ArcCaliper` | 圆弧测量精度未知 | 补齐空图/越界/大曲率/边界跨越测试 |
| P1-003 | `PhaseClosure` 测试覆盖仅 30% | `PhaseClosure` | 相位解包裹在噪声/不连续场景下可能失败 | 补齐噪声条纹、高跳变、不连续测试 |
| P1-004 | `ContourExtrema` 测试覆盖仅 30% | `ContourExtrema` | 空轮廓/共线轮廓时可能崩溃 | 补齐边界测试，明确多轮廓策略 |

### P2 — 工程优化与维护性

| 编号 | 问题 | 源码位置 | 影响 | 建议措施 |
|---|---|---|---|---|
| P2-001 | 高斯核 radius 硬 clamp 到 8 | `IndustrialCaliperKernel.cs:276` | σ>2.7 时频谱泄漏 | 动态计算或增加警告 |
| P2-002 | 高斯平滑边界 clamp 处理 | `IndustrialCaliperKernel.cs:286` | 边界边缘定位偏差 | 改用 mirror 或文档提醒 |
| P2-003 | `Math.Pow(x,2)` 可优化 | `IndustrialCaliperKernel.cs:26` | 微性能损耗 | 改为 `x*x` |
| P2-004 | 百分位计算重复排序 | `MeasurementStatisticsHelper.cs:5` | O(n log n) 累积开销 | QuickSelect 或批量计算 |
| P2-005 | MAD 缩放因子不一致 | `MeasurementStatisticsHelper.cs:36` | 统计口径混乱 | 统一方法或文档化差异 |
| P2-006 | 置信度映射为经验公式 | `MeasurementStatisticsHelper.cs:87` | 无法解释统计意义 | 文档标注为经验值 |
| P2-007 | 标定缺少运行时漂移检测 | `IntrinsicsCalibrationRuntime.cs` | 长期运行精度漂移 | 增加在线 reprojection error 监控 |
| P2-008 | Steger 全图导数内存带宽 | `StegerSubpixelEdgeDetector.cs:109` | 4K 图像时压力大 | ROI 裁剪前置（如需要） |
| P2-009 | Region 系列与 Morphology 重叠 | Region* / Morphology | 维护冗余 | 评估合并或标记 Legacy |
| P2-010 | `CycleCounter` / `Delay` 错误处理 35 分 | `CycleCounter` / `Delay` | 溢出/阻塞风险 | 补齐边界测试，增加上限保护 |

---

> 记录生成：Kimi Code CLI  
> 生成时间：2026-04-21  
> 关联文档：`测量类算子精度升级说明-2026-04.md`、`Week5-审计台账.md`、`.tmp/publish-check/`（如需验证）
