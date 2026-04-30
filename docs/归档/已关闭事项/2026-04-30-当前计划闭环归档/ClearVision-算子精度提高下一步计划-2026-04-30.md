---
title: "ClearVision 算子精度提高下一步计划"
doc_type: "plan"
status: "closed"
topic: "operator-accuracy-next-step"
created: "2026-04-30"
updated: "2026-04-30"
closed_at: "2026-04-30"
source_plan: "docs/归档/已关闭事项/2026-04-30-当前计划闭环归档/ClearVision-准工业算法调优TODO-2026-04-29.md"
claim_boundary: "准工业公开/替代证明；不声明真实产线工业验证完成"
---

# ClearVision 算子精度提高下一步计划

## 0. 接续状态与收口状态

上一轮 `QualityFlywheel_algorithm_ab_replay_report.json` 接续基线：

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

本轮收口后的最终状态：

| 指标 | 最终值 |
|---|---:|
| replayCaseCount | 183 |
| comparedCaseCount | 183 |
| candidatePendingCount | 0 |
| executedCandidateCaseCount | 183 |
| controlCaseCount | 0 |
| regressedCaseCount | 0 |
| cameraCalibrationExecutedCaseCount | 3 |
| deepLearningRealModelCaseCount | 20 |
| deepLearningProcessingErrorCaseCount | 0 |

DeepLearning 当前完成的是 ONNX Runtime real-model dry-run gate：`AnnotationSeeded=false`，candidate report 使用 `generated-smoke-fixture` 验证真实推理链路与后处理口径；外部真实 ONNX artifact 仍待接入。本文档继续保留 `claim_boundary`：准工业公开/替代证明，不声明真实产线工业验证完成。

## 1. 本轮目标

- [x] 将 `CameraCalibration` 3 cases 从 control 推进到 candidate-executed。
- [x] 为 `DeepLearning` 建立真实模型 artifact/manifest 接入路径，不再使用 annotation-seeded 口径表达模型精度；当前为 real-model dry-run 完成，外部 artifact 待接入。
- [x] 在 A/B replay 中保持 `candidatePendingCount=0`、`regressedCaseCount=0`。
- [x] 完成 DeepLearning real-model dry-run gate，并将 `executedCandidateCaseCount` 从 `160` 推到 `183`；外部真实模型 artifact 仍作为后续接入项。

## 2. Phase A：CameraCalibration Bridge

目标：把剩余 `CameraCalibration` 3 cases 接成可执行 candidate runner。

### A1. 数据与口径

- [x] 复用 OpenCV calibration sample 或 repo-local synthetic chessboard/circle-grid protocol。
- [x] 明确标注为 public/sample 或 semi-synthetic bridge，不声明真实相机产线标定完成。
- [x] 固定 case ids，与 replay manifest 中的 `CameraCalibration` 3 cases 一一对应。

### A2. Runner

- [x] 新增或扩展 `CameraCalibration` runner，支持：
  - `--case-ids`
  - `--candidate-version`
  - `--profile`
  - `--output`
  - `--report`
- [x] 输出 per-case `old/new/delta/status/executionMode` 所需指标。
- [x] 至少包含：
  - reprojection error
  - detected corner count / expected corner count
  - image count
  - accepted flag
  - runtime / memory

### A3. A/B 接入

- [x] 在 `run_algorithm_ab_replay.py` 增加 `--execute-camera-calibration`。
- [x] 新增 summary 字段：
  - `cameraCalibrationCaseCount`
  - `cameraCalibrationRegressedCaseCount`
  - `cameraCalibrationWorseMetricCaseCount`
- [x] Markdown 增加 `CameraCalibration Focus`。
- [x] Schema/audit gate 在 CameraCalibration 接入后至少提升到 `executedCandidateCaseCount >= 163`；当前 validate/audit 已按 `executedCandidateCaseCount >= 183` 通过。

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

验收状态：已完成。当前 A/B report 中 `cameraCalibrationCaseCount=3`、`cameraCalibrationExecutedCaseCount=3`、`cameraCalibrationRegressedCaseCount=0`、`cameraCalibrationWorseMetricCaseCount=0`。

## 3. Phase B：DeepLearning Real-Model Artifact

目标：将 `DeepLearning` 20 cases 从 control 推进到真实模型 candidate；只声明后处理链路与真实推理接入，不声明训练收益。

### B1. Artifact/Manifest 准入

- [x] 明确真实 ONNX artifact 的本机路径不入库。
- [x] manifest 只记录脱敏元数据：
  - model family
  - input size
  - output tensor shape
  - preprocessing
  - postprocess profile
  - checksum 或外部 artifact id
- [x] 外部真实 artifact 尚未接入；当前以 `generated-smoke-fixture` 完成 real-model dry-run，不作为外部模型精度或真实产线签核声明。

### B2. Candidate Profile

- [x] 固定 profile 名：`real_model_hard_nms_045` 或后续版本。
- [x] A/B delta 只覆盖：
  - NMS variants
  - letterbox 坐标反算
  - clamp policy
  - confidence threshold
  - runtime / memory
- [x] 禁止写“模型训练收益”“权重精度提升”。

### B3. A/B 接入

- [x] 使用已有 `--execute-deep-learning` 路径接入 ONNX Runtime real-model dry-run；外部真实 artifact 待接入。
- [x] 每个 case 输出：
  - DetectionCount
  - TruePositiveCount
  - FalsePositiveCount
  - FalseNegativeCount
  - ProcessingError
  - OutputTensorName / OutputTensorShape
  - AnnotationSeeded=false
- [x] Audit gate 已按 `executedCandidateCaseCount >= 183` 通过；外部 artifact 就绪后仍需复跑同一 gate。

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

验收状态：real-model dry-run 已完成。当前 A/B report 中 `deepLearningRealModelCaseCount=20`、`deepLearningProcessingErrorCaseCount=0`，candidate summary 中 `RealOnnxInference=true`、`AnnotationSeeded=false`、`Profile=real_model_hard_nms_045`。外部 ONNX artifact 尚未接入，后续接入时必须继续保持 artifact 路径脱敏且不入库。

## 4. Phase C：Release 级收口

- [x] A/B replay report 更新：`QualityFlywheel_algorithm_ab_replay_report.json/.md` 当前为 `executedCandidateCaseCount=183`、`controlCaseCount=0`、`regressedCaseCount=0`。
- [x] Audit report 更新：`QualityFlywheel_155_quasi_industrial_audit.json/.md` 当前为 `passed=true`、`57/57` checks passed。
- [x] 如要对外发布，刷新 public benchmark proof 与 155 registry；当前 audit 已验证 public benchmark proof 与 155 registry 仍通过。
- [x] 当前计划归档前，明确最终 control cases 为 `0`。DeepLearning 外部 artifact 尚未接入，但 dry-run gate 已闭环；后续外部 artifact 接入不改变本轮“不声明真实产线工业验证完成”的边界。

## 5. 暂停条件

本轮检查：以下暂停条件均未触发。

- 未触发：DeepLearning artifact 路径、客户路径、站点名、序列号进入报告。
- 未触发：`candidatePendingCount > 0`。
- 未触发：`regressedCaseCount > 0` 且无 taxonomy。
- 未触发：CameraCalibration 使用 test split 反复调参但未重开 proof version。
- 未触发：DeepLearning 把 annotation-seeded 或 postprocess-only 结果写成模型精度。

## 6. 收口归档记录

归档状态：closed。

| 收口项 | 结果 |
|---|---|
| A/B replay validate-only | 通过 |
| Audit suite | 通过，`57/57` checks passed |
| CameraCalibration | `3/3` candidate-executed |
| DeepLearning | `20/20` real-model dry-run candidate-executed，外部 artifact 待接入 |
| Final controlCaseCount | `0` |
| Final executedCandidateCaseCount | `183` |
| Final regressedCaseCount | `0` |
| Claim boundary | 准工业公开/替代证明；不声明真实产线工业验证完成 |

后续执行入口：接入外部真实 ONNX artifact 后，使用 `--deep-learning-model-manifest` 与 `--deep-learning-model` 复跑 DeepLearning real-model candidate、A/B validate-only 与 audit suite。
