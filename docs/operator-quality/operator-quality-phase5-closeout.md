# ClearVision 算子质量提升阶段 5 收口

## 结论边界

阶段 5 按“建立 Benchmark → 冻结基线 → 只实施数据证明的升级 → 重新 Benchmark → 治理与兼容收口”执行。正式结论来自固定合成数学数据与 ONNX 预处理契约夹具；Anomaly 的公开 MVTec 证据继续单独引用。上述证据不代表 E4、商业级精度、生产现场验收、Release Ready 或 Field Verified。

冻结产品基线为 `ce266626e0bec0a8cd4a68c11b176df95e8cb482`。正式 acceptance 的实现与 harness commit 为 `9df0fa73db4b2a94a748f377147ded3511162746`，baseline 与 after 报告均记录 `repositoryDirty=false`，并通过相同数据、模型、预处理、harness 和运行环境身份校验。

## Benchmark 身份

- Dataset: `clearvision-operator-precision-synthetic-v1` / `1.0.0`
- Dataset source/license: 仓库内确定性生成 / `CC0-1.0`
- Manifest SHA-256: `ec29b22e0bbda301a0fef4b23375413d7a569240f8c4abf06d45318493938920`
- Generated input/truth SHA-256: `85389261aa17bb99ecd70c51dfcb18545a0a417417b0b16ef33f07c42174ae72`
- Seed: `20260715`
- Split: train/validation/test 固定为 case index modulo 5 的 `0 / 1 / 2,3,4`
- Test cases: Caliper `135`、Circle `140`、Line `140`、Anomaly `48`；uncertainty 独立固定 `280` cases
- Identity ONNX SHA-256: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Embedding manifest SHA-256: `963888736d8d11fbbc4179c102b059c0a29220a21a9a0a13992aabc31b01f9c3`
- Reference input SHA-256: `e18a231c2db2b801f4f41e314508b296dea562ce5883cda94c7499fc92743c8c`
- Reference output SHA-256: `89321c2ae539bbe018a4acc4766456f93ae1a1db5689f7e1f446b6aefbc0201f`
- Preprocessing fingerprint: `c9e4e703eb2bdeab37496422a47243afc424d84769fa2ecb7dd75787361c86a8`
- Harness Program SHA-256: `58a1fba586e60e33a822b29175d3069423c956d62d9855f6c037870a36e6120e`
- Harness run-script SHA-256: `2e38238e5029bbee1687ac04cfd305c402641cc757bf5549398ab9232909b6f7`
- Environment: Windows `10.0.22000` x64、.NET runtime `8.0.19`、SDK `9.0.304`、OpenCV `4.9.0`、Server GC `false`
- 正式证据：`quality/evals/reports/operator-precision-baseline-acceptance.json`、`quality/evals/reports/operator-precision-after-acceptance.json`、`quality/evals/reports/operator-precision-phase5-comparison.json`

卡尺覆盖 clean、模糊、噪声、相位、极性、饱和、纹理、双边缘和遮挡；圆覆盖 clean、短弧、离群点、尺度和椭圆干扰，并跨角度覆盖；直线覆盖 clean、断边、毛刺、遮挡和离群点。候选由 validation split 选择，以下数值来自独立 test split。

## Benchmark 决策

| Domain | Path | Bias | RMSE | P95 error | Failure | Ambiguity | Outlier | Latency P95 | Allocation | Adopted |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| Caliper | `LegacyGradientCentroid` | -0.898781 | 3.605459 | 13.560420 | 0 | 0.029630 | 0.066667 | 0.007379 ms | 1,312 B/case | Baseline/default |
| Caliper | `IntegratedGaussianDerivative` | -5.693582 | 17.288111 | 50.827452 | 0 | 0 | 0.118519 | 0.043705 ms | 27,013 B/case | **No** |
| Circle | `AlgebraicL2` | -0.187351 | 1.994612 | 3.922396 | 0 | 0.392857 | 0 | 0.014930 ms | 6,192 B/case | Baseline/default |
| Circle | `ProductionOrthogonalWelsch` | 0.070351 | 1.207139 | 1.256742 | 0 | 0.114286 | 0.054762 | 0.302913 ms | 198,270 B/case | **Yes, opt-in** |
| Line | `L2` | 0.004843 | 0.621696 | 1.457463 | 0 | 0.400000 | 0 | 0.027732 ms | 2,744 B/case | Baseline/default |
| Line | `ProductionWelsch` | -0.003164 | 0.081725 | 0.178097 | 0 | 0 | 0.086429 | 0.173719 ms | 115,850 B/case | **Yes, opt-in** |

决策摘要：

- Circle：RMSE 改善 `39.479997%`，P95 error 改善 `67.959834%`，failure 未恶化，ambiguity 降低，且 P95/分配在书面预算内，因此保留 opt-in Welsch 精化。
- Line：RMSE 改善 `86.854588%`，P95 error 改善 `87.780361%`，failure 未恶化，ambiguity 降低，且 P95/分配在书面预算内，因此保留 opt-in Welsch 精化。
- Caliper：kernel-level `GaussianDerivative` 在局部定位器上改善，但接入正式 detector/pairing 后 RMSE/P95 分别恶化 `379.498220% / 274.822118%`，分配 `27,013 B/case` 也超过书面 `20,000 B/case` 诊断预算，因此不进入正式算子。

## 已实施升级与回滚

### Circle

- 共享正交几何精化内核支持 L2/Huber/Welsch、稳健权重、退化检测、收敛诊断、尺度、残差和原始 covariance。
- 仅统计圆拟合路径可选择 `RefinementLoss=Huber|Welsch`；默认仍为 `Legacy`。
- `MinEnclosingCircle` 的最小包围语义未改变。
- 回滚：将 `RefinementLoss` 设回 `Legacy`，旧工程无需迁移。

### Caliper

- 正式算子不暴露 `Quadratic`、`GaussianDerivative` 或 `Erf` EdgeModel 参数；旧默认和旧算法保持不变。
- 输出继续明确记录 `EdgeModel=Legacy`、残差、测量证据、歧义和失败诊断。
- `CaliperEdgeModelKernel` 仅作为 Benchmark 候选内核保留，不构成正式能力或默认切换。
- 回滚：无需切换；正式路径从未采用候选模型。

### LineMeasurement

- 支持 `FitLoss=L2|Huber|Welsch`，区分初始 seed 与稳健 refine，并输出残差、尺度、异常点和退化诊断。
- 默认仍为 `L2`；稳健模式失败时不静默回退到 L2。
- 回滚：将 `FitLoss` 设回 `L2`。

### MeasurementEvidence

- 统一证据包含 Value、Unit、CoordinateFrame、Sigma/Covariance、Provenance、SourceOperator、SourceAlgorithm、SourceParametersFingerprint 和 QualityFlags。
- 负数或非有限 Sigma 被拒绝；非方阵或包含非有限元素的 covariance 整体置空并标记无效，不压缩矩阵维度。
- Line L2 的像素残差不再伪装成角度 sigma；Sigma 为 null，并标记 `AngleSigmaUnavailable`、`ResidualUncertaintyOnly`、`LegacyCompatibility`。
- Residual heuristic 的 68%/95% coverage 为 `0.896429 / 0.953571`，说明 68% 区间过度保守；raw covariance 为 `0.550000 / 0.785714`，明显未校准。因此 covariance 仅标记 `UncalibratedCovariance`，不得作为统计置信区间。

### Anomaly

- 轻量传统特征模式继续作为兼容默认；未把 ONNX embedding 切成默认算法。
- ONNX embedding 必须由 manifest 声明完整 resize、插值、颜色顺序、scale、mean/std、layout、数据类型和输出归一化；不假设 ImageNet mean/std。
- 训练、建库、加载和推理共享预处理 fingerprint；feature bank 同时绑定模型内容 SHA、预处理 SHA 和 bank identity。
- session cache 每次按实际模型内容 SHA 校验，同路径、同长度、恢复 mtime 但内容被替换时仍 fail-closed。
- 预处理 reference RMSE 为 `0`，身份 mismatch 实际执行并记录 `mismatchRejectedFailClosed=1`。
- 回滚：显式切回传统模式。缺 manifest 或旧身份不完整的 ONNX bank 不允许静默兼容。

## 未采用候选

- Caliper `IntegratedGaussianDerivative`：正式 detector/pairing 集成精度严重回退且 allocation 超预算。
- Caliper `Quadratic`：相对 legacy 没有可重复的目标指标改善。
- Caliper `Erf`：没有精度收益，且 P95 延迟和分配显著增加。
- Measurement covariance 作为“已校准置信区间”：68%/95% coverage 不合格。
- ONNX embedding 作为 Anomaly 默认算法：未采用；本阶段只收紧 manifest 和 feature-bank 身份治理。

## 四轴质量状态

四轴独立生成和投影，禁止用单一 Stable、A/B/C、测试数量、TotalScore、QScore 或 Accepted 代替：

- Execution：158 个正式算子均为 `Implemented`。
- AlgorithmQuality：154 `Unknown`、1 `PublicDatasetEvidence`（Anomaly）、1 `SyntheticBenchmarkEvidence`（Caliper）、2 `SyntheticBenchmarkValidated`（Circle/Line）。
- ProductionReadiness：150 `Unknown`、5 `Experimental`、1 `CompatibilityOnly`、2 `Reference`。
- FieldValidation：158 个均为 `NotValidated`。

状态由 runtime metadata 生成，并同步到 Application DTO、AI 只读目录、Prompt、知识图谱、文档目录和 OperatorLibrary descriptor。`Unknown`、compatibility-only、synthetic 或 public-dataset 证据均不会自动提升为 Release Ready 或 Field Verified。

## 兼容、版本与门禁

- 算子版本：Caliper `1.2.1`、Circle `1.2.0`、Line `1.2.1`、Anomaly `1.2.0`。
- OperatorLibrary：`1.0.3`；实际 pack + 本地 NuGet restore + package acceptance `43/43` 通过，smoke 默认版本、lock file、README、third-party notice 和 SBOM 身份一致。
- 旧工程、package、runtime load/save/run：`78/78` 通过。
- Desktop preview 与 Station package/deploy/replay/store：`84/84` 通过。
- 本地 PR lane：governance `3692` classified / 0 errors / 0 warnings；Product `2435 passed / 2 existing skips`；Desktop `599/599`；OperatorLibrary `43/43`；Frontend `43/43`；UI contract `967/967`；Stage 4 smoke `3/3`。
- OperatorLibrary industrial profile：17/17 子门禁通过；包括 measurement regression `183/183`、accuracy `3/3`、determinism `16/16`、stability `1/1`、acceptance performance `2/2`、calibration `135/135`、calibration integration `7/7`、detection regression `112/112`、detection accuracy `2/2`、detection stability `1/1`、detection performance `1/1`、matching determinism `3/3`、matching stability `1/1`、preprocessing robustness `1/1`、virtual PLC `56/56 + 15/15`。
- Final product Nightly：`1560 total / 1541 passed / 19 failed / 0 skipped`；19 个失败与 `quality/evals/reports/stage4-nightly-attribution.json` 的 `remaining_blocker` 集合精确一致，missing=0、new=0。
- 既有 warning：`System.Collections.Immutable` 8/9 解析冲突仍如实保留；未通过降低阈值、删除断言、改 lane 或增加 skip 处理。
- Benchmark 只属于 Nightly/Manual；普通 PR lane 未加入阶段 5 acceptance Benchmark。

## 后续 P3 路线（本阶段不实施）

1. 使用获授权的真实卡尺、圆和直线现场数据建立跨设备、镜头、曝光、温漂和材料域的独立 test split。
2. 对 sigma/covariance 做独立校准、可靠性图和 68%/95% coverage 验证后，再评估 uncertainty claim。
3. 在目标工控机复验 P50/P95、allocation、长时间稳定性和降级策略。
4. 为实际部署的 Anomaly 模型和现场数据建立模型卡、许可、manifest、feature-bank lineage 和漂移回放。
5. 只有现场签署和发布门禁全部具备后，才讨论 ProductionReadiness 或 FieldValidation 提升。

## 复现命令

```powershell
& ".\scripts\reproduce-operator-precision-baseline.ps1" `
  -Profile acceptance `
  -ResultsDirectory "quality\evals\reports"

& ".\scripts\run-operator-precision-benchmark.ps1" `
  -Profile acceptance `
  -Label after `
  -ResultsDirectory "quality\evals\reports"

& ".\scripts\compare-operator-precision-benchmarks.ps1"
```

这些命令只忽略本套固定生成报告；任何源码、配置或其他未预期脏区仍会 fail-closed。远端 SHA 和 GitHub Actions 状态在最终交付时单独核对，不用于伪造 Release Ready 或 Field Verified。
