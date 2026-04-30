# 算法 A/B 回放报告 v2 Schema 说明

`QualityFlywheel_algorithm_ab_replay_report.json` 使用 `2026-04-29.algorithm-ab-replay.v2` schema，schema 文件位于：

```text
quality/evals/schemas/algorithm_ab_replay_report.v2.schema.json
```

## 强制口径

- `candidatePendingCount` 必须为 `0`。
- `comparedCaseCount` 必须等于 `replayCaseCount`。
- 下一阶段 `executedCandidateCaseCount` 门槛为 `>= 160`。
- `old`、`new`、`delta`、`status`、`executionMode` 必须在每个 replay case 中同时存在。
- 报告不得包含本机绝对路径、客户名、站点名或序列号等隐私/现场信息。
- 报告只支持“准工业公开/替代证明”口径，不代表真实产线签核完成。

## 当前 candidate-executed 范围

- `AkazeFeatureMatch` / `OrbFeatureMatch`：HPatches candidate v4。
- `TemplateMatching`：HPatches-style homography bridge candidate v1。
- `ShapeMatching`：semi-synthetic geometric dataset bridge candidate v1。
- `SurfaceDefectDetection`：KolektorSDD2 candidate v1。
- `AnomalyDetection`：MVTec AD Lite candidate v1。
- `EdgeDetection`：BSDS500 candidate v1。
- `SemanticSegmentation`：VOC-style protocol bridge candidate v1。
- `DeepLearning`：仅在显式提供真实模型 manifest/artifact 时进入 real-model candidate；默认 `--execute-candidates` 不混入模型精度口径。

## 验收命令

```powershell
python quality/tools/run_algorithm_ab_replay.py --validate-only
python quality/tools/run_quality_suite.py --suite audit_suite --run
```
