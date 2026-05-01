---
title: "ClearVision 算子精度继续补全补强计划"
doc_type: "plan"
status: "closed"
topic: "operator-precision-continuation"
created: "2026-04-30"
updated: "2026-04-30"
closed: "2026-04-30"
archive_reason: "Phase A-D closed; Phase E/F blocked by missing real external artifacts"
source_audit: "../../../审计资料/外部审计/ChatGPT审计/ClearVision-operator-precision-audit-2026-04-30.md"
predecessor_plans:
  - "./ClearVision-准工业算法调优TODO-2026-04-29.md"
  - "./ClearVision-算子精度提高下一步计划-2026-04-30.md"
review_note: "2026-04-30 复审后收敛为算子精度攻坚计划；不再铺开全库治理、Release Gate 或长期性能趋势"
claim_boundary: "Improve operator precision without claiming public benchmark, substitute, dry-run, or semisynthetic evidence as real production-line validation"
---

# ClearVision 算子精度继续补全补强计划

> 来源审计：[ClearVision 算子库准工业精度审计报告](../../../审计资料/外部审计/ChatGPT审计/ClearVision-operator-precision-audit-2026-04-30.md)
> 接续计划：[准工业算法调优 TODO](./ClearVision-准工业算法调优TODO-2026-04-29.md) 与 [算子精度提高下一步计划](./ClearVision-算子精度提高下一步计划-2026-04-30.md)
> 当前状态：`closed`

## 归档结论

归档日期：`2026-04-30`

本计划按“不发散、只补强核心算子精度”的范围完成阶段闭环：
- Phase A Measurement stress、Phase B Template/Shape precision、Phase C Matching tail/replay-safe profile、Phase D Surface/Anomaly/Edge detection precision 已形成可复现报告链。
- `algorithm_improvement_suite` 已覆盖对应 active entries，A/B replay scoped validation、报告 validate、proof assets validate 均通过。
- 产品默认参数保持保守：Matching center-only、Anomaly threshold v2 均作为 promotion-ready/default-off 候选保留；EdgeDetection 因 recall tradeoff 保持 hold。
- Phase E `DeepLearning_real_model_accuracy_v2` 与 Phase F `CameraCalibration_real_sample_precision_v2` 需要真实 ONNX/model manifest、真实或授权标定样本，当前因外部 artifact 缺失保持 blocked，不用 smoke fixture、sample bridge 或替代数据冒充真实精度完成。

归档判定：A-D 阶段完成并可归档；E/F 作为后续 artifact-driven 专项，不阻塞本轮计划关闭。

---

## 0. 复审结论

上一版计划覆盖了证据治理、Core20 看板、155 全库补证、Release Gate、性能趋势等内容，方向正确但容易发散。复审后，本计划收敛为一件事：

```text
继续提高核心算子的实际精度，并用可复现 A/B、公开/替代/真实数据指标证明提升。
```

本轮不把精力摊到全库治理和平台门禁，只聚焦审计中点名、且最能体现算子精度的 6 条攻坚线：

1. 测量类：`CaliperTool`、`ArcCaliper`、`LineMeasurement`、`CircleMeasurement`、`GeometricFitting`
2. 模板/形状匹配：`TemplateMatching`、`ShapeMatching`、`GradientShapeMatch`、`PyramidShapeMatch`
3. 特征/平面匹配：`AkazeFeatureMatch`、`OrbFeatureMatch`、`PlanarMatching`
4. 缺陷/异常/边缘：`SurfaceDefectDetection`、`AnomalyDetection`、`EdgeDetection`
5. 深度学习检测：`DeepLearning`
6. 标定：`CameraCalibration`

## 1. 不做清单

为避免无限发散，本轮明确不做：

- [ ] 不做 `155` 全库一次性补证。
- [ ] 不新增大而全的 Release Gate v2。
- [ ] 不做长期性能趋势或 pinned hardware 基线，除非某个精度改动明显引入延迟风险。
- [ ] 不创建泛化的治理模板库，除非它直接服务某个算子的精度复现。
- [ ] 不把文档口径修订当成主线，只保留必要的 claim boundary。
- [ ] 不把真实产线 sign-off 当成本轮必须完成项；有真实数据就接，没有就先用公开/替代数据把算法精度做实。

---

## 2. 总体目标

本轮目标不是“证明全库已达准工业”，而是把最关键算子的指标继续往上推：

| 攻坚线 | 当前问题 | 本轮精度目标 |
|---|---|---|
| 测量类 | 半合成 oracle 通过，但缺真实测量误差闭环；鲁棒几何拟合不足 | 增加重复性/离群/漂移评估，降低 P95 measurement error 和 outlier rate |
| 模板/形状匹配 | 固定尺度低旋转可用，旋转/尺度/遮挡泛化不足 | 增加旋转尺度搜索和响应面亚像素拟合，降低 P95 position error |
| 特征/平面匹配 | AKAZE/ORB A/B 有收益，但尾部误差仍大 | 收敛 HPatches backlog，降低 P95 corner/position error |
| 缺陷/异常/边缘 | public proof 已有，但 PixelF1、AUROC、BoundaryF1 仍偏弱 | 优先降低漏检，补 fixed-FPR recall、category 指标和 localization error |
| DeepLearning | 当前 dry-run 只证明推理链路，不能证明模型精度 | 接真实 ONNX artifact 后输出非零 AP/Recall/Precision 阈值 |
| CameraCalibration | sample bridge 已有，缺真实相机误差闭环 | 输出真实/授权样本的 reprojection、round-trip、multi-session drift |

---

## 3. Phase A：测量类精度补强

优先级：最高。原因：测量类最接近工业使用，也最容易用明确误差指标证明精度提升。

覆盖算子：

- `CaliperTool`
- `ArcCaliper`
- `LineMeasurement`
- `CircleMeasurement`
- `GeometricFitting`

### A1. 鲁棒测量评估集

任务：

- [ ] 扩展 `MeasurementGeometryOracleRunner` 的 stress case：
  - blur
  - noise
  - low contrast
  - occlusion
  - polarity flip
  - subpixel offset
  - outlier contour
  - weak edge
- [ ] 每个测量算子至少新增 `100` 个 stress case，不重复上一轮 1500-case oracle。
- [ ] 每个 case 输出误差分量，而不是只输出 pass/fail：
  - position error
  - angle error
  - radius / distance error
  - edge count
  - uncertainty
  - outlier count

验收门槛：

| 指标 | Gate |
|---|---:|
| stress case count | 每算子 >= 100 |
| pass regression | 0 |
| P95 pixel / measurement error | 不高于上一版 |
| outlier rate | 必须输出并分桶 |

### A2. CaliperTool / IndustrialCaliperKernel 算法补强

任务：

- [ ] 增加多卡尺阵列后的鲁棒聚合策略。
- [ ] 引入或验证 RANSAC / Huber / Tukey 离群边缘剔除。
- [ ] 将 edge pair 的异常原因写入诊断：
  - weak-gradient
  - polarity-mismatch
  - pair-distance-outlier
  - occluded-edge
  - unstable-subpixel-peak
- [ ] 用 A/B 对比以下指标：
  - P95 distance error
  - repeatability sigma
  - outlier rate
  - runtime delta

本阶段不追求完整 GR&R 文档，只先把算法的鲁棒测量误差降下来。

### A3. 几何拟合 outlier 收敛

任务：

- [ ] 对 `GeometricFitting` 增加真实轮廓/替代轮廓中的离群点 replay。
- [ ] 对 line/circle/arc fitting 输出 residual distribution。
- [ ] 增加最小样本失败、重复点、局部遮挡、错轮廓混入的 taxonomy。

输出：

```text
quality/evals/reports/QualityFlywheel_measurement_precision_stress_v2.json
quality/evals/reports/QualityFlywheel_measurement_precision_stress_v2.md
```

---

## 4. Phase B：模板/形状匹配精度补强

目标：把当前“固定尺度、低旋转可用”的模板匹配，推进到可量化的旋转/尺度/遮挡鲁棒性。

覆盖算子：

- `TemplateMatching`
- `ShapeMatching`
- `GradientShapeMatch`
- `PyramidShapeMatch`

### B1. TemplateMatching 姿态鲁棒性

任务：

- [x] 增加旋转搜索 profile：
  - small rotation：`[-5, 5] deg`
  - medium rotation：`[-15, 15] deg`
- [x] 增加尺度搜索 profile：
  - scale range：`0.9..1.1`
  - scale step：`0.05`
- [x] 为 TemplateMatching 姿态搜索增加 pyramid levels：至少 3 层候选。
- [x] 增加响应面亚像素峰值拟合，输出 subpixel offset 和 peak curvature。
- [ ] 扩展 replay 场景：
  - illumination shift
  - partial occlusion
  - contamination / stain
  - batch appearance drift

验收门槛：

| 指标 | Gate |
|---|---:|
| P95 position error | 不高于上一版 |
| rotation replay pass rate | 必须输出 |
| false positive rate | 不升高，或明确阈值取舍 |
| regressed cases | 0，或有 taxonomy |

2026-04-30 推进记录：

- `TemplateMatchOperator` 新增固定尺度响应面的 3x3 二次峰值拟合，输出 `SubpixelOffsetX`、`SubpixelOffsetY`、`PeakCurvature`。
- `TemplateMatchOperator` 新增默认关闭的 bounded pose search，支持 `EnablePoseSearch`、`AngleStart/Extent/Step`、`ScaleMin/Max/Step`，并输出 `Angle`、`Scale`。
- `TemplateMatchOperator` 新增默认关闭的 `PyramidLevels`，三层 pyramid 先用粗层筛 pose seed，再扩展角度/尺度邻域回原图细层决胜，避免小模板 coarse 层误剪真实姿态。
- `TemplateMatchingHomographyBridgeRunner` v2 candidate report 已追加 small rotation、medium rotation、scale、rotation+scale replay；当前 TemplateMatching bridge `32/32` passed，`8` 个 pose case 明确记录 `PyramidLevels=3`，P95 position error `0.1934px`。
- 边界：当前 pyramid 证据仍是 in-repo public-protocol proxy；illumination shift、partial occlusion、contamination / stain、batch appearance drift 仍在 B1 后续 replay 扩展中。

### B2. Shape family 统一对比

任务：

- [x] 将 `ShapeMatching`、`GradientShapeMatch`、`PyramidShapeMatch` 放入同一 replay leaderboard。
- [x] 每个算子输出统一字段；缺失覆盖显式以 `null` / `metricCaseCount=0` / `not-covered` 标注，不作推断：
  - position error
  - angle error
  - scale error
  - score margin
  - occlusion sensitivity
- [x] 选择一个主推 profile，一个保守 fallback profile。

输出：

```text
quality/evals/reports/QualityFlywheel_shape_matching_precision_v2.json
quality/evals/reports/QualityFlywheel_shape_matching_precision_v2.md
```

2026-04-30 推进记录：

- 新增 `quality/tools/build_shape_matching_precision_v2.py`，可顺序执行 `TemplateMatchingHomographyBridgeRunner` 与 `ShapeMatchingGeometricDatasetRunner`，并聚合 `GradientShapeMatch`、`PyramidShapeMatch` 现有基线。
- 新增 active suite entry：`shape_matching_precision_v2_execute`。
- 当前 v2 结果：`209/209` passed，`0` failed；`TemplateMatching` homography/pose bridge `32/32`，`ShapeMatching` 全量几何 pose dataset `36/36`，shape family leaderboard accepted。
- `TemplateMatching` pose replay coverage：rotation cases `6/6`、scale cases `4/4`、pyramid >=3 cases `8`、max pyramid levels `3`。
- 当前主推 profile：`ShapeMatching/geometric_dataset_precision_v2`；保守 fallback：`GradientShapeMatch/contract_baseline`。
- 边界：`TemplateMatching` 姿态搜索已进入 v2 replay；遮挡、污染、批次外观漂移尚未纳入本轮门槛。

---

## 5. Phase C：特征/平面匹配尾部误差收敛

目标：不是继续堆平均值，而是收敛审计点名的 P95 尾部定位误差。

覆盖算子：

- `AkazeFeatureMatch`
- `OrbFeatureMatch`
- `PlanarMatching`

### C1. HPatches backlog 精准分桶

任务：

- [x] 复用上一轮 `QualityFlywheel_matching_failure_backlog_v1`。
- [x] 将失败样本分桶：
  - viewpoint-large
  - repeated-texture
  - low-texture
  - reflection
  - partial-visibility
  - invalid-homography
  - insufficient-inliers
- [x] 每个失败样本记录：
  - inlier ratio
  - mean/max reprojection error
  - projected area ratio
  - corners inside count
  - P95 corner error

2026-04-30 推进记录：

- 新增 `quality/tools/build_matching_tail_error_reduction_report.py`，复用 `QualityFlywheel_matching_failure_backlog_v1` 与 HPatches candidate/baseline reports，输出 `QualityFlywheel_matching_tail_error_reduction_v2`。
- 当前尾部 taxonomy 覆盖 `extreme_viewpoint_crop`、`projected_area_drift`、`reprojection_outlier`、`illumination_residual`、`insufficient_correspondences`、`partial_viewpoint_crop`、`localization_tail`。
- `HPatchesFeatureMatchDatasetRunner` 已新增 `MeanCornerErrorPx`、`MaxCornerErrorPx`、`P95CornerErrorPx`；`AkazeFeatureMatch` / `OrbFeatureMatch` 已输出 `Corners` 诊断。已全量重跑 AKAZE/ORB candidate v4 与 PlanarMatching ORB/AKAZE baselines，P95 corner 已从 `-` 变成实数。
- 当前分桶摘要：AKAZE `90/116`、P95 position `321.632px`、P95 corner `9.247px`；ORB `90/116`、P95 position `267.972px`、P95 corner `8.454px`；PlanarMatching(ORB) P95 corner `10.310px`；PlanarMatching(AKAZE) P95 corner `8.254px`。large-viewpoint failure 仍是主要冻结点。
- 新增 `QualityFlywheel_matching_tail_case_drilldown_v2`，定位 `64` 条 center-gate tail row、`48` 条跨算子重复 tail row；主要可行动模式是 `center in image + inlier/reprojection stable + corners cropped`。

### C2. Replay-safe profile 继续收敛

任务：

- [x] 不牺牲现有 A/B replay fixed case。
- [x] 分别评估 AKAZE / ORB / PlanarMatching 的候选：
  - stricter ratio + more features
  - looser RANSAC threshold
  - local consistency filter
  - multi-hypothesis homography selection
  - default-off center-only projection gate
- [ ] 如果引入 BRISK 或 learned feature adapter，必须作为候选 profile，不替换默认稳定路径。

2026-04-30 replay-safe profile 评估记录：

- 新增默认关闭的 `AllowCenterOnlyProjection`，只在 projected center 可见、homography 几何有效、`inlierRatio >= 0.75` 且 `areaRatio <= 1.5` 或 `meanReprojectionError <= 1.2px` 时放行 center-only projection，避免宽松接收大外推四边形。
- 新增 `QualityFlywheel_matching_replay_safe_profile_candidates_v2`，将 replay pass、full pass、P95 position、P95 corner 同时作为 promotion gate。
- `OrbFeatureMatch/center_only_projection_v1`：full pass `112/116`、replay delta `+2 (18/20)`、P95 position delta `-265.411px`、P95 corner delta `0`，结论 `promote-candidate`；评估 profile 已切到 `center_only_v1`，算子默认参数仍保持关闭。
- `AkazeFeatureMatch/center_only_projection_v1`：full pass `114/116`、replay delta `+6 (19/20)`、P95 position delta `-319.617px`、P95 corner delta `0`，结论 `promote-candidate`；评估 profile 已切到 `center_only_v1`，算子默认参数仍保持关闭。
- `run_algorithm_ab_replay.py` 新增 `--validation-scope matching`，matching-only replay 现在可独立通过：`37` fixed、`0` regressed、matching viewpoint fixed `10`，不再被 DeepLearning / 全量 183 cases 硬门槛误拦。
- `QualityFlywheel_hpatches_matching_family_leaderboard` 与 `QualityFlywheel_matching_algorithm_improvement_v1` 已迁到 `center_only_v1` 主结论：AKAZE `114/116`、ORB `112/116`，remaining matching backlog 收敛到 `4` cases。
- 新增防回退测试：默认关闭时 center-only crop 不通过；center-only gate 对大外推且重投影不稳、低 inlier ratio 的 homography 显式拒绝。
- ORB `replay_safe_high_ratio`：full pass `90/116`、replay delta `0`、P95 corner delta `-1.758px`，但 P95 position delta `+15.295px`，结论 `hold-position-regression`。
- ORB `high_ratio_ransac6`：full pass `90/116`、replay delta `0`、P95 corner delta `-0.214px`，但 P95 position delta `+15.295px`，结论 `hold-position-regression`。
- AKAZE `partial_plane_low_detector_threshold`：P95 position delta `-4.370px`、P95 corner delta `-4.038px`，但 full pass `89/116`，结论 `reject-full-pass-regression`。
- 当前 promotion-ready profile 数量为 `2`；产品算子默认值不直接改动，后续若要默认启用需另走 release/field replay gate。

2026-04-30 replay-safe profile 最后收口：

- `build_matching_replay_safe_profile_report.py` 已升级为正式 gate：支持 `--validate-only`，并校验 center-only 两个 profile 均满足 full pass、matching replay pass、P95 position、P95 corner 不回退。
- `QualityFlywheel_matching_replay_safe_profile_candidates_v2` 明确记录 `profileGate.status=promotion-ready-default-off`、`productDefaultChange=false`、`releaseGateStatus=blocked-missing-field-replay`。
- `algorithm_improvement_suite` 新增 Matching 收口链：scoped A/B replay、replay-safe profile gate、HPatches leaderboard、matching improvement report；`algorithm_ab_replay_execute` 已改为 `--execute-matching --candidate-version center_only_v1 --validation-scope matching`。
- 已用 suite runner 跑通新增 Matching 收口 entry；报告链固定为 `fixed=37`、`regressed=0`、promotion-ready profile `2` 个，产品默认仍保持关闭。

验收门槛：

| 指标 | Gate |
|---|---:|
| replay regression | 0 |
| P95 position/corner error | 下降，或明确冻结原因 |
| large-viewpoint failure count | 下降 |
| mean runtime | 不超过 baseline 1.5x，超出需说明 |

输出：

```text
quality/evals/reports/QualityFlywheel_matching_tail_error_reduction_v2.json
quality/evals/reports/QualityFlywheel_matching_tail_error_reduction_v2.md
quality/evals/reports/QualityFlywheel_matching_tail_case_drilldown_v2.json
quality/evals/reports/QualityFlywheel_matching_tail_case_drilldown_v2.md
quality/evals/reports/QualityFlywheel_matching_replay_safe_profile_candidates_v2.json
quality/evals/reports/QualityFlywheel_matching_replay_safe_profile_candidates_v2.md
quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.json
quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.md
quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.json
quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.md
quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.json
quality/evals/reports/QualityFlywheel_matching_algorithm_improvement_v1.md
quality/evals/reports/OrbFeatureMatch_hpatches_candidate_center_only_v1.json
quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_center_only_v1.json
quality/evals/suites/algorithm_improvement_suite.json
```

---

## 6. Phase D：缺陷/异常/边缘检测精度补强

目标：从“公开数据 proof 已接入”推进到“漏检和定位误差可控”。

覆盖算子：

- `SurfaceDefectDetection`
- `AnomalyDetection`
- `EdgeDetection`

### D1. SurfaceDefectDetection 漏检优先

任务：

- [x] 固定 low-contrast miss replay。
- [x] 输出 recall at fixed FPR，不只看 PixelF1。
- [x] 对正常样本 false positive 和缺陷样本 miss 分开统计。
- [x] 增加后处理 profile：
  - illumination normalization
  - adaptive threshold
  - connected component filtering
  - small defect preservation

验收门槛：

| 指标 | Gate |
|---|---:|
| recall at fixed FPR | 不低于 baseline |
| miss count | 下降或有 taxonomy |
| FP-normal | 不高于上一版，或明确取舍 |

### D2. AnomalyDetection 类别级收敛

任务：

- [x] 对剩余 missed anomalies 按 category 输出 AUROC/F1。
- [x] 固定 threshold calibration split，不用 proof set 反复调参。
- [x] 对 `broken`、`defective`、`bent`、`glue` 等剩余失败类单独记录误差。

验收门槛：

| 指标 | Gate |
|---|---:|
| ImageAuroc | 不低于上一版 |
| PixelAuroc | 不低于上一版 |
| category missed count | 下降或有原因 |

### D3. EdgeDetection 定位精度

任务：

- [x] 增加 boundary localization error。
- [x] 增加 low-contrast edge replay。
- [x] 区分 precision gain 与 recall drop，不用单一 BoundaryF1 掩盖取舍。

2026-04-30 Phase D 推进记录：

- `run_algorithm_ab_replay.py` 新增 `--validation-scope detection`，SurfaceDefectDetection / AnomalyDetection / EdgeDetection 可独立 candidate-executed 并通过 scoped replay gate。
- Detection scoped replay 当前结果：SurfaceDefectDetection improved `10`、regressed `0`；AnomalyDetection improved `14`、detected/image-correct `11`、regressed `0`；EdgeDetection improved `12`、worse metric `8`、regressed `0`。
- 新增 `QualityFlywheel_detection_precision_v2` 聚合报告：Surface PixelF1 `+0.013708`、FP/normal `-0.088367`，recall at fixed FPR 保持 `0.5182`；Anomaly v2 ImageAUROC `+0.256879`、PixelAUROC `+0.198397`、ImageF1 `+0.780013`，剩余 missed defect type 为 bent `5`、broken `5`、defective `5`、glue `3`。
- EdgeDetection runner 已新增 boundary localization distance：`BoundaryToPredictedMeanDistancePx`、`PredictedToBoundaryMeanDistancePx` 及 consensus 对应字段。
- 新增 `QualityFlywheel_edge_detection_recall_guard_sweep_v1`，对 replay subset 试 `45/135`、`40/120`、`35/105` recall-guard 阈值；结论 `hold-current-no-recall-safe-profile`，没有找到同时恢复 recall 且保住 F1/precision 增益的 profile，因此 EdgeDetection 不推广。
- `algorithm_improvement_suite` 新增 Phase D active entries：`anomaly_detection_threshold_calibration_v1`、`anomaly_detection_candidate_v2_execute`、`detection_ab_replay_execute`、`edge_detection_recall_guard_sweep`、`detection_precision_v2_build`。
- 产品默认参数不改；Surface / Anomaly 作为候选证据链保留，Edge 保持 hold。
- 继续补 Anomaly threshold calibration：新增 `QualityFlywheel_anomaly_threshold_calibration_v1`，基于 v1 score sweep 选中阈值 `0.10`，Precision `0.958333`、Recall `0.793103`、F1 `0.867925`，FN `32 -> 18`，FP `0 -> 3`，product default 不改。
- 已生成 `AnomalyDetection_mvtec_candidate_v2`，并将 detection scoped replay 显式切到 `--anomaly-detection-candidate-version v2`；新增 `anomaly_detection_candidate_v2_execute` 可从校准报告一键重建并校验 full v2 candidate，防止干净重跑时误用默认 anomaly 参数。

输出：

```text
quality/evals/reports/QualityFlywheel_detection_precision_v2.json
quality/evals/reports/QualityFlywheel_detection_precision_v2.md
quality/evals/reports/QualityFlywheel_anomaly_threshold_calibration_v1.json
quality/evals/reports/QualityFlywheel_anomaly_threshold_calibration_v1.md
quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.json
quality/evals/reports/AnomalyDetection_mvtec_candidate_v2.md
quality/evals/reports/AnomalyDetection_mvtec_candidate_replay_v2.json
quality/evals/reports/AnomalyDetection_mvtec_candidate_replay_v2.md
quality/evals/reports/QualityFlywheel_edge_detection_recall_guard_sweep_v1.json
quality/evals/reports/QualityFlywheel_edge_detection_recall_guard_sweep_v1.md
```

---

## 7. Phase E：DeepLearning 真实模型精度

目标：把 `DeepLearning` 从 runtime/dry-run 证明推进到真实模型精度报告；如果没有真实模型 artifact，则本阶段保持阻塞，不编造收益。

任务：

- [ ] 接入外部真实 ONNX artifact 与 manifest，模型文件不入库。
- [ ] 复跑：

```powershell
python quality/tools/run_algorithm_ab_replay.py `
  --execute-deep-learning `
  --deep-learning-model-manifest <manifest> `
  --deep-learning-model <onnx>
```

- [ ] 输出：
  - AP50
  - AP75
  - mAP
  - Precision
  - Recall
  - FalseNegativeRate
  - latency
  - per-class metrics
- [ ] 设置非零阈值。若模型能力不足，只能标记为 `baseline-record-only`，不能写“精度达标”。
- [ ] 失败 taxonomy：
  - wrong-class
  - low-confidence-miss
  - localization-miss
  - NMS-suppression-miss
  - small-object-miss
  - occlusion-miss
  - false-positive-background

验收门槛：

| 指标 | Gate |
|---|---|
| AnnotationSeeded | false |
| RealOnnxInference | true |
| AP/Precision/Recall threshold | 非零，或标记 baseline-record-only |
| local raw model path leak | 0 |

输出：

```text
quality/evals/reports/DeepLearning_real_model_accuracy_v2.json
quality/evals/reports/DeepLearning_real_model_accuracy_v2.md
```

---

## 8. Phase F：CameraCalibration 真实标定误差

目标：把 sample bridge 推进到真实或授权标定样本上的误差闭环。

任务：

- [ ] 接入真实相机或授权标定图像包。
- [ ] 输出：
  - RMS reprojection error
  - max reprojection error
  - round-trip error
  - corner detection success rate
  - multi-session drift
- [ ] 覆盖至少一种真实变化源：
  - 多姿态
  - 多距离
  - 多分辨率
  - 多相机

验收门槛：

| 指标 | Gate |
|---|---:|
| real/sample claim split | 必须分开 |
| reprojection metrics | 必须输出 |
| multi-session drift | 至少记录 baseline |

输出：

```text
quality/evals/reports/CameraCalibration_real_sample_precision_v2.json
quality/evals/reports/CameraCalibration_real_sample_precision_v2.md
```

---

## 9. 执行顺序

```text
Week 1:
Phase A 测量类精度补强
优先 CaliperTool / GeometricFitting，因为它们最能体现准工业测量能力。

Week 2:
Phase B 模板/形状匹配
Phase C 特征/平面匹配尾部误差
优先降低 P95，而不是追平均值。

Week 3:
Phase D 缺陷/异常/边缘检测
以漏检、fixed-FPR recall、定位误差为主。

Week 4:
Phase E DeepLearning 真实模型精度
Phase F CameraCalibration 真实标定误差
如果没有外部 artifact/真实样本，就保持阻塞，不用替代证据冒充。
```

---

## 10. 本轮最小验收包

本计划关闭前，至少交付以下精度报告中的 3 份，其中 Phase A 必须完成：

- [x] `QualityFlywheel_measurement_precision_stress_v2`
- [x] `QualityFlywheel_shape_matching_precision_v2`
- [x] `QualityFlywheel_matching_tail_error_reduction_v2`
- [x] `QualityFlywheel_detection_precision_v2`
- [ ] `DeepLearning_real_model_accuracy_v2`
- [ ] `CameraCalibration_real_sample_precision_v2`

关闭条件：

- [x] 至少 3 条攻坚线有 A/B old/new/delta 报告。
- [x] 所有完成攻坚线 `regressedCaseCount=0`，或每个 regression 都有 taxonomy 和取舍说明。
- [x] 至少 1 条攻坚线明确降低 P95 error、miss count 或 false negative rate。
- [x] DeepLearning 若未接入真实 artifact，必须保持阻塞，不算完成项。
- [x] 不新增 155 全库、Release Gate、长期性能趋势等旁支任务。

关闭说明：
- 本轮交付 `QualityFlywheel_measurement_precision_stress_v2`、`QualityFlywheel_shape_matching_precision_v2`、`QualityFlywheel_matching_tail_error_reduction_v2`、`QualityFlywheel_detection_precision_v2`，超过最小验收包要求。
- `QualityFlywheel_algorithm_ab_replay_report` 在 matching / detection scoped validation 下均为 `regressedCaseCount=0`；Detection scoped replay 中 Surface improved `10`、Anomaly v2 detected/image-correct `11`、Edge no pass/fail regression。
- `DeepLearning_real_model_accuracy_v2` 与 `CameraCalibration_real_sample_precision_v2` 因外部 artifact 缺失保持 blocked，不计入本轮完成项。

---

## 11. 验证命令

基础 A/B 与审计验证：

```powershell
python quality/tools/run_algorithm_ab_replay.py --validate-only
python quality/tools/run_quality_suite.py --suite algorithm_improvement_suite --run
python quality/tools/run_quality_suite.py --suite public_benchmark_suite --run
python quality/tools/run_quality_suite.py --suite audit_suite --run
```

DeepLearning 真实模型接入后：

```powershell
python quality/tools/run_algorithm_ab_replay.py `
  --execute-deep-learning `
  --deep-learning-model-manifest <manifest> `
  --deep-learning-model <onnx>
```

涉及 .NET 算子测试时按串行脚本执行：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "FeatureMatchOperatorBaseTests","PlanarMatchingOperatorTests","DeepLearningOperatorTests" `
  -Verbosity minimal
```

---

## 12. 暂停条件

出现以下情况暂停对应攻坚线：

- [ ] 精度均值提升但 P95 或漏检明显变差，且没有取舍说明。
- [ ] A/B replay 出现 regression 且未分桶。
- [ ] DeepLearning 使用 dry-run 或 generated fixture 声明模型精度提升。
- [ ] 半合成 oracle 被写成真实测量闭环。
- [ ] 为了补文档或 gate 导致本轮偏离算子精度提升主线。
