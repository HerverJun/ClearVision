---
title: "ClearVision 算子精度提高下一步计划"
doc_type: "plan"
status: "active"
topic: "operator-accuracy-next-step"
created: "2026-04-30"
updated: "2026-04-30"
source_plan: "docs/归档/已关闭事项/2026-04-30-当前计划闭环归档/ClearVision-准工业算法调优TODO-2026-04-29.md"
claim_boundary: "准工业公开/替代证明；不声明真实产线工业验证完成"
---

# ClearVision 算子精度提高下一步计划

## 0. 接续状态

上一轮 `QualityFlywheel_algorithm_ab_replay_report.json` 已达到：

| 指标 | 当前值 |
|---|---:|
| replayCaseCount | 183 |
| comparedCaseCount | 183 |
| candidatePendingCount | 0 |
| executedCandidateCaseCount | 160 |
| controlCaseCount | 23 |
| regressedCaseCount | 0 |

剩余 `23` 个 control case 只保留两类：

- `CameraCalibration`：3 cases，优先做 OpenCV calibration sample bridge。
- `DeepLearning`：20 cases，必须接真实模型 artifact/manifest 后才进入 real-model candidate。

## 1. 本轮目标

- [ ] 将 `CameraCalibration` 3 cases 从 control 推进到 candidate-executed。
- [ ] 为 `DeepLearning` 建立真实模型 artifact/manifest 接入路径，不再使用 annotation-seeded 口径表达模型精度。
- [ ] 在 A/B replay 中保持 `candidatePendingCount=0`、`regressedCaseCount=0`。
- [ ] 若真实模型 artifact 已就绪，将 `executedCandidateCaseCount` 从 `160` 推到 `180+`；若未就绪，则先完成 DeepLearning real-model dry-run gate 与文档化阻塞原因。

## 2. Phase A：CameraCalibration Bridge

目标：把剩余 `CameraCalibration` 3 cases 接成可执行 candidate runner。

### A1. 数据与口径

- [ ] 复用 OpenCV calibration sample 或 repo-local synthetic chessboard/circle-grid protocol。
- [ ] 明确标注为 public/sample 或 semi-synthetic bridge，不声明真实相机产线标定完成。
- [ ] 固定 case ids，与 replay manifest 中的 `CameraCalibration` 3 cases 一一对应。

### A2. Runner

- [ ] 新增或扩展 `CameraCalibration` runner，支持：
  - `--case-ids`
  - `--candidate-version`
  - `--profile`
  - `--output`
  - `--report`
- [ ] 输出 per-case `old/new/delta/status/executionMode` 所需指标。
- [ ] 至少包含：
  - reprojection error
  - detected corner count / expected corner count
  - image count
  - accepted flag
  - runtime / memory

### A3. A/B 接入

- [ ] 在 `run_algorithm_ab_replay.py` 增加 `--execute-camera-calibration`。
- [ ] 新增 summary 字段：
  - `cameraCalibrationCaseCount`
  - `cameraCalibrationRegressedCaseCount`
  - `cameraCalibrationWorseMetricCaseCount`
- [ ] Markdown 增加 `CameraCalibration Focus`。
- [ ] Schema/audit gate 在 CameraCalibration 接入后至少提升到 `executedCandidateCaseCount >= 163`。

### A4. 验收

```powershell
python quality/tools/run_algorithm_ab_replay.py --execute-camera-calibration
python quality/tools/run_algorithm_ab_replay.py --validate-only
python quality/tools/run_quality_suite.py --suite audit_suite --run
```

Gate：

| 指标 | Gate |
|---|---:|
| CameraCalibration executed cases | 3 |
| CameraCalibration regressed cases | 0 |
| CameraCalibration worse metric cases | 0，或必须写明 taxonomy |
| raw path leak | 0 |

## 3. Phase B：DeepLearning Real-Model Artifact

目标：将 `DeepLearning` 20 cases 从 control 推进到真实模型 candidate；只声明后处理链路与真实推理接入，不声明训练收益。

### B1. Artifact/Manifest 准入

- [ ] 明确真实 ONNX artifact 的本机路径不入库。
- [ ] manifest 只记录脱敏元数据：
  - model family
  - input size
  - output tensor shape
  - preprocessing
  - postprocess profile
  - checksum 或外部 artifact id
- [ ] 若 artifact 缺失，A/B 报告必须保持 DeepLearning control，并在计划中记录 blocked reason。

### B2. Candidate Profile

- [ ] 固定 profile 名：`real_model_hard_nms_045` 或后续版本。
- [ ] A/B delta 只覆盖：
  - NMS variants
  - letterbox 坐标反算
  - clamp policy
  - confidence threshold
  - runtime / memory
- [ ] 禁止写“模型训练收益”“权重精度提升”。

### B3. A/B 接入

- [ ] 使用已有 `--execute-deep-learning` 路径接真实 artifact。
- [ ] 每个 case 输出：
  - DetectionCount
  - TruePositiveCount
  - FalsePositiveCount
  - FalseNegativeCount
  - ProcessingError
  - OutputTensorName / OutputTensorShape
  - AnnotationSeeded=false
- [ ] Schema/audit gate 在 artifact 就绪后提升到 `executedCandidateCaseCount >= 183`。

### B4. 验收

```powershell
python quality/tools/run_algorithm_ab_replay.py --execute-deep-learning --deep-learning-model-manifest <manifest> --deep-learning-model <onnx>
python quality/tools/run_algorithm_ab_replay.py --validate-only
python quality/tools/run_quality_suite.py --suite audit_suite --run
```

Gate：

| 指标 | Gate |
|---|---:|
| DeepLearning real-model candidate cases | 20 |
| ProcessingError cases | 0 |
| AnnotationSeeded | false |
| regressed cases | 0，或必须有明确风险说明 |
| raw path leak | 0 |

## 4. Phase C：Release 级收口

- [ ] A/B replay report 更新。
- [ ] Audit report 更新。
- [ ] 如要对外发布，刷新 public benchmark proof 与 155 registry。
- [ ] 当前计划归档前，明确最终 control cases 是否为 `0`；若不为 `0`，写明阻塞原因与下一计划。

## 5. 暂停条件

- [ ] DeepLearning artifact 路径、客户路径、站点名、序列号进入报告。
- [ ] `candidatePendingCount > 0`。
- [ ] `regressedCaseCount > 0` 且无 taxonomy。
- [ ] CameraCalibration 使用 test split 反复调参但未重开 proof version。
- [ ] DeepLearning 把 annotation-seeded 或 postprocess-only 结果写成模型精度。
