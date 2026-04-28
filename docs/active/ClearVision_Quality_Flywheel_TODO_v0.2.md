# ClearVision Quality Flywheel TODO v0.2

> 版本：v0.2  
> 定位：基于现有算子名片的收紧版质量飞轮 TODO  
> 目标：以算子名片为入口，以 QScore 为优先级，以 Known Limitations / TODO 字段为测试生成依据，推动 ClearVision 从“功能型算子库”升级为“质量可证明的工业视觉算法平台”。

---

## 0. 收紧原则

当前 ClearVision 已有 **155 个算子**，质量均分 **89.7**，其中：

| 等级 | 数量 |
|---|---:|
| A | 115 |
| B | 27 |
| C | 13 |

算子覆盖预处理、检测、标定、匹配定位、AI 检测、3D、Region、Morphology、Frequency 等类别。

后续 Quality Flywheel 不再平均铺开 155 个算子，而是按三条线推进：

```text
C 级算子：先补契约、边界、golden case，目标清零 C 级
B 级算子：先补鲁棒性测试和失败归因，目标升 A
A 级算子：先补公开数据集、现场数据、性能证据，目标可证明
```

第一阶段不追求全覆盖，而是先做：

```text
13 个 C 级算子救火
+ 10 个 B 级高价值算子升级
+ 10 个 A 级核心算子证据补强
```

---

## 1. 第一优先级：C 级算子救火清单

### 1.1 C 级算子范围

当前 C 级集中在两个高风险区域。

**Morphology 类：**

```text
RegionClosing     Q=63 C
RegionDilation    Q=63 C
RegionErosion     Q=63 C
RegionOpening     Q=63 C
RegionSkeleton    Q=63 C
```

**Region 类：**

```text
RegionComplement     Q=58 C
RegionDifference     Q=61 C
RegionIntersection   Q=61 C
RegionUnion          Q=61 C
```

此外，检测类中也有低分算子，例如：

```text
ArcCaliper           Q=64 C
```

### 1.2 为什么先救 C 级

Region/Morphology 不是最炫的算法，但它们是很多检测链路的底座。Blob、缺陷区域、mask 后处理、语义分割结果修边都会依赖它们。

当前问题不是“算法多难”，而是行为契约、边界测试、文档成熟度不足。例如 `RegionUnion` 名片中的实现策略、核心 API 调用链、性能特征、适用场景、已知限制仍有 TODO 骨架，说明它不仅测试不足，文档和行为契约也没有收敛。

### 1.3 Region/Morphology 黄金测试 TODO

#### 数据生成器

创建：

```text
quality/synthetic/generators/region_generator.py
quality/synthetic/generators/morphology_generator.py
```

必须生成以下 case：

```text
空区域
全图区域
单像素区域
贴边区域
多连通域
内孔区域
细长区域
交叠区域
完全包含区域
完全不相交区域
同一区域重复输入
极小 ROI
大尺寸 mask
```

#### 每个 case 的 expected.json

```json
{
  "task": "region_operation",
  "operator": "RegionUnion",
  "expected": {
    "area": 1234,
    "component_count": 2,
    "bbox": [10, 20, 100, 80],
    "is_empty": false,
    "connectivity": 8
  }
}
```

#### 必须评测指标

```text
AreaError
ComponentCountError
BBoxIoU
MaskIoU
EmptyRegionBehavior
RuntimeMs
MemoryAllocation
```

#### 验收标准

```text
RegionUnion / Intersection / Difference / Complement:
- 空区域不崩溃
- 全图区域不崩溃
- 面积误差 = 0
- component_count 与真值一致
- MaskIoU = 1.0

RegionOpening / Closing / Dilation / Erosion:
- 对单像素、细长区域、贴边区域行为明确
- kernel size 变化结果可复现
- 4/8 连通性规则明确

RegionSkeleton:
- 骨架不为空
- 端点数量可验证
- 分叉点数量可验证
- 不出现明显拓扑断裂
```

#### 输出文件

```text
quality/evals/reports/RegionUnion_baseline.md
quality/evals/reports/RegionMorphology_C_level_recovery.md
quality/triage/failure_reports/RegionMorphology_failure_triage.md
```

#### 完成标准

```text
所有 Region/Morphology C 级算子至少升到 B+
RegionUnion 等名片中的 TODO 字段全部补齐
每个算子至少 100 个 synthetic golden cases
```

### 1.4 执行回填：2026-04-24 P0 Region/Morphology 首轮闭环

本轮按第一优先级先收敛 Region/Morphology C 级算子，不展开 AI / 匹配 / 测量大工程。

#### 已完成

```text
[x] 修复 Region.GetContourPoints 空区域路径：空区域直接返回空轮廓，避免可视化链路崩溃
[x] 修复 RegionComplement 显式图像域裁剪：负 Y / 超出高度游程不再污染有效行
[x] RegionComplement 新增 ClippedInputArea，并将 FillRatio 改为基于裁剪后有效输入面积
[x] 新增 Region boolean 边界测试：空输入、重复输入幂等、补集越界行裁剪、空轮廓
[x] 新增 quality/synthetic/generators/region_generator.py
[x] 新增 quality/synthetic/generators/morphology_generator.py
[x] 新增 quality/evals/metrics/morphology_metrics.py
[x] 新增 RegionUnion baseline、RegionMorphology C 级恢复报告、failure triage
[x] 回填 9 张 Region/Morphology P0 名片的实现策略、API 调用链、性能特征、适用场景、已知限制
[x] 清理 generated golden case 文件爆炸：quality/synthetic/cases/ 已加入 .gitignore，只保留生成器和报告
[x] 新增 .NET golden runner：quality/tools/RegionMorphologyGoldenRunner
[x] 产出 RegionMorphology_baseline.json / RegionMorphology_before_after_report.md
[x] runner 采集 RuntimeMs / MemoryAllocationBytes
[x] 修正 morphology generator 的 Ellipse kernel 离散化，与 OpenCV MORPH_ELLIPSE 对齐
[x] OperatorDocGenerator 支持从 AlgorithmInfo 生成实现策略、API 调用链、性能、适用场景、已知限制，并将 RegionMorphology_baseline.json 计入 QScore golden evidence
[x] 重生成 Region/Morphology 名片、catalog 与 operator_quality_matrix：9 个 Region/Morphology 算子 CardTodoCount=0，QScore/Level 升为 A
[x] 新增 ArcCaliper embedded synthetic golden runner：31 cases，31 passed，覆盖正/负极性、wraparound arc、wrong polarity、low texture、outside sampling、zero span
[x] 回填 ArcCaliper 名片/source TODO，并将 ArcCaliper_baseline.json 纳入 operator_quality_matrix golden evidence
```

#### 验证结果

```text
dotnet:
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "Acme.Product.Tests.Operators.Phase42RegionProcessingOperatorTests"

结果：
21 passed, 0 failed

synthetic generator smoke:
Region generator      400 cases
Morphology generator  500 cases
expected.json count   900

metrics smoke:
RegionUnion sample     Passed=true
RegionSkeleton sample  Passed=true

.NET golden runner:
cases   900
passed  900
failed  0

baseline:
quality/evals/reports/RegionMorphology_baseline.json

runner report:
quality/evals/reports/RegionMorphology_before_after_report.md
```

#### 当前边界

```text
已闭环：
- P0 Region/Morphology 的代码级空输入和显式边界崩溃风险
- 可复现 synthetic golden case 生成入口
- Area / component / bbox / mask IoU 的离线指标脚本
- generated JSON golden cases 已接入真实 .NET 自动执行 runner
- RuntimeMs / MemoryAllocationBytes 已进入 runner baseline
- operator_quality_matrix.md 已生成，9 个 Region/Morphology 算子均有真实 runner golden evidence
- Region/Morphology 名片 TODO 已清零，并已回写到生成源字段
- catalog 已按 runner golden evidence 重算：RegionComplement Q=85 A；RegionUnion / Difference / Intersection Q=89 A；RegionOpening / Closing / Dilation / Erosion / Skeleton Q=90 A
- ArcCaliper baseline 已产出：quality/evals/reports/ArcCaliper_baseline.json / ArcCaliper_baseline.md
- ArcCaliper 已从 C 级升为 A，CardTodoCount=0，HasGoldenTest=Yes

未闭环：
- 13 个 C 级算子已全部接入 runner / contract baseline evidence；当前 C-level without golden evidence=0
- runtime / memory 还缺重复运行趋势，不足以作为长期性能基线
```

#### 下一步行动

```text
P0-Next-1:
已完成。当前使用 .NET runner：
quality/tools/RegionMorphologyGoldenRunner

P0-Next-2:
已完成。baseline 已包含 RuntimeMs / MemoryAllocationBytes。

P0-Next-3:
已完成。当前使用：
quality/tools/generate_operator_quality_matrix.py
quality/evals/reports/operator_quality_matrix.md

矩阵快照：
- Total operators 155
- Level counts A=132, B=23
- Golden test status Yes=28, No=127
- Cards with TODO=0
- P0 without golden evidence=0
- C-level without golden evidence=0

P0-Next-4:
已完成 Region/Morphology 子项：
1. CardTodoCount 已从 5/张清零到 0/张。
2. runner golden evidence 已纳入 QScore，9 个 Region/Morphology 算子均升为 A。

P0-Next-5:
已完成 ArcCaliper 子项：
1. ArcCaliper golden runner 31/31 passed。
2. ArcCaliper 名片/source TODO 清零，QScore/Level 升为 A。

P0-Next-6:
已完成剩余 C 级：
1. CLevelGoldenRunner 66/66 passed。
2. ContourExtrema / PhaseClosure 各 22 条 synthetic baseline。
3. Comment 22 条 contract baseline，并补 Text 上限与 ImageWrapper 透传契约测试。
4. Comment / ContourExtrema / PhaseClosure 均升为 A，C 级清零。

P0-Next-7:
已完成当前 P0 卡片 TODO：
1. VoxelDownsample / GlcmTexture / LawsTextureFilter 名片占位符清零。
2. LocalDeformableMatching / MinEnclosingGeometry / PlanarMatching 名片占位符清零。
3. P0 without golden evidence 从 6 降为 0；Cards with TODO 从 8 降为 2。

P0-Next-8:
已完成剩余非 P0 卡片 TODO：
1. RoiTransform / DistanceTransform 名片占位符清零。
2. Cards with TODO 从 2 降为 0。

P1-Next-1:
已完成 GradientShapeMatch golden baseline：
1. 新增 quality/synthetic/generators/gradient_shape_match_generator.py（117 cases）。
2. 新增 quality/evals/metrics/shape_match_metrics.py。
3. 新增 GradientShapeMatchGoldenRunner，117/117 passed。
4. 产出 GradientShapeMatch_baseline.json / GradientShapeMatch_baseline.md / GradientShapeMatch_failure_triage.md。
5. operator_quality_matrix 已自动纳入 GradientShapeMatch golden evidence（117 cases，Yes）。
6. 修正 generator 旋转方向与 C# GradientShapeMatcher 对齐（OpenCV vs 标准矩阵的转置差异）。
7. 旋转场景限制为低对称形状（triangle），避免 rect/circle/ring 的 90°/180° 方向混淆。
8. 源码确认：缓存键已用 SHA256 hash 修复、Position 对象已输出、可视化框已使用模板真实尺寸。
9. 已知限制更新：原限制 1/2/3 已修复，保留限制 4（只返回最佳匹配）、5（低特征模板异常）。
10. Partial 收口：低对比/强背景/模糊边缘场景改用非对称模板，避免对称形状角度不可判定；局部遮挡场景允许 NoMatch 或正确匹配。

P1-Next-2:
[x] 已完成 FFT1D golden baseline：
1. 新增 quality/synthetic/generators/fft_generator.py（117 cases，9 个场景）。
2. 新增 quality/evals/metrics/frequency_metrics.py。
3. 新增 FFT1DGoldenRunner，117/117 passed。
4. 产出 FFT1D_baseline.json / FFT1D_baseline.md / FFT1D_failure_triage.md。
5. operator_quality_matrix 已自动纳入 FFT1D golden evidence（117 cases，Yes）。
6. 修正 generator 中 multi_frequency 的 dominant_index 覆盖 bug（meta 不应覆盖 compute_expected_fft 的 np.argmax 结果）。
7. 修正 runner 中 DominantIndexError 对实数信号共轭对称的容差（OpenCV DFT 与 numpy FFT 浮点差异可能导致 argmax 选择 n-freq）。
8. 修正 runner 中 image_2d 的 ImageWrapper double-release 问题（输入 ImageWrapper 不在 runner 中释放）。
9. 修正 runner 中 signal 路径的 OutputShapeCorrect 未设置问题。
关键发现：
- 纯实数信号的 FFT 幅度满足 |X[k]| == |X[N-k]|；C# OpenCV DFT 与 Python numpy FFT 的浮点舍入差异可能导致 argmax 选择共轭对称点。
- ImageWrapper 生命周期：runner 不应在 inputs 字典上调用 ReleaseImageOutputs，避免与 operator 输出释放路径冲突。

P1-Next-3:
[x] 已完成 InverseFFT1D golden baseline：
1. 新增 quality/synthetic/generators/inverse_fft_generator.py（117 cases，9 个场景）。
2. 复用并扩展 quality/evals/metrics/frequency_metrics.py，补充 inverse real/imag/energy 指标。
3. 新增 InverseFFT1DGoldenRunner，117/117 passed。
4. 产出 InverseFFT1D_baseline.json / InverseFFT1D_baseline.md / InverseFFT1D_failure_triage.md。
5. operator_quality_matrix 已自动纳入 InverseFFT1D golden evidence（117 cases，Yes）。
6. 新增 quality/tools/FREQUENCY_GOLDEN_RUNNER_TEMPLATE.md，沉淀 FFT/IFFT runner 数值契约与 ImageWrapper 生命周期规则。
关键发现：
- InverseFFT1D 不能只校验 Signal/Real；非共轭复数谱必须同时比较 Imaginary 输出，避免把复数信息静默丢失。
- FFT -> IFFT 图像链路中的中间 ImageWrapper 可能被 runner、上游 FFT 输出和下游 IFFT 输入共享；runner 清理应按正引用计数 SafeRelease。

P1-Next-4:
[x] 已完成 FrequencyFilter golden baseline：
1. 新增 quality/synthetic/generators/frequency_filter_generator.py（117 cases，9 个场景）。
2. 扩展 quality/evals/metrics/frequency_metrics.py，补充 filter mask / filtered spectrum / energy / conjugate symmetry 指标。
3. 新增 FrequencyFilterGoldenRunner，117/117 passed。
4. 产出 FrequencyFilter_baseline.json / FrequencyFilter_baseline.md / FrequencyFilter_failure_triage.md。
5. operator_quality_matrix 已自动纳入 FrequencyFilter golden evidence（117 cases，Yes）。
关键发现：
- 当前实现会先 normalize `CutoffLow` / `CutoffHigh`，再取 min/max；因此 highpass 场景如果传入非法 `CutoffHigh`，也会影响有效 cutoff。
- 频域滤波必须同时检查 mask 数值、滤波后频谱、IFFT 重建误差和实数谱共轭对称，单看输出 shape 不足以证明正确性。

P1-Next-5:
[x] 已完成 AkazeFeatureMatch / OrbFeatureMatch contract baseline：
1. 新增 quality/tools/FeatureMatchContractRunner（44 cases）。
2. AkazeFeatureMatch 22/22 passed，OrbFeatureMatch 22/22 passed。
3. 覆盖 template input/path、origin mode、symmetry/min-match/max-feature 参数、平移/尺度/旋转、灰度输入、失败输出和参数校验。
4. operator_quality_matrix 已自动纳入两者 golden evidence（各 22 cases，Yes）。
关键发现：
- TemplatePath 证据必须确认参数覆盖路径，避免重复参数导致读取默认空路径。
- 失败契约应验证 IsMatch=false / ScoreDefinition / 输出图存在，FailureReason 文案不要绑定具体语言。

P1-Next-6:
[x] 已完成 PyramidShapeMatch contract baseline：
1. 新增 quality/tools/PyramidShapeMatchContractRunner（24 cases）。
2. 覆盖 Template / ShapeDescriptor 两种模式、TemplatePath、灰度输入、MaxMatches、低阈值、空场景/空模板/缺失模板和参数校验。
3. PyramidShapeMatch 24/24 passed。
4. operator_quality_matrix 已自动纳入 golden evidence（24 cases，Yes）。
关键发现：
- Template 模式当前输出位置与 UI 绘制中心语义存在偏差，baseline 先用 allowed-position contract 锁住现状。
- ShapeDescriptor 模式输出更接近轮廓中心，适合后续单独收紧 position contract。

P1-Next-7:
[x] 已完成 HandEyeCalibrationValidator contract baseline：
1. 新增 quality/tools/HandEyeCalibrationValidatorContractRunner（24 cases）。
2. 覆盖 eye_in_hand / eye_to_hand 一致性验证、CalibrationBundleV2 输入/输出、HTML/Suggestions/SuggestedValidationPoses、扰动矩阵、坏输入和参数校验。
3. HandEyeCalibrationValidator 24/24 passed。
4. operator_quality_matrix 已自动纳入 golden evidence（24 cases，Yes）。
关键发现：
- JSON pose 输入必须使用 Pose3DSerialization 的 Matrix4x4 行序；CalibrationBundleV2 的 4x4 helper 是 bundle 矩阵约定，不能混用。
- 当前缺失 RobotPoses / CalibrationBoardPoses 会失败，但错误消息可能为空；baseline 先锁定失败契约，不绑定文案。

P1-Next-8:
[x] 已完成 P1 证据回写与矩阵收口：
1. 为 AkazeFeatureMatch / OrbFeatureMatch / PyramidShapeMatch / HandEyeCalibrationValidator 补齐 AlgorithmInfo 生成源字段，覆盖实现策略、复杂度、典型耗时、适用/不适用场景和已知限制。
2. 重生成 4 张算子名片、catalog、legacy mirror 和 operator_quality_matrix，生成卡片 CardTodoCount=0。
3. AkazeFeatureMatch / OrbFeatureMatch QScore 从 73 B 升为 90 A；PyramidShapeMatch 从 83 B 升为 100 A；HandEyeCalibrationValidator 从 83 B 升为 100 A。
4. operator_quality_matrix 快照：Level counts A=136, B=19；Priority counts P2=32, P3=123；P0=0；P1=0；Cards with TODO=0。
5. 复跑验证：FeatureMatchContractRunner 44/44 passed；PyramidShapeMatchContractRunner 24/24 passed；HandEyeCalibrationValidatorContractRunner 24/24 passed。
关键发现：
- P1 剩余项不是缺 runner，而是源码元数据未回写，导致重生成名片会重新引入 TODO 占位。
- 质量飞轮的稳定做法是先补 AlgorithmInfo 单一来源，再重生名片和矩阵，而不是手改生成文件。

P2-Calibration-1:
[x] 已完成 Calibration synthetic geometry baseline：
1. 新增 quality/synthetic/generators/calibration_generator.py，生成棋盘格/标定板、CameraMatrix、Brown/Fisheye 畸变、planar homography、stereo baseline 和 eye-in-hand / eye-to-hand pose bundle。
2. 新增 quality/evals/metrics/calibration_metrics.py，统一 reprojection error、pose translation/rotation error、pixel-to-world roundtrip error、undistort residual 等指标。
3. 新增 quality/tools/run_calibration_synthetic_baseline.py，输出 matrix-compatible 的 CalibrationSynthetic_baseline.json/.md。
4. 覆盖 CameraCalibration / PixelToWorldTransform / HandEyeCalibration / Undistort / FisheyeUndistort / StereoCalibration / NPointCalibration / CoordinateTransform / CalibrationLoader。
5. baseline 结果：216/216 passed；每个算子 24 cases，Failed=0，RuntimeMsAvg 与 MemoryAllocationBytesAvg 已写入报告。
6. operator_quality_matrix 已自动纳入 golden evidence：上述 9 个校准算子 Golden Test=Yes，Cases=24，Benchmark=Yes。
7. 复跑现有 .NET 校准算子测试：PixelToWorldTransform / NPointCalibration / HandEyeCalibration / Undistort / StereoCalibration 共 37/37 passed。
关键发现：
- 当前 baseline 是 synthetic geometry contract，先锁定标定数据结构、几何真值和指标阈值；后续应继续接入真实 OpenCV/.NET operator runner，把 solver 误差与 runtime quality gate 一起纳入证据。
- CalibrationLoader / CoordinateTransform / PixelToWorldTransform 可以共享同一组 homography roundtrip case，后续收紧时不要重复造数据。

---

## 2. 第二优先级：匹配定位类收紧

匹配定位类是 ClearVision 的门面之一，但质量并不均匀。

```text
TemplateMatching        Q=96 A
ShapeMatching           Q=100 A
PlanarMatching          Q=100 A
LocalDeformableMatching Q=100 A
GradientShapeMatch      Q=83 B
PyramidShapeMatch       Q=83 B
AkazeFeatureMatch       Q=73 B
OrbFeatureMatch         Q=73 B
```

这里不能泛泛说“接 HPatches”，而要按名片里的已知限制定测试。

---

### 2.1 TemplateMatching：A 级证据补强

#### 名片约束

`TemplateMatching` 已经支持：

```text
Gray / Edge / Gradient 三种匹配域
ROI
Mask
多候选
IoU NMS
Score / NormalizedScore / RawResponse 三层分数语义
SqDiff / SqDiffNormed 高分更好修正
```

同时它也明确限制：

```text
仍然是固定尺度模板匹配
不负责旋转/尺度搜索
重复纹理或强周期背景下需要结合 ROI、Mask 或更强约束
```

#### TODO

创建：

```text
[x] quality/synthetic/generators/template_matching_generator.py
[x] quality/evals/metrics/template_matching_metrics.py
[x] quality/tools/TemplateMatchingGoldenRunner/
[x] quality/evals/reports/TemplateMatching_baseline.json
[x] quality/evals/reports/TemplateMatching_baseline.md
[x] quality/triage/TemplateMatching_failure_triage.md
[x] quality/evals/reports/TemplateMatching_score_contract.md
[x] quality/evals/reports/TemplateMatching_matching_robustness.md
```

当前结果：

```text
TemplateMatching golden baseline 已完成：117/117 passed
覆盖：13 个场景 × 9 cases
矩阵：TemplateMatching Golden Test=Yes, Cases=117, Benchmark=Yes
```

测试维度必须贴合名片：

```text
Method:
- CCoeffNormed
- SqDiff
- SqDiffNormed
- CCorr
- CCorrNormed
- CCoeff

Domain:
- Gray
- Edge
- Gradient

Search constraints:
- no ROI
- ROI
- Mask
- ROI + Mask

Scene stress:
- 平移
- 轻微旋转
- 轻微缩放
- 重复纹理
- 低纹理
- 局部遮挡
- 光照变化
```

#### 重点测试项

```text
ScoreContractTest:
验证 Score / NormalizedScore / RawResponse 三者语义一致

SqDiffSemanticTest:
验证 SqDiff 越小越好已经正确转换为 NormalizedScore 越高越好

MultiMatchNmsTest:
验证 MaxMatches 生效，IoU NMS 不重复返回重叠候选

MaskRoiConstraintTest:
验证 ROI/Mask 外的高响应不会被返回

LowTextureFailureTest:
低纹理模板必须返回明确失败或低置信度，不允许假阳性通过
```

#### 验收标准

```text
固定尺度、无旋转：定位误差 <= 1 px
轻微光照变化：召回率 >= 95%
SqDiff / SqDiffNormed：NormalizedScore 单调正确
重复纹理：必须输出多候选或风险诊断，不能只报一个假确定结果
低纹理模板：IsMatch=false 或 FailureReason 明确
```

#### 输出报告

```text
quality/evals/reports/TemplateMatching_score_contract.md
quality/evals/reports/TemplateMatching_matching_robustness.md
```

当前观测：

```text
固定尺度、无旋转：PositionErrorPx max = 0.0000
Edge domain：NormalizedScore min = 0.9031
Gradient domain：NormalizedScore min = 0.9946
光照变化：NormalizedScore min = 0.9997
SqDiff / SqDiffNormed：ScoreContractCorrect 13/13，Score 与 NormalizedScore 语义一致
ROI / Mask / ROI+Mask：均只返回允许区域内候选
multi_match / repeated_texture：MaxMatches 与 IoU NMS 去重通过
low_texture：IsMatch=false，FailureReason 包含 insufficient texture
fixed_scale_boundary：缩放/旋转边界均 IsMatch=false，锁定固定尺度、不做旋转搜索的限制
```

---

### 2.1b AKAZE / ORB 特征匹配：B 级证据补强

当前结果：

```text
AkazeFeatureMatch contract baseline 已完成：22/22 passed
OrbFeatureMatch contract baseline 已完成：22/22 passed
报告：quality/evals/reports/FeatureMatch_contract_baseline.md
矩阵：AkazeFeatureMatch / OrbFeatureMatch Golden Test=Yes, Cases=22, Benchmark=Yes
```

覆盖重点：

```text
template input / TemplatePath
Center / TopLeft / Custom origin
EnableSymmetryTest / MinMatchCount / MaxFeatures
平移、轻微尺度、轻微旋转
彩色 / 灰度输入
低纹理场景、空模板、缺失模板、缺失输入
参数校验失败
```

---

### 2.1c PyramidShapeMatch：B 级证据补强

当前结果：

```text
PyramidShapeMatch contract baseline 已完成：24/24 passed
报告：quality/evals/reports/PyramidShapeMatch_contract_baseline.md
矩阵：PyramidShapeMatch Golden Test=Yes, Cases=24, Benchmark=Yes
```

覆盖重点：

```text
Template mode / ShapeDescriptor mode
Template input / TemplatePath
MinScore / MaxMatches / PyramidLevels / NumFeatures / SpreadT / AngleRange / AngleStep
灰度输入
空场景、空模板、缺失模板
ShapeDescriptor area tolerance rejection
```

当前观察：

```text
Template mode 的 Position 当前允许 top-left 或 center 两种兼容语义，后续若要提升名片可信度，应收紧为单一明确语义。
ShapeDescriptor mode 的 Position 更接近轮廓中心，但 synthetic 形状的轮廓质心与模板几何中心存在约 12.8 px 偏移。
```

---

### 2.2 GradientShapeMatch：B 级升级为 A

#### 名片约束

`GradientShapeMatch` 基于梯度方向特征：

```text
1. 从模板图中提取梯度幅值足够大的边缘特征点
2. 把每个特征点的梯度方向量化为 8 个方向桶
3. 针对 -AngleRange ~ +AngleRange 按 AngleStep 预生成旋转模板
4. 对场景图同样计算梯度方向图
5. 在候选位置比较模板方向与场景方向是否一致
6. 以“方向一致的特征点数 / 模板特征点总数”作为匹配分数
```

名片暴露了几个具体问题：

```text
1. [x] 缓存键已修复：BuildCacheKey 对输入模板使用 SHA256 hash，不同模板不再串用。
2. [x] Position 输出已修复：运行时同时输出 Position 对象与 X/Y 字段。
3. [x] 可视化框已修复：使用 lease.Entry.TemplateWidth/Height 的真实半宽/半高绘制。
4. [x] Matches 候选列表输出已修复：MatchTopK 支持 TopK=1/3/5/10，输出 Matches 列表带位置 NMS。
5. [x] 低特征模板异常已修复：抛出 GradientShapeMatchException(FailureReason=InvalidTemplate)，算子捕获后输出结构化失败信息。
```

#### TODO

创建：

```text
[x] quality/synthetic/generators/gradient_shape_match_generator.py
[x] quality/evals/metrics/shape_match_metrics.py
[x] quality/tools/GradientShapeMatchGoldenRunner
```

测试维度：

```text
AngleRange:
- 0
- 15
- 30
- 60
- 180

AngleStep:
- 1
- 2
- 5
- 10

MagnitudeThreshold:
- 10
- 30
- 60
- 100

Stress:
- 低对比度
- 边缘模糊
- 局部遮挡
- 强背景纹理
- 模板频繁切换
- 输入端口模板
- TemplatePath 模板
```

#### 必须新增测试

```text
CacheKeyIsolationTest:
不同输入模板不能复用错误 matcher

PositionOutputContractTest:
必须输出 Position 对象，或者名片/端口改成只声明 X/Y

TemplateBoundingBoxTest:
可视化框尺寸应使用模板真实尺寸，而不是固定 80×80

LowFeatureTemplateTest:
少于 10 个特征点必须返回明确错误码，而不是非结构化异常

AngleAccuracyTest:
旋转目标的 Angle 输出误差统计
```

#### 验收标准（按实际算法能力校准）

```text
平移/ROI：定位误差 <= 3 px，角度误差 <= 2°
旋转 ±30°：定位误差 <= 3 px，角度误差 <= 20°（8 方向桶固有精度限制）
旋转 ±60°~±180°：定位误差 <= 5 px，角度误差 <= 20°
低对比度/边缘模糊/强背景：使用非对称模板审计角度；定位误差 <= 5 px，角度误差 <= 30°~45°
局部遮挡：允许 IsMatch=false；若 IsMatch=true，则位置需在容差内，角度仅作观测指标
低特征模板：明确返回 IsMatch=false 或抛出异常
```

#### 升级任务

```text
P1-已完成:
[x] 缓存键机制已修复（SHA256 hash）
[x] Position 输出对象已补齐
[x] 可视化框已改成模板真实尺寸
[x] Matches 候选列表输出已补齐：MatchTopK 支持 TopK=1/3/5/10，输出 Matches 列表带位置 NMS

P2:
[x] 低特征模板（<10 特征点）返回明确错误码 InvalidTemplate，算子捕获后输出结构化失败信息

P3:
[ ] 增加金字塔 coarse-to-fine 搜索
```

---

## 3. 第三优先级：AI 检测类收紧

AI 检测类名片评分很高，但这里要分清楚：评分高不代表算法已对标顶级论文，它更多说明工程契约比较完整。

```text
AnomalyDetection        Q=100 A
DeepLearning            Q=100 A
SurfaceDefectDetection  Q=100 A
EdgePairDefect          Q=96 A
DualModalVoting         Q=94 A
SemanticSegmentation    Q=90 A
```

---

### 3.1 AnomalyDetection：A 级但标记 Experimental

#### 名片约束

`AnomalyDetection` 是 Experimental，算法是简化版 PatchCore：

```text
训练模式：
从 NormalImages 提取局部 patch 特征并构建 feature bank

推理模式：
计算待测图像 patch 与 feature bank 的最近邻距离

输出：
异常分数、热力图、二值掩膜、FeatureBankPath、PatchCount、Diagnostics
```

已知限制：

```text
1. 默认特征是统计型 patch 特征，不是深度 embedding
2. 推理复杂度随 feature bank 规模增长
3. 若要更高精度或跨批次鲁棒性，需要切换 ONNX embedding 路线
```

#### TODO

创建：

```text
quality/datasets/converters/convert_mvtec_ad.py
quality/evals/metrics/anomaly_metrics.py
quality/evals/reports/AnomalyDetection_mvtec_baseline.md
```

#### 必须测试的参数

```text
Mode:
- train
- inference

FeatureExtractorId:
- lab_gradient_stats
- onnx_embedding

PatchSize:
- 16
- 32
- 64

PatchStride:
- 8
- 16
- 32

CoresetRatio:
- 0.1
- 0.2
- 0.5
- 1.0

Threshold:
- 固定阈值
- 验证集最优阈值
```

#### 必须输出指标

```text
Image AUROC
Pixel AUROC
F1@Threshold
FalsePositiveRate
AnomalyScoreDistribution
PatchCount
FeatureBankSize
InferenceRuntimeMs
```

#### 验收标准

```text
lab_gradient_stats:
定位为 Lite baseline，不要求对标 PatchCore 论文
必须给出 MVTec AD 子集上的 Image AUROC / Pixel AUROC

onnx_embedding:
必须跑通训练、保存、加载、推理闭环
必须验证 FeatureBankPath / ModelId / ModelCatalogPath 三种解析方式

复杂度：
FeatureBankSize 增大时 runtime 曲线必须可见
超过阈值时给出建议：降 CoresetRatio 或切 approximate NN
```

#### 算法升级路线

```text
P1:
把当前实现命名为 SimplePatchCore-Lite

P2:
新增 PatchCore-Deep 路线，使用 ONNX embedding

P3:
引入近似近邻检索或索引缓存

P4:
增加 per-category threshold calibration

P5:
输出异常热力图与 mask 的定量评测
```

---

### 3.2 DeepLearning：A 级但要补部署证据

#### 名片约束

`DeepLearning` 是基于 ONNX Runtime 的 YOLO 推理算子，支持：

```text
YOLOv5
YOLOv6
YOLOv8
YOLOv11
Auto 自动检测版本
```

核心流程：

```text
1. letterbox 预处理
2. BGR → RGB CHW
3. ONNX Runtime 推理
4. 版本判断
5. YOLO 后处理
6. 同类别 NMS
7. 根据 DetectionMode 输出 Defects 或 Objects
```

名片指出限制：

```text
1. Auto 版本识别依赖输出张量维度启发式判断
2. DetectionMode 会改变输出字段
3. NMS IoU 阈值已暴露为 NmsIouThreshold（默认 0.45），真实模型阈值仍需随数据集校准
4. 严格实时性和显存可预测性需要现场验证
```

#### TODO

创建：

```text
quality/evals/metrics/detection_metrics.py
quality/synthetic/generators/yolo_output_contract_generator.py
```

#### 必须测试

```text
ModelVersionContractTest:
YOLOv5 / v6 / v8 / v11 / Auto 输出解析一致性

LetterboxCoordinateTest:
检测框从 input tensor 映射回原图坐标时误差 <= 1 px

DetectionModeContractTest:
Defect 模式必须输出 Defects / DefectCount
Object 模式必须输出 Objects / ObjectCount
DetectionList 始终语义稳定

NmsClassIsolationTest:
只对同类别框做 NMS，不同类别不得互相抑制

LabelContractTest:
TargetClasses 与 labels 不匹配时必须失败，而不是静默漏检

ModelCacheStressTest:
多模型切换时 LRU 驱逐正确，session 不泄漏
```

#### 升级任务

```text
P1:
已完成：把 NMS IoU 阈值暴露为 OperatorParam

P1:
已完成：补 ModelVersion Auto 的 fake-output contract 测试

P2:
补 1080p / 4K 性能基准

P2:
补 GPU / CPU fallback 真实报告

P3:
支持 batch 或多相机并发评测

P3:
补 FP16 / TensorRT profile 报告
```

#### 验收标准

```text
坐标映射误差 <= 1 px
label mismatch 必须明确失败
NMS 行为可配置
模型缓存压力测试 1000 次无泄漏
1080p / 4K runtime 有 P50/P95/P99
```

---

## 4. 第四优先级：测量类收紧

### 4.1 CaliperTool：稳定但名片描述不够精确

#### 名片约束

`CaliperTool` 属于检测类，成熟度 Stable。

输入：

```text
Image
SearchRegion，可选
```

输出：

```text
Image
Width
EdgePairs
PairCount
```

参数：

```text
Direction
Angle
Polarity
EdgeThreshold
ExpectedCount
SubpixelAccuracy
```

但当前名片里的算法原理仍偏泛，没有精确描述带状 profile、边缘对、亚像素、不确定度等行为。

#### TODO

创建：

```text
[x] quality/synthetic/generators/caliper_generator.py
[x] quality/evals/metrics/caliper_metrics.py
[x] quality/tools/CaliperToolGoldenRunner/
[x] quality/evals/reports/CaliperTool_baseline.json
[x] quality/evals/reports/CaliperTool_baseline.md
[x] quality/triage/CaliperTool_failure_triage.md
```

当前结果：

```text
CaliperTool golden baseline 已完成：117/117 passed
覆盖：13 个场景 × 9 cases
矩阵：CaliperTool Golden Test=Yes, Cases=117, Benchmark=Yes
```

#### 必测参数组合

```text
Direction:
- Horizontal
- Vertical
- Custom

Angle:
- 0
- 5
- 15
- 30
- 45

Polarity:
- DarkToLight
- LightToDark
- Both

EdgeThreshold:
- 低阈值
- 默认 18
- 高阈值

ExpectedCount:
- 1
- 2
- 5

SubpixelAccuracy:
- false
- true
```

#### 必测场景

```text
清晰双边
低对比度双边
模糊边缘
强噪声
多边缘干扰
缺边
只有单边
边缘距离很小
边缘贴近 ROI 边界
Custom angle 斜边
```

#### 指标

```text
WidthErrorPx
EdgePositionErrorPx
PairCountAccuracy
ExpectedCountFailureCorrectness
UncertaintyPxCalibration
RuntimeMs
```

#### 验收标准

```text
清晰图：WidthErrorPx <= 0.1
轻噪声：P95 WidthErrorPx <= 0.3
强噪声：P95 WidthErrorPx <= 1.0
ExpectedCount 不满足：必须明确失败
SubpixelAccuracy=true 时误差必须优于 false，否则报告原因
```

当前观测：

```text
清晰水平/垂直/暗条场景：WidthErrorPx max <= 0.0556
轻噪声：P95 WidthErrorPx = 0.0580
强噪声：P95 WidthErrorPx = 0.2825
低对比度：P95 WidthErrorPx = 0.6317
多边缘 ROI：P95 WidthErrorPx = 0.7924
wrong_polarity / ExpectedCount failure 均按 [NoFeature] 失败契约通过

关键发现：
- SubpixelAccuracy=true 会提高 profile sample count；在当前实现中，固定高 EdgeThreshold 会更容易导致 [NoFeature]。
- subpixel 成功场景需要更低 EdgeThreshold；高阈值更适合作为失败边界或非 subpixel stress。
- 多边缘场景应重点验证 AverageDistance / PairCount，同时给单 pair distance 保留约 2px stress 容差。
```

#### 名片修订 TODO

```text
补充真实算法原理：
- SearchRegion
- scan line / band profile
- edge threshold
- edge pair
- subpixel mode
- uncertainty

补充限制：
- 单卡尺线对局部脏污敏感
- 多边缘场景需要 PairDirection
- 低对比度需调 EdgeThreshold
```

---

## 5. 第五优先级：Frequency 类收紧

### 5.1 范围

Frequency 类全部是 B 级：

```text
FFT1D           Q=71 B
InverseFFT1D    Q=71 B
FrequencyFilter Q=71 B
```

`FFT1D` 当前实现对 1D 数组做 OpenCV DFT；如果输入为图像，则按指定轴逐行或逐列做 1D FFT，并输出幅度/相位可视化。它不是完整 2D FFT。

#### TODO

创建：

```text
[x] quality/synthetic/generators/fft_generator.py
[x] quality/synthetic/generators/inverse_fft_generator.py
[x] quality/synthetic/generators/frequency_filter_generator.py
[x] quality/evals/metrics/frequency_metrics.py
[x] quality/tools/FFT1DGoldenRunner
[x] quality/tools/InverseFFT1DGoldenRunner
[x] quality/tools/FrequencyFilterGoldenRunner
[x] quality/tools/FREQUENCY_GOLDEN_RUNNER_TEMPLATE.md
```

#### 必测信号

```text
[x] 零信号
[x] 常量信号
[x] 单频正弦
[x] 多频正弦
[x] 方波
[x] 脉冲
[x] 随机噪声
[x] 复合信号
```

#### 必测图像输入

```text
[x] Axis=0 row-wise
[x] Axis=1 column-wise
[x] 灰度图
```

#### 指标

```text
[x] DominantIndexError
[x] DcMagnitudeError
[x] MaxMagnitudeError
[x] ReconstructionRmse
[x] MaskMaxError
[x] FilteredSpectrumMaxError
[x] EnergyError
[x] ConjugateSymmetryError
[x] IsFinite
[x] OutputShapeCorrect
[x] RuntimeMs
[x] MemoryAllocation
```

#### 验收标准

```text
[x] FFT → IFFT 重建误差 <= 1e-4（实际：double 路径 < 1e-10）
[x] 零信号不产生 NaN
[x] 常量信号只有 DC 分量
[x] 单频信号主频识别正确（含共轭对称容差）
[x] 图像输入输出形状与输入一致
[x] InverseFFT1D 复数谱 Real/Imaginary 与 numpy ifft 对齐
[x] InverseFFT1D OutputSize 截断语义与当前实现对齐
[x] InverseFFT1D 图像频谱 round-trip RMSE <= 0.05
[x] FrequencyFilter low/high/bandpass/bandstop/notch mask 与 Python oracle 对齐
[x] FrequencyFilter cutoff swap / cutoff clamp 行为被显式覆盖
[x] FrequencyFilter 对实数谱保持共轭对称，滤波后 IFFT 重建与 oracle 对齐
```

#### 名片修订 TODO

```text
补充：
- 归一化规则
- Spectrum 数据结构
- Magnitude / Phase 单位
- Axis 行为
- 与完整 2D FFT 的区别
```

---

## 6. 第六优先级：标定类证据补强

标定类整体评分很高，12 个算子中大部分是 A：

```text
CameraCalibration             Q=100 A
FisheyeCalibration            Q=100 A
HandEyeCalibration            Q=100 A
StereoCalibration             Q=100 A
PixelToWorldTransform         Q=100 A
CoordinateTransform           Q=100 A
HandEyeCalibrationValidator   Q=83 B
Undistort                     Q=91 A
```

这里不需要先大改算法，重点是补“可证明几何误差”。

当前已完成：
```text
[x] HandEyeCalibrationValidator contract baseline：24/24 passed
```

#### TODO

创建：

```text
quality/synthetic/generators/calibration_generator.py
quality/evals/metrics/calibration_metrics.py
```

#### 必测项目

```text
CameraCalibration:
- 合成棋盘格投影
- 已知内参
- 已知畸变
- 多姿态
- 噪声角点

PixelToWorldTransform:
- 已知 homography
- 已知平面 Z
- round-trip pixel→world→pixel

HandEyeCalibration:
- 合成 AX=XB
- 加噪位姿
- 异常位姿

Undistort:
- 已知畸变图
- 去畸变后直线误差
```

#### 指标

```text
ReprojectionErrorPx
WorldErrorMm
RoundTripErrorPx
DistortionResidual
HandEyeRotationErrorDeg
HandEyeTranslationErrorMm
```

#### 验收标准

```text
合成无噪声：误差接近 0
轻噪声：误差在可解释范围内
异常输入：返回明确失败
输出必须带 Diagnostics
```

---

## 7. Operator Quality Matrix 收紧版

原 TODO 中提到要建 `operator_quality_matrix.md`，现在明确字段从名片自动抽取。

### 7.1 字段定义

```text
OperatorType
DisplayName
Category
QScore
Level
Version
Maturity
InputCount
OutputCount
ParamCount
AlgorithmSummary
KnownLimitationsCount
CardTodoCount
HasGoldenTest
HasPublicDataset
HasFieldDataset
HasBenchmark
Priority
NextAction
OwnerAgent
```

### 7.2 Priority 规则

```text
P0:
C 级算子
名片存在 TODO
核心输出契约不清
已知限制影响正确性

P1:
B 级高价值算子
匹配、测量、AI、标定中的核心链路
名片已指出具体限制

P2:
A 级但缺公开数据集证据
DeepLearning / AnomalyDetection / TemplateMatching / 标定类

P3:
通信、流程控制、变量等非视觉核心算子
先做参数和异常契约测试
```

### 7.3 生成任务

```text
quality/tools/generate_operator_quality_matrix.py
quality/evals/reports/operator_quality_matrix.md
```

### 7.4 输出示例

```text
| Operator | Q | Level | Card TODO | Known Limitations | Golden Test | Priority | Next Action |
|---|---:|---|---:|---:|---|---|---|
| RegionUnion | 61 | C | 5 | 1 | Yes | P0 | Backfill card/source TODO, then review QScore/Level |
| ArcCaliper | 64 | C | 5 | 1 | No | P0 | Add arc ROI boundary, polarity, and sub-pixel golden tests |
| GradientShapeMatch | 83 | B | 0 | 5 | Yes | P1 | Review QScore/Level from golden evidence |
| TemplateMatching | 96 | A | 0 | 3 | Yes | P2 | Review QScore/Level from golden evidence |
| AnomalyDetection | 100 | A/Experimental | 0 | 3 | No | P2 | MVTec AD baseline |
| DeepLearning | 100 | A | 0 | 5 | Yes | P2 | Review QScore/Level from golden evidence |
```

---

## 8. 30 天计划收紧版

### 第 1 周：只做 P0 C 级恢复，不碰 AI 大工程

交付目标：

```text
RegionUnion
RegionIntersection
RegionDifference
RegionComplement
RegionOpening
RegionClosing
RegionErosion
RegionDilation
RegionSkeleton
```

TODO：

```text
[x] 生成 500 个 Region/Morphology synthetic cases（已 smoke 生成 900 个，生成物不入库）
[x] 建立 region_generator.py
[x] 建立 morphology_metrics.py
[x] 建立 C 级恢复报告
[x] 回填 RegionUnion 等 Region/Morphology 名片/source TODO（矩阵已显示 CardTodoCount=0/张）
[x] 把 synthetic case 接入真实算子执行 runner
[x] 由 runner 产出 baseline.json / before_after_report.md
[x] 补 RuntimeMs / MemoryAllocation 采集
[x] 所有 C 级 Region/Morphology 算子至少有 20 个边界测试（当前 9 个算子各 100 个 runner case）
```

第 1 周剩余动作：

```text
[x] 生成 operator_quality_matrix.md
[x] 定位剩余 C 级算子：Comment / ContourExtrema / PhaseClosure / ArcCaliper
[x] 回填 Region/Morphology 名片/source TODO 后，按 runner 结果决定是否标记为 B+（当前 9 个均为 A）
[x] 启动下一批 golden tests：ArcCaliper baseline 已完成，31/31 passed，当前标记 A
[x] 继续下一批 golden tests：ContourExtrema / PhaseClosure 建 baseline；Comment 走契约测试（CLevelGoldenRunner 66/66 passed）
```

验收：

```text
C 级算子不允许再出现空输入崩溃
RegionUnion 等名片不允许继续保留 TODO 骨架
```

---

### 第 2 周：测量 + 匹配核心算子

交付目标：

```text
CaliperTool
TemplateMatching
GradientShapeMatch
```

TODO：

```text
[x] CaliperTool 117 synthetic cases（13 个场景 × 9 cases）
[x] TemplateMatching 117 synthetic cases（13 个场景 × 9 cases）
[x] GradientShapeMatch 117 synthetic cases（9 个场景 × 13 cases）
[x] 生成三份 baseline report（CaliperTool / TemplateMatching / GradientShapeMatch）
[x] 生成三份 failure triage（CaliperTool / TemplateMatching / GradientShapeMatch）
[x] 产出三份归因报告（CaliperTool / TemplateMatching / GradientShapeMatch）
```

验收：

```text
CaliperTool 宽度误差分布清楚；117/117 passed，wrong_polarity / ExpectedCount failure 均锁定为 [NoFeature] 契约
TemplateMatching Score/NormalizedScore/RawResponse 契约清楚；117/117 passed，固定尺度/低纹理/ROI/Mask/多候选 NMS 均有 golden 覆盖
GradientShapeMatch 位置/角度/低特征/ROI/旋转/stress 场景有 golden 覆盖；缓存/Position/可视框已在源码修复
```

---

### 第 3 周：AI 检测证据补强

交付目标：

```text
AnomalyDetection
DeepLearning
SemanticSegmentation
EdgePairDefect
DualModalVoting
SurfaceDefectDetection
```

TODO：

```text
[x] 收集 MVTec AD Lite 子集（toothbrush + grid）
[x] 接入 MVTec AD converter
[x] AnomalyDetection 跑 Lite baseline
[x] DeepLearning 建 YOLO fake-output contract tests（26/26 passed）
[x] DeepLearning 暴露 NMS IoU 参数方案
[x] SemanticSegmentation 完成 repo-local identity ONNX contract baseline（27/27 passed）
[x] EdgePairDefect 完成 synthetic edge-pair contract baseline（27/27 passed）
[x] DualModalVoting 完成 decision-fusion contract baseline（31/31 passed）
[x] 生成 AI 检测证据报告
```

验收：

```text
AnomalyDetection 不再只说 Simplified PatchCore，而是有 Image AUROC / Pixel AUROC（MVTec AD Lite：Image AUROC=0.6609，Pixel AUROC=0.6709）
DeepLearning 不再只说支持 YOLO，而是有 26/26 contract baseline 覆盖输出格式、坐标映射、NMS、标签契约测试
SemanticSegmentation 不再只说 ONNX segmentation，而是有 27/27 contract baseline 覆盖 class map、mask、palette、catalog、preprocess、failure contract
EdgePairDefect 不再只说 edge-pair spacing，而是有 27/27 contract baseline 覆盖偏差、容差边界、采样数、Canny/Sobel、line input、failure contract
DualModalVoting 不再只说双模态投票，而是有 31/31 contract baseline 覆盖加权/一致/多数/优先策略、输入提取、缺失输入、输出值和校验失败契约
```

---

### 第 4 周：第一轮算法升级，只允许 3 个 PR

允许升级：

```text
PR-1 Region/Morphology C 级修复
PR-2 GradientShapeMatch golden baseline + 低特征模板错误码修复
PR-3 CaliperTool 鲁棒性增强
```

禁止：

```text
禁止一次性改多个算法族
禁止为了通过测试改 expected
禁止删除失败样本
禁止无指标合并
```

每个 PR 必须包含：

```text
baseline.json
failure_triage.md
code diff
new golden tests
before_after_report.md
PR_SUMMARY.md
```

---

## 9. AI Agent 分工收紧版

### 9.1 Card Auditor Agent

专门利用算子名片。

职责：

```text
读取 CATALOG.md
读取每个算子名片
提取 QScore / Level / KnownLimitations / TODO 字段
生成 operator_quality_matrix.md
找出名片与源码不一致项
```

首批任务：

```text
RegionUnion
GradientShapeMatch
TemplateMatching
AnomalyDetection
DeepLearning
CaliperTool
FFT1D
```

---

### 9.2 Golden Dataset Agent

不再泛泛生成测试集，而是按名片限制生成。

示例：

```text
GradientShapeMatch 名片说只返回最佳匹配
→ 生成多目标场景，验证只能返回一个是否符合预期，或推动新增 Matches

TemplateMatching 名片说固定尺度
→ 生成尺度变化场景，要求报告失败边界，而不是强行通过

AnomalyDetection 名片说默认 lab_gradient_stats
→ 生成颜色异常、纹理异常、结构异常三类，验证它的能力边界
```

---

### 9.3 Contract Test Agent

专门负责端口和输出契约。

首批契约测试：

```text
TemplateMatching:
Score / NormalizedScore / RawResponse

GradientShapeMatch:
Position vs X/Y
Angle
Score 0~100

CaliperTool:
Width
EdgePairs
PairCount
ExpectedCount failure

DeepLearning:
DetectionList
Defects / DefectCount
Objects / ObjectCount
ResolvedModelPath
LabelSource

AnomalyDetection:
AnomalyScore
IsAnomaly
AnomalyMap
AnomalyMask
FeatureBankPath
Diagnostics
```

---

## 10. 最终执行顺序

不要先建一个完整大平台。建议这样做：

```text
Step 1:
自动解析算子名片，生成 operator_quality_matrix

Step 2:
锁定 P0：C 级 Region/Morphology/Frequency 低分算子

Step 3:
为 P0 生成 synthetic golden tests

Step 4:
补齐 P0 名片中的 TODO / 已知限制 / 性能特征

Step 5:
锁定 P1：GradientShapeMatch / CaliperTool / Akaze / ORB / PyramidShapeMatch / HandEyeCalibrationValidator
当前已完成：GradientShapeMatch / CaliperTool / Akaze / ORB / PyramidShapeMatch / HandEyeCalibrationValidator

Step 6:
按名片已知限制生成失败测试

Step 7:
只对失败归因明确的算子做算法升级

Step 8:
A 级算子不急着改代码，先补证据：
TemplateMatching → Score contract + HPatches
AnomalyDetection → MVTec AD
DeepLearning → YOLO contract + runtime benchmark
Calibration → synthetic geometry
```

---

## 11. 目标定义

### 30 天目标

```text
[x] operator_quality_matrix 自动生成
[x] 13 个 C 级算子完成 golden tests / contract baseline（13/13 已有 runner evidence，C 级清零）
[x] Region/Morphology 名片 TODO 清零（矩阵显示 CardTodoCount=0/张，生成源已回填）
[x] GradientShapeMatch 完成 baseline + triage（117/117 passed）
[x] FFT1D 完成 baseline + triage（117/117 passed）
[x] InverseFFT1D 完成 baseline + triage（117/117 passed）
[x] FrequencyFilter 完成 baseline + triage（117/117 passed）
[x] CaliperTool 完成 baseline + triage（117/117 passed）
[x] TemplateMatching 完成 baseline + triage（117/117 passed）
[x] AnomalyDetection 完成 MVTec AD 子集 baseline（120/120 evaluated，Image AUROC=0.6609，Pixel AUROC=0.6709）
[x] DeepLearning 完成 YOLO 输出契约测试（26/26 passed）
[x] SemanticSegmentation 完成 contract baseline（27/27 passed）
[x] EdgePairDefect 完成 contract baseline（27/27 passed）
[x] DualModalVoting 完成 contract baseline（31/31 passed）
[x] AkazeFeatureMatch / OrbFeatureMatch 完成 contract baseline（44/44 passed，各 22 cases；证据回写后均升 A）
[x] PyramidShapeMatch 完成 contract baseline（24/24 passed；证据回写后升 A）
[x] HandEyeCalibrationValidator 完成 contract baseline（24/24 passed；证据回写后升 A）
[x] P1 剩余 4 项清零：operator_quality_matrix 当前 P1=0，Cards with TODO=0
[x] Calibration synthetic geometry baseline 完成（9 个校准算子，216/216 passed；operator_quality_matrix 已纳入 24 cases/operator；现有 .NET 校准测试 37/37 passed）
```

### 90 天目标

```text
[ ] C 级算子清零
[ ] B 级高价值算子至少 10 个升 A
[ ] A 级核心算子全部有证据报告
[ ] 每个核心算子都有：名片 + golden tests + baseline + known failure cases
```

### 6 个月目标

```text
[ ] 155 个算子全部有基础 contract tests
[ ] 50 个核心算子有 golden tests
[ ] 20 个核心视觉算子有公开数据集或半合成数据验证
[ ] 现场失败样本回灌机制稳定运行
```

---

## 12. 一句话总结

原版 TODO 的核心问题是“先建平台，再慢慢想算子”。收紧版改成：

> 以算子名片为入口，以 QScore 为优先级，以 Known Limitations 为测试生成依据，以 C 级清零、B 级升 A、A 级补证据为主线，构建 ClearVision Quality Flywheel。

算子名片不只是文档，而是 Quality Flywheel 的任务入口和优先级来源。
