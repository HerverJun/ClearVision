# Vision Agent RuntimePreview v1.4 Release Review Simulator Evidence

## Scope

RuntimePreview v1.4 upgrades the v1.3 Manifest Dry-Run into a metadata-only release review simulator. The simulator closes the loop across Station Compatibility, Operator Contract validation, Release Review Decision, Review Desk v2, Redacted Flow Corpus v2, Agent Explanation v3, and GovernanceStore v4.

The release review path remains offline and redacted. It does not create packages, deploy, hot-load, connect to stations, read real images, load real models/templates, write PLCs, or enable a Real RuntimePreview adapter.

## Release Review Chain

`RuntimePreviewPreReleaseReviewService` now chains:

1. workflow draft inspection
2. pilot readiness
3. session simulation
4. package readiness
5. manifest dry-run
6. station compatibility dry-run
7. operator contract validation
8. release review decision

The emitted `PreReleaseReviewReport` includes `reviewId`, `caseId`, `sessionId`, `workflowDraftHash`, `manifestId`, `stationProfileId`, `operatorContractVersion`, `readinessStatus`, `packageReviewAllowed`, `stationCompatible`, `operatorContractsSatisfied`, `releaseReviewAllowed`, `requiresEngineerApproval`, `blockedReasons`, `riskLevel`, `engineerActions`, and the safety flags `metadataOnly=true`, `packageCreated=false`, `deploymentExecuted=false`, `realResourcesTouched=false`.

## Station Compatibility

`RuntimePreviewStationCompatibilityDryRunService` evaluates redacted `RuntimePreviewStationProfile` metadata only:

- `stationProfileId`
- `stationType`
- `runtimeVersion`
- `supportedOperatorTypes`
- `supportedModelKinds`
- `cameraBindingSlots`
- `outputChannelKinds`
- `maxOperatorCount`
- `plcWriteAllowed=false`
- `resourcePolicy`
- `networkPolicy=redacted`

The dry-run checks runtime version, operator support, camera slot count, output channel kind, model/template metadata dependency closure, operator count, PLC/Station direct intent, and manifest risk. It never connects to a real Station.

## Operator Contracts

`RuntimePreviewOperatorContractRegistry` provides metadata-only contracts for ImageAcquisition, TemplateMatching, CircleMeasurement, MeasureDistance, DeepLearning, ResultOutput, and the existing major operator families. Each contract declares:

- `requiredInputs`
- `requiredOutputs`
- `requiredParameters`
- `resourceDependencies`
- `forbiddenParameters`
- `runtimeDependencies`
- `manifestFields`
- `stationCompatibilityRequirements`
- `riskTags`

Validation checks contracts only. It does not execute operators and does not read image, model, or template files.

## Redacted Corpus v2

The v1.4 corpus contains 32 metadata-only cases. It covers low-spec IPC operator count overflow, camera slot shortage, unsupported DeepLearning, missing output channel, PLC write blocked, runtime too old, incompatible model kinds, missing template metadata, traditional vision pass, DeepLearning approval required, multi-station review, release allowed, release blocked, and engineer approval paths.

Each case now includes `stationProfileId`, `operatorContractExpectations`, `expectedStationCompatibility`, `expectedReleaseReviewDecision`, `requiredEngineerApprovals`, and `expectedBlockedReasons`.

## Review Desk v2

The developer-only RuntimePreview console is now a pre-release review desk. It supports selecting a corpus case and station profile, running the full review chain, displaying readiness, package readiness, manifest dry-run, station compatibility, operator contract validation, release decision, risk, blocked reasons, and engineer actions. Lookup/export supports `reviewId`, `manifestId`, `stationProfileId`, and `caseId`.

The desk remains hidden from normal users. DOM, API, console output, and generated artifacts are redacted.

## Explanation v3

Agent Explanation v3 is written for industrial vision engineers. It explains why a release is allowed, blocked, or requires approval; which operator contract failed; which resource dependency is not closed; why the target station is compatible or incompatible; why `workflowDraftAllowed=true` can still result in `releaseReviewAllowed=false`; and which engineer action should be fixed first.

The v1.3 readability issue is covered by the v1.4 evidence: status fields are populated, and Ready/Blocked states are not emitted as `None`.

## GovernanceStore v4

GovernanceStore remains JSONL based and adds these streams:

- `pre_release_review_report`
- `station_compatibility_report`
- `operator_contract_validation_report`

Lookup/export/index/retention/corruption recovery now cover `reviewId`, `stationProfileId`, `manifestId`, and `caseId` for the new streams while preserving manifest, package, business, planner, and shadow report artifacts.

## Artifact Set

New v1.4 artifacts:

- `quality/evals/reports/runtime_preview_redacted_flow_corpus_v2.json`
- `quality/evals/reports/runtime_preview_redacted_flow_corpus_v2.md`
- `quality/evals/reports/runtime_preview_station_profiles_sample.json`
- `quality/evals/reports/runtime_preview_station_profiles_sample.md`
- `quality/evals/reports/runtime_preview_operator_contract_registry.json`
- `quality/evals/reports/runtime_preview_operator_contract_registry.md`
- `quality/evals/reports/runtime_preview_operator_contract_validation_sample.json`
- `quality/evals/reports/runtime_preview_operator_contract_validation_sample.md`
- `quality/evals/reports/runtime_preview_station_compatibility_dry_run.sample.json`
- `quality/evals/reports/runtime_preview_station_compatibility_dry_run.sample.md`
- `quality/evals/reports/runtime_preview_pre_release_review_report.sample.json`
- `quality/evals/reports/runtime_preview_pre_release_review_report.sample.md`
- `quality/evals/reports/runtime_preview_agent_explanation_v3.json`
- `quality/evals/reports/runtime_preview_agent_explanation_v3.md`
- `quality/evals/reports/runtime_preview_governance_export_sample.json`
- `quality/evals/reports/runtime_preview_governance_export_sample.md`

Preserved artifacts include manifest dry-run, package readiness, business benchmark, planner autonomy, real LLM shadow fixed/holdout sample reports, runtime preview scenario corpus, governance audit, and deploy readiness metadata-only reports.

## Quality Results

Local evidence as of 2026-06-07:

| Gate | Result |
| --- | --- |
| Backend Agent harness | 453 passed / 0 failed, minimum 370 |
| AI endpoint regression | 30 passed / 0 failed, minimum 30 |
| UI contract | 136 passed / 0 failed, minimum 135 |
| Business benchmark | 70 cases, accepted=true, minimum 70 |
| Redacted corpus v2 | 32 cases, accepted=true, minimum 30 |
| Manifest dry-run | 32 cases, accepted=true, minimum 30 |
| Station compatibility | 32 cases, accepted=true, minimum 30 |
| Operator contract validation | 32 cases, accepted=true, minimum 30 |
| Pre-release review | 32 cases, accepted=true, minimum 30 |
| Agent explanation v3 | 32 cases, accepted=true, minimum 30 |
| Artifact/source scan | 44 artifact files, 20 JSON reports, 6280 source files scanned |
| Redaction gate | forbiddenHitCount=0, redactionPass=true |

Commands/evidence:

- `& "./scripts/run-dotnet-test-serial.ps1" ... -LogFileName "agent_engineering_harness.trx" -MinimumTotalTests 370 -MinimumPassedTests 370`
- `& "./scripts/run-dotnet-test-serial.ps1" ... -LogFileName "AiModelEndpointsTests.trx" -MinimumTotalTests 30 -MinimumPassedTests 30`
- `npm run test:agent-ui-contract`
- `dotnet run --project quality/tools/VisionAgentBusinessBenchmarkRunner/VisionAgentBusinessBenchmarkRunner.csproj ...`
- `python quality/tools/run_runtime_preview_scenario_evidence.py ... --minimum-cases 30`
- `python quality/tools/assert_vision_agent_report_artifacts.py --scan-source-files --write-manifest quality/evals/reports/vision_agent_quality_artifact_manifest.json --write-report quality/evals/reports/vision_agent_quality_artifact_manifest.md`

## CI Evidence

Current local pre-push evidence:

| Field | Value |
| --- | --- |
| runId | local-pre-push-20260607-runtime-preview-v1.4 |
| runNumber | local-pre-push |
| artifactId | local-vision-agent-quality-artifact-manifest |
| digest | sha256:cabf7757e8ad75508b98b2fe97b9204cc8832040d3d933f6901d0ee59c1d47d8 |
| headSha | fabc086a11e3016145a2e296750ab1315aa0f9ed |
| branch | codex初稿 |
| conclusion | local-pass |

Remote GitHub Actions evidence must be refreshed after the v1.4 commit is pushed. The workflow is configured to run artifact assertion with non-local workflow metadata before upload.

## Safety Statement

RuntimePreview v1.4 did not advance any real resource capability. This round does not add real camera SDK access, real Station access, real image reads, real model/template file loads, PLC writes, real package archive creation, real packaging, real deployment, hot-load, Real RuntimePreview adapter activation, shell/system command tool access, or `Acme.Product.*` dependency.
