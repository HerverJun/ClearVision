# Vision Agent CI Evidence Report 20260606

## Workflow Run #16

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| branchName | codex初稿 |
| commitSha | ae8117cf7d39a181278752d8e65ff3ee8cd142c4 |
| runId | 27056660772 |
| runAttempt | 1 |
| runNumber | 16 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27056660772 |
| startedAtUtc | 2026-06-06T07:49:54Z |
| completedAtUtc | 2026-06-06T07:52:44Z |

## Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7452601406 |
| sizeBytes | 121613 |
| digest | sha256:42e5af47dbad085a5d41512d73fa20c87b35f4bc7f6efd5341bb165ed5eb8159 |
| createdAtUtc | 2026-06-06T07:52:36Z |

The artifact zip is available from GitHub Actions for authenticated users. The public REST metadata confirms artifact name, id, digest, run id, branch, and head SHA. `Assert Vision Agent Artifact Reports` completed successfully before upload, enforcing non-local `workflowRun` metadata and secret/BaseUrl redaction.

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

## Run #16 Benchmark Summary

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

## RuntimePreview Pilot v0.7 Local Evidence

The RuntimePreview Pilot v0.7 skeleton commit raises local quality gates beyond run #16:

- backend Agent tests: 200 total / 200 passed
- AI model endpoint regression: 8 total / 8 passed
- UI contract tests: 55 total / 55 passed
- executable business benchmark: 36 / 36 accepted
- planner autonomy + permission negative benchmark: 21 / 21 accepted

The final CI run for the v0.7 skeleton must be recorded after this commit is pushed. Stable CI must continue to keep fixed/holdout real LLM shadow eval default-off.

## Safety Boundary

Run #16 and the RuntimePreview Pilot v0.7 skeleton do not advance real camera, real Station, or real deployment capability:

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
