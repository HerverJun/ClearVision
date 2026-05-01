# 第6批 AnomalyDetection 准工业算法调优报告

GeneratedAtUtc: `2026-04-29T16:13:54+00:00`

## Scope

- Operator: `AnomalyDetection`
- Dataset: `MVTec AD Lite`
- Claim boundary: public benchmark quasi-industrial evidence; no real field sign-off claim.

## Result

- Image AUROC: `0.6609` -> `0.9178`
- Pixel AUROC: `0.6709` -> `0.8692`
- A/B anomaly replay score-improved: `14` / `20`
- A/B anomaly replay detected/image-correct: `5`
- A/B anomaly replay regressed: `0`
- Remaining missed anomalies in full candidate: `32`

## Evidence

- `quality/evals/reports/AnomalyDetection_mvtec_candidate_v1.json`
- `quality/evals/reports/AnomalyDetection_mvtec_sweep_v1.json`
- `quality/evals/reports/AnomalyDetection_mvtec_failure_taxonomy_v1.json`
- `quality/evals/reports/QualityFlywheel_anomaly_detection_algorithm_improvement_v1.json`
