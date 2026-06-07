# Vision Agent CI Evidence Report 20260606

## Workflow Run #32 - RuntimePreview Final Pre-Pilot Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| job | Agent Engineering Harness |
| branchName | codex初稿 |
| commitSha | cac7d7519de439bac7283fc4b0f9b6b03f82a07e |
| headSha | cac7d7519de439bac7283fc4b0f9b6b03f82a07e |
| runId | 27083878486 |
| runAttempt | 1 |
| runNumber | 32 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27083878486 |
| jobId | 79934500290 |
| jobUrl | https://github.com/HerverJun/ClearVision/actions/runs/27083878486/job/79934500290 |
| startedAtUtc | 2026-06-07T05:33:05Z |
| completedAtUtc | 2026-06-07T05:36:02Z |

## Workflow Run #32 Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7461102803 |
| size | 467265 bytes |
| digest | sha256:354ce5f2307ebe93531b32ff6f952a677af9a8fc807726bc2213f387774d91e3 |
| expired | false |
| createdAtUtc | 2026-06-07T05:35:53Z |
| expiresAtUtc | 2026-09-05T05:33:05Z |

Run #32 is the remote CI closure for RuntimePreview Final pre-pilot hardening. It supersedes failed run #31 and the local pre-push v1.4 evidence. The remote job completed the quality suite, generated the real LLM shadow sample, asserted artifact reports with non-local workflow metadata, and uploaded the `vision-agent-quality-suite` artifact.

Remote quality evidence:

- Backend Agent harness: `593` passed, `0` failed, minimum `560`.
- AI endpoint regression: `42` passed, `0` failed, minimum `42`.
- UI contract: `194` passed, `0` failed, minimum `190`.
- Business benchmark: `120` cases, `120` passed, `accepted=true`.
- Planner autonomy benchmark: `21` cases, `21` passed, `accepted=true`.
- RuntimePreview Final evidence: `60` metadata-only cases, `realResourcesTouched=false`.
- Station Profile Final: `12` profiles, `accepted=true`.
- Operator Contract Registry Final: `16` metadata-only contracts, version `operator-contract-registry.final.metadata-only`.
- PreRelease Review Final: `60` reports, `14` release allowed, `10` require approval, `36` blocked.
- GovernanceStore Final: JSONL v4 final export, `60` sessions, `600` audit events.
- Artifact/source scan: `72` artifact files, `33` reports, `3381` source files scanned.
- Redaction: `forbiddenHitCount=0`, `redactionPass=true`.

Remote run #32 does not add real camera SDK access, real Station access, real image/model/template reads, PLC writes, real package creation, packaging, deployment, hot-load, Real RuntimePreview adapter activation, shell/system command tool access, or legacy non-ClearVision product namespace dependency.

## Historical Local Pre-Push Evidence - RuntimePreview v1.4 Release Review Simulator

This historical section records the earlier local pre-push evidence for RuntimePreview v1.4 before the final hardening gate was raised. It is superseded by Workflow Run #32 above.

| Field | Value |
| --- | --- |
| branchName | codex初稿 |
| headSha | fabc086a11e3016145a2e296750ab1315aa0f9ed |
| runId | local-pre-push-20260607-runtime-preview-v1.4 |
| runAttempt | local |
| runNumber | local-pre-push |
| conclusion | local-pass |
| artifactId | local-vision-agent-quality-artifact-manifest |
| digest | sha256:cabf7757e8ad75508b98b2fe97b9204cc8832040d3d933f6901d0ee59c1d47d8 |

Local quality evidence:

- Backend Agent harness: `453` passed, `0` failed, minimum `370`.
- AI endpoint regression: `30` passed, `0` failed, minimum `30`.
- UI contract: `136` passed, `0` failed, minimum `135`.
- Business benchmark: `70` cases, `accepted=true`.
- RuntimePreview v1.4 evidence: `32` metadata-only cases, `realResourcesTouched=false`.
- Artifact/source scan: `44` artifact files, `20` JSON reports, `6280` source files scanned.
- Redaction: `forbiddenHitCount=0`, `redactionPass=true`.

RuntimePreview v1.4 adds metadata-only Station Compatibility, Operator Contract validation, PreReleaseReview decisioning, Review Desk v2, Redacted Flow Corpus v2, Agent Explanation v3, and GovernanceStore v4 streams. It does not add real camera SDK access, real Station access, real image/model/template reads, PLC writes, real package creation, packaging, deployment, hot-load, or a Real RuntimePreview adapter.

## Workflow Run #28 - Latest RuntimePreview v1.3 Manifest Dry-Run Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| job | Agent Engineering Harness |
| branchName | codex初稿 |
| commitSha | a9c98ea6d4f982c2d6570447b0f010d4ce9cfb6f |
| headSha | a9c98ea6d4f982c2d6570447b0f010d4ce9cfb6f |
| runId | 27065707900 |
| runAttempt | 1 |
| runNumber | 28 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27065707900 |
| completedAtUtc | 2026-06-06T15:06:42Z |

## Workflow Run #28 Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7455439493 |
| size | 206 KB |
| digest | sha256:a924fc679bbebf4c3e48600aa0e6696ae6a52602fe0cd5bdc2fea57e3c4de232 |
| expired | false |

Run #28 is the latest v1.3 evidence for RuntimePackage Manifest Dry-Run, Redacted Flow Corpus, Package Readiness Bridge v2, Productized Pilot Console, Governance Store v3, and Agent Explanation v2. It supersedes run #26 for RuntimePreview readiness evidence.

Artifact contents include the existing Vision Agent benchmark reports plus the new v1.3 reports:

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `runtime_preview_scenario_corpus.json`
- `runtime_preview_scenario_corpus.md`
- `runtime_preview_redacted_flow_corpus.json`
- `runtime_preview_redacted_flow_corpus.md`
- `runtime_preview_scenario_evidence.json`
- `runtime_preview_scenario_evidence.md`
- `runtime_preview_deploy_readiness_report.sample.json`
- `runtime_preview_deploy_readiness_report.sample.md`
- `runtime_preview_package_readiness_report.sample.json`
- `runtime_preview_package_readiness_report.sample.md`
- `runtime_package_manifest_dry_run.sample.json`
- `runtime_package_manifest_dry_run.sample.md`
- `runtime_preview_governance_audit_sample.json`
- `runtime_preview_governance_audit_sample.md`
- `runtime_preview_governance_export_sample.json`
- `runtime_preview_governance_export_sample.md`
- `runtime_preview_agent_explanation_benchmark.json`
- `runtime_preview_agent_explanation_benchmark.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
- `real_llm_planner_shadow_eval.holdout.json`
- `real_llm_planner_shadow_eval.holdout.md`
- `vision_agent_quality_artifact_manifest.json`
- `vision_agent_quality_artifact_manifest.md`
- `agent_engineering_harness.trx`
- `agent_ai_model_config_endpoints.trx`
- `agent_ui_contract_output.txt`

## Run #28 RuntimePreview v1.3 Summary

| Gate | Result |
| --- | --- |
| backend Agent tests | 333 total / 333 passed |
| AI endpoint regression | 25 total / 25 passed |
| UI contract tests | 111 total / 111 passed |
| executable business benchmark | 55 / 55 accepted |
| planner autonomy + permission negative benchmark | 21 / 21 accepted |
| RuntimePreview scenario evidence | 20 / 20 accepted |
| RuntimePreview redacted flow corpus | 20 / 20 accepted |
| Package readiness bridge v2 sample | 20 cases, 7 ready, 13 blocked, packageCreated=false, deploymentExecuted=false |
| RuntimePackage manifest dry-run sample | 20 cases, 7 review allowed, 13 blocked, manifestArtifactGenerated=false, packageCreated=false |
| Agent explanation v2 benchmark | 20 / 20 accepted |
| artifact/source/report/session/audit/manifest scan | CI assertion passed; local scan scanned 3338 source/report files, forbiddenHitCount=0, redactionPass=true |

Artifact manifest scan policy:

- scanPolicyVersion: `2026-06-06.runtime-preview-v1.3-manifest-dry-run-scan.v3`
- sourceFilesScanned: `3338` in local pre-push scan
- reportsScanned: `13`
- auditReportsScanned: `2`
- sessionReportsScanned: `14`
- forbiddenHitCount: `0`
- redactionPass: `true`

CI artifact report checks:

- business benchmark `workflowRun.commitSha`: `a9c98ea6d4f982c2d6570447b0f010d4ce9cfb6f`
- business benchmark `workflowRun.runId`: `27065707900`
- redacted flow corpus `workflowRun.runId`: `27065707900`
- manifest dry-run `workflowRun.runId`: `27065707900`
- package readiness `workflowRun.runId`: `27065707900`
- Agent explanation `workflowRun.runId`: `27065707900`
- All CI artifact JSON reports are required by `assert_vision_agent_report_artifacts.py --require-non-local-workflow-run`; run #28 succeeded, so `commitSha`, `branchName`, `runId`, and `runAttempt` were non-local in uploaded reports.

RuntimePreview v1.3 adds metadata-only Redacted Flow Corpus, RuntimePackage Manifest Dry-Run, Package Readiness Bridge v2, pre-release review desk UI, Governance Store v3 manifest stream, and Agent Explanation v2. It still does not touch real camera SDKs, Station, image files, model files, PLC, real package files, packaging, deployment, hot-load paths, or a Real RuntimePreview adapter.

## Workflow Run #26 - Latest RuntimePreview v1.2 Scenario Corpus Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| job | Agent Engineering Harness |
| branchName | codex初稿 |
| commitSha | 78392d9f031d14d4a5cbf0ed4e8842db8ca4bf29 |
| headSha | 78392d9f031d14d4a5cbf0ed4e8842db8ca4bf29 |
| runId | 27063945580 |
| runAttempt | 1 |
| runNumber | 26 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27063945580 |
| completedAtUtc | 2026-06-06T13:47:45Z |

## Workflow Run #26 Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7454900152 |
| sizeBytes | 180974 |
| digest | sha256:f15249e4e07a55f5d1995443fb32f7bfaa33d75f42cb418297b0e07801c4b45d |
| createdAtUtc | 2026-06-06T13:47:38Z |
| expired | false |

Run #26 is the latest v1.2 evidence for RuntimePreview Scenario Corpus + Package Readiness Bridge + Independent Pilot Console. It supersedes run #24 for artifact evidence because the upload list now includes v1.2 corpus, package readiness, governance export, and Agent explanation reports.

Artifact contents verified after download:

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `runtime_preview_scenario_corpus.json`
- `runtime_preview_scenario_corpus.md`
- `runtime_preview_scenario_evidence.json`
- `runtime_preview_scenario_evidence.md`
- `runtime_preview_deploy_readiness_report.sample.json`
- `runtime_preview_deploy_readiness_report.sample.md`
- `runtime_preview_package_readiness_report.sample.json`
- `runtime_preview_package_readiness_report.sample.md`
- `runtime_preview_governance_audit_sample.json`
- `runtime_preview_governance_audit_sample.md`
- `runtime_preview_governance_export_sample.json`
- `runtime_preview_governance_export_sample.md`
- `runtime_preview_agent_explanation_benchmark.json`
- `runtime_preview_agent_explanation_benchmark.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
- `real_llm_planner_shadow_eval.holdout.json`
- `real_llm_planner_shadow_eval.holdout.md`
- `vision_agent_quality_artifact_manifest.json`
- `vision_agent_quality_artifact_manifest.md`
- `agent_engineering_harness.trx`
- `agent_ai_model_config_endpoints.trx`
- `agent_ui_contract_output.txt`

## Run #26 RuntimePreview v1.2 Summary

| Gate | Result |
| --- | --- |
| backend Agent tests | 291 total / 291 passed |
| AI endpoint regression | 22 total / 22 passed |
| UI contract tests | 90 total / 90 passed |
| executable business benchmark | 45 / 45 accepted |
| planner autonomy + permission negative benchmark | 21 / 21 accepted |
| RuntimePreview scenario corpus | 15 cases, accepted |
| RuntimePreview scenario evidence | 15 / 15 accepted |
| Package readiness bridge sample | 6 ready / 9 blocked, packageCreated=false, deploymentExecuted=false |
| Agent explanation benchmark | 15 / 15 accepted |
| artifact/source/report/session/audit scan | forbiddenHitCount=0, redactionPass=true |

Artifact manifest scan fields:

- scanPolicyVersion: `2026-06-06.runtime-preview-v1.2-governance-scan.v2`
- sourceFilesScanned: 2809
- reportsScanned: 11
- auditReportsScanned: 2
- sessionReportsScanned: 14
- forbiddenHitCount: 0
- redactionPass: true

CI artifact report checks:

- business benchmark `workflowRun.commitSha`: `78392d9f031d14d4a5cbf0ed4e8842db8ca4bf29`
- business benchmark `workflowRun.runId`: `27063945580`
- scenario corpus `workflowRun.runId`: `27063945580`
- package readiness `workflowRun.runId`: `27063945580`
- Agent explanation `workflowRun.runId`: `27063945580`
- All checked `workflowRun.commitSha`, `branchName`, `runId`, and `runAttempt` fields are non-local.

RuntimePreview v1.2 adds metadata-only Scenario Corpus, Package Readiness Bridge, independent developer Pilot Console, governance index/export hardening, and Agent explanation benchmark. It still does not touch real camera SDKs, Station, image files, model files, PLC, `.cvpkg` creation, packaging, deployment, hot-load paths, or a Real RuntimePreview adapter.

## Workflow Run #24 - Latest RuntimePreview v1.1 Persistent Governance Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| job | Agent Engineering Harness |
| branchName | codex初稿 |
| commitSha | 888568abb4a3b108f1af1690f5a656ba5f26cf6e |
| headSha | 888568abb4a3b108f1af1690f5a656ba5f26cf6e |
| runId | 27062552222 |
| runAttempt | 1 |
| runNumber | 24 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27062552222 |
| completedAtUtc | 2026-06-06T12:43:52Z |

## Workflow Run #24 Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7454479284 |
| sizeBytes | 152933 |
| digest | sha256:fce70cdf762750f5251e5045696accf3b1890a0eb66fc4667a8a26794e304484 |
| createdAtUtc | 2026-06-06T12:43:44Z |
| expired | false |

Run #24 is the latest v1.1 evidence for RuntimePreview Persistent Governance. It supersedes run #23 for artifact evidence because the upload list now includes the v1.1 scenario, deploy-readiness, and audit sample reports.

Artifact contents verified after download:

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `runtime_preview_scenario_evidence.json`
- `runtime_preview_scenario_evidence.md`
- `runtime_preview_deploy_readiness_report.sample.json`
- `runtime_preview_deploy_readiness_report.sample.md`
- `runtime_preview_governance_audit_sample.json`
- `runtime_preview_governance_audit_sample.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
- `real_llm_planner_shadow_eval.holdout.json`
- `real_llm_planner_shadow_eval.holdout.md`
- `vision_agent_quality_artifact_manifest.json`
- `vision_agent_quality_artifact_manifest.md`
- `agent_engineering_harness.trx`
- `agent_ai_model_config_endpoints.trx`
- `agent_ui_contract_output.txt`

## Run #24 RuntimePreview v1.1 Summary

| Gate | Result |
| --- | --- |
| backend Agent tests | 253 total / 253 passed |
| AI endpoint regression | 19 total / 19 passed |
| UI contract tests | 78 total / 78 passed |
| executable business benchmark | 37 / 37 accepted |
| planner autonomy + permission negative benchmark | 21 / 21 accepted |
| RuntimePreview scenario evidence | 8 / 8 accepted |
| deploy readiness sample | 4 ready / 4 blocked, packageCreated=false, deploymentExecuted=false |
| governance audit sample | 13 redacted append-only events |
| artifact/source/report/session/audit scan | forbiddenHitCount=0, redactionPass=true |

Artifact manifest scan fields:

- scanPolicyVersion: `2026-06-06.runtime-preview-v1-governance-scan.v1`
- sourceFilesScanned: 2799
- reportsScanned: 7
- auditReportsScanned: 2
- sessionReportsScanned: 6
- forbiddenHitCount: 0
- redactionPass: true

RuntimePreview v1.1 adds persistent metadata governance storage, retention cleanup, session replay, report export, scenario evidence, metadata-only deploy readiness reporting, and a fuller developer Pilot Console. It still does not touch real camera SDKs, Station, image files, model files, PLC, packaging, deployment, hot-load paths, or a Real RuntimePreview adapter.

## Workflow Run #21 - Latest RuntimePreview v1.0 Readiness Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| job | Agent Engineering Harness |
| branchName | codex初稿 |
| commitSha | 6b1b5cfbc8437440cba1d9aca70d6cc131c419a5 |
| headSha | 6b1b5cfbc8437440cba1d9aca70d6cc131c419a5 |
| runId | 27060964639 |
| runAttempt | 1 |
| runNumber | 21 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27060964639 |
| completedAtUtc | 2026-06-06T11:26:59Z |

## Workflow Run #21 Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7453990636 |
| digest | sha256:c050efd9546d50d644973f38cf85b79ff8b04611e651ea1026257fef9bbd64e9 |
| conclusion | success |

Run #21 is the latest v1.0 evidence for RuntimePreview Readiness. It is an evidence-only follow-up run on head SHA `6b1b5cfbc8437440cba1d9aca70d6cc131c419a5`; no production C#/JS logic was changed for this evidence closure.

## Workflow Run #20 - RuntimePreview v1.0 Readiness Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| job | Agent Engineering Harness |
| branchName | codex初稿 |
| commitSha | 40aa090efcfd2e2995a9fa09f842331e533f8db9 |
| headSha | 40aa090efcfd2e2995a9fa09f842331e533f8db9 |
| runId | 27060841427 |
| runAttempt | 1 |
| runNumber | 20 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27060841427 |
| startedAtUtc | 2026-06-06T11:18:11Z |
| completedAtUtc | 2026-06-06T11:21:24Z |

## Workflow Run #20 Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7453957488 |
| sizeBytes | 142781 |
| digest | sha256:3bdcd8fdb330077c3cfd24a580d3fcee50e6b3da425fb6e9a784aa3f4d53b024 |
| createdAtUtc | 2026-06-06T11:21:16Z |
| expired | false |

Artifact contents verified after download:

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
- `real_llm_planner_shadow_eval.holdout.json`
- `real_llm_planner_shadow_eval.holdout.md`
- `vision_agent_quality_artifact_manifest.json`
- `vision_agent_quality_artifact_manifest.md`
- `agent_engineering_harness.trx`
- `agent_ai_model_config_endpoints.trx`
- `agent_ui_contract_output.txt`

CI artifact report checks:

- business benchmark `workflowRun.commitSha`: `40aa090efcfd2e2995a9fa09f842331e533f8db9`
- business benchmark `workflowRun.runId`: `27060841427`
- planner benchmark `workflowRun.runId`: `27060841427`
- fixed shadow eval `workflowRun.runId`: `27060841427`
- holdout shadow eval `workflowRun.runId`: `27060841427`
- all inspected JSON report `workflowRun` values were non-local

## Run #20 Benchmark Summary

Executable business benchmark:

- caseCount: 37
- runtimePreviewCaseCount: 7
- passedCaseCount: 37
- accepted: true

Planner autonomy benchmark:

- plannerCaseCount: 15
- permissionNegativeCaseCount: 6
- totalCaseCount: 21
- passedCaseCount: 21
- accepted: true

Default real LLM shadow eval samples:

- fixed caseSet runnerStatus: skipped
- holdout caseSet runnerStatus: skipped
- requestCount: 0
- skippedReason: `CV_AGENT_REAL_LLM_SHADOW_EVAL is not true; default CI shadow eval sample does not call real LLM.`
- safety mode: offline metadata only

Artifact manifest:

- scanPolicyVersion: `2026-06-06.runtime-preview-v1-governance-scan.v1`
- sourceFilesScanned: 2791
- reportsScanned: 4
- redactionPass: true

## Workflow Run #18 - RuntimePreview Pilot v0.8 Final Evidence

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| branchName | codex初稿 |
| commitSha | 941cffbd5cd53eca3924081f4da7694ae64bd0f3 |
| headSha | 941cffbd5cd53eca3924081f4da7694ae64bd0f3 |
| runId | 27059205031 |
| runAttempt | 1 |
| runNumber | 18 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27059205031 |
| startedAtUtc | 2026-06-06T09:56:08Z |
| completedAtUtc | 2026-06-06T09:59:06Z |

## Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7453442164 |
| sizeBytes | 129534 |
| digest | sha256:6ae2e0f2a09cf951554389fcd0a70e960586c6ce2cf304b219648561045b83f4 |
| createdAtUtc | 2026-06-06T09:58:57Z |

The artifact zip is available from GitHub Actions for authenticated users. GitHub Actions metadata confirms artifact name, id, digest, run id, branch, and head SHA. `Assert Vision Agent Artifact Reports` completed successfully before upload, enforcing non-local `workflowRun` metadata and secret/BaseUrl redaction.

Expected artifact contents:

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
- `real_llm_planner_shadow_eval.holdout.json`
- `real_llm_planner_shadow_eval.holdout.md`
- `vision_agent_quality_artifact_manifest.json`
- `vision_agent_quality_artifact_manifest.md`
- `agent_engineering_harness.trx`
- `agent_ai_model_config_endpoints.trx`
- `agent_ui_contract_output.txt`

## CI Step Evidence

| Step | Conclusion |
| --- | --- |
| Run Vision Agent Quality Suite | success |
| Generate Real LLM Shadow Eval Sample | success |
| Assert Vision Agent Artifact Reports | success |
| Upload Vision Agent Quality Reports | success |

The shadow sample step generates both fixed and holdout reports with the real LLM disabled by default. Manual CPA fixed/holdout results remain planner-shadow evidence only and are not part of the stable CI gate.

## Run #18 Benchmark Summary

Executable business benchmark:

- caseCount: 36
- runtimePreviewCaseCount: 6
- passedCaseCount: 36
- accepted: true

Planner autonomy benchmark:

- plannerCaseCount: 15
- permissionNegativeCaseCount: 6
- totalCaseCount: 21
- passedCaseCount: 21
- accepted: true

Default real LLM shadow eval samples:

- fixed caseSet runnerStatus: skipped
- holdout caseSet runnerStatus: skipped
- requestCount: 0
- skippedReason: `CV_AGENT_REAL_LLM_SHADOW_EVAL is not true; default CI shadow eval sample does not call real LLM.`
- safety mode: offline metadata only

## RuntimePreview Pilot v0.8 Evidence

RuntimePreview Pilot v0.8 is covered by run #18 and the matching local pre-push run:

- backend Agent tests: 203 total / 203 passed
- AI model endpoint regression: 9 total / 9 passed
- UI contract tests: 58 total / 58 passed
- executable business benchmark: 36 / 36 accepted
- planner autonomy + permission negative benchmark: 21 / 21 accepted
- artifact/source assertion: 13 artifact files validated, 4 reports validated, 3310 source files scanned

Stable CI continues to keep fixed/holdout real LLM shadow eval default-off.

## RuntimePreview v1.0 Local Pre-Push Evidence

RuntimePreview v1.0 Readiness local quality suite completed on 2026-06-06 before push. Remote CI evidence must be refreshed after this commit is pushed.

| Gate | Result |
| --- | --- |
| backend Agent tests | 243 total / 243 passed |
| AI endpoint regression | 14 total / 14 passed |
| UI contract tests | 68 total / 68 passed |
| executable business benchmark | 37 / 37 accepted |
| planner autonomy + permission negative benchmark | 21 / 21 accepted |
| artifact/source assertion | 13 artifact files validated, 4 reports validated, 3315 source files scanned |

v1.0 adds RuntimePreview session governance, PermissionBroker, ResourceBroker, metadata-only simulation harness, append-only audit trail, report archive, developer-hidden Pilot Console, and Real RuntimePreview RFC boundaries.

## RuntimePreview Pilot Endpoint Permission Note

RuntimePreview v1.0 adds an explicit `RuntimePreviewPermissionBroker` gate for `POST /api/settings/runtime-preview-pilot/readiness` and covers it with endpoint regression. The endpoint remains metadata-only and still does not touch real camera SDKs, Station, image files, model files, PLC, packaging, deployment, or hot-load paths.

## Safety Boundary

Run #18 and RuntimePreview Pilot v0.8 do not advance real camera, real Station, real RuntimePreview, or real deployment capability:

- no real camera SDK integration
- no real Station access
- no real image file read
- no real vision model file load
- no PLC write
- no packaging, deployment, hot-load, or downlink
- no image bytes/base64 returned
- no arbitrary path read
- RuntimePreview remains offline/metadata-only
- stable CI keeps real LLM shadow eval default-off
