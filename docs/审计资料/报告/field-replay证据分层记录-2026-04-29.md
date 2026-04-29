---
title: "Field Replay 证据分层记录"
doc_type: "audit-evidence"
status: "active"
created: "2026-04-29"
updated: "2026-04-29"
---

# Field Replay 证据分层记录

## 分层口径

| 标签 | 含义 | 可用于 |
|---|---|---|
| `field-substitute` | 匿名化、合成化或公开数据桥接后的现场替代样本 | 验证 replay 机制、triage 元数据、回归化流程 |
| `internal-lab` | 内部实验室设备或受控样件数据 | 验证硬件/光学/标定链路，不等同客户现场 |
| `real-field` | 脱敏后的真实现场样本，带来源、manifest、复现命令和 triage 标签 | 发布风险评估与现场问题闭环 |

## 当前覆盖

来源 manifest：`quality/field_replay/manifests/field_replay_manifest.json`
当前报告：`quality/evals/reports/field_replay_drill_2026_04_01.md`

| Operator | 当前标签 | 样本数 | 来源说明 | 复现入口 |
|---|---|---:|---|---|
| DeepLearning | `field-substitute` | 20 | 匿名化推理失败模式种子，覆盖 low-confidence、overlap、provider fallback、large-frame pressure | `dataset_heavy_suite:deep_learning_detection_dataset` |
| TemplateMatching | `field-substitute` | 20 | 匹配定位替代样本，覆盖 low-texture、homography-edge、rotation-scale、blank-negative | `dataset_heavy_suite:template_matching_public_bridge` |
| CaliperTool | `field-substitute` | 20 | 测量替代样本，覆盖 blurred-edge、wrong-polarity、strong-noise、thin-part | `golden_core50_suite:caliper_tool_golden` |
| SurfaceDefectDetection | `field-substitute` | 20 | 表面缺陷替代样本，覆盖 low-contrast scratch、reference drift、clean negative、spot defect | `golden_core50_suite:p2_inspection_residual` |
| CameraCalibration | `field-substitute` | 20 | 标定替代样本，覆盖 missing-board、mixed-resolution、insufficient-samples、preview-only | `golden_core50_suite:calibration_geometry_round_trip` |

## 当前结论

- 已经能区分替代证据和真实现场证据。
- 5 个优先算子已有 `field-substitute` replay 种子，三次 drill 报告均在 `quality/evals/reports/` 下留存。
- 当前仍没有 `real-field` 样本。真实现场闭环需要人工提供脱敏样本、来源记录和授权边界后才能标记完成。

## 验证命令

```powershell
python quality/tools/run_quality_suite.py --suite field_replay_suite --validate-only
```
