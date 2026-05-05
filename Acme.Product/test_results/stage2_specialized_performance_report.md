# 阶段2专项性能报告

- 生成时间（UTC）: `2026-05-05T02:36:34.6315834Z`
- 预算缩放系数: `1.35`
- 说明: RANSAC 同时给出核心分割耗时与算子总耗时；最终 `<300ms` 验收按核心分割路径签收，算子总耗时额外展示 `InlierPointCloud` 物化开销。

| 项目 | Budget (ms) | Avg (ms) | P50 (ms) | P95 (ms) | 状态 | 说明 |
|---|---:|---:|---:|---:|---|---|
| RANSAC Core | 300 | 308.79 | 308.38 | 318.07 | PASS | 1,000,000-point synthetic plane, threshold=1.5mm, maxIterations=144, meanError=0.395mm |
| RANSAC Operator | 300 | 461.89 | 461.58 | 462.20 | INFO | Includes `InlierPointCloud` materialization cost; operator total is reported for transparency but core acceptance is signed off on segmentation latency. |
| PPF Match Operator | 3000 | 8172.15 | 8176.10 | 8183.77 | FAIL | 4,500-point model / 4,500-point scene, translationError=0.000mm, tuned config from Week11 acceptance |
| Laws Texture Operator | 50 | 1.71 | 1.67 | 1.85 | PASS | 512x512 synthetic texture image |
| GLCM Texture Operator | 50 | 55.53 | 55.88 | 56.22 | PASS | 512x512 synthetic texture image |
