# 第5批 SurfaceDefectDetection 准工业算法调优报告

**生成时间**：2026-05-01T04:56:01+00:00

## 1. 结论

本轮把 SurfaceDefectDetection 接入 executable candidate replay，并在 KolektorSDD2 上形成 validation sweep、test candidate、failure taxonomy 与 A/B replay 证据链。报告只声明准工业公开/替代证明，不声明真实产线签核。

## 2. 结果

| Metric | Baseline | Candidate |
|---|---:|---:|
| Pixel F1 | 0.2692 | 0.2692 |
| Image AUROC | 0.7724 | 0.7724 |
| Image F1 | 0.5671 | 0.5671 |
| FP/normal | 0.1398 | 0.1398 |

## 3. A/B replay

- Replay cases: 20
- Improved metric cases: 0
- Regressed cases: 0
- Worse metric cases: 0

## 4. 失败 taxonomy

| Taxonomy | Count |
|---|---:|
| low_contrast_defect_miss | 15 |
| mask_overgrowth_false_positive | 1 |
| oversegmentation_false_positive | 2 |
| small_defect_miss | 2 |
| texture_noise_false_positive | 123 |
| undersegmentation_false_negative | 46 |

## 5. 证据文件

- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_baseline.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_candidate_v2.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_sweep_v2.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v2.json`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_component_telemetry_v2.csv`
- `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_component_distribution_v2.csv`
- `quality/evals/reports/QualityFlywheel_surface_defect_component_rule_selector_v2.json`
- `quality/evals/reports/QualityFlywheel_algorithm_ab_replay_report.json`
