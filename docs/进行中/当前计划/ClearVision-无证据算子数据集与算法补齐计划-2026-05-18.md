---
title: "ClearVision 无证据算子数据集与算法补齐计划"
doc_type: "plan"
status: "active"
topic: "FrameChangeTrigger evidence closure"
created: "2026-05-18"
updated: "2026-05-18"
owner: "Quality Flywheel Agent"
---

# ClearVision 无证据算子数据集与算法补齐计划

## 背景

当前质量矩阵 `quality/evals/reports/operator_quality_matrix.md` 显示：156 个正式算子中，只有 `OperatorType.FrameChangeTrigger` 处于 `Any evidence signal = No`。该算子已进入正式目录和线序视频流模板，但质量证据列仍为：

| 算子 | Contract | Golden | Dataset | Field | 当前风险 |
|---|---|---|---|---|---|
| `FrameChangeTrigger` | No | No | No | No | 正式目录已有入口，但没有可审计证据支撑 |

已有 `FrameChangeTriggerOperatorTests` 说明基础行为存在普通单测，但它没有进入质量飞轮证据体系，不能支撑 release claim、dataset claim 或 field replay claim。

## 目标

1. 将 `FrameChangeTrigger` 从无证据状态提升到至少 `Contract=Yes`、`Dataset=Yes`、`FieldReplay=Yes`。
2. 建立视频流到料触发的确定性数据集、runner、报告和矩阵更新路径。
3. 补齐算法鲁棒性：抖动、光照漂移、局部噪声、重复触发、ROI 边界和短路语义。
4. 建立防扩散规则：后续新进正式目录的算子不得以 `Any evidence signal = No` 状态进入当前质量矩阵。

## 范围

本计划只处理当前完全无证据算子：

- `FrameChangeTrigger`

不把 134 个 `Dataset=No` 的算子全部纳入本轮。那些算子大多已有 contract 或 golden 证据，另起 dataset-tier 扩面计划更合适。

## 数据集补齐计划

### D0 证据口径冻结

- [ ] 在 `docs/operator-quality/operator_quality_evidence_manifest.md` 中追加 `FrameChangeTrigger` 的证据关闭条件。
- [ ] 在质量矩阵生成脚本中确认普通 xUnit 单测不自动等价为 quality contract evidence，必须有 runner/report 产物。
- [ ] 新增 evidence closure 报告名约定：
  - `quality/evals/reports/FrameChangeTrigger_contract_baseline.json`
  - `quality/evals/reports/FrameChangeTrigger_contract_baseline.md`
  - `quality/evals/reports/FrameChangeTrigger_dataset_baseline.json`
  - `quality/evals/reports/FrameChangeTrigger_dataset_baseline.md`
  - `quality/evals/reports/QualityFlywheel_frame_change_trigger_evidence_closure_v1.md`

### D1 Contract 证据

- [ ] 新建 `FrameChangeTriggerContractRunner` 或接入现有 contract runner 批次。
- [ ] 覆盖最小合同场景：
  - 首帧建立 baseline，必须短路，`Reason=baseline`。
  - 大面积变化触发，`Triggered=true`，不中断下游。
  - 小变化低于阈值，`Reason=below_threshold`。
  - 冷却期重复变化，`Reason=cooldown`。
  - `Enabled=false` 时透传且不中断。
  - `ShortCircuitWhenNotTriggered=false` 时未触发也不中断。
  - 缺少 `Image`、空图、非法阈值、非法比例输出稳定错误。
  - ROI 裁剪到图像边界，不能越界或抛异常。
- [ ] 验收门槛：不少于 24 个 contract cases，失败数为 0，报告记录 runtime/memory、输入类型、输出字段完整性。

### D2 Dataset-tier 合成序列

- [ ] 新建固定 seed 的合成视频序列数据集：
  - 静态空场景：不触发。
  - 端子进入 ROI：触发一次。
  - 端子停留：冷却期内不重复触发。
  - 端子离开后再次进入：冷却期后重新触发。
  - 小面积噪声、盐椒噪声、压缩噪声：不误触发。
  - 光照整体漂移：不误触发或按策略降权。
  - 局部反光闪烁：不误触发。
  - 轻微相机抖动：不误触发。
  - ROI 外运动：不触发。
  - ROI 边缘进入、半遮挡、低对比进入：应按标注触发。
- [ ] 输出 manifest：
  - `quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json`
  - 记录 seed、帧尺寸、ROI、标签、期望触发帧、噪声模型、许可状态。
- [ ] 新建 dataset runner：
  - 输入序列和参数 profile。
  - 输出逐序列 trigger event、reason 分布、重复触发统计、false trigger、missed trigger。
- [ ] 验收门槛：
  - 至少 120 条序列。
  - Trigger Precision >= 0.98。
  - Trigger Recall >= 0.95。
  - Duplicate suppression rate >= 0.98。
  - Static/noise false trigger rate <= 0.02。
  - P95 runtime <= 3 ms for 256x256 ROI。

### D3 Field-substitute replay

- [ ] 从线序视频流模板构造 field-substitute replay manifest：
  - `ImageAcquisition(Continuous) -> FrameChangeTrigger -> DeepLearning -> BoxFilter -> BoxNms -> DetectionSequenceJudge -> ResultOutput`
- [ ] 用匿名/合成帧序列模拟现场无光电触发输送线。
- [ ] 验证未触发帧短路，不进入 DeepLearning，触发帧继续向下游传递。
- [ ] 验收门槛：
  - 至少 20 条 replay cases。
  - 未到料帧下游执行次数为 0。
  - 到料帧下游执行次数与标注触发次数一致。
  - 报告明确声明 field-substitute，不声明真实产线签核。

## 算法补齐计划

### A1 参数与错误契约硬化

- [ ] `ValidateParameters` 补齐：
  - `MinChangePixels >= 0`
  - `CooldownMs` 在 0 到 60000 之间
  - `RoiX/RoiY/RoiW/RoiH` 非负
  - `ShortCircuitWhenNotTriggered`、`Enabled` 类型异常可诊断
- [ ] 统一错误消息，避免下游只能看到泛化 failure。
- [ ] 输出增加 `BaselineReady`、`TotalPixels`、`CooldownRemainingMs`、`EffectivePixelThreshold`、`EffectiveMinChangeRatio`。

### A2 抗噪与抗光照漂移

- [ ] 抽出纯算法核 `FrameChangeTriggerKernel`，让 runner 和单测不依赖完整算子执行器。
- [ ] 增加可选预处理：
  - `BlurSize` 用于抑制点噪声。
  - `MorphOpenSize` 用于去除孤立噪声。
  - `NormalizeMode` 支持 `None`、`MeanShift` 或 `PercentileClip`。
- [ ] 增加参考帧更新策略：
  - `ReferenceUpdateMode=PreviousFrame|StableBackground|ExponentialMovingAverage`
  - `ReferenceUpdateAlpha` 控制慢速光照漂移适配。
- [ ] 对低对比进入场景增加 `AdaptivePixelThreshold` 候选，但默认关闭。

### A3 触发语义与流程稳定性

- [ ] 增加 `MinConsecutiveChangedFrames`，避免单帧闪烁误触发。
- [ ] 增加 `ResetAfterNoChangeFrames`，物料离开后允许下一次到料重新建立状态。
- [ ] 增加 `TriggerOnRisingEdgeOnly`，把“持续变化”与“到料边沿”分开。
- [ ] 覆盖并发/多实例状态隔离，确保不同 operator id 不共享 baseline。
- [ ] 检查 state TTL 清理在长时间运行中不会泄漏 Mat。

### A4 默认 profile 收敛

- [ ] 形成三个默认关闭 profile：
  - `line_fast_default`：低延迟，适合清晰物料进入。
  - `line_noise_guard`：抗噪优先，适合反光或抖动。
  - `line_low_contrast`：低对比候选，需要 dataset 指标通过后才允许推荐。
- [ ] 在模板中只默认使用 `line_fast_default`。
- [ ] 其他 profile 只能作为 opt-in evidence profile，不默认开启。

## 交付物

| 交付物 | 路径 | 目的 |
|---|---|---|
| Contract runner | `quality/tools/FrameChangeTriggerContractRunner/` | 进入 contract evidence |
| Dataset manifest | `quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json` | 固定数据集口径 |
| Dataset runner | `quality/tools/FrameChangeTriggerDatasetRunner/` | 生成 dataset baseline |
| Contract report | `quality/evals/reports/FrameChangeTrigger_contract_baseline.*` | 关闭 contract gap |
| Dataset report | `quality/evals/reports/FrameChangeTrigger_dataset_baseline.*` | 关闭 dataset gap |
| Field replay report | `quality/evals/reports/FrameChangeTrigger_field_substitute_baseline.*` | 关闭 field-substitute gap |
| 质量矩阵更新 | `quality/evals/reports/operator_quality_matrix.md` | 从无证据状态移除 |

## 验收标准

- [ ] `operator_quality_matrix.md` 中 `FrameChangeTrigger` 行变为：
  - `HasContractTest=Yes`
  - `HasDatasetEvidence=Yes`
  - `HasFieldReplay=Yes`
  - `Any evidence signal=Yes`
- [ ] `docs/operator-quality/operator_quality_evidence_manifest.md` 中不再把 `FrameChangeTrigger` 标为无证据例外。
- [ ] 新增 runner 均可本地复现，报告无大 JSON 进入 Git，必要 raw payload 作为 artifact。
- [ ] `./scripts/check-quality-report-size.ps1` 通过，白名单保持 0。
- [ ] `./scripts/check-text-encoding.ps1` 通过。
- [ ] 定向测试通过：
  - `FrameChangeTriggerOperatorTests`
  - 新增 contract runner 对应测试
  - 视频流模板短路语义测试

## 风险

- 合成数据过于干净，容易高估现场可靠性；必须保留 field-substitute 边界声明。
- 光照漂移和物料慢速进入可能互相混淆；算法 profile 需要默认保守。
- 触发短路会影响下游统计和结果面板语义；field replay 必须覆盖未触发帧不生成误导性 NG。
- 当前已有普通单测但质量矩阵未采纳；补证据时要避免重复造一套不可维护的 runner。

## 执行顺序

1. 先做 D1 contract runner，快速把无证据状态降为 contract evidenced。
2. 再做 A1 参数与错误契约硬化，消除已知边界风险。
3. 做 D2 dataset-tier 合成序列和 runner。
4. 做 A2/A3 算法鲁棒性迭代，按 dataset 指标选择默认 profile。
5. 做 D3 field-substitute replay，验证流程短路和下游集成。
6. 重新生成质量矩阵和证据 manifest，关闭无证据算子风险。
