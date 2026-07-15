# ClearVision 算子质量提升阶段 5 收口

## 结论边界

阶段 5 严格按“建立 Benchmark → 冻结基线 → 只实施数据证明的升级 → 重新 Benchmark → 治理与兼容收口”执行。精度证据来自固定合成数学数据与预处理契约夹具；Anomaly 的补充证据来自公开 MVTec 数据。上述证据均不代表 E4、商业级精度、生产现场验收、Release Ready 或 Field Verified。

基线提交为 `ce266626e0bec0a8cd4a68c11b176df95e8cb482`，升级后 Benchmark 来源提交为 `97d25440bf540b3543ddbd687377daa7cdab2685`。两次 acceptance 报告绑定相同数据、种子、模型、预处理指纹和运行环境：

- Dataset: `clearvision-operator-precision-synthetic-v1` / `1.0.0`
- Dataset SHA-256: `32495a03d7706969d9e33b18ef609d837844da6678bff9d08a5debbe02f379f5`
- Seed: `20260715`
- Identity ONNX SHA-256: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `d4bf2ecf122f0e5b95c255812f7cea809cb333ad6fe7d37d1b5853c80fa4bde2`
- 完整证据：`quality/evals/reports/operator-precision-baseline-acceptance.json`、`quality/evals/reports/operator-precision-after-acceptance.json`、`quality/evals/reports/operator-precision-phase5-comparison.json`

## 采用决策

| Domain | Baseline | Production path | RMSE improvement | P95 error improvement | Failure delta | Adopted |
|---|---|---|---:|---:|---:|---|
| Caliper | `LegacyGradientCentroid` | `ProductionGaussianDerivative` | 94.780834% | 97.114241% | 0 | Yes, opt-in |
| Circle | `AlgebraicL2` | `ProductionOrthogonalWelsch` | 41.697092% | 58.420071% | 0 | Yes, opt-in |
| Line | `L2` | `ProductionWelsch` | 88.185166% | 90.223009% | 0 | Yes, opt-in |

生产 conformance 行在 after acceptance 中直接调用正式内核。Circle 与 Line 的 bias、RMSE、P95、failure、ambiguity 和 outlier 指标与获胜候选精确一致；Caliper 正式内核在 RMSE、P95、failure 与 ambiguity 上不劣于获胜候选。三条正式路径均通过书面 P95 延迟与 allocation 预算。

## 已实施升级与回滚

### Circle

- 共享正交几何精化内核支持 L2/Huber/Welsch、退化检测、收敛诊断、稳健尺度、权重与原始 covariance。
- 仅统计圆拟合路径可选择 `RefinementLoss=Huber|Welsch`；默认仍为 `Legacy`。
- `MinEnclosingCircle` 的最小包围语义未改变。
- 回滚：将 `RefinementLoss` 设回 `Legacy`，无需迁移旧工程。

### Caliper

- 新增显式 `EdgeModel=GaussianDerivative`，输出模型、残差、sigma、歧义和失败原因。
- 默认仍为 `Legacy`；新模型失败时不静默回退。
- 回滚：将 `EdgeModel` 设回 `Legacy`。

### LineMeasurement

- 新增 `FitLoss=L2|Huber|Welsch`，明确区分 seed 与 refine，并输出残差、稳健尺度、异常点与退化诊断。
- 默认仍为 `L2`；稳健模式失败时不回退到 L2。
- 回滚：将 `FitLoss` 设回 `L2`。

### MeasurementEvidence

- 统一证据包含 value、unit、coordinate frame、sigma/covariance、provenance、source operator/algorithm/parameter fingerprint 与 quality flags。
- 启发式 sigma 明确标记 `Provenance=Heuristic`。
- 原始 covariance 仅标记为 `UncalibratedCovariance`，不得解释为已校准统计置信区间。

### Anomaly

- 传统特征模式保持默认，旧工程未选择 ONNX 时行为不变。
- ONNX embedding 必须提供完整 manifest；训练、建库和推理共享同一预处理 fingerprint。
- Feature bank 绑定模型内容 SHA、预处理 fingerprint 与 bank identity SHA；任一不一致均 fail-closed。
- 不再隐式假设 ImageNet mean/std。
- 回滚：切回传统模式。缺失 manifest 或旧身份不完整的 ONNX feature bank 不允许静默兼容，这是有意的 fail-closed 边界。

## 未采用候选

- Caliper `Quadratic`：没有相对于 legacy 的可重复精度收益。
- Caliper `Erf`：没有精度收益，且 P95 延迟与 allocation 显著增加。
- Measurement covariance 作为“已校准置信度”：未采用。Residual heuristic 的 68%/95% coverage 为 `0.891666666666667 / 0.954166666666667`，raw covariance 为 `0.566666666666667 / 0.791666666666667`，校准未改善。
- ONNX embedding 作为 Anomaly 默认算法：未采用。传统模式继续作为兼容默认，manifest 身份绑定属于治理升级而非默认算法切换。

## 四轴质量状态

四个轴独立生成和投影，禁止用单一 Stable、等级、测试数量或 Accepted 代替：

- Execution: 158 个正式算子均为 `Implemented`。
- AlgorithmQuality: 3 个为 `SyntheticBenchmarkValidated`（Caliper/Circle/Line），1 个为 `PublicDatasetEvidence`（Anomaly），154 个为 `Unknown`。
- ProductionReadiness: 150 个 `Unknown`，5 个 `Experimental`，2 个 `Reference`，1 个 `CompatibilityOnly`。
- FieldValidation: 158 个均为 `NotValidated`。

该状态由 runtime metadata 生成，并同步到 Application DTO、AI 只读目录、Prompt、知识图谱、文档目录与 OperatorLibrary descriptor。完整报告为 `quality/evals/reports/operator_quality_four_axis.json`。任何 `Unknown`、compatibility-only、synthetic 或 public-dataset 证据都不会自动提升为 Release Ready 或 Field Verified。

## 兼容、门禁与 Nightly

- 算子版本：Caliper `1.1.0`、Circle `1.2.0`、Line `1.1.0`、Anomaly `1.1.0`。
- 旧默认保持：Caliper `Legacy`、Circle `Legacy`、Line `L2`、Anomaly traditional。
- OperatorLibrary 的 `IOperatorDescriptor.QualityState` 使用默认接口实现，第三方旧实现可继续编译；正式 adapter 会投影完整四轴状态。
- Benchmark 仅进入 `schedule` 与 `workflow_dispatch` 的 Nightly/Manual job，不进入普通 PR job。
- `quality/evals/reports/stage4-nightly-attribution.json` 保留 19 个既有范围外阻断；该文件的阶段 5 前 SHA-256 为 `cbcf54cfea46e1de71175b9967a1e8eba43f5e9efe8758040ef14d47119c78b5`。阶段 5 不修改这些归因、断言、阈值、lane 或 skip。

最终验证结果：

- Metadata / AI knowledge parity：23/23 通过。
- Phase 5 precision contract：6/6 通过。
- OperatorLibrary package smoke：43/43 通过；industrial profile 的 17 个子门禁全部通过。
- Measurement：regression 183/183、accuracy 3/3、determinism 16/16、stability 1/1，acceptance performance 通过。
- 旧工程、package 与 runtime load/save/run：78/78 通过。
- Desktop preview 与 Station：84/84 通过。
- PR Quality Lane：Product 2432 passed / 2 existing UI skips；Desktop 599/599；OperatorLibrary 43/43；FrontendV2 43/43；UI contract 967/967；Stage 4 smoke 3/3。
- Nightly attribution：1560 total，1541 passed，19 failed，0 skipped；失败集合逐项与 `stage4-nightly-attribution.json` 的 19 个 `remaining_blocker` 精确一致，没有阶段 5 新增失败。
- 已知非阻断警告仍为 `System.Collections.Immutable` 8/9 解析警告；本阶段未通过降低阈值、删除断言、改 lane 或增加 skip 处理该警告。

## 后续 P3 路线（本阶段不实施）

1. 使用获授权的真实卡尺、圆和线现场数据建立跨设备、镜头、曝光、温漂与材料域的独立 test split。
2. 对 sigma/covariance 做独立校准、可靠性图与 68%/95% coverage 验证后，再评估是否提升 uncertainty claim。
3. 在目标工控机上复验 P50/P95、allocation、长时间稳定性和降级策略。
4. 为实际部署的 Anomaly 模型与现场数据建立模型卡、许可、manifest、feature-bank lineage 和漂移回放。
5. 只有现场签署和发布门禁全部具备后，才允许讨论 ProductionReadiness 或 FieldValidation 的提升。

## 复现命令

```powershell
& ".\scripts\run-operator-precision-benchmark.ps1" -Profile acceptance -Label after -ResultsDirectory "quality\evals\reports"
& ".\scripts\compare-operator-precision-benchmarks.ps1"
dotnet run --project scripts\OperatorDocGenerator\OperatorDocGenerator.csproj -- . --overwrite --enforce-version-bump
dotnet run --project quality\tools\OperatorKnowledgeGraphRunner\OperatorKnowledgeGraphRunner.csproj
```
