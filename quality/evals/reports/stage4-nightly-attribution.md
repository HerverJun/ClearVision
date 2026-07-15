# Stage 4 Nightly Attribution

- Baseline SHA: `6c0fa1f029362239c6c5f252152bec44b3fb773c`
- Gate: `product-nightly` (`Lane=Nightly&Purpose!=Performance`)
- Before: 1551 total / 1527 passed / 24 failed / 0 skipped
- Scope closure: 5 image-contract regressions fixed
- After: 1551 total / 1532 passed / 19 failed / 0 skipped
- Remaining blockers: exactly the 19 deferred unrelated failures; no Stage 4 failure remains

| FQN | Attribution | Status | Root cause |
| --- | --- | --- | --- |
| `ClearVision.Product.Tests.AI.AgentEvaluation.VisionAgentIndustrialScenarioEvaluationTests.Scenario eval: hole distance measurement should keep calibration scale pending` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.AgentEvaluation.VisionAgentIndustrialScenarioEvaluationTests.Scenario eval: metal scratch detection should use real surface defect schema` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.AgentEvaluation.VisionAgentIndustrialScenarioEvaluationTests.Scenario eval: modify existing flow should append defect detection and preserve old nodes` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.BuildFromPlanEntryParityTests.LegacyContractWithoutHash_ShouldSucceedWithExplicitWarningThroughRunService` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.BuildFromPlanEntryParityTests.ToolLoopMode_ShouldMatchAcrossAgentRunWebMessageAndInternalEntries` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests.VisionAgentBuildOrchestratorTests.Black-box scenario: hole distance measurement Build quality` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests.VisionAgentBuildOrchestratorTests.BuildReadiness preview should keep station camera resource pending in strict and draft` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests.VisionAgentBuildOrchestratorTests.BuildReadiness preview should share strict/draft defer semantics with BuildFromPlan` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentBuildOrchestratorTests.VisionAgentBuildOrchestratorTests.End-to-end mature strawberry Plan repair and Build should create editable classification draft` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentConsolidationTests.CompleteLesionFlow` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentGenerateFlow.VisionAgentGenerateFlowTests.BuildFromPlan controlled blocker should not fall back to legacy RequirementBriefExtractor` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentGenerateFlow.VisionAgentGenerateFlowTests.BuildFromPlan GenerateFlow should use dedicated Build pipeline even when use flag is false` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentGenerateFlow.VisionAgentGenerateFlowTests.BuildFromPlan service chain should build confirmed plan without legacy RequirementBrief` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.AI.VisionAgentGenerateFlow.VisionAgentGenerateFlowTests.BuildFromPlan system exception should return controlled new-pipeline failure without legacy fallback` | Deferred unrelated / AI | remaining_blocker | Canonical resource/readiness/clarification/tool-loop behavior and legacy oracle drift. |
| `ClearVision.Product.Tests.Integration.ResultAnalysisServiceIntegrationTests.ExportToCsvAsync_ShouldGenerateCorrectFormat` | Deferred unrelated / ResultAnalysis | remaining_blocker | Existing canonical outcome statistics and CSV schema drift from legacy test fixtures. |
| `ClearVision.Product.Tests.Integration.ResultAnalysisServiceIntegrationTests.GetStatisticsAsync_WithResults_ShouldReturnCorrectStatistics` | Deferred unrelated / ResultAnalysis | remaining_blocker | Existing canonical outcome statistics and CSV schema drift from legacy test fixtures. |
| `ClearVision.Product.Tests.Integration.Week11_TextureColorFlowIntegrationTests.Flow_Laws_Glcm_ShouldDifferentiateConstantAndCheckerboard` | Previous-stage regression / Stage 4 scope | fixed | Stage 2 fail-closed image admission lacked operator-specific E2 contracts despite verified runtime support. |
| `ClearVision.Product.Tests.Operators.DeepLearningMultiTaskOperatorTests.ClassificationExplicitTask_ShouldRunRealOnnxInference` | Deferred unrelated / Model output | remaining_blocker | Existing classification output contract does not provide DetectionList expected by the test. |
| `ClearVision.Product.Tests.Operators.PixelStatisticsOperatorTests.ExecuteAsync_FloatChannelAll_ShouldFlattenChannelsWithExactRobustStats` | Previous-stage regression / Stage 4 scope | fixed | Stage 2 fail-closed image admission lacked operator-specific E2 contracts despite verified runtime support. |
| `ClearVision.Product.Tests.Operators.PixelStatisticsOperatorTests.ExecuteAsync_FloatImage_ShouldComputeExactMedian` | Previous-stage regression / Stage 4 scope | fixed | Stage 2 fail-closed image admission lacked operator-specific E2 contracts despite verified runtime support. |
| `ClearVision.Product.Tests.Operators.PixelStatisticsOperatorTests.ExecuteAsync_WithMask_ShouldReturnAnalyticStatsAndUncertainty` | Previous-stage regression / Stage 4 scope | fixed | Stage 2 fail-closed image admission lacked operator-specific E2 contracts despite verified runtime support. |
| `ClearVision.Product.Tests.Operators.ShadingCorrectionOperatorTests.ExecuteAsync_WithSixteenBitColorInputInLumaOnlyMode_ShouldPreserveSixteenBitColorImage` | Previous-stage regression / Stage 4 scope | fixed | Stage 2 fail-closed image admission lacked operator-specific E2 contracts despite verified runtime support. |
| `ClearVision.Product.Tests.Runtime.RuntimeMvpTests.RuntimeHost_EventSubscribers_WhenOneThrows_ShouldContinuePublishing` | Deferred unrelated / Runtime | remaining_blocker | Existing package-configured source conflicts with the older conditional ImageAcquisition FilePath contract. |
| `ClearVision.Product.Tests.Runtime.RuntimeMvpTests.RuntimeHost_RunPackageConfiguredSingleAsync_ShouldUsePackageConfiguredInputs` | Deferred unrelated / Runtime | remaining_blocker | Existing package-configured source conflicts with the older conditional ImageAcquisition FilePath contract. |

## Reproduction

```powershell
& "./scripts/run-classified-test-gate.ps1" `
  -Gate product-nightly `
  -Configuration Debug `
  -NoBuild `
  -NoRestore `
  -ResultsDirectory ".tmp/stage4-after/nightly-final" `
  -LogFileName "product-nightly-after-final.trx" `
  -ReturnExitCode
```
