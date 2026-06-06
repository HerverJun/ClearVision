# Vision Agent CI Evidence Report 20260606

## Workflow Run

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| branchName | codex初稿 |
| commitSha | c181d96a601528bd57113cc79e1e1e7c1c08bdd9 |
| runId | 27054646723 |
| runAttempt | 1 |
| runNumber | 13 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27054646723 |
| startedAtUtc | 2026-06-06T06:11:13Z |
| completedAtUtc | 2026-06-06T06:13:53Z |

## Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7451868642 |
| sizeBytes | 115078 |
| digest | sha256:5a99ed340a569346642b956e62ddd70bf94d8f838c068033bc0c8a8744997895 |
| createdAtUtc | 2026-06-06T06:13:46Z |

The artifact zip is available from GitHub Actions for authenticated users. The public REST metadata confirms the artifact name, id, digest, run id, branch, and head SHA. The CI step `Assert Vision Agent Artifact Reports` completed successfully before upload, which enforces non-local `workflowRun` metadata and secret/BaseUrl redaction.

Expected artifact contents:

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
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

## Benchmark Summary

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

Default real LLM shadow eval sample:

- runnerStatus: skipped
- requestCount: 0
- skippedReason: `CV_AGENT_REAL_LLM_SHADOW_EVAL is not true; default CI shadow eval sample does not call real LLM.`
- safety mode: offline metadata only

## Test Summary

Local and CI quality suite minimums were not lowered:

- backend Agent tests: 184 total / 184 passed
- AI model endpoint regression: 8 total / 8 passed
- UI contract tests: 49 total / 49 passed

Local CPA manual shadow trial after planner protocol tuning:

- parseSuccessRate: 1.0000
- unsafeAttemptRate: 0
- averageNextActionMatchScore: 1.0000
- averageOrderedPrefixScore: 1.0000
- averageFullPlanMatchScore: 1.0000
- averageToolPlanMatchScore: 1.0000

## Safety Boundary

This CI run and manual shadow trial did not advance real camera, real Station, or real deployment capability:

- no real camera SDK integration
- no real Station access
- no real image file read
- no real vision model file load
- no PLC write
- no packaging, deployment, hot-load, or downlink
- RuntimePreview remains offline/metadata-only
- stable CI keeps real LLM shadow eval default-off
