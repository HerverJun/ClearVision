# ClearVision 算子质量阶段 5 证据收口

## 基线执行方式

冻结基线为 `ce266626e0bec0a8cd4a68c11b176df95e8cb482`。`scripts/reproduce-operator-precision-baseline.ps1` 会：

1. 在短路径 detached worktree 中检出该提交；
2. 将已提交且内容 SHA 固定的 `OperatorProductE2EBenchmarkRunner` 适配器复制到隔离 worktree；
3. 在该 worktree 内编译旧版 Product Infrastructure；
4. 通过正式 `CircleMeasurementOperator.ExecuteAsync` 和 `LineMeasurementOperator.ExecuteAsync`、`ImageWrapper` 图像输入以及完整 OperatorBase 生命周期执行；
5. 记录旧产品仓库 SHA、正式源码 SHA、实际加载的 Core/Infrastructure assembly SHA、harness SHA 和 clean 状态；
6. 运行结束后删除隔离 worktree。

不再使用 `SourceShaOverride`。数学参考/共享内核报告的 `sourceSha` 只表示实际执行它们的当前 harness commit，不能再解释为旧产品执行证据。

正式产品 E2E 数据为 `clearvision-operator-product-e2e-synthetic-v1/1.0.0`，manifest SHA 为 `a507e7345388017506dc60f544598721042a0cdaa12dd0f2402cb6236256eaeb`，生成 raster/truth SHA 为 `5d60098525547bf873b9e4618b3d7b5a08bf202164295dac9c2eff6f99c2507a`。split 单位是 scenario bundle：bundle index 模 5 的 `0/1/2,3,4` 分别为 train/validation/test；每个 split 都保留全部场景。

## 正式算子端到端结果

旧提交默认路径与当前默认路径在 Circle/Line 的 validation/test 四个 aggregate metric row 上，`caseCount`、Bias、RMSE、P95、Failure、Ambiguity、Outlier、secondary/residual diagnostic summary 和 failure taxonomy 一致。该 conformance 仅证明这些聚合精度、失败和诊断摘要字段没有偏离 `ce266626`；未生成逐案例诊断语义 fingerprint，因此不宣称逐案例诊断完全一致。

| Domain | 当前默认 | 候选模式 | Test RMSE 改善 | Test P95 改善 | Failure delta | P95 latency cost | managed allocation cost | 结论 |
|---|---|---|---:|---:|---:|---:|---:|---|
| Circle | `CaliperFitV2/Legacy` | `CaliperFitV2/Welsch` | `-8.204056%` | `-20.513562%` | `+0.013889` | `-1.7503 ms` | `+173,585.19 B/case` | **不采用** |
| Line | `FitLine/L2` | `FitLine/Welsch` | `+0.985996%` | `+15.820713%` | `0` | `+0.7287 ms` | `+129,262.09 B/case` | **采用为 opt-in** |

Circle 的 validation RMSE/P95 已回退，独立 test 又出现 RMSE/P95 回退和新增 failure，因此点集内核中的改善不能支持正式算子采用。代码中的 Welsch 参数为兼容保留的非默认模式，但不再标记为已验证采用；回滚方式是省略 `RefinementLoss` 或设为 `Legacy`。

Line Welsch 在 validation 同时改善 RMSE/P95，独立 test 的 RMSE/P95 也均改善，Failure/Ambiguity 未恶化，P95 和 managed allocation 增量在书面预算内，因此仅 `Method=FitLine; FitLoss=Welsch` 模式保留 `SyntheticBenchmarkValidated`。整个 `LineMeasurement` OperatorType 不继承该模式级成熟度；默认仍为 L2，回滚方式是省略 `FitLoss` 或设为 `L2`。

Circle 正式路径覆盖参数映射、profile sampling、edge selection、歧义判断、`BuildRobustFit`、MAD/Huber、Welsch refine、coverage/angular coverage/residual gates；Line 正式路径覆盖灰度转换、Canny、HoughP seed、候选排序、caliper sampling、stripe center、L2/Welsch refine 和最终候选输出。点集内核 Benchmark 只作为补充证据。

## 性能证据边界

- Latency：包围完整正式 `ExecuteAsync` 调用，包括图像解码、Canny/profile、候选选择、overlay 和输出构造。
- Managed allocation：`GC.GetAllocatedBytesForCurrentThread`，只代表 benchmark thread managed allocation，不代表完整进程或 OpenCV native allocation。
- Process resources：报告单独记录完整 benchmark 进程的 peak working set、private bytes 和 working set，不能归因到单个算子案例。

## 四轴与证据身份

权威清单为 `quality/evals/reports/operator-quality-phase5-evidence.json`。它绑定：

- baseline/after/comparison 报告路径和 SHA-256；
- dataset、generated raster/truth、model、embedding manifest、preprocess、harness 和产品实现身份；
- 算法模式、默认状态、采用结论、回滚方式和 claim boundary；
- public-dataset、正式产品 E2E 与补充 kernel evidence 的不同作用域。

`OperatorQualityStateCatalog` 从嵌入的该 manifest 生成状态；manifest 缺失、schema/条目非法时 fail-closed 为 `Unknown`。`scripts/verify-operator-quality-evidence.ps1` 对仓库中的报告内容和 SHA 做完整校验，Nightly/Manual 同时重放 baseline、after、comparison 并验证 tracked evidence identity。

整算子四轴汇总：

- Execution：158 `Implemented`；
- AlgorithmQuality：154 `Unknown`、3 `SyntheticBenchmarkEvidence`（Caliper/Circle/Line）、1 `PublicDatasetEvidence`（仅 Anomaly traditional default scope）；
- ProductionReadiness：保持 150 `Unknown`、5 `Experimental`、1 `CompatibilityOnly`、2 `Reference`；
- FieldValidation：158 `NotValidated`。

模式级结论：

- Circle `RefinementLoss=Welsch`：`SyntheticBenchmarkEvidence`，`Adopted=false`；
- Line `FitLoss=Welsch`：`SyntheticBenchmarkValidated`，`Adopted=true`，`IsDefault=false`；
- Anomaly `lab_gradient_stats`：`PublicDatasetEvidence`；
- Anomaly `onnx_embedding`：仅 `SyntheticBenchmarkEvidence` 的预处理/身份契约，不继承 traditional 模式的公开数据精度。

## 补充 Benchmark 与既有结论

`operator-precision-*.json` 继续保留卡尺、点集圆/线、不确定度和 Anomaly 预处理契约，但已明确标注为 supplemental kernel/preprocessing evidence：

- Caliper GaussianDerivative 正式集成仍拒绝：RMSE/P95 回退且 allocation 超预算；
- Measurement covariance 68%/95% coverage 仍为 `0.55/0.785714`，保持 `UncalibratedCovariance`；
- ONNX model SHA、embedding manifest SHA、preprocess fingerprint 与 feature-bank identity 继续 fail-closed；
- MVTec 公开证据只属于传统 `lab_gradient_stats` 模式。

## CI 与剩余阻断

- Benchmark 仍只在 Nightly/Manual 运行，不进入普通 PR lane；
- 原有 Nightly 19 个 AI、Runtime、ResultAnalysis 和 DeepLearning blocker 保持不变，本阶段没有降低阈值、删除断言、改 lane 或增加 skip；
- 上一基线远端 Safe CI 的 Windows/OpenCV Unicode ImageAcquisition 失败，以及 Vision Agent Quality Suite 的既有 AI blocker，仍属于范围外阻断；在最终 SHA 的远端运行完成前不得写成全绿；
- 不宣称 Release Ready、Field Verified、E4、商业级或现场精度。

## 复现

```powershell
& ".\scripts\run-operator-product-e2e-evidence.ps1" `
  -Profile acceptance `
  -ResultsDirectory ".tmp\operator-precision"

& ".\scripts\run-operator-precision-benchmark.ps1" `
  -Profile acceptance `
  -Label baseline `
  -ResultsDirectory ".tmp\operator-precision"

& ".\scripts\run-operator-precision-benchmark.ps1" `
  -Profile acceptance `
  -Label after `
  -ResultsDirectory ".tmp\operator-precision"

& ".\scripts\compare-operator-precision-benchmarks.ps1" `
  -BaselinePath ".tmp\operator-precision\operator-precision-baseline-acceptance.json" `
  -AfterPath ".tmp\operator-precision\operator-precision-after-acceptance.json" `
  -OutputPath ".tmp\operator-precision\operator-precision-phase5-comparison.json" `
  -ReportPath ".tmp\operator-precision\operator-precision-phase5-comparison.md"

& ".\scripts\verify-operator-quality-evidence.ps1" `
  -FreshEvidenceDirectory ".tmp\operator-precision"
```
