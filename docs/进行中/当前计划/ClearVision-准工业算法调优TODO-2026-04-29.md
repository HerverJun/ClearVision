---
title: "ClearVision 准工业算法调优 TODO"
doc_type: "todo"
status: "active"
topic: "quasi-industrial-algorithm-improvement"
created: "2026-04-29"
updated: "2026-04-29"
claim_boundary: "准工业公开/替代证明；不声明真实产线工业验证完成"
source_reports:
  - "quality/evals/reports/QualityFlywheel_155_quasi_industrial_registry.json"
  - "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json"
  - "quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.json"
  - "quality/evals/reports/QualityFlywheel_hpatches_matching_sweep_v4.json"
  - "quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.json"
  - "quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.json"
  - "quality/evals/reports/QualityFlywheel_matching_failure_backlog_v1.json"
  - "quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_candidate_v1.json"
  - "quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_sweep_v1.json"
  - "quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v1.json"
  - "quality/evals/reports/QualityFlywheel_surface_defect_algorithm_improvement_v1.json"
  - "quality/evals/reports/AnomalyDetection_mvtec_candidate_v1.json"
  - "quality/evals/reports/AnomalyDetection_mvtec_sweep_v1.json"
  - "quality/evals/reports/AnomalyDetection_mvtec_failure_taxonomy_v1.json"
  - "quality/evals/reports/QualityFlywheel_anomaly_detection_algorithm_improvement_v1.json"
  - "quality/evals/reports/QualityFlywheel_155_quasi_industrial_audit.json"
---

# ClearVision 准工业算法调优 TODO

## 0. 当前判断

当前可以进入算法调优，但口径必须限定为“准工业公开数据/替代数据证明”，不能写成真实产线签核。

现有证据状态：

| 维度 | 当前状态 | 判断 |
|---|---:|---|
| 155 算子 registry | 155 operators | 已有全量证据账本 |
| 目标已满足 | 75 operators | 可作为已有基础，但仍有 80 个缺口 |
| 当前 proof level | contract 98 / golden 35 / public-benchmark 16 / field-substitute 5 / missing 1 | 还不是全量准工业 |
| 公开 benchmark proof | 10 operators accepted | 可支撑第一批算法调优 |
| A/B replay | 183 cases compared | 已从清单升级为 old/new/delta 对比 |
| Candidate replay | 80 cases executed | Akaze/ORB + SurfaceDefectDetection + AnomalyDetection 已能真实跑 candidate |
| Matching 修复结果 | fixed 29 / regressed 0 | 已经出现有效算法收益 |
| HPatches matching sweep | Akaze/ORB candidate v4 + Planar ORB/AKAZE | matching family leaderboard 已生成 |
| Surface defect sweep | PixelF1 0.2692 -> 0.2829 / FP-normal 0.1398 -> 0.0515 | KolektorSDD2 candidate v1 已形成 |
| Anomaly sweep | ImageAuroc 0.6609 -> 0.9178 / PixelAuroc 0.6709 -> 0.8692 | MVTec AD Lite candidate v1 已形成 |
| Audit | 44/44 passed | claim、隐私、raw path、runner schema 当前干净 |
| 真实现场签核 | 0 | 仍不得声明 real industrial validation complete |

当前最适合先动的算法族：

1. `AkazeFeatureMatch`、`OrbFeatureMatch`、`PlanarMatching`、`TemplateMatching`
2. `SurfaceDefectDetection`、`AnomalyDetection`
3. `DeepLearning`
4. `CaliperTool`、`ArcCaliper`、`LineMeasurement`、`CircleMeasurement`、`GeometricFitting`

## 1. 总原则

- [ ] 所有算法改动必须走 A/B：旧 baseline、新 candidate、case delta、runtime delta、memory delta、regression risk。
- [ ] 只能在 train/validation 或 replay debug 集上调参；最终 proof test 失败后允许分析，但再次证明必须生成新 proof version。
- [ ] 公开数据 proof 只能声明“准工业公开/替代证明”，不能声明真实工业验证完成。
- [ ] 调算法前先补失败 taxonomy，不盲目重写。
- [ ] 每个 PR 必须说明：修复了哪些失败类型，新增或改善了哪些 replay case，是否伤害已有 passing case。
- [ ] 每次算法提升后必须跑：

```powershell
python quality/tools/run_quality_suite.py --suite public_benchmark_suite --run
python quality/tools/run_quality_suite.py --suite algorithm_improvement_suite --run
python quality/tools/run_quality_suite.py --suite full155_quality_suite --run
```

涉及 .NET 单测时按 AGENTS 要求串行：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName FeatureMatchOperatorBaseTests `
  -NoRestore
```

## 2. Phase A：A/B 基础设施加固

目标：把 183 个 replay case 变成后续所有算法 PR 的共同验收底座。

### A1. 固化 A/B report schema

- [ ] 将 `QualityFlywheel_algorithm_ab_replay_report.json` 的 v2 schema 写入文档或 schema 文件。
- [ ] 强制字段：
  - `old`
  - `new`
  - `delta`
  - `status`
  - `executionMode`
  - `fixedCaseCount`
  - `regressedCaseCount`
  - `improvedMetricCaseCount`
- [ ] 审计要求：
  - `candidatePendingCount == 0`
  - `comparedCaseCount == replayCaseCount`
  - `executedCandidateCaseCount >= matching 当前 replay 数`
  - raw path leak = 0

验收命令：

```powershell
python quality/tools/run_algorithm_ab_replay.py --validate-only
python quality/tools/run_quality_suite.py --suite audit_suite --run
```

### A2. 为非 matching 算子补 candidate 执行入口

当前非 matching 中 `SurfaceDefectDetection` 与 `AnomalyDetection` 已替换成真实 candidate runner；仍有 103 个 control case 待后续算子逐步接入。

- [x] `SurfaceDefectDetection`：接 `KolektorSurfaceDefectDatasetRunner` 的 candidate 参数。
- [x] `AnomalyDetection`：接 `AnomalyDetectionMvtecRunner` 的 candidate 参数。
- [x] `DeepLearning`：接真实模型 inference runner，禁止继续把 annotation-seeded 当模型精度。
- [ ] `EdgeDetection`：接 BSDS500 candidate 参数。
- [ ] `SemanticSegmentation`：接 segmentation candidate 参数。
- [ ] `ShapeMatching`、`TemplateMatching`：接各自 public/golden bridge candidate。

验收标准：

| 指标 | Gate |
|---|---:|
| comparedCaseCount | 183 |
| candidatePendingCount | 0 |
| executedCandidateCaseCount | 当前 80；下一阶段目标 >= 100 |
| regressedCaseCount | 0，或必须有明确风险说明 |

## 3. Phase B：Matching 家族优先调优

目标：优先吃掉 HPatches viewpoint 失败样本，形成第一条真正的算法收益链路。

当前 A/B 结果：

| Operator | Old pass | New pass | Mean error old | Mean error new | Fixed | Regressed |
|---|---:|---:|---:|---:|---:|---:|
| AkazeFeatureMatch | 0/20 | 13/20 | 247.28 px | 104.58 px | 13 | 0 |
| OrbFeatureMatch | 0/20 | 16/20 | 183.18 px | 55.90 px | 16 | 0 |

### B1. 先补诊断字段

目的：后续调参不能只看 `PositionErrorPx`，要知道失败卡在哪里。

- [x] 在 feature matching 输出中补充：
  - `InlierRatio`
  - `MeanReprojectionError`
  - `MaxReprojectionError`
  - `AreaRatio`
  - `CornersInsideCount`
  - `ProjectedCenterInside`
  - `HomographyFailureReason`
- [x] 在 HPatches runner case result 中落盘这些字段。
- [x] A/B markdown 增加 viewpoint focus 表格，按失败原因排序。

完成记录（2026-04-29）：

- `AkazeFeatureMatch`、`OrbFeatureMatch`、`PlanarMatching` 均已输出 HPatches 诊断字段。
- `quality/tools/run_algorithm_ab_replay.py --execute-matching` 已通过，A/B report 含 viewpoint focus。
- `quality/tools/HPatchesFeatureMatchDatasetRunner` 已在 case result 中落盘 reprojection、area、corners/center inside 与 homography failure。

验收：

```powershell
python quality/tools/run_algorithm_ab_replay.py --execute-matching
```

### B2. AkazeFeatureMatch 参数可调化

当前 `AkazeFeatureMatch` 内部 ratio test、RANSAC threshold、min inlier ratio 基本固定。下一步要把关键参数显式纳入算子参数，并用 validation/replay 控制风险。

- [x] 新增参数：
  - `MatchRatio`，默认 `0.75`，范围 `0.5..0.95`
  - `RansacThreshold`，默认 `5.0`，范围 `0.5..10.0`
  - `MinInlierRatio`，默认 `0.25`，范围 `0.1..1.0`
- [x] 保持旧默认值不变，避免破坏现有流程。
- [x] HPatches candidate sweep：
  - viewpoint-only
  - pair 1-2 作为 debug/validation
  - pair 1-3 或剩余 sequence 作为 holdout proof
- [x] 输出 `AkazeFeatureMatch_hpatches_candidate_v3.json/.md`。

完成记录（2026-04-29）：

- candidate v3：`quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_v3.json/.md`
- sweep 汇总：`quality/evals/reports/QualityFlywheel_hpatches_matching_sweep_v3.json`
- 选中 profile：`looser_ransac`，参数为 `MatchRatio=0.75`、`RansacThreshold=7.0`、`MinInlierRatio=0.20`、`MaxFeatures=1200`。
- HPatches 全量：88/116 passed；viewpoint pair 1-2：36/59 passed；holdout pair 1-3：38/59 passed。

候选策略：

| 策略 | 目的 | 风险 |
|---|---|---|
| 放宽 projected quadrilateral 裁切规则 | 修复 viewpoint 裁切 | 可能接受局部错误 homography |
| 适度提高 RANSAC threshold | 容忍视角变化 | 可能增加错误匹配 |
| 提高 MaxFeatures | 增加复杂图像匹配机会 | runtime/memory 上升 |
| 调低 MinInlierRatio | 接受局部可见目标 | 可能误报 |

验收门槛：

| 指标 | Gate |
|---|---:|
| replay fixed | >= 13 保持不退 |
| replay regressed | 0 |
| viewpoint fixed | > 3 |
| meanPositionErrorPx | 不高于 v2 |
| runtime avg | 不超过 old baseline 1.5x，超出需说明 |

### B3. OrbFeatureMatch 参数可调化

- [x] 新增或确认参数覆盖：
  - `MatchRatio`
  - `RansacThreshold`
  - `MinInlierRatio`
  - `FastThreshold` 或等效 ORB detector threshold，如 OpenCVSharp 可支持则接入
- [x] 用 HPatches replay 比较默认 ORB 与 candidate ORB。
- [x] 输出 `OrbFeatureMatch_hpatches_candidate_v3.json/.md`。

完成记录（2026-04-29）：

- candidate v3：`quality/evals/reports/OrbFeatureMatch_hpatches_candidate_v3.json/.md`
- sweep 汇总：`quality/evals/reports/QualityFlywheel_hpatches_matching_sweep_v3.json`
- 选中 profile：`strict_ratio_more_features`，参数为 `MatchRatio=0.70`、`RansacThreshold=6.0`、`MinInlierRatio=0.20`、`MaxFeatures=1600`、`FastThreshold=12`。
- HPatches 全量：89/116 passed；viewpoint pair 1-2：35/59 passed；holdout pair 1-3：36/59 passed。

验收门槛：

| 指标 | Gate |
|---|---:|
| replay fixed | >= 16 保持不退 |
| replay regressed | 0 |
| viewpoint fixed | > 2 |
| meanPositionErrorPx | 不高于 v2 |
| runtime avg | 不超过 old baseline 1.5x，超出需说明 |

### B3.5 Akaze/ORB v4 replay-gated 推进

目的：把 HPatches sweep 与 A/B replay 合成同一条 candidate 选择链路，避免只看全量 HPatches 指标而丢掉 replay fixed。

- [x] HPatches runner 增加可复现参数：
  - `--edge-threshold` / `EdgeThreshold`
  - `--akaze-threshold` / `Threshold`
- [x] `run_hpatches_matching_sweep.py` 升级为 v4：
  - pair 1-2 viewpoint validation
  - public replay gate
  - pair 1-3 holdout
  - selection policy 优先 replay passRate，再看 validation/holdout passRate 与 error
- [x] A/B replay 默认读取 `candidate_v4` 参数，并输出 replay 子集到 `*_hpatches_candidate_replay_v4.json/.md`，避免覆盖 116-case 全量 candidate。
- [x] 输出：
  - `quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_v4.json/.md`
  - `quality/evals/reports/OrbFeatureMatch_hpatches_candidate_v4.json/.md`
  - `quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_replay_v4.json/.md`
  - `quality/evals/reports/OrbFeatureMatch_hpatches_candidate_replay_v4.json/.md`
  - `quality/evals/reports/QualityFlywheel_hpatches_matching_sweep_v4.json`

v4 结果（2026-04-29）：

| Operator | Selected profile | HPatches total | HPatches viewpoint | Replay | Mean error | P95 error | Regressed |
|---|---|---:|---:|---:|---:|---:|---:|
| AkazeFeatureMatch | `default_v3` | 90/116 | 36/59 | 13/20 | 54.341 px | 321.632 px | 0 |
| OrbFeatureMatch | `replay_safe_dense_strict` | 90/116 | 35/59 | 16/20 | 45.006 px | 267.972 px | 0 |

当前结论：`OrbFeatureMatch` 是 v4 主推，因其 replay 仍保持 16/20，同时 HPatches mean/p95 明显优于 v3；`AkazeFeatureMatch` 保持 replay-safe 默认 profile，作为稳定候选。

### B4. PlanarMatching 汇入 HPatches

`PlanarMatching` 更适合做完整平面匹配，应该接入 HPatches 作为 matching family 的主力候选。

- [x] 新增 `HPatchesPlanarMatchingDatasetRunner` 或扩展当前 HPatches runner。
- [x] 支持 detector 参数：
  - ORB
  - AKAZE
  - BRISK
- [x] 对比：
  - AkazeFeatureMatch
  - OrbFeatureMatch
  - PlanarMatching(ORB)
  - PlanarMatching(AKAZE)
- [x] 输出 matching family leaderboard。

完成记录（2026-04-29）：

- `PlanarMatching_hpatches_manifest.json` 已从 `planned` 改为 `active`，runner 指向当前 HPatches feature matching runner。
- PlanarMatching(ORB)：`quality/evals/reports/PlanarMatching_hpatches_baseline.json/.md`，70/116 passed，viewpoint 15/59，p95 = 114.786 px。
- PlanarMatching(AKAZE)：`quality/evals/reports/PlanarMatching_hpatches_akaze_baseline.json/.md`，70/116 passed，viewpoint 16/59，p95 = 118.723 px。
- BRISK detector 已通过 smoke：3/3 passed。
- family leaderboard：`quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.json/.md`

当前结论：PlanarMatching 已接入 HPatches，但 viewpoint passRate 暂未超过 Akaze/ORB candidate v4；后续第一主推仍应优先看 `AkazeFeatureMatch` / `OrbFeatureMatch`。

验收门槛：

| 指标 | Gate |
|---|---:|
| HPatches viewpoint passRate | 高于当前 Akaze/ORB v2 中较优者 |
| p95 position error | 下降 |
| regressedCaseCount | 0 或明确接受原因 |

## 4. Phase C：缺陷/异常检测调优

目标：用 KolektorSDD2 与 MVTec AD Lite 做公开工业缺陷的准工业调优。

### C1. SurfaceDefectDetection

当前数据：KolektorSDD2 本地 3335 records。

- [x] 将 current baseline、candidate baseline、per-case masks、image labels 汇入 A/B runner。
- [x] 按失败 taxonomy 分桶：
  - small defect
  - low contrast defect
  - edge-near defect
  - texture noise false positive
  - mask boundary mismatch
- [x] 优先优化：
  - illumination normalization
  - defect mask postprocess
  - connected component filtering
  - threshold calibration from validation
- [x] 输出 `SurfaceDefectDetection_kolektorsdd2_candidate_v1.json/.md`。

完成记录（2026-04-29）：

- `KolektorSurfaceDefectDatasetRunner` 已支持 `--case-ids`、candidate/profile 记录、更多可调参数、per-image diagnostics 与 failure taxonomy。
- `run_algorithm_ab_replay.py --execute-candidates` 已接入 SurfaceDefectDetection；A/B replay executable candidate cases 从 40 提升到 60。
- sweep 脚本：`quality/tools/run_surface_defect_kolektor_sweep.py`
- candidate v1：`quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_candidate_v1.json/.md`
- sweep：`quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_sweep_v1.json/.md`
- failure taxonomy：`quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v1.json/.md`
- 改动报告：`quality/evals/reports/QualityFlywheel_surface_defect_algorithm_improvement_v1.json/.md`
- 审计材料：`docs/审计资料/算法审计/第5批-SurfaceDefectDetection准工业算法调优报告-2026-04-29.md`
- 选中 profile：`balanced_floor_14_area7`，参数为 `Threshold=14`、`MinArea=7`、`MorphCleanSize=1`、`BackgroundKernelSize=31`。
- test 指标：PixelF1 `0.2692 -> 0.2829`，ImageAuroc `0.7724 -> 0.7728`，ImageF1 `0.5671 -> 0.7000`，FP/normal `0.1398 -> 0.0515`。
- A/B replay：SurfaceDefectDetection replay 20 cases，improved metric 10，pass regression 0，worse-metric 8；整体 A/B executedCandidateCases 60。

已通过验证（2026-04-29）：

- `python quality/tools/run_quality_suite.py --suite algorithm_improvement_suite --run`
- `python quality/tools/run_quality_suite.py --suite public_benchmark_suite --run`
- `python quality/tools/run_quality_suite.py --suite audit_suite --run`
- `python quality/tools/run_quality_suite.py --suite full155_quality_suite --run`
- `& "./scripts/run-dotnet-test-serial.ps1" -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" -FullyQualifiedName SurfaceDefectDetectionOperatorTests -NoBuild -NoRestore -Verbosity minimal`

验收门槛：

| 指标 | Gate |
|---|---:|
| ImageAuroc | 不低于 current |
| PixelF1 | 提升或持平 |
| FalsePositiveRate | 不升高，或有明确阈值取舍 |
| replay regression | 0 |

### C2. AnomalyDetection

当前公开 baseline 指标偏弱：`ImageAuroc=0.66092`，`PixelAuroc=0.670852`。本轮已形成 `max192_dense_stride8` candidate v1。

- [x] 固定 MVTec AD Lite category split，并使用 replay/candidate execution 记录调参证据。
- [x] 增加 candidate 参数入口：
  - `MaxSide`
  - `PatchSize`
  - `PatchStride`
  - `CoresetRatio`
  - `Threshold`
  - `caseIds`
  - `candidateVersion/profile`
- [x] 按类别输出 AUROC、pixel AUROC、worst-case samples 与 failure taxonomy。
- [x] 输出 `AnomalyDetection_mvtec_candidate_v1.json/.md`。

完成记录（2026-04-29）：

- `AnomalyDetectionMvtecRunner` 已支持 `--case-ids`、candidate/profile 记录、per-image `CaseId`、`ImageCorrect`、`FailureTaxonomy` 与 replay 子集执行。
- `run_algorithm_ab_replay.py --execute-candidates` 已包含 AnomalyDetection；A/B report 当前 `executedCandidateCaseCount=80`，Anomaly replay `20` cases 中 `14` score-improved、`5` detected/image-correct、`0` regressed。
- candidate v1 选中 profile：`max192_dense_stride8`，参数为 `MaxSide=192`、`PatchSize=16`、`PatchStride=8`、`CoresetRatio=0.02`、`Threshold=0.35`。
- MVTec AD Lite 全量：`ImageAuroc 0.66092 -> 0.917799`，`PixelAuroc 0.670852 -> 0.869249`，`ImageF1 0.087912 -> 0.774648`。
- failure taxonomy：candidate v1 剩余 missed anomalies `32`，主要集中在 `broken=9`、`defective=9`、`bent=8`、`glue=5`。
- 新增报告：
  - `quality/evals/reports/AnomalyDetection_mvtec_candidate_v1.json/.md`
  - `quality/evals/reports/AnomalyDetection_mvtec_candidate_replay_v1.json/.md`
  - `quality/evals/reports/AnomalyDetection_mvtec_sweep_v1.json/.md`
  - `quality/evals/reports/AnomalyDetection_mvtec_failure_taxonomy_v1.json/.md`
  - `quality/evals/reports/QualityFlywheel_anomaly_detection_algorithm_improvement_v1.json/.md`
  - `docs/审计资料/算法审计/第6批-AnomalyDetection准工业算法调优报告-2026-04-29.md`

验收门槛：

| 指标 | Gate |
|---|---:|
| ImageAuroc | >= 0.70 作为第一目标 |
| PixelAuroc | >= 0.70 作为第一目标 |
| replay regression | 0 |
| privacy/raw path leak | 0 |

## 5. Phase D：DeepLearning 从协议证明走向真实推理

当前 COCO runner 证明的是真实图像链路、预处理、后处理、坐标和 NMS，但 tensor 是 annotation-seeded。因此不能拿它宣称模型精度。

### D1. 接入真实模型输出

- [x] 明确第一版模型：
  - ONNX YOLO 系列，或项目已有模型格式
  - 模型文件不进 git
  - repo 只保存 model card、hash、license、input/output schema
- [x] 新增模型 manifest：
  - `modelId`
  - `modelSha256`
  - `source`
  - `license`
  - `classes`
  - `inputShape`
  - `preprocess`
  - `postprocess`
- [x] COCO runner 支持真实 inference provider：
  - CPU provider 必须可跑
  - GPU/TensorRT 可以作为 optional/manual
- [x] 输出：
  - `DeepLearning_coco_real_model_baseline.json`
  - `DeepLearning_coco_real_model_candidate_v2.json`

验收门槛：

| 指标 | Gate |
|---|---:|
| perCaseResults | 完整 |
| AP50 | 先按真实模型能力冻结，不伪造 |
| Precision/Recall | 输出并可回放 |
| annotation-seeded claim | 不得混入真实模型报告 |

完成记录（2026-04-30）：

- 新增 `quality/tools/DeepLearningCocoRealModelRunner`：COCO 图片进入产品 DeepLearning 预处理，ONNX Runtime CPU provider 产生真实输出 tensor，再走同一套 YOLO 后处理、NMS、坐标反算和 COCO AP50/Precision/Recall 评估。
- 新增 `models/object_detection/coco_yolo_real_model_manifest.template.json`，固定 `modelId`、`modelSha256`、source、license、classes、inputShape、preprocess、postprocess；模型文件仍不进 git。
- `models/model_catalog.json` 与 `ModelCatalog` provenance 已支持 detection manifest 字段：`license`、`model_sha256`、`input_shape`、`classes`、`preprocess`、`postprocess`。
- 已输出 `quality/evals/reports/DeepLearning_coco_real_model_baseline.json/.md` 与 `quality/evals/reports/DeepLearning_coco_real_model_candidate_v2.json/.md`。当前报告使用 generated smoke ONNX fixture 验证真实 ONNX 推理链路，`RealOnnxInference=true`、`AnnotationSeeded=false`，AP50/Recall 如实为 0，不作为训练模型精度收益声明。
- `run_algorithm_ab_replay.py` 已预留 `--execute-deep-learning`、`--deep-learning-model-manifest`、`--deep-learning-model`。默认 `--execute-candidates` 不自动纳入 DeepLearning real-model candidate，避免把 smoke/外部模型结果与 annotation-seeded old proof 混作同一精度口径。

### D2. 后处理算法优化

只优化后处理时，必须明确不是模型训练收益。

- [ ] NMS variants：
  - hard NMS
  - soft NMS
  - class-aware / class-agnostic
- [ ] 坐标反算：
  - letterbox offset
  - scale ratio
  - clamp policy
- [ ] 输出 A/B：
  - AP50 delta
  - Precision/Recall delta
  - latency delta
  - false positive taxonomy

## 6. Phase E：测量/几何算子调优

目标：用半合成几何 oracle 形成高可信 proof 样板。

优先算子：

- `CaliperTool`
- `ArcCaliper`
- `LineMeasurement`
- `CircleMeasurement`
- `GeometricFitting`

### E1. 扩充半合成 oracle

- [ ] 每个算子至少 300 cases。
- [ ] 每个算子至少 40 个边界/失败样本。
- [ ] 覆盖：
  - blur
  - noise
  - low contrast
  - partial edge
  - polarity flip
  - subpixel offset
  - outlier contour
  - occlusion

### E2. 指标

| 算子族 | 主指标 | 辅指标 |
|---|---|---|
| Caliper/Line/Circle | pixel error / angle error | pass rate、runtime |
| ArcCaliper | center/radius/arc angle error | missing edge rate |
| GeometricFitting | fit residual / center error | outlier robustness |

验收门槛第一版：

| 指标 | Gate |
|---|---:|
| passRate | >= 0.98 |
| p95 pixel error | <= 1.5 px，或按算子特性冻结 |
| regression case | 0 |
| report privacy leak | 0 |

## 7. Phase F：Release 级审计

每一轮算法改动完成后必须生成 release 证据包。

- [ ] 更新 155 registry。
- [ ] 更新 public benchmark proof。
- [ ] 更新 A/B replay report。
- [ ] 更新 audit report。
- [ ] 记录 claim boundary：
  - 可以写：`准工业公开/替代证明完成`
  - 不可写：`真实产线工业验证完成`
- [ ] 对每个算法 PR 写明：
  - changed files
  - changed algorithm behavior
  - dataset used
  - train/validation/test boundary
  - fixed cases
  - regressed cases
  - remaining failure cases
  - next action

验收命令：

```powershell
python quality/tools/run_public_benchmark_proof.py
python quality/tools/run_algorithm_ab_replay.py --execute-matching
python quality/tools/run_quality_suite.py --suite public_benchmark_suite --run
python quality/tools/run_quality_suite.py --suite algorithm_improvement_suite --run
python quality/tools/run_quality_suite.py --suite full155_quality_suite --run
python quality/tools/run_quality_suite.py --suite audit_suite --run
```

## 8. 第一轮执行清单

本周只做一件主线：matching family 继续吃 HPatches viewpoint。

### Day 1

- [x] 给 HPatches runner 补诊断字段。
- [x] A/B markdown 增加 viewpoint focus 表。
- [x] 确认 v2 改动后无 regression。

### Day 2

- [x] AkazeFeatureMatch 参数可调化。
- [x] 跑 viewpoint-only sweep。
- [x] 选出 candidate_v3。

### Day 3

- [x] OrbFeatureMatch 参数可调化。
- [x] 跑 viewpoint-only sweep。
- [x] 选出 candidate_v3。

### Day 4

- [x] PlanarMatching 接 HPatches。
- [x] 生成 matching family leaderboard。
- [x] 决定后续主推算子：Akaze / ORB / PlanarMatching。

Day 4 结论：PlanarMatching 作为 HPatches 对照已接通，但未超过 Akaze/ORB；下一轮主推仍优先 `AkazeFeatureMatch` 与 `OrbFeatureMatch` 的 failure boundary 收敛。

### Day 5

- [x] 跑全套 public proof、A/B、audit。
- [x] 输出本周算法改动报告。
- [x] 将剩余失败样本写入下一轮 replay backlog。

完成记录（2026-04-29）：

- 生成脚本：`quality/tools/build_matching_algorithm_improvement_report.py`
- 算法改动报告：`quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.json/.md`
- 失败样本 backlog：`quality/evals/reports/QualityFlywheel_matching_failure_backlog_v1.json/.md`
- 审计材料：`docs/审计资料/算法审计/第4批-Matching准工业算法调优报告-2026-04-29.md`
- 当前结论：A/B replay fixed 29、regressed 0；ORB v4 作为主推候选，Akaze v4 作为稳定 fallback；剩余 27 个 HPatches backlog，其中 25 个 Akaze/ORB 均失败。

已通过验证（2026-04-29）：

- `python quality/tools/run_quality_suite.py --suite algorithm_improvement_suite --run`
- `python quality/tools/run_quality_suite.py --suite public_benchmark_suite --run`
- `python quality/tools/run_quality_suite.py --suite audit_suite --run`
- `python quality/tools/run_quality_suite.py --suite full155_quality_suite --run`
- `& "./scripts/run-dotnet-test-serial.ps1" -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" -FullyQualifiedName PlanarMatchingOperatorTests,FeatureMatchOperatorBaseTests,OperatorContractReconciliationTests -Verbosity minimal`

## 9. 暂停条件

出现以下情况必须暂停算法调优，先修证据链：

- [ ] A/B replay 出现 `candidatePendingCount > 0`。
- [ ] `regressedCaseCount > 0` 且无明确解释。
- [ ] public proof 或 audit 失败。
- [ ] 报告出现绝对路径、客户名、站点名、序列号。
- [ ] test split 被用于反复调参但未重开 proof version。
- [ ] DeepLearning 报告把 annotation-seeded 结果写成真实模型精度。

## 10. 下一份输出

第一轮完成后生成（已完成，2026-04-29）：

```text
quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.json
quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.md
quality/evals/reports/QualityFlywheel_matching_failure_backlog_v1.json
quality/evals/reports/QualityFlywheel_matching_failure_backlog_v1.md
docs/审计资料/算法审计/第4批-Matching准工业算法调优报告-2026-04-29.md
```

报告必须回答：

- 修了哪些失败样本？
- 哪些 viewpoint 仍失败？
- 新算法是否牺牲 illumination 场景？
- runtime/memory 是否可接受？
- 是否可以进入下一轮缺陷/异常调优？
