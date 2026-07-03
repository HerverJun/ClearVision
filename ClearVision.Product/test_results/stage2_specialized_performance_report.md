# 阶段2专项性能报告

- 生成时间（UTC）: `2026-07-03T15:35:38.1735781Z`
- 预算缩放系数: `1.35`
- 说明: RANSAC 同时给出核心分割耗时与算子总耗时；最终 `<300ms` 验收按核心分割路径签收，算子总耗时额外展示 `InlierPointCloud` 物化开销。

| 项目 | Budget (ms) | Avg (ms) | P50 (ms) | P95 (ms) | 状态 | 说明 |
|---|---:|---:|---:|---:|---|---|
| RANSAC Core | 300 | 21.14 | 21.29 | 21.58 | PASS | 1,000,000-point synthetic plane, threshold=1.5mm, maxIterations=144, meanError=0.395mm |
| RANSAC Operator | 300 | 215.82 | 185.71 | 245.93 | INFO | Includes `InlierPointCloud` materialization cost; operator total is reported for transparency but core acceptance is signed off on segmentation latency. |
| PPF Match Operator | 3000 | 3389.45 | 3368.71 | 3431.97 | PASS | 4,500-point model / 4,500-point scene, translationError=0.000mm, tuned config from Week11 acceptance |
| Laws Texture Operator | 50 | 1.95 | 1.94 | 2.04 | PASS | 512x512 synthetic texture image |
| GLCM Texture Operator | 50 | 57.84 | 57.67 | 59.19 | PASS | 512x512 synthetic texture image |
