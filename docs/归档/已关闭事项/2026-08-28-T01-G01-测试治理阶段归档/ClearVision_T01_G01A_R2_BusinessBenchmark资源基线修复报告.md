# ClearVision T01-G01A-R2 Business Benchmark Canonical Resource Baseline Repair

## Conclusion

- The formal Business Benchmark improved from 94/120 to 120/120 cases, `accepted=true`, with `parameterCompletionRate` increasing from `0.4250` to `0.7667`. The existing `0.70` threshold was not changed.
- All 92 ready cases are resource-complete. All 28 intentional missing-resource cases remain editable as workflow drafts and blocked from deployment, without confirmations for their missing identities.
- Product validation, readiness, precheck, canonical resource contracts, PPF, coverage, workflows, FrontendV2, StudioUI, Playwright, and WebView2 Host were not modified.
- The full Vision Agent Quality lane advanced past the repaired Business Benchmark but failed the out-of-scope Planner Autonomy runner in `VA-PL-002`, `VA-PL-004`, and `VA-PL-008`. A separate artifact assertion also found an existing source-scan violation outside this task's allowed scope.
- Therefore, `ALL_LOCAL_LANES=NO`, no push is allowed, and the final state is `G01A_R2_BLOCKED_BY_LOCAL_TESTS`.

## Git and Evidence Boundary

- Initial remote SHA: `bea404394ac8cf403cca719c1990c426414a06c2`
- Initial local SHA: `5887387c0f9bc03489df994adbda5b0f2f6b039d`
- Preserved parent commits: `47d7494688cf335cfb3d3ead1f3fcdc67fea2eec`, `5887387c0f9bc03489df994adbda5b0f2f6b039d`
- Isolated worktree: `C:\cv-t01-g01a-r1-wt-20260730-213926`
- R1 evidence: `C:\cv-t01-g01a-r1-evidence-20260730-213926\verification-summary.md`
- R2 evidence: `C:\cv-t01-g01a-r2-evidence-20260731`
- Pre-fix benchmark artifacts: `before-business-benchmark.json`, `before-business-benchmark.md`, `before-business-benchmark.console.log`, `before-business-benchmark.exit-code.txt`
- Formal post-fix log: `formal-business-benchmark.console.log`, `formal-business-benchmark.exit-code.txt`

## Pre-Fix Failure Inventory

All 26 failed cases had the following shared execution evidence. The case-specific fields and root-cause classification follow in the table.

```text
ExpectedPrecheckReady=true
ExpectedRuntimePreviewReady=null
FlowOperators=op_cam:ImageAcquisition, op_match:TemplateMatching
ResourceParameters=op_cam.CameraBindingId=mock-camera-binding; op_match.TemplateId=catalog-template-a
ManualResourceConfirmations=camera_binding/op_cam.CameraBindingId; template_artifact/op_match.TemplateId
ValidationMissingResources=template_artifact/op_match.Template
PrecheckMissingResources=template_artifact/op_match.Template
FailureAssertions=precheckReady expected True.
ShouldBeResourceComplete=true
```

| CaseId | Category | TaskType | UserRequest | Classification | RootCause |
| --- | --- | --- | --- | --- | --- |
| VA-BM-006 | template_matching | generate | Generate a bracket alignment flow using template matching. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | `TemplateId` metadata did not bind the canonical `Template` input, while the helper confirmed `TemplateId`; precheck therefore could not resolve `op_match.Template`. |
| VA-BM-008 | template_matching | parameter_completion | Review and fill ROI parameters for template matching. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-009 | template_matching | modify_existing_flow | Raise the template matching minimum score threshold to 0.86. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-022 | modify_existing_flow | modify_existing_flow | Replace a template matching operator with a catalog template variant. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-024 | modify_existing_flow | modify_existing_flow | Change ResultJudgment thresholds while preserving existing connections. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-025 | parameter_completion | parameter_completion | Fill ImageAcquisition CameraId from a catalog selection. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-027 | parameter_completion | parameter_completion | Fill TemplateMatching TemplateId instead of TemplatePath. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-028 | parameter_completion | parameter_completion | Fill ResultOutput OutputChannelId and suppress conflicting Channel prompts. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-029 | parameter_completion | parameter_completion | Disable ImageAcquisition FilePath when camera source is selected. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-032 | runtime_preview | runtime_preview | Keep developer hidden RuntimePreview controls disabled by default. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-034 | precheck | precheck | Run static runtime package precheck for a ready draft. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-041 | package_readiness | package_readiness | Explain why a ready template matching draft can proceed to package review while no package is created. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-056 | station_compatibility | release_review | Check that a traditional template flow is compatible with the standard release Station profile. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-060 | operator_contract_validation | release_review | Validate ImageAcquisition TemplateMatching and ResultOutput metadata contracts. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-066 | pre_release_review | release_review | Require multi-station engineer approval for a metadata summary output flow. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-067 | pre_release_review | release_review | Block release review when the output channel kind is absent on the target Station profile. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-071 | release_review_final | release_review | Run Release Review Final for a traditional vision draft and keep all real deployment gates closed. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-073 | operator_contract_final | release_review | Validate final operator contract registry coverage for template matching metadata. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-081 | release_review_final | release_review | Run Release Review Final for a traditional vision draft and keep all real deployment gates closed. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-083 | operator_contract_final | release_review | Validate final operator contract registry coverage for template matching metadata. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-091 | release_review_final | release_review | Run Release Review Final for a traditional vision draft and keep all real deployment gates closed. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-093 | operator_contract_final | release_review | Validate final operator contract registry coverage for template matching metadata. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-101 | release_review_final | release_review | Run Release Review Final for a traditional vision draft and keep all real deployment gates closed. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-103 | operator_contract_final | release_review | Validate final operator contract registry coverage for template matching metadata. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-111 | release_review_final | release_review | Run Release Review Final for a traditional vision draft and keep all real deployment gates closed. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |
| VA-BM-113 | operator_contract_final | release_review | Validate final operator contract registry coverage for template matching metadata. | B_READY_CASE_MISSING_CANONICAL_RESOURCE_IDENTITY | Shared template fixture did not bind or confirm the same canonical `Template` identity. |

Classification counts: `A=0, B=26, C=0, D=0, E=0, F=0, G=0`. The shared builder also used deprecated `CameraBindingId`, but all 26 direct failures were caused by the unbound canonical `Template` identity and are assigned one primary classification, B.

## Intentional Missing-Resource Boundary

The 28 intentional missing-resource cases are identified by semantic builders, not `CaseId` conditionals.

| Missing resource | CaseIds | Product contract exposed |
| --- | --- | --- |
| Camera binding | 003, 017, 048, 065 | `camera_binding / CameraId` |
| Template | 007, 020, 049, 061, 070 | `template_artifact / Template` |
| Model | 016, 045, 053 | `model_resource / ModelPath` |
| Result output | 018, 044, 050, 062, 074, 084, 094, 104, 114 | `output_file / ResultOutput.SaveToFile`; the rejected synthetic `OutputChannelId` is not injected |
| PLC metadata | 019, 055, 077, 087, 097, 107, 117 | `plc_address / MitsubishiMcCommunication.Address` |

Every case above has `workflowDraftAllowed=true`, `readyForDeployment=false`, `deploymentBlocked=true`, and `metadataOnly=true`. No real camera, file, model, PLC, or Station is touched, and the missing resource never receives a manual confirmation. `VA-BM-012` retains its historical calibration-review semantics because the current product contract has no corresponding deployment resource requirement; it remains resource-complete and is not counted among the 28 intentional missing cases.

## Canonical Resource Mapping

| Resource | Flow contract | Confirmation contract | Canonical identity behavior |
| --- | --- | --- | --- |
| Camera | `ImageAcquisition.CameraId` with a stable binding ID | `camera_binding / <op>.CameraId / bound` | `VisionAgentResourceIdentity` normalizes to `camera_binding_id` |
| Template | Required `Template` input is connected; `TemplateId` supplies a stable catalog ID | resource key remains `<op>.TemplateId`; canonical parameter maps to `Template` | validation and precheck share `resource:v1|template_artifact|...|template` |
| Model | `DeepLearning.ModelId` | `model_resource / <op>.ModelId / bound` | canonical parameter is `modelid` |
| Result output | `ResultOutput.SaveToFile=true` | product resource kind `output_file`; absence of confirmation blocks deployment | product normalization maps the identity to `output_channel` |
| PLC | `MitsubishiMcCommunication.Address` | `plc_address / <op>.Address / bound` | product normalization maps the identity to `plc_output` |

Every generated confirmation includes `canonicalId`, `resourceKey`, `parameterName`, `status=bound`, `valueSummary`, and `metadataOnly=true`. Product precheck continues to validate the real `resourceType/operatorId/parameterName/resourceKey`; no bypass or test-only magic value was introduced.

## Shared Builder and Runner Changes

The repair updates `ValidWireFlow`, `ValidTemplateFlow`, `ValidHoleFlow`, `ValidModelIdFlow`, `ValidTemplateAndModelFlow`, `MultiCameraFlow`, the six missing-resource builders, `Case`, `BuildArguments`, and `ManualConfirmationsFor`. There is no `if (caseId == "VA-BM-xxx")` special case.

Runtime invariants now enforce:

1. Exactly 120 unique case IDs.
2. Exactly 28 intentional missing-resource cases.
3. No ready flow uses `CameraBindingId`, lowercase `cameraId`, or lowercase `sourceType` aliases.
4. Ready configured resources and manual confirmations use matching canonical identities; `TemplateId` also requires the canonical `Template` input binding.
5. Intentional missing identities cannot receive confirmations and must remain blocked by formal precheck.
6. Validation and precheck cannot report different canonical identities for the same resource.
7. Written JSON and Markdown must contain the current run timestamp, source commit SHA, and 120-case metadata.

The case-sensitive post-run parameter audit found `CameraBindingId=0`, lowercase `cameraId=0`, lowercase `sourceType=0`, canonical `CameraId=170`, and canonical `SourceType=174` occurrences across generated case flows.

## Before and After Metrics

| Metric | Before | After |
| --- | ---: | ---: |
| Cases | 120 | 120 |
| Case assertions passed | 94 | 120 |
| Accepted | false | true |
| Parameter completion | 0.4250 | 0.7667 |
| Ready cases | 92 | 92 |
| Ready cases parameter-complete | 51 | 92 |
| Intentional missing cases | 28 | 28 |
| Intentional missing still blocked | 28 | 28 |
| Safety violations | 0 | 0 |

The formal generated artifacts are bound to `workflowRun.commitSha=5887387c0f9bc03489df994adbda5b0f2f6b039d`, branch `t01-g01a-r1`, run ID `local-r2-quality`, and one shared `generatedAtUtc`. This is the source HEAD used to generate them; the final commit contains only this task's allowed files.

## Verification Commands and Results

| Verification | Command summary | Result |
| --- | --- | --- |
| Pre-fix Business Benchmark | `python quality/tools/run_vision_agent_business_benchmark.py --output C:\cv-t01-g01a-r2-evidence-20260731\before-business-benchmark.json --report C:\cv-t01-g01a-r2-evidence-20260731\before-business-benchmark.md` | FAIL exit 1; 94/120; completion 0.4250 |
| Formal Business Benchmark | `python quality/tools/run_vision_agent_business_benchmark.py --output quality/evals/reports/VisionAgent_business_benchmark_baseline.json --report quality/evals/reports/VisionAgent_business_benchmark_baseline.md` | PASS exit 0; 120/120; completion 0.7667; accepted true |
| ImageAcquisitionOperatorTests | `& .\scripts\run-dotnet-test-serial.ps1 -Project ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj -FullyQualifiedName ClearVision.Product.Tests.Operators.ImageAcquisitionOperatorTests -NoBuild -NoRestore ...` | PASS 18/18 |
| Four BuildFromPlan tests | Same serial script with the four fully-qualified methods merged into one invocation | PASS 4/4 |
| VisionAgentGenerateFlowTests | Same serial script with `ClearVision.Product.Tests.AI.VisionAgentGenerateFlow.VisionAgentGenerateFlowTests` | PASS 22/22 |
| Test Governance | `& .\scripts\run-test-governance.ps1 -ReportDirectory C:\cv-t01-g01a-r2-evidence-20260731\formal\governance -FailOnWarning` | PASS; 3710 definitions; 0 issues |
| Product PR | `& .\scripts\run-classified-test-gate.ps1 -Gate product-pr -Configuration Debug -Verbosity normal -ResultsDirectory C:\cv-t01-g01a-r2-evidence-20260731\formal\product-pr -LogFileName product-pr.trx -NoBuild -NoRestore` | PASS; 2448 total, 2446 passed, 2 skipped, 0 failed |
| Vision Agent Quality | `python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run` | FAIL exit 1 after backend 615/615, UI 397/397, desktop endpoints 44/44, and Business 120/120 passed; Planner Autonomy 18/21 |
| Safe CI Desktop PR | `& .\scripts\run-classified-test-gate.ps1 -Gate desktop-pr -Configuration Debug -Verbosity normal -ResultsDirectory C:\cv-t01-g01a-r2-evidence-20260731\formal\safe-ci\desktop-pr -LogFileName desktop-pr.trx -NoBuild -NoRestore` | PASS 619/619 |
| Safe CI JavaScript syntax | Workflow-equivalent `node --check` over the selected JavaScript files | PASS 31 files |
| Safe CI UI contract | `npm run test:agent-ui-contract` from `ClearVision.Product/tests/ClearVision.Product.UI.Tests` | PASS 397/397 |
| Safe CI diff checks | Diff-base self-test plus `check-diff-hygiene.ps1 -BaseRef origin/codex初稿` | PASS |
| Artifact assertion | `assert_vision_agent_report_artifacts.py --scan-source-files --write-manifest ...` | FAIL exit 2 before manifest write; existing `Authorization bearer literal` in `inspection-controller-memory.test.mjs` |

## Local Blockers

Planner evidence is under `C:\cv-t01-g01a-r2-evidence-20260731\formal\planner-autonomy-failure.*`. Cases `VA-PL-002`, `VA-PL-004`, and `VA-PL-008` each fail with `precheck readyForDeployment expected True.` The Planner Autonomy runner is outside this task's permitted modification scope.

Artifact assertion evidence is `C:\cv-t01-g01a-r2-evidence-20260731\formal\artifact-assertion.log` and `.exit-code.txt`. It identifies `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/inspection-controller-memory.test.mjs`; the manifest was not written. That file is also outside this task's permitted scope.

## Scope Proof and Remaining Issue

- The only task changes are the Business Benchmark runner, its generated JSON and Markdown baselines, and this report.
- There are no changes under `ClearVision.Product/src/**`, `.github/workflows/**`, PPF, coverage, FrontendV2, StudioUI, Playwright, or WebView2 Host in the isolated task diff.
- The main worktree and `studio-ui-next` had independent pre-existing work. This task issued no write command in either location.
- The R1 evidence directory was not modified.
- The artifact manifest remains unchanged because the assertion stopped before writing it.
- Product full coverage was not run. The known issue remains: Product coverage is still blocked by the PPFMatcher coverlet instrumentation hotspot.
- Because `ALL_LOCAL_LANES=NO`, this task must not perform a partial-green push or trigger remote Safe CI / Vision Agent Quality.

Final state: `G01A_R2_BLOCKED_BY_LOCAL_TESTS`.
