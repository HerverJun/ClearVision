# ClearVision 视觉算子科学性与稳定性分析报告

## 1. 摘要

本报告对 ClearVision 仓库中 `ClearVision.Product.Infrastructure/Operators/` 下的视觉相关算子做**只读**数学与逻辑审查，聚焦：几何测量与拟合、工业卡尺、标定与单应、特征/模板匹配、边缘与形态学、数值稳定性与退化输入契约。

**总体判断**

| 维度 | 评价 | 说明 |
|------|------|------|
| 工程成熟度 | 较高 | 测量栈已沉淀 `MeasurementGeometryHelper`、`GeometryRefinementKernel`、`IndustrialCaliperKernel`、`CircleCaliperFitV2Kernel`、`HomographyVerificationHelper` 等共享内核，失败码/诊断/残差/置信度字段较完整 |
| 工业计量科学性 | 中高（分层） | 卡尺圆 V2、正交 IRLS 精化、N 点标定 RANSAC、特征匹配对称检验等接近工业惯例；霍夫圆/椭圆默认路径与部分代数圆拟合仍偏“检测/示意” |
| 数值稳定性 | 中等偏上 | 广泛使用 `1e-9`/`1e-12` 退化门限、`double.IsFinite`、中心化缩放圆拟合、MAD 鲁棒尺度；但 σ 启发式、线段点在段上 AABB 判定、椭圆距离近似仍有缺陷 |
| 逻辑/契约 | 中等 | 多处 `StatusCode`/`[NoFeature]`/`[DegenerateGeometry]` 明确；`Confidence` 多为经验映射而非统计置信区间；亚像素边缘算子已标注 Reference 生命周期 |

**最值得优先的纯数学/逻辑优化（摘要）**

1. **P0**：将 `GeometricFittingOperator.FitCircleLeastSquares` 从代数 Kasa 类升级为 **Taubin / Pratt + 几何正交精化**（复用已有 `GeometryRefinementKernel.RefineCircle`）。
2. **P0**：修正 `MeasurementGeometryHelper.IsPointOnSegment` 的 AABB 伪判定（非共线点也可被判“在线段上”）。
3. **P1**：统一圆/线默认路径：检测用霍夫 → 精化用正交+鲁棒损失；默认 `FitLoss`/`RefinementLoss` 从 Legacy/L2 切到 Huber。
4. **P1**：RANSAC 增加按内点率自适应迭代；内点选择后做局部最优重估。
5. **P2**：不确定性从“启发式分数位”升级为协方差传播 + 标定残差溯源，并标注 `Heuristic` vs `Propagated`。

---

## 2. 分析范围与方法

### 2.1 代码范围

- 主路径：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/`
- 约 **158** 个 `*Operator.cs`（含 Features 子目录、通信/流程类算子）
- 精读共享内核与代表算子：
  - 几何：`MeasurementGeometryHelper.cs`、`GeometryRefinementKernel.cs`、`GeometricFittingOperator.cs`
  - 测量：`CircleMeasurementOperator.cs`、`LineMeasurementOperator.cs`、`AngleMeasurementOperator.cs`、`PointLineDistanceOperator.cs`、`WidthMeasurementOperator.cs`
  - 卡尺：`IndustrialCaliperKernel.cs`、`CircleCaliperFitV2Kernel.cs`
  - 标定/匹配：`NPointCalibrationOperator.cs`、`HomographyVerificationHelper.cs`、`TemplateMatchOperator.cs`、`Features/FeatureMatchOperatorBase.cs`
  - 其他：`SubpixelEdgeDetectionOperator.cs`、`MeasurementStatisticsHelper.cs`

### 2.2 方法

- 只读扫描（Glob/Grep/Read），**未改代码、未跑测试**
- 评价准则：教科书/工业视觉惯例（正交距离拟合、鲁棒估计、亚像素边缘模型、单应退化与重投影、不确定性传播）
- 不覆盖：UI、PLC/串口通信正确性、ONNX 模型精度、真实产线验收证据

### 2.3 分析日期

2026-07-19

---

## 3. 算子库结构概览

### 3.1 目录与分层

```
Operators/
  *.cs                    # 绝大多数算子 + 共享 Helper/Kernel
  Features/               # ORB/AKAZE/形状匹配等
  DatabaseWrite/          # 非视觉
  ImageWrapper.cs         # 图像生命周期包装
```

视觉相关能力呈现 **“算子壳 + 共享 Kernel”** 结构，科学性主要取决于 Kernel。

### 3.2 按功能粗分

| 类别 | 代表 | 规模感 |
|------|------|--------|
| 几何测量/拟合/卡尺 | Circle/Line/Angle/Width/Gap/PointLine/CaliperFitV2 | 高（核心竞争力） |
| 标定与坐标 | NPoint/Camera/Fisheye/HandEye/PixelToWorld/Affine/Perspective | 中高 |
| 匹配与定位 | Template/ORB/AKAZE/Shape/Planar/PPF/Deformable | 中高 |
| 边缘/轮廓/Blob/形态学 | Canny/Subpixel/FindContours/Blob/Region* | 中 |
| 图像预处理 | Blur/Threshold/CLAHE/Color/FFT 等 | 中 |
| AI/OCR/码 | DeepLearning/OCR/Code/Anomaly/Semantic | 中（浅评） |
| 流程/通信/变量 | PLC/Modbus/Logic/Variable… | 非本报告重点 |

### 3.3 共享数学资产

| 组件 | 作用 |
|------|------|
| `MeasurementGeometryHelper` | 点线距、交点、角度、有限差分不确定度传播 |
| `GeometryRefinementKernel` | 圆/线正交残差 + Huber/Welsch IRLS + 近似协方差 |
| `IndustrialCaliperKernel` | 剖面双线性采样、高斯平滑、导数峰、二次峰偏移、极性对 |
| `CircleCaliperFitV2Kernel` | 径向卡尺圆：极性假设竞争、覆盖率/角覆盖、MAD 离群、精化门控 |
| `HomographyVerificationHelper` | RANSAC 单应 + 内点比 + 重投影 + 四边形面积/入界检验 |
| `MeasurementStatisticsHelper` | 分位/MAD/圆统计；**显式声明** Confidence 非统计 CI |

---

## 4. 科学性评估（按算子类别）

### 4.1 几何测量与拟合

#### 4.1.1 点线距离 / 线线距离 / 角度

**正确性**

- 点到无限线：标准 `ax+by+c` 形式 + 范数归一，`DistancePointToInfiniteLine` 正确。
- 点到线段：投影参数 `t` clamp 到 `[0,1]`，`ProjectPointToSegment` 正确。
- 三点角：`acos(clamp(dot))` + 臂长退化检查，符合数值安全实践。
- 两线夹角：取 **无向角**（`Abs(dot)` → `[0,90°]`），与工业“两线锐角夹角”一致；`AngleMeasurementOperator` 在线模式下走无向角是有意设计。

**风险**

1. **`IsPointOnSegment` 使用轴对齐包围盒**（`MeasurementGeometryHelper` 约 L300–307）：仅检查点是否在线段端点 AABB 内，**未验证共线**。
   - 后果：`TryGetSegmentIntersection` 可能误判段交点；`DistanceSegmentToSegment` 可能错误返回 0。
   - 修复：共线判据 + 参数 `t∈[0,1]`（与 `ProjectPointToSegment` 同一套）。

2. **角度不确定度** `PropagateThreePointAngleUncertaintyDegrees` 使用 `σ_θ ≈ sqrt((σ1/L1)^2+(σ2/L2)^2)`，忽略相关与夹角本身；小夹角/近共线时偏乐观。应改用同文件已有的 `PropagateCoordinateUncertainty`。

3. **点/线 σ 启发式** `EstimatePointSigma`：有小数分量 → 0.05 px，否则 0.5 px。适合默认先验，不是计量溯源。

#### 4.1.2 几何拟合 `GeometricFittingOperator`

- 轮廓 4× 上采样再二值提取 → 合理降低量化影响。
- 直线：`Cv2.FitLine` + `Atan2` → 标准。
- 椭圆：`Cv2.FitEllipse` + 一阶几何距离近似 `|F|/||∇F||` → RANSAC 可用，非精确几何距离。
- **圆**：`FitCircleLeastSquares` 为质心平移 + 尺度归一后的 **代数圆拟合（Kasa 族）**。
  - 优点：改善条件数；退化 `scale < 1e-9` 有处理。
  - 偏差：代数距离对弧段有系统半径偏置（Chernov 等）。应升级 Taubin/Pratt，或复用已有 **`GeometryRefinementKernel.RefineCircle`**（当前未贯通）。

#### 4.1.3 直线测量 `LineMeasurementOperator`

- FitLoss：L2 / Huber / Welsch；非 L2 失败 **不静默回退** → 契约清晰。
- 输出 `MeasurementEvidence` + `HeuristicUncertainty` flags → 诚实。
- 固定 Canny(50,150) 偏经验；可改为自适应阈值。

#### 4.1.4 圆测量 `CircleMeasurementOperator`

| 路径 | 定位 | 评价 |
|------|------|------|
| HoughCircle | 检测/粗定位 | 不宜作精密默认 |
| FitEllipse | 轮廓近似圆 | 非径向卡尺 |
| **CaliperFitV2** | 工业卡尺圆 | **推荐计量路径** |

默认 Method 仍是 `HoughCircle`：从产品默认科学性角度偏弱——建议文档与默认值指向 CaliperFitV2（需现场 ROI/标称半径参数）。

#### 4.1.5 宽度测量 `WidthMeasurementOperator`

- 平行边多扫描 + Robust 离群剔除（σ·k）+ 分位统计（Mean/Min/Max/P95/Std）→ 工业宽度测量常见形态。
- 科学性取决于 AutoEdge 边缘定位是否亚像素与是否垂直于中线；若边缘来自像素梯度峰值，宽度仍受约 0.5 px 量化限制。

### 4.2 工业卡尺内核

#### `IndustrialCaliperKernel`

**优点**

1. 沿扫描方向参数 `t∈[0,1]`，法向平均厚度 → 符合卡尺工具惯例。
2. 双线性灰度采样 → 亚像素剖面。
3. 高斯平滑 + 一阶导数 + 局部极大 + **二次插值峰偏移** `QuadraticPeakOffset` → 经典亚像素边缘位置。
4. 极性 DarkToLight / LightToDark 与 pair 方向过滤 → 工业边缘对宽度测量必需。
5. 阈值：`median + 2·MAD`（scaled）→ 比固定阈值稳健。

**可优化**

- 导数为离散差分，未与高斯 σ 联合为 **高斯导（Canny 式）**。
- 未显式输出边缘 **曲率/置信度**（模板匹配有 PeakCurvature，卡尺可对称）。
- `acrossCount = ceil(thickness)` 为均匀采样平均，可改为横向高斯窗。

#### `CircleCaliperFitV2Kernel`

- Auto 极性双假设评分 + **不可区分则 AmbiguousEdge 失败** → 避免错误极性静默成功，科学性优秀。
- `MinAngularCoverageDegrees` / `MinCoverageRatio` → 防止弧段过短导致圆心病态。
- OutlierMode Mad/Huber + MaxResidualRmse 门控 → 符合鲁棒几何估计流程。
- 失败码枚举完整（InvalidInput / InsufficientEdges / DegenerateFit …）。

建议：默认 `RefinementLoss` 为 `Legacy` 时文档标明“兼容路径非最优”；产品默认宜 Huber。

### 4.3 标定与坐标变换

#### N 点标定 `NPointCalibrationOperator`

- Affine / Perspective + RANSAC 重投影阈值、置信度、迭代上限、内点比、最大可接受误差 → **工业标定门禁完整**。
- 点集 `TryValidatePointSet`（校验共线/退化）在 Validate 阶段调用 → 正确。
- 输出区分 Inlier / AllSample 重投影误差与 Scope → 避免混淆。

#### 单应验证 `HomographyVerificationHelper`

- `FindHomography` RANSAC + 内点数/比 + 均值/峰值重投影 + 投影四边形面积比与入界 → 显著优于“只看匹配分数”。
- `allowCenterOnlyProjection` 有严格门槛 → 可控的宽松模式。

**优化可能**：条件数/`det(H)` 符号与幅值门控，降低退化/反射误通过。

#### 相机/鱼眼/手眼

存在 `CameraCalibrationOperator`、`Fisheye*`、`HandEyeCalibration*` 等；本报告未逐行精读 OpenCV 标定标志与棋盘逻辑。工业上需确认：畸变模型与下游 `Undistort`/`PixelToWorld` 的 **内参约定一致**、手眼 eye-in-hand / eye-to-hand 轴约定文档化。

### 4.4 匹配与定位

#### 模板匹配 `TemplateMatchOperator`

- 多方法（CCoeffNormed 等）、Gray/Edge/Gradient 域、姿态角/尺度搜索、金字塔、IoU-NMS、**亚像素峰值偏移与 PeakCurvature** → 高于朴素封装。
- 分数阈值与 NormalizedScore/RawResponse 分离 → 契约清晰。

科学限制：相关峰亚像素在平坦峰或噪声峰时偏差；曲率可作为拒绝准则（已有输出，应在默认门控使用）。

#### 特征匹配 `FeatureMatchOperatorBase`

- Lowe ratio + **对称（交叉）检验** → 标准优良实践。
- Hamming + BFMatcher 适合 ORB/AKAZE 二进制描述子。
- 单应侧应始终配合 `HomographyVerificationHelper`。

### 4.5 边缘 / 轮廓 / 形态学 / 滤波

- 常规 OpenCV 封装（Canny、形态学、阈值、高斯等）科学上无争议，参数单位与 OpenCV 一致。
- `SubpixelEdgeDetectionOperator`：**Lifecycle = Reference**，元数据明确“非工业定型，计量前需验证”——诚实且正确。
- 形态学：存在 `MorphologyExecutionHelper` 统一执行——有利于避免重复错误。

### 4.6 OpenCV 调用与坐标系约定

- 图像坐标：**x 右 y 下**，与 OpenCV 一致；角度多用 `Atan2(vy,vx)*180/π`，线方向归一化到约 `[-90°,90°)`（`NormalizeLineDirectionDegrees`）。
- 颜色：`BGR2GRAY` 符合 OpenCV 默认 Mat 通道序。
- 椭圆角：依赖 `RotatedRect.Angle` OpenCV 约定——下游若按“主轴角”解释需统一文档。
- 直线 `LineData` 用起终点浮点存储：有利于亚像素；但 `float` 与 `double` 混用时有截断（不确定度传播里强制 float 构造 `LineData`）。

---

## 5. 数值稳定性与边界条件

### 5.1 已有良好实践

| 实践 | 位置 |
|------|------|
| 范数/长度 `< 1e-9` 退化失败 | 几何距离、角度、投影 |
| `double.IsFinite` 校验 | 点/线解析、精化发散 |
| `Math.Clamp` 对 cos 避免 acos NaN | 夹角 |
| 圆拟合中心化+尺度归一 | `FitCircleLeastSquares` |
| 法方程主元选择 + 奇异失败 | `GeometryRefinementKernel.Solve` |
| MAD×1.4826 鲁棒尺度 | 统计 Helper / 精化 / 卡尺阈值 |
| 有限差分步长 `max(1e-4, |x|·1e-4)` | 不确定度传播 |
| 卡尺取消令牌与采样预算 | `IndustrialCaliperKernel` |
| 圆卡尺采样工作量上限 | `MaxSamplingWorkUnits` |

### 5.2 残余风险

1. **代数圆偏差**（弧段、偏心噪声）→ 系统误差。
2. **线段交点 AABB 判定** → 逻辑错误风险高于数值误差。
3. **协方差**：`CircleCovariance` 为加权法方程逆 × σ²，未做有限样本校正；`LineCovariance` 假设角/偏移对角。Evidence 中 `UncalibratedCovariance` 标注正确。
4. **Confidence = 1/(1+u)**：单调合理，**非**覆盖概率；需防止 UI 展示为“95% 置信”。
5. **RANSAC 迭代固定**：未按内点率自适应，极端污染率下可能不足。
6. **三点定圆** `a` 接近 0 时用 `1e-6` 绝对阈值：大坐标时宜相对尺度。
7. **高斯消元** 3×3 可接受；更高维建议 QR/SVD。

---

## 6. 逻辑与契约问题

| 问题 | 说明 | 建议 |
|------|------|------|
| 默认圆路径霍夫 | 易被当成精密测量 | 默认 CaliperFitV2 或强制显示“检测级” |
| 几何拟合圆未走正交精化 | 与 Line/Circle 测量精化内核分裂 | 统一调用 `GeometryRefinementKernel` |
| 线段“点在段上”AABB | 交点/段距逻辑错误 | 共线+参数域 |
| Confidence 语义 | 经验分数 | 契约字段拆分 `Score` / `Sigma` / `CI` |
| 亚像素边缘 Reference | 已标注 | 流程中禁止默认接入计量链 |
| 角度线模式无向角 | 与三点有向角不同 | 文档/端口说明必须写清 |
| 宽度/间隙 Unit 常仅 Pixel | 与标定链路脱节时用户误读 | 强制绑定 CalibrationBundle 或拒绝 mm 输出 |

失败模式方面，测量算子普遍使用 `[NoFeature]` / `[DegenerateGeometry]` / `[InvalidParameter]`、`StatusCode`、圆卡尺细粒度 `FailureCode`——良好契约，应推广。

---

## 7. 纯数学/逻辑优化建议

### P0（正确性 / 计量偏差）

1. **修复 `IsPointOnSegment`**
   - 文件：`MeasurementGeometryHelper.cs`
   - 使用：共线检验 + `t∈[0,1]`（与 `ProjectPointToSegment` 同一套）。
   - 回归：斜线段附近点、平行近交、端点容差。

2. **圆代数拟合升级 + 复用正交精化**
   - 文件：`GeometricFittingOperator.cs`、`GeometryRefinementKernel.cs`
   - 流程：Taubin/Pratt 初值 → `RefineCircle(..., Huber)` → 残差/协方差。
   - 同步让 `CircleMeasurement` 非 V2 路径在候选圆上可选精化。

3. **三点角不确定度改有限差分**
   - 复用 `PropagateCoordinateUncertainty`，替换臂长近似公式。

### P1（稳定性 / 与工业惯例对齐）

4. **统一默认鲁棒损失**：线/圆精化默认 Huber；Welsch 作高污染可选项。
5. **RANSAC 自适应迭代** + 内点再估计（可加 LO-RANSAC）。
6. **椭圆残差**：近边界用当前一阶近似；远点或高离心率改 **Sampson 距离** 或迭代几何距离。
7. **卡尺导数**：高斯核与导数一体（尺度空间一致），横向加权平均。
8. **霍夫圆默认**：Studio 新建节点默认 Method=CaliperFitV2，或向导收集 NominalRadius。
9. **单应**：增加 `det(H)` 符号/幅值与条件数门控。
10. **σ 输入端口**：允许上游亚像素算子注入 `PointSigma`，替代分数位启发式。

### P2（严谨计量与可证明优化）

11. 协方差：完整参数相关、自由度校正、可选 bootstrap。
12. 圆/椭圆 **外点混合模型**（Tukey biweight）。
13. 角度用 `atan2` 叉积形式得到有符号角，提供 `AngleMode=Signed|Unsigned`。
14. 宽度测量与标定：输出同时带 px 与 world，误差传播串联标定 RMSE。
15. 将 `Confidence` 重命名或并列 `EmpiricalScore`，避免监管/计量语境误解。
16. 几何精化 Gauss–Newton 加 **阻尼（LM）** 与线搜索。
17. 椭圆拟合自实现正交精化（OpenCV FitEllipse 对噪声敏感时）。

---

## 8. 做得好的实践（肯定清单）

1. **测量内核集中化**，避免多处复制点线公式。
2. **`GeometryRefinementKernel`**：正交圆残差雅可比、Huber/Welsch 权重常数（1.345 / 2.9846）符合稳健统计惯例。
3. **`CircleCaliperFitV2`**：极性竞争、角覆盖、离群、残差门控、诊断与 evidence 合同版本化。
4. **不确定度有限差分框架** + 多种 gap/距离传播封装。
5. **`MeasurementStatisticsHelper` 明确 Confidence 非 CI**。
6. **特征匹配 ratio + 对称检验**；**单应多维验证**。
7. **N 点标定** 内点/全体误差分离与严格 Validate。
8. **线测量 FitLoss 失败不静默回退**。
9. **亚像素边缘 Reference 生命周期** 诚实标注。
10. **模板匹配** 亚像素峰与曲率输出，利于后续逻辑门控。
11. 广泛 `IsFinite`/退化码，利于流水线排错。
12. 轮廓上采样再拟合，降低离散化偏差的工程意识正确。

---

## 9. 结论与优先路线图

### 9.1 结论

ClearVision 视觉算子库在 **工业测量方向已具备可观的科学内核**（卡尺剖面、鲁棒正交精化、标定门禁、单应验证、证据字段），明显优于“仅 OpenCV 一行调用”的封装库。

主要短板：

1. **部分默认路径仍停留在检测级**（霍夫圆、代数圆拟合）；
2. **少量几何谓词错误/过松**（线段点判定）；
3. **不确定性多为启发式**，与真正计量溯源还有距离；
4. **内核能力未完全复用**（精化 Kernel 未贯通 GeometricFitting）。

这些问题多为**可局部修补的数学/逻辑问题**，不需要推倒重来。

### 9.2 建议路线图

| 阶段 | 目标 | 关键动作 |
|------|------|----------|
| 短期（1–2 周） | 消灭逻辑错误 + 圆偏差 | 修 `IsPointOnSegment`；几何拟合圆接 `RefineCircle`；单测覆盖斜线交点/弧段圆 |
| 中期（1 月） | 默认路径工业级 | 圆测量默认/推荐 CaliperFitV2；线默认 Huber；RANSAC 自适应；σ 端口 |
| 长期 | 计量可信 | 协方差标定、px↔world 串联、证据字段进报表、真机 Gauge R&R（需硬件，非纯数学） |

### 9.3 质量边界声明

本报告评估的是**算法与逻辑科学性**，不构成产线验收。公开数据集、半合成样张或仿真**不能**替代真实工况计量确认（与项目 `CLAUDE.md` 中质量 vs 证据成熟度原则一致）。

---

## 附录 A：关键文件索引

| 路径 | 角色 |
|------|------|
| `.../Operators/MeasurementGeometryHelper.cs` | 几何基元与不确定度 |
| `.../Operators/GeometryRefinementKernel.cs` | 圆/线正交 IRLS |
| `.../Operators/IndustrialCaliperKernel.cs` | 卡尺剖面与边缘 |
| `.../Operators/CircleCaliperFitV2Kernel.cs` | 卡尺圆 V2 |
| `.../Operators/GeometricFittingOperator.cs` | 轮廓拟合 + 代数圆 |
| `.../Operators/CircleMeasurementOperator.cs` | 圆测量多路径 |
| `.../Operators/LineMeasurementOperator.cs` | 线测量 + 证据 |
| `.../Operators/AngleMeasurementOperator.cs` | 角度 |
| `.../Operators/PointLineDistanceOperator.cs` | 点线距 |
| `.../Operators/NPointCalibrationOperator.cs` | N 点标定 |
| `.../Operators/HomographyVerificationHelper.cs` | 单应验证 |
| `.../Operators/Features/FeatureMatchOperatorBase.cs` | 特征匹配基类 |
| `.../Operators/TemplateMatchOperator.cs` | 模板匹配 |
| `.../Operators/MeasurementStatisticsHelper.cs` | 统计与 Confidence |
| `.../Operators/SubpixelEdgeDetectionOperator.cs` | 参考级亚像素边缘 |

## 附录 B：分析元数据

- **模式**：只读分析，未修改仓库代码
- **日期**：2026-07-19
- **仓库**：`c:\Users\HerverJun\Desktop\ClearVision`
- **局限**：未运行单元测试/基准；AI/OCR/3D(PPF) 仅结构级涉及；标定 OpenCV 参数矩阵未全量公式核验

---

*报告结束*
