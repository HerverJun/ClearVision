---
title: "ClearVision 现有公共数据集精度升级调优计划"
doc_type: "plan"
status: "closed"
topic: "operator-precision-tuning"
created: "2026-05-01"
updated: "2026-05-01"
closed_at: "2026-05-01"
close_reason: "默认关闭候选 profile、治理报表和 release/field replay 门禁标准已闭环；default-on 与产线签核转为后续触发项。"
dataset_scope:
  - "quality/public_datasets/hpatches"
  - "quality/public_datasets/coco2017"
  - "quality/public_datasets/kolektorsdd2"
  - "quality/public_datasets/mvtec_ad_lite"
  - "quality/public_datasets/bsds500"
  - "quality/public_datasets/opencv_calibration_samples"
claim_boundary: "仅基于当前已落盘公开数据集做研究评估和候选调优；不代表真实产线签核，不升级 IndustrialStatus。"
---

# ClearVision 现有公共数据集精度升级调优计划

## 背景

本计划不再等待大数据集下载，先基于当前已经落盘的数据集推进一轮可执行的精度升级调优。当前可用数据总量约 `7.743 GB`：

| 数据集 | 已落盘体量 | 主要覆盖方向 | 本轮定位 |
| --- | ---: | --- | --- |
| `hpatches` | `2.983 GB` | 特征匹配、平面匹配、局部描述子鲁棒性 | 可作为匹配类算子公开基准主数据 |
| `coco2017` | `2.531 GB` | 深度学习目标检测、后处理、标签映射 | 数据够用，真实 AP 依赖外部 ONNX 模型 |
| `kolektorsdd2` | `1.589 GB` | 表面缺陷检测、像素级 mask、正常样本误报 | 可作为 `SurfaceDefectDetection` 本轮主门禁 |
| `mvtec_ad_lite` | `0.505 GB` | 异常检测 smoke、阈值校准、异常热图回放 | 只做 advisory，不做 MVTec full 结论 |
| `bsds500` | `0.134 GB` | 边缘检测冻结测试、人类边界标注 | 可作为 `EdgeDetection` 本轮主门禁 |
| `opencv_calibration_samples` | `0.001 GB` | 标定几何 sanity、相机标定回归 | 只做 contract/geometry smoke |

当前数据已经足够支撑“公开数据 smoke + 候选算法 A/B + 默认关闭候选门禁”的调优闭环；不足以支撑以下结论：

- 不足以声明异常检测达到 MVTec AD full 前列，因为当前只有 `mvtec_ad_lite`。
- 不足以训练或评估 ONNX edge candidate 的完整泛化能力，因为缺少 `BIPEDv2/UDED`。
- 不足以声明 COCO 真实模型 AP，除非补齐真实 `YOLO11n` 或等价 ONNX 模型权重。

因此本轮目标是先把可用数据转化为稳定的精度证据和候选调参闭环，所有候选能力默认关闭，报告明确“公开数据研究评估，不代表产线签核”。

## 目标

1. 基于 `kolektorsdd2` 提升 `SurfaceDefectDetection` 的低对比缺陷召回，并控制正常样本误报。
2. 基于 `mvtec_ad_lite` 建立 `AnomalyDetection` 的轻量异常回放和阈值校准链路，作为后续 MVTec full 的预演。
3. 基于 `coco2017` 建立真实模型目标检测评估路径，先修正 IO schema、标签顺序、NMS 和阈值问题。
4. 基于 `bsds500` 固化 `EdgeDetection` 的 Canny recall-safe profile，避免边缘召回因自动阈值策略回退。
5. 基于 `hpatches` 扩展匹配类算子的公开基准证据，包括局部特征、平面匹配、模板匹配到 homography 的稳定性。
6. 基于 `opencv_calibration_samples` 保持标定几何链路 smoke，防止精度调优改坏基础几何 contract。

## 范围

### 纳入

- 当前已落盘的 6 类公开数据集。
- 现有 runner、suite、manifest、index 和报告生成链路。
- 默认关闭的候选参数、阈值 profile、A/B replay 报告。
- 只提交代码、manifest、index、配置、报告；不提交 raw dataset、模型权重、大文件。

### 不纳入

- 不继续下载 `MVTec AD full`、`MVTec LOCO AD`、`MVTec AD2`、`BIPEDv2`、`UDED`。
- 不训练或随仓库分发商业受限模型权重。
- 不升级任何算子的 `IndustrialStatus`。
- 不把 lite/smoke 结果包装成完整公开榜单结论。

## 总体策略

本轮按“先锁 baseline，再跑候选，再做门禁，再产报告”的方式推进。

1. 冻结当前默认算子输出，记录 baseline 指标和失败样本。
2. 针对每类算子只开少量高收益候选，不做无边界网格搜索。
3. 每个候选必须有 `baseline`、`candidate`、`delta`、`failure taxonomy`。
4. 晋级只允许进入 default-off profile 或建议清单；默认行为保持不变。
5. 报告必须保留数据集许可、非产线签核、阈值校准边界。

## 调优主线

### 1. SurfaceDefectDetection：KSDD2 主门禁

数据：`quality/public_datasets/kolektorsdd2`。

现状判断：这是当前最适合做实质精度提升的方向。KSDD2 同时包含缺陷图、正常图和像素级 mask，能直接衡量漏检、误报、过分割和低对比召回。

候选 profile：

| Profile | 参数方向 | 目的 |
| --- | --- | --- |
| `baseline_default` | 当前默认参数 | 锁定默认行为 |
| `clahe_local_mean_light` | `NormalizationMode=ClaheLocalMean`，低 clip limit | 提升低对比缺陷响应 |
| `clahe_response_stats` | `ComponentFilterMode=ResponseStats` | 过滤纹理噪声和孤立误报 |
| `recall_guard_low_threshold` | 较低响应阈值 + 面积约束 | 降低低对比漏检 |
| `precision_guard_normal` | 响应统计过滤 + 正常样本 replay | 控制 normal FP |

建议 sweep 范围：

| 参数 | 候选值 |
| --- | --- |
| `ClaheClipLimit` | `1.5`, `2.0`, `3.0` |
| `ClaheTileGridSize` | `8`, `12`, `16` |
| 响应阈值 | `10`, `12`, `15`, `18`, `22` |
| 最小连通域面积 | `3`, `4`, `6`, `8` |
| `ComponentFilterMode` | `None`, `ResponseStats` |

核心指标：

- `PixelF1`
- `PixelPrecision`
- `PixelRecall`
- `ImageRecall`
- `FP/normal`
- fixed-FPR recall
- 低对比漏检数
- 过分割样本数
- 处理失败数

晋级门禁：

- KSDD2 test `PixelF1` 相比当前 baseline 至少提升 `0.03`。
- `FP/normal <= 0.06`。
- fixed-FPR recall 不下降。
- 低对比漏检数下降。
- 处理失败数为 `0`。
- 候选只能进入 default-off profile，不直接改默认行为。

建议命令：

```powershell
dotnet build quality/tools/KolektorSurfaceDefectDatasetRunner/KolektorSurfaceDefectDatasetRunner.csproj -v minimal --no-restore
dotnet run --project quality/tools/KolektorSurfaceDefectDatasetRunner/KolektorSurfaceDefectDatasetRunner.csproj -- --dataset quality/public_datasets/kolektorsdd2 --output quality/reports/surface_defect_ksdd2_baseline.json
```

产物：

- `quality/reports/surface_defect_ksdd2_baseline.json`
- `quality/reports/surface_defect_ksdd2_candidate_ab.json`
- `quality/reports/surface_defect_ksdd2_failure_taxonomy.md`
- `QualityFlywheel_detection_precision_v3` 中的 surface 小节更新。

### 2. AnomalyDetection：MVTec AD Lite advisory

数据：`quality/public_datasets/mvtec_ad_lite`。

现状判断：`mvtec_ad_lite` 可以用于 runner、阈值校准、异常热图和失败样本 taxonomy，但不能替代 MVTec AD full。它适合做轻量预演，帮助后续接入 full 数据集时少走弯路。

候选路线：

| Profile | 前提 | 目的 |
| --- | --- | --- |
| `baseline_builtin` | 不依赖外部模型 | 锁定当前异常检测默认表现 |
| `threshold_replay_calibrated` | train/val 校准 | 检查阈值选择是否稳定 |
| `onnx_embedding_resnet18` | 外部 ONNX embedding 已存在 | 验证 embedding 特征链路 |
| `patch_stride_sweep` | 不改公共参数 | 优化局部异常热图分辨率 |

建议 sweep 范围：

| 参数 | 候选值 |
| --- | --- |
| patch size | `16`, `24`, `32` |
| stride | `8`, `16` |
| coreset ratio | `0.02`, `0.05`, `0.10` |
| threshold source | `train`, `validation`, `replay_validation` |
| feature extractor | 默认 extractor，`onnx_embedding` |

核心指标：

- `ImageAUROC`
- `PixelAUROC`
- `ImageF1`
- `PixelF1`
- 正常样本误报率
- 阈值稳定性
- 处理失败数

晋级门禁：

- Lite 数据只给 advisory，不改默认阈值。
- candidate 不允许低于 baseline 的 `ImageAUROC` 和 `PixelAUROC`。
- test 阈值只能由 train/val 或 replay validation 产生，不能从 test 反推。
- 处理失败数为 `0`。
- 如果没有外部 ONNX embedding 模型，本轮只验证内置链路和阈值策略，不声明 embedding 精度。

建议命令：

```powershell
dotnet build quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj -v minimal --no-restore
dotnet run --project quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj -- --dataset quality/public_datasets/mvtec_ad_lite --output quality/reports/anomaly_mvtec_lite_baseline.json
```

ONNX embedding 候选存在时再运行：

```powershell
dotnet run --project quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj -- --dataset quality/public_datasets/mvtec_ad_lite --feature-extractor-id onnx_embedding --embedding-model quality/models/embeddings/resnet18_avgpool.onnx --embedding-model-id resnet18_avgpool --output quality/reports/anomaly_mvtec_lite_onnx_embedding.json
```

产物：

- `quality/reports/anomaly_mvtec_lite_baseline.json`
- `quality/reports/anomaly_mvtec_lite_candidate_ab.json`
- `quality/reports/anomaly_mvtec_lite_threshold_notes.md`

### 3. DeepLearning：COCO 2017 real-model 路径

数据：`quality/public_datasets/coco2017`。

现状判断：当前 COCO 数据体量足够做 120-case smoke、500-case candidate 和后续 5000-case manual full。真正限制不是数据，而是外部真实 ONNX 检测模型是否已准备好。没有真实模型时，只能做 contract/postprocess smoke，不能声明 AP。

候选路线：

| Profile | 前提 | 目的 |
| --- | --- | --- |
| `annotation_seeded_contract` | 无真实模型 | 验证 COCO index、标签、NMS、metric contract |
| `yolo11n_onnx_smoke_120` | 外部 YOLO11n ONNX | 快速验证 IO schema 和 provider |
| `yolo11n_onnx_candidate_500` | 外部 YOLO11n ONNX | 进入候选门禁 |
| `yolo11n_onnx_manual_5000` | 外部 YOLO11n ONNX | 人工确认后跑完整抽样 |

建议调优项：

| 调优项 | 候选范围 |
| --- | --- |
| confidence threshold | `0.20`, `0.25`, `0.30`, `0.35` |
| NMS IoU | `0.45`, `0.50`, `0.60`, `0.70` |
| image size | `640` 固定优先，必要时比较 `512` |
| label mapping | COCO 80 类顺序校验 |
| provider | CPU baseline，GPU/provider 只做性能补充 |

核心指标：

- `AP50`
- `Precision@50`
- `Recall@50`
- label order check
- `AnnotationSeeded=false`
- decode failure count
- provider fallback count

晋级门禁：

- 500-case COCO subset `AP50 >= 0.45`。
- `Precision@50 >= 0.45`。
- `Recall@50 >= 0.35`。
- `AnnotationSeeded=false`。
- 标签顺序校验通过。
- 真实模型 license 和 SHA256 必须进 manifest；模型文件不进 git。

建议命令：

```powershell
dotnet build quality/tools/DeepLearningCocoRealModelRunner/DeepLearningCocoRealModelRunner.csproj -v minimal --no-restore
dotnet run --project quality/tools/DeepLearningCocoRealModelRunner/DeepLearningCocoRealModelRunner.csproj -- --dataset quality/public_datasets/coco2017 --case-limit 120 --output quality/reports/deep_learning_coco_contract_smoke.json
```

真实模型存在时运行：

```powershell
dotnet run --project quality/tools/DeepLearningCocoRealModelRunner/DeepLearningCocoRealModelRunner.csproj -- --dataset quality/public_datasets/coco2017 --model quality/models/object_detection/yolo11n.onnx --case-limit 500 --min-ap50 0.45 --min-precision-at-50 0.45 --min-recall-at-50 0.35 --output quality/reports/deep_learning_coco_yolo11n_500.json
```

产物：

- `quality/reports/deep_learning_coco_contract_smoke.json`
- `quality/reports/deep_learning_coco_yolo11n_120.json`
- `quality/reports/deep_learning_coco_yolo11n_500.json`
- `models/object_detection/coco_yolo_real_model_manifest.template.json` 的本地填写版本，不提交模型。

### 4. EdgeDetection：BSDS500 冻结测试

数据：`quality/public_datasets/bsds500`。

现状判断：BSDS500 虽小，但适合作为边缘检测冻结测试。当前没有 BIPEDv2/UDED，因此本轮重点不做 ONNX edge candidate 的完整结论，而是把 Canny recall-safe profile 做扎实。

候选 profile：

| Profile | 参数方向 | 目的 |
| --- | --- | --- |
| `canny_default` | 当前默认 Canny | 锁定默认行为 |
| `canny_fixed_low` | 较低阈值 | 提升召回 |
| `canny_recall_guard_percentile` | `AutoThresholdStrategy=RecallGuardPercentile` | 防止弱边缘漏检 |
| `canny_otsu_gradient` | `AutoThresholdStrategy=OtsuGradient` | 自动阈值候选 |
| `onnx_edge_default_off` | 仅保留接口 | 等 BIPED/UDED 或外部模型后再评估 |

建议 sweep 范围：

| 参数 | 候选值 |
| --- | --- |
| low/high threshold | `35/105`, `40/120`, `45/135`, `50/150` |
| `L2Gradient` | `false`, `true` |
| `AutoThresholdStrategy` | `None`, `RecallGuardPercentile`, `OtsuGradient` |
| `ApertureSize` | `3`, `5` |

核心指标：

- `BoundaryF1`
- boundary precision
- boundary recall
- recall delta vs baseline
- over-edge ratio
- processing failure count

晋级门禁：

- Canny candidate 的 BSDS replay recall 下降不超过 `0.01`。
- `BoundaryF1` 不低于 baseline。
- over-edge ratio 不显著恶化。
- 默认仍保持 `Method=Canny`。
- `Method=OnnxEdge` 只保留 default-off，不做晋级。

建议命令：

```powershell
dotnet build quality/tools/BsdsEdgeContourDatasetRunner/BsdsEdgeContourDatasetRunner.csproj -v minimal --no-restore
dotnet run --project quality/tools/BsdsEdgeContourDatasetRunner/BsdsEdgeContourDatasetRunner.csproj -- --dataset quality/public_datasets/bsds500 --output quality/reports/edge_bsds500_canny_baseline.json
```

产物：

- `quality/reports/edge_bsds500_canny_baseline.json`
- `quality/reports/edge_bsds500_canny_candidate_ab.json`
- `quality/reports/edge_bsds500_recall_safe_profile.md`

### 5. Matching / Planar / Template：HPatches 主数据

数据：`quality/public_datasets/hpatches`。

现状判断：`hpatches` 是当前体量最大且质量最高的公开数据之一，适合把匹配类算子的精度证据继续做深。它不直接覆盖检测/异常/边缘，但能提升定位、配准、模板、平面检测等视觉基础能力。

候选方向：

| 方向 | 调优项 | 目标 |
| --- | --- | --- |
| feature matching | ORB/AKAZE ratio、cross-check、top-k | 提升弱纹理和视角变化匹配 |
| planar matching | RANSAC reprojection threshold、min inliers | 降低误 homography |
| template-to-plane bridge | 匹配点过滤、边界裁剪 | 改善模板定位稳定性 |
| replay guard | baseline/candidate diff | 防止高难度序列回退 |

核心指标：

- matching pass rate
- homography inlier ratio
- P50/P95 corner error
- sequence-level fail count
- illumination subset delta
- viewpoint subset delta

晋级门禁：

- HPatches overall pass rate 不低于 baseline。
- P95 corner error 下降，或在 pass rate 提升时不恶化。
- viewpoint 子集不能回退超过 `1%`。
- replay regression count 为 `0`。

建议命令：

```powershell
dotnet build quality/tools/HPatchesFeatureMatchDatasetRunner/HPatchesFeatureMatchDatasetRunner.csproj -v minimal --no-restore
dotnet run --project quality/tools/HPatchesFeatureMatchDatasetRunner/HPatchesFeatureMatchDatasetRunner.csproj -- --dataset quality/public_datasets/hpatches --output quality/reports/hpatches_matching_baseline.json
```

产物：

- `quality/reports/hpatches_matching_baseline.json`
- `quality/reports/hpatches_matching_candidate_ab.json`
- `quality/reports/hpatches_matching_family_leaderboard.md`

### 6. CameraCalibration：OpenCV samples smoke

数据：`quality/public_datasets/opencv_calibration_samples`。

现状判断：该数据集非常小，不适合做精度上限结论，但适合作为标定几何 contract 的轻量回归。

核心指标：

- reprojection error
- board detection success
- intrinsic matrix sanity
- distortion coefficient sanity
- repeated run stability

晋级门禁：

- 不能出现 board detection failure 增加。
- reprojection error 不高于 baseline。
- 标定输出必须保持 deterministic 或在容差内稳定。

建议命令：

```powershell
dotnet build quality/tools/OpenCvCalibrationDatasetRunner/OpenCvCalibrationDatasetRunner.csproj -v minimal --no-restore
dotnet run --project quality/tools/OpenCvCalibrationDatasetRunner/OpenCvCalibrationDatasetRunner.csproj -- --dataset quality/public_datasets/opencv_calibration_samples --output quality/reports/opencv_calibration_samples_baseline.json
```

## 执行阶段

### Phase 0：数据和 baseline 冻结

- [ ] 确认 `quality/public_datasets/` 下只使用本计划列出的 6 类数据。
- [ ] 运行 index builder，确认 manifest、SHA256、license 字段完整。
- [ ] 生成本轮 dataset inventory，记录总量 `7.743 GB`。
- [ ] 运行 `public_benchmark_suite --validate-only`，确认 suite 配置可解析。
- [ ] 运行 `dataset_heavy_suite --dry-run`，确认 heavy runner 不触发新下载。

命令：

```powershell
python quality/datasets/build_public_dataset_indexes.py --root quality/public_datasets
python quality/tools/run_quality_suite.py --suite public_benchmark_suite --validate-only
python quality/tools/run_quality_suite.py --suite dataset_heavy_suite --dry-run
```

### Phase 1：默认行为 baseline

- [ ] KSDD2 生成 `SurfaceDefectDetection` baseline。
- [ ] MVTec AD Lite 生成 `AnomalyDetection` baseline。
- [ ] COCO 生成 deep learning contract smoke。
- [ ] BSDS500 生成 Canny baseline。
- [ ] HPatches 生成 matching baseline。
- [ ] OpenCV samples 生成 calibration baseline。

验收：

- 每个 runner 都有 JSON 报告。
- 报告内包含 dataset id、split、case count、license、失败数。
- 没有 raw absolute path 泄漏到报告。

### Phase 2：候选 sweep

- [ ] Surface 只 sweep CLAHE、response stats、阈值、连通域面积。
- [ ] Anomaly 只 sweep patch、stride、coreset、threshold source；ONNX embedding 仅在模型存在时启用。
- [ ] DeepLearning 只 sweep confidence、NMS、label mapping；真实模型不存在时不跑 AP gate。
- [ ] Edge 只 sweep Canny threshold、L2Gradient、auto-threshold strategy。
- [ ] HPatches 只 sweep ratio、RANSAC threshold、min inliers、cross-check。

验收：

- 每个候选都有 baseline/candidate delta。
- 每个候选都有失败样本列表。
- 不出现“test 上反调阈值”。

### Phase 3：A/B replay 和失败 taxonomy

- [ ] Surface 按低对比漏检、纹理误报、过分割、正常样本误报归类。
- [ ] Anomaly 按 image-level miss、pixel-level diffuse anomaly、normal FP、threshold unstable 归类。
- [ ] DeepLearning 按 label mismatch、NMS duplicate、small object miss、provider decode failure 归类。
- [ ] Edge 按 weak boundary miss、texture over-edge、human annotation disagreement 归类。
- [ ] HPatches 按 illumination、viewpoint、low texture、homography fail 归类。

验收：

- 每类至少输出 top failure cases。
- 每个 failure case 能定位 dataset、split、case id。
- 报告明确 candidate 是否满足晋级门禁。

### Phase 4：报告和默认关闭 profile

- [ ] 更新 `QualityFlywheel_detection_precision_v3`。
- [ ] 将通过门禁的候选写成 default-off profile。
- [ ] 没通过门禁的候选保留在 advisory，不进入默认路径。
- [ ] 文档注明数据集许可和评估边界。

命令：

```powershell
python quality/tools/build_detection_precision_v3.py
```

验收：

- v3 报告里每个结论都有数据集、metric、runner、时间戳。
- 所有候选明确“是否建议进入 default-off profile”。
- 没有任何结论声明真实产线签核完成。

### Phase 5：是否补数据的决策点

本轮跑完后再决定是否恢复下载大数据：

| 缺口 | 需要补的数据 | 触发条件 |
| --- | --- | --- |
| 异常检测完整公开结论 | `MVTec AD full` / `MVTec LOCO AD` | Lite candidate 明显优于 baseline，且 runner 已稳定 |
| ONNX edge candidate 训练/校准 | `BIPEDv2` / `UDED` | Canny recall-safe 已冻结，且需要神经网络边缘候选 |
| 深度学习真实 AP | YOLO11n 或等价 ONNX | COCO contract smoke 全部通过 |
| AD2 public-part advisory | `MVTec AD2 public part` | MVTec full/LOCO 已形成稳定门禁后再加 |

## 统一门禁

| 方向 | 本轮主数据 | 晋级目标 | 必须满足 |
| --- | --- | --- | --- |
| `SurfaceDefectDetection` | `kolektorsdd2` | default-off precision candidate | `PixelF1 +0.03`，`FP/normal <= 0.06`，fixed-FPR recall 不下降 |
| `AnomalyDetection` | `mvtec_ad_lite` | advisory candidate | AUROC 不低于 baseline，阈值不从 test 反调，失败数为 `0` |
| `DeepLearning` | `coco2017` | real-model gate 或 contract gate | 真实模型时 `AP50 >= 0.45`；无模型时只通过 contract smoke |
| `EdgeDetection` | `bsds500` | Canny recall-safe profile | recall 下降不超过 `0.01`，`BoundaryF1` 不低于 baseline |
| Matching / Planar | `hpatches` | matching family candidate | pass rate 不下降，P95 corner error 不恶化，regression count 为 `0` |
| Calibration | `opencv_calibration_samples` | geometry smoke | reprojection error 不升，输出稳定 |

## 构建和测试

构建：

```powershell
dotnet build ClearVision.OperatorLibrary/ClearVision.OperatorLibrary.csproj -v minimal --no-restore
```

目标单测按项目串行运行，避免同一 `.csproj` 并发：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.OperatorLibrary.Tests/ClearVision.OperatorLibrary.Tests.csproj" -FullyQualifiedName SurfaceDefectDetectionOperatorTests,AnomalyDetectionOperatorTests,DeepLearningOperatorTests,EdgeDetectionOperatorTests -NoBuild -NoRestore
```

数据和 suite 校验：

```powershell
python quality/tools/run_quality_suite.py --suite public_benchmark_suite --validate-only
python quality/tools/run_quality_suite.py --suite dataset_heavy_suite --dry-run
python quality/tools/run_quality_suite.py --suite algorithm_improvement_suite --dry-run
```

## 风险

| 风险 | 影响 | 处理 |
| --- | --- | --- |
| `mvtec_ad_lite` 覆盖不足 | 异常检测结论偏弱 | 只做 advisory，不升级默认阈值 |
| 缺少 `BIPEDv2/UDED` | ONNX edge candidate 无法完整评估 | 本轮只冻结 Canny recall-safe |
| 缺少真实 YOLO ONNX | COCO 只能做 contract smoke | 模型存在前不声明 AP |
| KSDD2 类别单一 | Surface 泛化有限 | 报告明确只覆盖 KSDD2；后续再补 MVTec full |
| public dataset license 边界 | 商业使用受限 | 报告和 manifest 保留 license，raw/model 不进 git |
| sweep 过宽 | 调优成本过高且容易过拟合 | 每类只保留少量高收益参数 |

## 完成定义

- [x] 当前 6 类数据集 manifest/index 纳入本轮公开数据范围。
- [x] baseline 报告已生成并进入 v3 汇总证据链。
- [x] Surface、Edge、HPatches 三条主线已完成候选证据或暂停结论：Surface 保留 taxonomy_v2/targeted evidence，Edge 暂停晋升并转为 recall-not-lower profile，HPatches 形成 Matching default-off profile。
- [x] Anomaly 和 DeepLearning 在缺 full 数据/缺真实模型时输出明确 advisory/blocker，不输出过度结论。
- [x] `QualityFlywheel_detection_precision_v3` 已更新，并保留 claim boundary。
- [x] 目标构建、目标单测和报表 validate-only 已通过；原始 suite dry-run 未作为本轮关闭阻塞项重跑。
- [x] 没有 raw dataset、模型权重、大文件进入本轮计划归档。

## 2026-05-01 收口回填

本计划于 2026-05-01 收口，关闭口径为：`default-off candidates ready; default-on blocked by required release/field replay packet`。本轮完成的是候选 profile 和晋升治理闭环，不声明产品默认开启、真实产线 sign-off 或工业现场最终精度通过。

### 已落地内容

| 方向 | 收口状态 | 说明 |
| --- | --- | --- |
| `AnomalyDetection` | default-off candidate ready with FP tradeoff | `mvtec_lite_v2` 默认关闭；配置开关、兼容性检查、`UseDefault/Fail` 回退路径已接入；代码 profile 对齐证据参数 `PatchSize=16`、`PatchStride=8`、`CoresetRatio=0.02`、`Threshold=0.10`。 |
| `Matching / HPatches` | default-off candidates ready | `OrbFeatureMatch/replay_safe_dense_strict` 作为主候选，`AkazeFeatureMatch/default_v3` 作为 opt-in evidence profile；均不改产品默认。 |
| `SurfaceDefectDetection` | hold current targeted evidence | 已按 taxonomy 聚焦 `texture_noise_false_positive`、`low_contrast_defect_miss`、`undersegmentation_false_negative`，不再盲目降全局阈值；当前未作为默认晋升依据。 |
| `EdgeDetection` | paused | 不按 F1 单点晋升；下一轮只接受 `recall 不降` 的候选 profile。 |
| `DeepLearning` | blocked external model | real-model 阻塞从主线 validation 隔离；无真实 ONNX artifact 时不声明 COCO AP。 |
| `CameraCalibration` | stable smoke | 保留 OpenCV samples geometry smoke，不作为生产标定签核。 |

### 关键证据报表

- `quality/evals/reports/QualityFlywheel_matching_default_off_profiles_v3.json`
- `quality/evals/reports/QualityFlywheel_matching_default_off_profiles_v3.md`
- `quality/evals/reports/QualityFlywheel_candidate_release_field_replay_gate_v1.json`
- `quality/evals/reports/QualityFlywheel_candidate_release_field_replay_gate_v1.md`
- `quality/evals/reports/QualityFlywheel_candidate_profile_governance_v1.json`
- `quality/evals/reports/QualityFlywheel_candidate_profile_governance_v1.md`
- `quality/evals/reports/QualityFlywheel_detection_precision_v3.json`
- `quality/evals/reports/QualityFlywheel_detection_precision_v3.md`

### 签下的 release/field replay 门禁标准

`AnomalyDetection/mvtec_lite_v2`：

- FP delta 上限：`+3`。
- normal FPR 上限：`0.10`。
- image precision 下限：`0.95`。
- 当前公开 lite 证据：FP delta `3`，normal FPR `0.0909`，precision `0.958333`，在签核标准内。
- 仍需真实 release/field replay packet 补齐 FP severity review 和 fallback 结果。

`OrbFeatureMatch/replay_safe_dense_strict`：

- runtime delta 上限：`+5ms/case`。
- runtime delta percent 上限：`+25%`。
- candidate mean runtime 上限：`30ms/case`。
- 当前公开 HPatches 证据：`+4.713ms/case`，`+21.812%`，candidate mean `26.323ms/case`，在签核标准内。
- 仍需真实 release/field replay packet 提供 pinned hardware profile。

### 验证记录

- `dotnet build ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj -v minimal --no-restore` 通过，只有既有 warning。
- 单进程目标测试通过：`AnomalyDetectionOperatorTests` 与 `FeatureMatchOperatorBaseTests` 共 `19` 个测试，`0` failed。
- `python -m py_compile` 覆盖本轮新增/更新报表脚本，通过。
- 以下报表 `--validate-only` 通过：
  - `quality/tools/build_matching_default_off_profile_report.py`
  - `quality/tools/build_candidate_release_field_replay_gate.py`
  - `quality/tools/build_candidate_profile_governance.py`
  - `quality/tools/build_detection_precision_v3.py`
- `git diff --check` 通过。

### 后续重开条件

只有出现以下任一条件时，才建议从归档中重开或新建计划：

- 提供真实 release/field replay packet，且包含候选 profile 显式启用、同 build/hardware baseline、脱敏 manifest、per-case FP/FN/runtime/fallback 诊断。
- 准备推进 `mvtec_lite_v2` 或 `replay_safe_dense_strict` 的 default-on 评审。
- 提供 MVTec full/LOCO、BIPED/UDED 或真实 COCO ONNX artifact，足以支撑新一轮完整精度结论。

## 结论

以当前 `7.743 GB` 数据集，本轮最值得优先投入的是 `SurfaceDefectDetection`、`EdgeDetection` 和 HPatches 覆盖的匹配类基础能力；这三类可以形成较强的公开数据门禁。`AnomalyDetection` 和 `DeepLearning` 当前也能推进 runner、阈值、contract 和模型接口，但完整精度结论分别受限于 MVTec full/LOCO 和真实 ONNX 模型。整体策略是先把现有数据榨干，形成可复跑、可比较、可回退的 precision flywheel，再决定是否继续下载大数据。
