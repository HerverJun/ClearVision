# Vision Agent CI Evidence Report 20260606

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
