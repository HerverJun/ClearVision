# Vision Agent CI Evidence Report 20260606

## Workflow Run

| Field | Value |
| --- | --- |
| workflow | Vision Agent Quality Suite |
| branchName | codex初稿 |
| commitSha | 4e75e4e8aad7a41d03c0a5d92400330793729799 |
| runId | 27056536890 |
| runAttempt | 1 |
| runNumber | 15 |
| event | push |
| status | completed |
| conclusion | success |
| runUrl | https://github.com/HerverJun/ClearVision/actions/runs/27056536890 |
| startedAtUtc | 2026-06-06T07:43:40Z |
| completedAtUtc | 2026-06-06T07:46:42Z |

## Artifact

| Field | Value |
| --- | --- |
| artifactName | vision-agent-quality-suite |
| artifactId | 7452554649 |
| sizeBytes | 121766 |
| digest | sha256:05cdce4be893ea48b9bc128fd803f44f8fff63fc23568faff4a9bad5a76d7a85 |
| createdAtUtc | 2026-06-06T07:46:35Z |

The artifact zip is available from GitHub Actions for authenticated users. Public REST metadata confirms the artifact name, id, digest, run id, branch, and head SHA. The CI step `Assert Vision Agent Artifact Reports` completed successfully before upload, enforcing non-local `workflowRun` metadata and secret/BaseUrl redaction.

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

The shadow sample step generates both default-off fixed and holdout reports in CI. Stable CI still does not call the real LLM; the holdout real CPA report is a manual report and is not used as a required CI gate.

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

Default real LLM shadow eval samples:

- fixed caseSet runnerStatus: skipped
- holdout caseSet runnerStatus: skipped
- requestCount: 0
- skippedReason: `CV_AGENT_REAL_LLM_SHADOW_EVAL is not true; default CI shadow eval sample does not call real LLM.`
- safety mode: offline metadata only

## Test Summary

Local and CI quality suite minimums were not lowered:

- backend Agent tests: 184 total / 184 passed
- AI model endpoint regression: 8 total / 8 passed
- UI contract tests: 52 total / 52 passed

Manual CPA fixed shadow trial after planner protocol tuning:

- parseSuccessRate: 1.0000
- unsafeAttemptRate: 0
- averageNextActionMatchScore: 1.0000
- averageOrderedPrefixScore: 1.0000
- averageFullPlanMatchScore: 1.0000
- averageToolPlanMatchScore: 1.0000

Manual CPA holdout shadow trial:

- caseCount: 24
- parseSuccessRate: 1.0000
- unsafeAttemptRate: 0
- averageNextActionMatchScore: 1.0000
- averageOrderedPrefixScore: 1.0000
- averageFullPlanMatchScore: 1.0000
- policySafetyScore: 1.0000
- badToolNames: 0
- fallbackToMockSuggestedCount: 0

## RuntimePreview Pilot Gate

The holdout planner gate passes on the manual CPA run, but this report does not enable a Real RuntimePreview adapter. Entry to a pilot remains blocked until the pilot gate document is explicitly accepted and implemented with default-off behavior, resource allowlist, offline fallback, and no image bytes/base64.

## Safety Boundary

This CI run and manual shadow trial did not advance real camera, real Station, or real deployment capability:

- no real camera SDK integration
- no real Station access
- no real image file read
- no real vision model file load
- no PLC write
- no packaging, deployment, hot-load, or downlink
- no Real RuntimePreview adapter
- RuntimePreview remains offline/metadata-only
- stable CI keeps real LLM shadow eval default-off
