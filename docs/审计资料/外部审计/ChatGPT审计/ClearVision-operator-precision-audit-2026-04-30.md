---
title: "ClearVision 算子库准工业精度审计报告"
doc_type: "audit-report"
status: "reviewed"
created: "2026-04-30"
repository: "HerverJun/ClearVision"
branch: "main"
auditor: "GPT-5.5 Pro"
---

# ClearVision 算子库准工业精度审计报告

## 1. 审计结论

本次审计基于 `HerverJun/ClearVision` 仓库 `main` 分支中可读取的源码、质量矩阵、public benchmark 报告、field replay 分层记录、A/B replay 报告和工业 gate 文档完成。

总体结论如下：

> **不能笼统宣布“整个算子库已经达到准工业级精度”。更准确的判断是：准工业级证据治理已经基本建成，部分核心算子在受控场景下接近准工业可试用；但全库精度证据和真实产线闭环尚未达到准工业级，更未达到完整工业级。**

当前仓库已经具备比较完整的质量治理框架：155 算子质量矩阵、public benchmark proof、field replay 分层、A/B replay、audit suite、隐私路径检查、性能 smoke 报告和工业 gate 编排均已存在。新的准工业审计报告显示 57/57 项检查通过，说明“证据治理链路”已经明显成熟。

但是，现有证据仍存在关键边界：

- `real industrial validation complete = 0`。
- `real-field` 样本当前为 0。
- 155 个算子的 Industrial Status 仍全部是“功能可用但未完成现场工业验证”。
- Core20 proof baseline 中 20 个核心算子仍为 `blocked-missing-field-data`。
- 大量算子的精度 claim 仍停留在 contract-only 或 golden-only 层级。

因此，建议当前对外口径使用：

> **“ClearVision 算子库已具备准工业级证据治理能力，部分核心算子具备受控场景准工业试用条件；全库真实工业精度仍需 real-field 数据和产线 sign-off 闭环。”**

不建议使用：

> “155 个算子均已达到准工业级精度”  
> “算子库已完成工业级验证”  
> “真实产线精度已闭环”

---

## 2. 审计范围与限制

### 2.1 审计范围

本次审阅覆盖以下材料类型：

1. 算子实现源码：
   - 模板匹配、形状匹配、平面匹配、特征匹配、卡尺测量、深度学习推理、亚像素边缘等核心实现。
2. 质量证据材料：
   - 155 算子质量矩阵。
   - operator quality evidence manifest。
   - Core20 proof baseline。
   - public benchmark proof baseline。
   - field replay 分层记录。
   - quasi-industrial audit 报告。
   - A/B replay 报告。
3. 工业 gate 与性能材料：
   - operator-library industrial gate 文档。
   - operator performance benchmark current/trend 报告。

### 2.2 审计限制

本次审计存在以下限制：

1. 未在本地重新执行全量测试、benchmark 或 replay suite。
2. 未直接读取真实产线图片、标注文件或客户 sign-off 文件。
3. 当前环境无法联网，因此没有实时检索 2025 年 8 月之后的外部最新 SOTA 论文、开源实现或商业产品变更。
4. “当前最优算法/成熟工业实现”的对标依据为截至 2025 年 8 月的通用工业视觉实践，以及仓库内已有实现与报告。
5. 本报告重点评估“精度证据是否足以支撑准工业级声明”，不是一次完整的安全、性能、稳定性、UI、部署、许可或商业化审计。

---

## 3. 总体评级

| 维度 | 评级 | 审计判断 |
|---|---:|---|
| 证据治理 / 可审计性 | A- | 质量矩阵、证据分层、suite、A/B replay、public benchmark、隐私检查和 audit gate 已经比较完整。 |
| 算法实现成熟度 | B / B+ | 卡尺、模板匹配、特征匹配、标定、YOLO runtime 都有工程化增强，但多处仍是成熟 baseline，不是顶级工业算法。 |
| 精度证据强度 | C+ / B- | 少数核心算子有 public benchmark 或 field-substitute 证据，大量算子仍是 contract-only/golden-only。 |
| 真实产线闭环 | 未达标 | 当前报告明确显示 real industrial validation complete = 0，real-field 样本为 0。 |
| 准工业声明可用性 | 有条件可用 | 可声明“准工业证据治理已建立”和“部分核心算子可受控试用”，不宜声明“全库已达准工业精度”。 |

---

## 4. 证据体系审计

### 4.1 正向发现

仓库已经形成了较强的证据治理体系：

- `quality/evals/reports/operator_quality_matrix.md` 记录 155 个算子的证据矩阵。
- `docs/operator-quality/operator_quality_evidence_manifest.md` 定义 Contract、Golden、Dataset、Field replay、Precision Claim、Industrial Status 等证据层级。
- `quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.md` 显示准工业审计 57/57 项检查通过。
- `quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.md` 记录 10 个算子的 public benchmark/golden proof。
- `quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.md` 记录算法 A/B replay，183 个 case 执行，fixed cases = 29，regressed = 0。
- `docs/审计资料/报告/field-replay证据分层记录-2026-04-29.md` 明确区分 `field-substitute`、`internal-lab`、`real-field`。
- `docs/validation/operator-library-industrial-gate.md` 定义一键 industrial gate，包含 smoke、measurement、calibration、detection、plc 等多类 gate。

这些材料说明：项目已经从“算法功能实现”推进到了“可审计、可回归、可分层声明”的阶段。

### 4.2 主要缺口

证据体系的最大问题不是没有框架，而是证据强度还不够：

- 当前 `real-field` 样本为 0。
- 当前 `real industrial validation complete` 为 0。
- Core20 proof baseline 中 20 个核心算子全部 blocked，原因是 missing field data。
- 155 个算子中，Dataset 证据覆盖不足，Field replay 覆盖严重不足。
- 部分 public benchmark 验收阈值较低，能证明 runner 和流程可跑，但不足以证明工业精度。
- field-substitute replay 可证明 replay 机制和 triage 流程，但不能代替真实产线数据。

### 4.3 证据口径建议

建议把证据等级统一为以下对外口径：

| 证据等级 | 可声明内容 | 不可声明内容 |
|---|---|---|
| Contract | API、参数、错误处理、输入输出契约可用 | 精度达标、现场可用 |
| Golden | synthetic oracle 或固定回归场景通过 | 真实工况稳定 |
| Dataset | public/licensed/curated 数据集上达到阈值 | 客户产线闭环 |
| Field-substitute | 替代现场失败模式可 replay、可 triage、可 regressionize | real-field sign-off |
| Real-field | 脱敏真实现场样本、来源、授权、复现命令、triage、回归闭环齐备 | 若未覆盖多线体/多批次，不宜泛化到所有工业场景 |
| Line sign-off | 产线验收、稳定运行、客户/线体确认 | 泛化到未验证行业和设备 |

当前仓库最强口径应停留在：

> public benchmark + field-substitute + A/B replay 支撑的 quasi-industrial claim。

不应升级为：

> real industrial validation complete。

---

## 5. 算法源码对标审计

## 5.1 CaliperTool / IndustrialCaliperKernel

### 源码路径

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/CaliperToolOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/IndustrialCaliperKernel.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/ImageProcessing/SubPixelEdgeDetector.cs`

### 正向发现

CaliperTool 是当前库中最接近准工业可试用的一类算子。

实现上已经包含：

- ROI scan line / band profile 采样。
- averaging thickness。
- oversampling。
- 双线性采样。
- Gaussian smoothing。
- 导数峰值边缘检测。
- MAD 自适应阈值估计。
- 边缘极性过滤。
- edge pair 构造。
- 二次峰值插值实现 subpixel edge position。
- 距离、标准差、不确定度、sample pitch 等诊断输出。

这些设计明显优于简单像素级边缘检测，已经接近工业卡尺工具的基础结构。

### 与成熟工业实现的差距

与 HALCON、Cognex、Keyence 等成熟工业测量工具相比，当前实现仍有明显缺口：

1. 缺少多卡尺阵列后的鲁棒几何拟合。
2. 缺少 RANSAC/Huber/Tukey 等离群边缘剔除机制。
3. 缺少线、圆、弧、宽度的统一几何模型约束。
4. 缺少测量系统分析，即 GR&R、repeatability、reproducibility、温漂、光照漂移评估。
5. 缺少真实量具标定链路下的毫米/微米级误差闭环。
6. `SubPixelEdgeDetector` 明确说明 gradient-moment path 不是真正的 Zernike 实现，只是 lightweight first-order gradient moment。

### 评级

| 项目 | 评级 |
|---|---:|
| 受控场景功能可用性 | A- |
| 亚像素边缘工程实现 | B+ |
| 工业测量完整闭环 | B- / C+ |
| 准工业可试用性 | 有条件达到 |

### 审计结论

CaliperTool 可以作为“受控场景准工业试用”算子推进，尤其适合清晰边缘、固定光照、固定 ROI、低形变的宽度/边缘对测量场景。

但在没有真实量具标定、GR&R 和多批次产线数据之前，不建议声明其已经达到完整工业级测量精度。

---

## 5.2 TemplateMatchOperator

### 源码路径

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/TemplateMatchOperator.cs`

### 正向发现

TemplateMatchOperator 已经不是裸 `Cv2.MatchTemplate`：

- 支持 Gray、Edge、Gradient 等匹配域。
- 支持 ROI。
- 支持 mask。
- 支持不同 OpenCV template matching method。
- 包含低纹理拒绝。
- 有 score normalization。
- 支持 top-k candidate。
- 有 flood-fill suppression 和 IoU NMS。
- 输出候选框、分数、中心、区域等结构化结果。

这些设计使其适合固定尺度、低旋转、模板外观稳定的产线定位场景。

### 与成熟工业实现的差距

当前实现仍属于增强版灰度/边缘模板匹配，不是完整工业 shape-based matching：

1. 缺少显式旋转搜索。
2. 缺少尺度金字塔/多尺度模板库。
3. 缺少梯度方向 shape model。
4. 缺少遮挡鲁棒性建模。
5. 缺少亚像素响应峰值拟合。
6. 缺少多模板、多姿态、多实例工业部署策略。
7. OperatorMeta 自身也把适用范围限制在 fixed-scale、low-rotation scenes。

### 证据情况

public/golden proof 中 TemplateMatching 在 hpatches-style homography bridge 上表现很好，P95PositionErrorPx 和 MeanPositionErrorPx 都为 0。但该结果更能证明受控 bridge/golden case 上定位稳定，不能直接外推到真实产线中存在的光照变化、污渍、磨损、遮挡、尺度漂移和旋转变化。

### 评级

| 项目 | 评级 |
|---|---:|
| 固定尺度低旋转匹配 | A- |
| 通用工业定位 | B- |
| 抗遮挡/抗形变能力 | C+ |
| 准工业可试用性 | 有条件达到 |

### 审计结论

TemplateMatchOperator 在固定尺度、低旋转、模板稳定场景中可按准工业试用推进。若要支撑通用工业定位精度，需要增加旋转/尺度搜索、shape-based matching、响应面亚像素拟合和真实场景 replay。

---

## 5.3 PlanarMatching / AKAZE / ORB Feature Match

### 源码路径

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PlanarMatchingOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/HomographyVerificationHelper.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/Features/FeatureMatchOperatorBase.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/Features/AkazeFeatureMatchOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/Features/OrbFeatureMatchOperator.cs`

### 正向发现

Planar/feature matching 链路具备成熟 baseline 的完整结构：

- ORB/AKAZE/BRISK 特征。
- Lowe ratio test。
- 双向一致性检查。
- cross-check。
- RANSAC homography。
- inlier ratio 验证。
- reprojection error 验证。
- 投影四边形有效性检查。
- 面积比例检查。
- 中心点可见性检查。
- 多尺度候选。

`HomographyVerificationHelper` 的实现尤其值得肯定：它不仅检查 inlier 数量，还检查 inlier ratio、mean/max reprojection error、投影四边形凸性、自交、面积比例和图像边界内可见性。

### A/B 改进

A/B replay 报告显示：

- 183 个 replay case 已执行。
- fixed cases = 29。
- regressed = 0。
- AKAZE replay old pass 从 0 提升到 0.65。
- ORB replay old pass 从 0 提升到 0.8。

这说明 Codex/近期改动确实修复了部分历史失败场景，并且没有明显 replay 回归。

### 与成熟工业实现的差距

尽管工程链路完整，但与当前成熟工业/先进匹配方案相比还有差距：

1. AKAZE/ORB 是传统 baseline，不是强纹理/弱纹理/大视角变化下的最优选择。
2. 对 viewpoint 大变化、重复纹理、低纹理、反光场景鲁棒性有限。
3. 缺少 learned local features 或 transformer matching 类方法。
4. 缺少多假设 pose 验证和局部一致性过滤。
5. public benchmark 中 AKAZE/ORB 的 P95PositionErrorPx 很高，说明定位误差尾部仍大。

### 评级

| 项目 | 评级 |
|---|---:|
| 工程完整性 | A- |
| 传统特征 baseline 成熟度 | B+ |
| 高精度工业定位 | C+ / B- |
| 准工业可试用性 | 部分达到 |

### 审计结论

Feature/PlanarMatching 链路已经是合格工程 baseline，A/B replay 改进明显。但 public benchmark 的尾部定位误差仍偏大，不能按“工业高精定位”声明。建议将其定位为“受控纹理/平面目标准工业试用”，而不是“通用工业定位最优算法”。

---

## 5.4 DeepLearningOperator

### 源码路径

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/DeepLearningOperator.cs`
- `quality/tools/DeepLearningCocoImageInferenceRunner/Program.cs`
- `quality/tools/DeepLearningDetectionDatasetRunner/Program.cs`
- `quality/tools/DeepLearningRuntimeBenchmarkRunner/Program.cs`

### 正向发现

DeepLearningOperator 的 runtime 工程外壳比较完整：

- ONNX Runtime 推理。
- YOLOv5/v6/v8/v11 适配。
- model cache。
- CUDA/TensorRT/GPU fallback。
- letterbox 预处理。
- NMS。
- label contract 校验。
- TargetClasses 校验。
- provider fallback diagnostics。
- model provenance 输出。
- generated smoke fixture 机制。

这说明深度学习算子的运行框架已经具有较强工程化基础。

### 主要问题

当前精度证据不支持准工业级声明：

- public benchmark 中 DeepLearning 在 COCO2017 20 cases 上 AP50 = 0、PrecisionAt50 = 0、RecallAt50 = 0。
- 验收阈值也是 AP50 >= 0、PrecisionAt50 >= 0、RecallAt50 >= 0，门槛过低。
- A/B replay 中 DeepLearning real-model case 没有体现有效检测精度改善，只证明 processing-error 为 0。

这说明当前 DeepLearning 证据主要证明“pipeline 可以执行”，不是证明“检测精度达标”。

### 评级

| 项目 | 评级 |
|---|---:|
| Runtime 工程化 | A- |
| 模型精度证据 | D / C- |
| 准工业可试用性 | 未达到，除非限定为 runtime smoke |

### 审计结论

DeepLearningOperator 可以声明“runtime 工程框架已成型，真实模型推理链路可执行”，但不能声明“深度学习检测精度准工业级”。需要补充真实模型、真实标注、非零 AP/Recall/Precision 阈值、失败模式 replay 和产线样本闭环。

---

## 5.5 SurfaceDefectDetection / AnomalyDetection / EdgeDetection

### 相关证据路径

- `quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.md`
- `quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.md`

### 正向发现

这些算子已有 public benchmark proof 或 replay 证据：

- SurfaceDefectDetection：KolektorSDD2，1004 cases。
- AnomalyDetection：MVTec AD Lite，120 cases。
- EdgeDetection：BSDS500，200 cases。

这些公开数据集/协议桥接证据比单纯 contract/golden 更强，说明项目已经开始进入数据集级评估。

### 主要问题

当前指标仍偏弱：

- SurfaceDefectDetection 的 PixelF1 偏低。
- AnomalyDetection 的 ImageAuroc / PixelAuroc 偏低。
- EdgeDetection 的 BoundaryF1 处于基础 baseline 级别。
- A/B replay 中仍有 low-contrast miss、below-threshold miss、precision/recall 下滑等问题。

这些指标可以用于 smoke/proof，但不足以支撑“工业缺陷检测精度达标”。

### 评级

| 算子 | 当前判断 |
|---|---|
| SurfaceDefectDetection | public benchmark 已接入，但缺陷像素级指标和低对比稳定性不足。 |
| AnomalyDetection | anomaly proof 初步建立，但真实异常检出能力不足。 |
| EdgeDetection | baseline 可用，但离工业边缘稳定性、亚像素和低误检仍有差距。 |

### 审计结论

这些算子适合声明“公开数据集 smoke/proof 已建立”，不适合声明“工业缺陷检测/异常检测精度已达标”。

---

## 6. 性能与工业 Gate 审计

### 6.1 正向发现

`docs/validation/operator-library-industrial-gate.md` 定义了一键工业 gate，包含：

- operator-library-smoke。
- measurement-regression。
- measurement-accuracy。
- measurement-stability。
- measurement-performance。
- calibration。
- detection-regression。
- detection-performance。
- plc。

同时，gate 会通过 serial runner 执行 .NET 测试，并解析 TRX，防止空测试、缺失 TRX、失败计数、执行数不足等问题。

这是一项重要工程进展，有助于避免“测试命令成功但实际没跑”的假阳性。

### 6.2 当前性能证据

当前 operator performance benchmark 是 smoke 级别：

| Operator | Mean ms | P95 ms | 备注 |
|---|---:|---:|---|
| CaliperTool | 1.445 | 1.555 | measurement case |
| EdgeDetection | 1.818 | 1.846 | edge case |
| MeanFilter | 1.012 | 1.404 | preprocess case |
| TranslationRotationCalibration | 0.220 | 0.285 | calibration geometry case |

这些结果说明 smoke 场景性能正常，但还不足以支持 release 级性能声明。

### 6.3 主要缺口

- 当前 trend 报告显示 baseline not provided。
- 单次 local smoke run 不是 release conclusion。
- 缺少固定硬件、固定数据规模、固定分辨率、固定 runtime provider 的长期趋势。
- 缺少 P99、内存峰值、GC、并发、温度/负载、长时间运行稳定性。

### 审计结论

性能 gate 已有雏形，但当前性能证据仍是 smoke 级别。建议将性能声明限制为“smoke 性能通过”，不要升级为“工业稳定性能达标”。

---

## 7. 主要风险清单

| 风险编号 | 风险 | 严重度 | 说明 | 建议 |
|---|---|---:|---|---|
| R1 | 证据过度声明 | 高 | field-substitute/public benchmark 被误读为 real-field sign-off。 | 所有报告保留 Claim Boundary，不允许自动升级 IndustrialStatus。 |
| R2 | 真实现场样本缺失 | 高 | real-field = 0，Core20 blocked-missing-field-data。 | 建立脱敏真实现场数据包和授权/来源/manifest。 |
| R3 | DeepLearning 精度阈值过低 | 高 | AP50/Precision/Recall 阈值为 0，不能证明检测有效。 | 设置非零阈值，并要求真实标注集。 |
| R4 | 特征匹配尾部误差过大 | 中高 | AKAZE/ORB P95PositionErrorPx 较高。 | 收紧重投影误差、按场景分层评估，引入更强特征或 learned matching。 |
| R5 | 卡尺缺少 MSA/GR&R | 中高 | 当前有亚像素和不确定度，但没有量具系统闭环。 | 加入标准件、重复性、再现性、温漂和光照漂移测试。 |
| R6 | TemplateMatching 泛化受限 | 中 | 固定尺度低旋转场景可用，但不适合复杂姿态。 | 增加旋转/尺度搜索、shape-based matching、响应峰值亚像素拟合。 |
| R7 | Dataset 覆盖不足 | 中高 | 155 算子中大量仍是 contract/golden-only。 | 优先补 Core20 和高风险算子的 dataset/field replay。 |
| R8 | 性能证据不足 | 中 | 当前是 smoke 性能，没有长期 baseline。 | 建立 pinned hardware baseline、P95/P99、内存、并发、长稳趋势。 |

---

## 8. 是否达到“准工业级精度”的分项判定

| 范围 | 判定 | 说明 |
|---|---|---|
| 证据治理体系 | 达到 | 质量矩阵、证据 manifest、audit suite、A/B replay、public benchmark、field replay 分层均已建立。 |
| CaliperTool 受控测量场景 | 有条件达到 | 实现接近工业卡尺基础结构，但缺少 MSA/GR&R 和真实现场闭环。 |
| TemplateMatching 固定尺度低旋转场景 | 有条件达到 | 增强模板匹配可用，但不适合声明通用工业匹配。 |
| Planar/AKAZE/ORB 平面纹理匹配 | 部分达到 | 工程 baseline 合格，A/B 改善明显，但尾部误差较大。 |
| DeepLearning 检测精度 | 未达到 | runtime 可跑，但 AP/Precision/Recall 证据不足。 |
| Surface/Anomaly/Edge 公共数据 proof | 初步达到 | 有公开数据 proof，但指标偏弱，仅适合作为 smoke/proof。 |
| 155 算子全库准工业精度 | 未达到 | 大量算子仍缺 dataset/field replay，真实工业验证为 0。 |
| 真实工业级验证 | 未达到 | 缺少 real-field 数据和产线/客户 sign-off。 |

---

## 9. 推荐的下一步验收门槛

### 9.1 Core20 优先闭环

建议先不要追求 155 个算子一次性全部真实闭环，而是优先 Core20：

- TemplateMatching
- ShapeMatching
- GradientShapeMatch
- PyramidShapeMatch
- AkazeFeatureMatch
- OrbFeatureMatch
- PlanarMatching
- LocalDeformableMatching
- CaliperTool
- ArcCaliper
- LineMeasurement
- CircleMeasurement
- GeometricFitting
- EdgeDetection
- BlobAnalysis
- SurfaceDefectDetection
- AnomalyDetection
- DeepLearning
- SemanticSegmentation
- CameraCalibration

对每个 Core20 算子要求至少具备：

1. 数据 manifest。
2. 数据来源和授权边界。
3. 脱敏策略。
4. train/val/test 或 replay split。
5. 指标定义。
6. 阈值定义。
7. 失败模式 taxonomy。
8. 可复现 runner。
9. A/B replay。
10. 回归化失败样本。

### 9.2 Real-field 数据建议

对每个高风险算子建议最低真实现场样本量：

| 算子类型 | 建议 real-field 样本量 | 备注 |
|---|---:|---|
| 测量类 | 每场景 100+，每批次/设备多轮重复 | 必须含标准件或人工量测 ground truth。 |
| 模板/形状匹配 | 每场景 200+ | 覆盖光照、姿态、遮挡、污渍、批次变化。 |
| 缺陷检测 | 每缺陷类型 50+，正常样本 500+ | 特别关注漏检。 |
| 深度学习检测 | 每类别 100+ 标注实例，负样本充足 | 阈值不能为 0。 |
| 标定类 | 多相机/多分辨率/多姿态样本 | 需要 reprojection error、round-trip error 和稳定性。 |

### 9.3 指标建议

不同算子不应共用一个笼统 pass/fail，应使用任务相关指标：

| 算子类型 | 建议主指标 |
|---|---|
| Caliper/测量 | MAE、P95 error、repeatability、GR&R、uncertainty、outlier rate |
| Template/Shape/Planar | pass rate、P95 position error、angle error、scale error、false positive rate |
| Feature matching | homography pass rate、mean/max reprojection error、inlier ratio、P95 corner error |
| Defect/Anomaly | image AUROC、pixel AUROC、F1、recall at fixed FPR、miss rate |
| DeepLearning | AP50/AP75/mAP、precision、recall、false negative rate、latency |
| EdgeDetection | boundary F1、recall、localization error、subpixel repeatability |
| Calibration | RMS reprojection error、max reprojection error、round-trip error、multi-session drift |

### 9.4 准工业 release gate 建议

准工业 release 不应只跑 smoke，应至少要求：

1. `operator-library-industrial-gate` 全部通过。
2. Core20 dataset/field replay 全部通过。
3. A/B replay 无 regression。
4. real-field replay 至少覆盖核心业务场景。
5. 性能 baseline 固定硬件下 P95/P99 无回退。
6. DeepLearning 类算子阈值必须非零且与业务风险绑定。
7. 所有 failed/worse case 必须 triage，并进入 regression suite。
8. IndustrialStatus 只能由真实现场证据或 sign-off 升级。

---

## 10. 建议对外表述

### 10.1 可以使用的表述

> ClearVision 算子库已经建立了较完整的准工业证据治理体系，包括 155 算子质量矩阵、public benchmark proof、field replay 分层、A/B replay 和工业 gate。

> 部分核心算子，例如 CaliperTool、固定尺度 TemplateMatching、Planar/Feature Matching，在受控场景下已经具备准工业试用条件。

> 当前 public benchmark、semisynthetic bridge 和 field-substitute replay 支撑 quasi-industrial claim，但真实产线验证仍需 real-field 数据和线体验收。

### 10.2 不建议使用的表述

> 155 个算子均已达到准工业级精度。

> 算子库已经完成工业级验证。

> DeepLearning 检测精度已经达标。

> field-substitute replay 等同真实现场验证。

> public benchmark accepted 等同客户产线可用。

---

## 11. 最终审计意见

本次补强和 Codex 提升带来了真实进展：项目已经从“算子功能实现 + 零散测试”推进到“可审计、可回归、可分层声明”的准工业治理阶段。CaliperTool、TemplateMatching、Planar/Feature Matching 等核心算子也确实具备了更强工程化实现和 A/B replay 改进。

但从精度证明角度看，当前仍不能把全库评为准工业级精度。主要原因是：真实现场数据缺失、Core20 真实 proof blocked、大量算子缺 Dataset/Field replay、DeepLearning 精度阈值过低、部分 public benchmark 指标偏弱，以及性能仍停留在 smoke/baseline 初始阶段。

因此，本报告给出的最终结论是：

> **ClearVision 当前已达到“准工业证据治理体系基本成熟，部分核心算子受控场景准工业可试用”的阶段；尚未达到“155 算子全库准工业级精度”，也尚未达到“真实工业级产线闭环”。**

---

## 12. 主要证据来源路径

本报告主要依据以下仓库文件和源码：

### 质量与审计证据

- `quality/evals/reports/operator_quality_matrix.md`
- `docs/operator-quality/operator_quality_evidence_manifest.md`
- `quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.md`
- `quality/evals/reports/QualityFlywheel_core20_proof_baseline.md`
- `quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.md`
- `quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.md`
- `docs/审计资料/报告/field-replay证据分层记录-2026-04-29.md`

### 工业 gate 与性能证据

- `docs/validation/operator-library-industrial-gate.md`
- `docs/审计资料/报告/operator-performance-benchmark-current.md`
- `docs/审计资料/报告/operator-performance-trend-2026-04-29.md`

### 核心源码

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/CaliperToolOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/IndustrialCaliperKernel.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/ImageProcessing/SubPixelEdgeDetector.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/TemplateMatchOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PlanarMatchingOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/HomographyVerificationHelper.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/Features/FeatureMatchOperatorBase.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/Features/AkazeFeatureMatchOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/Features/OrbFeatureMatchOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/DeepLearningOperator.cs`

