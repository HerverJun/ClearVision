# Vision Agent RuntimePreview v1.4 Release Review Simulator Evidence

## Scope

RuntimePreview v1.4 upgrades the v1.3 Manifest Dry-Run into a metadata-only release review simulator. The simulator closes the loop across Station Compatibility, Operator Contract validation, Release Review Decision, Review Desk v2, Redacted Flow Corpus, Agent Explanation, and GovernanceStore v4.

The release review path remains offline and redacted. It does not create packages, deploy, hot-load, connect to stations, read real images, load real models/templates, write PLCs, or enable a Real RuntimePreview adapter.

## Release Review Chain

`RuntimePreviewPreReleaseReviewService` chains:

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

`RuntimePreviewOperatorContractRegistry` provides metadata-only contracts for ImageAcquisition, TemplateMatching, CircleMeasurement, MeasureDistance, DeepLearning, ResultOutput, and the existing major operator families. Each contract declares required inputs, outputs, parameters, resource dependencies, forbidden parameters, runtime dependencies, manifest fields, Station compatibility requirements, and risk tags.

Validation checks contracts only. It does not execute operators and does not read image, model, or template files.

## Redacted Corpus Final

The final v1.4 hardening corpus contains 60 metadata-only cases. It covers low-spec IPC operator count overflow, camera slot shortage, unsupported DeepLearning, missing output channel, PLC write blocked, runtime too old, incompatible model kinds, missing template metadata, traditional vision pass, DeepLearning approval required, multi-station review, release allowed, release blocked, engineer approval paths, external path denials, package path denial, and image byte payload denial.

Each case includes `stationProfileId`, `operatorContractExpectations`, `expectedStationCompatibility`, `expectedReleaseReviewDecision`, `requiredEngineerApprovals`, and `expectedBlockedReasons`.

## Review Desk v2

The developer-only RuntimePreview console is a pre-release review desk. It supports selecting a corpus case and station profile, running the full review chain, displaying readiness, package readiness, manifest dry-run, station compatibility, operator contract validation, release decision, risk, blocked reasons, and engineer actions. Lookup/export supports `reviewId`, `manifestId`, `stationProfileId`, and `caseId`.

The desk remains hidden from normal users. DOM, API, console output, and generated artifacts are redacted.

## Explanation Final

Agent Explanation Final is written for industrial vision engineers. It explains why a release is allowed, blocked, or requires approval; which operator contract failed; which resource dependency is not closed; why the target Station is compatible or incompatible; why `workflowDraftAllowed=true` can still result in `releaseReviewAllowed=false`; and which engineer action should be fixed first.

The v1.3 readability issue is covered by final evidence: status fields are populated, and Ready/Blocked states are not emitted as `None`.

## GovernanceStore v4

GovernanceStore remains JSONL based and adds these streams:

- `pre_release_review_report`
- `station_compatibility_report`
- `operator_contract_validation_report`
- `release_review_decision`
- `station_profile_snapshot`
- `operator_contract_registry_snapshot`
- `contract_coverage_report`
- `final_governance_export`

Lookup/export/index/retention/corruption recovery now cover `reviewId`, `stationProfileId`, `manifestId`, and `caseId` for the release review streams while preserving manifest, package, business, planner, shadow, audit, and deploy readiness artifacts.

## Artifact Set

Final v1.4 artifacts include:

- `quality/evals/reports/runtime_preview_redacted_flow_corpus_final.json`
- `quality/evals/reports/runtime_preview_redacted_flow_corpus_final.md`
- `quality/evals/reports/runtime_preview_station_profiles_final.json`
- `quality/evals/reports/runtime_preview_station_profiles_final.md`
- `quality/evals/reports/runtime_preview_operator_contract_registry_final.json`
- `quality/evals/reports/runtime_preview_operator_contract_registry_final.md`
- `quality/evals/reports/runtime_preview_operator_contract_coverage.json`
- `quality/evals/reports/runtime_preview_operator_contract_coverage.md`
- `quality/evals/reports/runtime_preview_operator_contract_validation_final.json`
- `quality/evals/reports/runtime_preview_operator_contract_validation_final.md`
- `quality/evals/reports/runtime_preview_station_compatibility_final.json`
- `quality/evals/reports/runtime_preview_station_compatibility_final.md`
- `quality/evals/reports/runtime_package_manifest_dry_run_final.json`
- `quality/evals/reports/runtime_package_manifest_dry_run_final.md`
- `quality/evals/reports/runtime_preview_package_readiness_final.json`
- `quality/evals/reports/runtime_preview_package_readiness_final.md`
- `quality/evals/reports/runtime_preview_pre_release_review_final.json`
- `quality/evals/reports/runtime_preview_pre_release_review_final.md`
- `quality/evals/reports/runtime_preview_release_decision_matrix.json`
- `quality/evals/reports/runtime_preview_release_decision_matrix.md`
- `quality/evals/reports/runtime_preview_agent_explanation_final.json`
- `quality/evals/reports/runtime_preview_agent_explanation_final.md`
- `quality/evals/reports/runtime_preview_governance_export_final.json`
- `quality/evals/reports/runtime_preview_governance_export_final.md`
- `quality/evals/reports/runtime_preview_report_readability_gate.json`
- `quality/evals/reports/runtime_preview_report_readability_gate.md`

Preserved artifacts include manifest dry-run samples, package readiness samples, business benchmark, planner autonomy, real LLM shadow fixed/holdout sample reports, runtime preview scenario corpus, governance audit, and deploy readiness metadata-only reports.

## Quality Results

Local evidence as of 2026-06-07:

| Gate | Result |
| --- | --- |
| Solution build | 0 errors |
| Backend Agent harness | 593 passed / 0 failed, minimum 560 |
| AI endpoint regression | 42 passed / 0 failed, minimum 42 |
| UI contract | 194 passed / 0 failed, minimum 190 |
| Business benchmark | 120 cases, 120 passed, accepted=true, minimum 120 |
| Redacted corpus final | 60 cases, accepted=true, minimum 60 |
| Manifest dry-run final | 60 cases, accepted=true, minimum 60 |
| Station compatibility final | 60 cases, accepted=true, minimum 60 |
| Operator contract validation final | 60 cases, accepted=true, minimum 60 |
| Pre-release review final | 60 cases, accepted=true, minimum 60 |
| Agent explanation final | 60 cases, accepted=true, minimum 60 |
| Artifact/source scan | 72 artifact files, 33 reports, 3380 source files scanned |
| Redaction gate | forbiddenHitCount=0, redactionPass=true |

Commands/evidence:

- `dotnet build "ClearVision.Product/ClearVision.Product.sln" --no-restore --nologo --verbosity minimal`
- `python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run`
- `python quality/tools/assert_vision_agent_report_artifacts.py --scan-source-files --write-manifest quality/evals/reports/vision_agent_quality_artifact_manifest.json --write-report quality/evals/reports/vision_agent_quality_artifact_manifest.md`

## CI Evidence

Current local pre-push evidence is accepted. Remote GitHub Actions evidence must be refreshed after this final hardening commit is pushed. The workflow is configured to run artifact assertion with non-local workflow metadata before upload, and the CI evidence report must be updated with the successful run id, artifact id, and digest.

## Safety Statement

RuntimePreview v1.4 did not advance any real resource capability. This round does not add real camera SDK access, real Station access, real image reads, real model/template file loads, PLC writes, real package archive creation, real packaging, real deployment, hot-load, Real RuntimePreview adapter activation, shell/system command tool access, or `Acme.Product.*` dependency.
