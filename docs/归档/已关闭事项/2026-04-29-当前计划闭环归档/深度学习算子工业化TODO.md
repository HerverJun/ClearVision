---
title: "深度学习算子工业化剩余 TODO"
doc_type: "closed-plan"
status: "closed"
topic: "当前计划闭环归档"
created: "2026-04-28"
updated: "2026-04-29"
closed_at: "2026-04-29"
source_path: "docs/进行中/当前计划/深度学习算子工业化TODO.md"
---

# 深度学习算子工业化剩余 TODO

> 归档说明：本计划已于 2026-04-29 完成当前工业化主线闭环并归档。`field replay` 尚未作为接口能力实现，已转为 `quality/evals/suites/dataset_heavy_suite.json` 中的 planned lane，不再阻塞本计划归档；后续应作为独立当前计划或质量飞轮子项重新开启。

> 核对日期：2026-04-28  
> 来源：`docs/进行中/未闭环事项/深度学习算子问题.md`  
> 结论：原高风险项大多已有修复证据；2026-04-29 已补真实 CPU provider smoke、YOLO 高候选框压力基准与真实数据证据分层模板。剩余风险集中在 GPU/TensorRT 实机人工跑批与 field replay 接口落地。

## 本轮落地记录（2026-04-29）

- 新增 `DeepLearningProviderInferenceRunner`，生成 `DeepLearning_provider_inference_baseline.md/json`；当前 CPU smoke 真实 ONNX inference 1/1 passed，报告区分 `RequestedProvider`、`ActiveProvider`、`FallbackToCpu`、`RealOnnxInference`。
- 新增 `DeepLearningPostprocessPressureRunner`，生成 `DeepLearning_postprocess_pressure_baseline.md/json`；1k/5k/10k candidates 压力场景 3/3 passed，并记录 runtime/memory budget。
- `RunInference(...)` 已避免对已是 `DenseTensor<float>` 的 selected tensor 做整 tensor `ToArray()` 重建；后处理输出补 `PostprocessDiagnostics`。
- `quality/evals/suites/dataset_heavy_suite.json` 已把 DeepLearning 证据分为 provider-inference、performance-contract、real-dataset、field-replay 层；新增真实生产 dataset manifest 模板。
- 验证：`DeepLearningOperatorTests` 已并入本轮定向测试批次，最终 97/97 passed；两个新增 quality runner 均已构建并实际生成报告。

## 已见闭环证据

- TensorRT 路径已存在 `TryAppendTensorRtExecutionProvider(...)`，并在失败时回退 CUDA；不再只是检测后写日志。
- 模型缓存已引入 `CachedModelSession.ModelSessionLease` 与 lease count，降低 use-after-dispose 风险。
- 标签回退已 fail-closed：`TryResolveBundledLabelsPath` 在没有命名 `TargetClasses` 时返回 null，并有对应单测。
- 预处理已覆盖灰度、四通道、16-bit 灰度、16-bit 三通道、float unit range 等输入。
- `DeepLearningOperator` 已补 `ModelId`、`ModelCatalogPath`、`LabelsPath`，并有 catalog 参数验证。
- 已有证据报告：
  - `quality/evals/reports/DeepLearning_contract_baseline.md`：26/26 passed。
  - `quality/evals/reports/DeepLearning_runtime_benchmark_baseline.md`：20/20 passed。
  - `quality/evals/reports/DeepLearning_detection_dataset_baseline.md`：36/36 passed。

## P1：真实 ONNX provider / GPU / TensorRT 端到端验证不足

### 未闭环证据

- runtime benchmark 明确写着只测 `preprocess+YOLO postprocess`，不测真实模型推理。
- 当前报告中的 active provider 为 `CPUExecutionProvider`，GPU/TensorRT 只是可用性/回退契约，不是实机推理通过证据。

### TODO

- [x] 增加可选的真实 ONNX smoke model 推理 suite，至少覆盖 CPU provider。
- [x] 在有 CUDA/TensorRT 环境时运行 provider-specific suite，记录 requested/active provider、fallback reason、latency。（通过 `--include-gpu` 作为 optional/manual 路径。）
- [x] 对 TensorRT 反射追加 provider 的失败原因做结构化输出，避免只在日志里出现。
- [x] CI 中将 GPU/TensorRT 标为 optional/manual，不阻塞无 GPU 环境；但发布前必须有人跑过并附报告。（已在 suite 中标为 manual，发布前仍需人工 GPU/TensorRT 报告。）

### 验收标准

- 生成 `DeepLearning_provider_inference_baseline.md/json`。
- 报告中明确区分 `RequestedProvider`、`ActiveProvider`、`FallbackToCpu`、`RealOnnxInference=true/false`。
- GPU/TensorRT 环境缺失时不会误宣称 GPU 已启用。

## P2：高候选框性能与分配仍未充分收敛

### 未闭环证据

- `RunInference(...)` 仍会对选中的 tensor 调用 `ToArray()` 后重建 `DenseTensor<float>`。
- 内部 `ApplyNMS(...)` 仍是按候选框列表执行的托管 NMS，当前基准只覆盖小规模候选框，不足以证明高候选框产线负载。

### TODO

- [x] 为 YOLO postprocess 增加高候选框压力基准：1k、5k、10k candidates，按 class 分布覆盖。
- [x] 优化 selected tensor 复制路径，避免不必要的整 tensor `ToArray()` 分配。
- [x] 评估按类别分桶、top-k prefilter、Span/ArrayPool 或 BoxNms 下游化策略，降低 NMS 抖动。（当前保留既有按类别分桶/空间索引 NMS，并用压力基准锁 runtime/memory。）
- [x] 将候选框上限、prefilter 阈值和实际丢弃数量写入 diagnostics。

### 验收标准

- 新增 `DeepLearning_postprocess_pressure_baseline.md/json`。
- 1080p 高频场景和 10k candidates 场景有 runtime/memory 上限。
- NMS 优化后保持现有 contract baseline 26/26 与 dataset bridge 36/36 通过。

## P3：真实数据质量证据仍需和合成桥分层

### 未闭环证据

- `DeepLearning_detection_dataset_baseline.md` 是 COCO-style semi-synthetic protocol bridge，报告也声明“不代表生产模型准确率”。
- 当前证据足够证明后处理协议，不足以证明某个现场模型的召回、误检和漂移稳定性。

### TODO

- [x] 为真实项目模型建立 dataset manifest：数据来源、许可/脱敏、类别表、版本、划分、指标阈值。（已新增 `DeepLearning_production_dataset_manifest.template.json`，具体项目仍需实例化。）
- [x] 将 semi-synthetic protocol bridge 与真实模型 dataset evidence 在矩阵中分层展示。
- [ ] 增加 field replay 接口后，把真实失败样本回灌为回归集。（本轮已在 suite 中预留 `field-replay` planned lane，接口与样本回灌仍待实现。）

### 验收标准

- 每个声明“生产可信”的 DeepLearning 模型都有 manifest、baseline、failure boundary。
- 质量矩阵能区分 contract、protocol bridge、真实 dataset、field replay 四类证据。
