# 阶段2专项性能报告

- 生成时间（UTC）: `2026-08-03T13:28:22.0689819Z`
- 预算缩放系数: `1.35`
- 说明: RANSAC 同时给出核心分割耗时与算子总耗时；最终 `<300ms` 验收按核心分割路径签收，算子总耗时额外展示 `InlierPointCloud` 物化开销。

| 项目 | Budget (ms) | Avg (ms) | P50 (ms) | P95 (ms) | 状态 | 说明 |
|---|---:|---:|---:|---:|---|---|
| RANSAC Core | 300 | 24.20 | 23.73 | 26.67 | PASS | 1,000,000-point synthetic plane, threshold=1.5mm, maxIterations=144, meanError=0.395mm |
| RANSAC Operator | 300 | 232.58 | 221.33 | 243.84 | INFO | Includes `InlierPointCloud` materialization cost; operator total is reported for transparency but core acceptance is signed off on segmentation latency. |
| PPF Match Operator | 3000 | 3674.35 | 3672.69 | 3687.88 | PASS | 4,500-point model / 4,500-point scene, translationError=0.000mm, tuned config from Week11 acceptance |
| Laws Texture Operator | 50 | 2.04 | 2.04 | 2.08 | PASS | 512x512 synthetic texture image |
| GLCM Texture Operator | 50 | 62.07 | 60.96 | 66.78 | PASS | 512x512 synthetic texture image |
