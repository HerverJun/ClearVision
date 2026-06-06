# Vision Agent CI Evidence Report 20260606

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
