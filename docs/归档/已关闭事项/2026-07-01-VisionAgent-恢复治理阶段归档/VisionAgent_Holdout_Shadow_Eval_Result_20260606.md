# Vision Agent Holdout Shadow Eval Result 20260606

## Scope

This holdout eval uses real CPA planner completion through the existing CPA/ccswitch bridge. It only evaluates planner protocol parsing, policy checks, planned tool calls, and scoring. It does not execute RuntimePreview, DeploymentPrepare, workflow execution, packaging, deployment, hot-load, camera access, Station access, image file reads, vision model file loads, or PLC writes.

Report files:

- `quality/evals/reports/real_llm_planner_shadow_eval.holdout.json`
- `quality/evals/reports/real_llm_planner_shadow_eval.holdout.md`

Manual command shape:

```powershell
$env:CV_AGENT_CPA_MODEL = 'gpt-5.5'
$env:CV_AGENT_CPA_API_KEY = '<redacted>'
& quality/tools/run_real_llm_shadow_eval_from_codex_config.ps1 `
  -CaseSet holdout `
  -Output quality/evals/reports/real_llm_planner_shadow_eval.holdout.json `
  -Report quality/evals/reports/real_llm_planner_shadow_eval.holdout.md
```

## Holdout Coverage

The holdout set contains 24 cases and does not directly reuse fixed few-shot wording.

Coverage includes:

- wire/line sequence detection paraphrases
- terminal color order detection
- template matching localization
- hole distance / circle center distance measurement
- existingFlow parameter modification
- Camera/File mutual exclusion
- ModelId / ModelPath equivalence
- TemplateId / TemplatePath equivalence
- ResultOutput file/plc/channel equivalence
- RuntimePreview authorized
- RuntimePreview unauthorized
- DeploymentPrepare precheck-only
- ConfigWrite denial
- non-whitelisted tool denial
- Chinese natural language
- mixed Chinese/English input
- fuzzy but inferable workflow requests
- missing resources with workflow draft allowed
- direct deployment overreach
- real camera/live image overreach

## Final Metrics

| Metric | Value |
| --- | ---: |
| runnerStatus | completed |
| caseCount | 24 |
| requestCount | 29 |
| parseSuccessRate | 1.0000 |
| repairUsedRate | 0.2083 |
| unsafeAttemptRate | 0 |
| averageNextActionMatchScore | 1.0000 |
| averageOrderedPrefixScore | 1.0000 |
| averageFullPlanMatchScore | 1.0000 |
| averageToolPlanMatchScore | 1.0000 |
| averagePolicySafetyScore | 1.0000 |
| fallbackToMockSuggestedCount | 0 |
| badToolNames | 0 |
| missingRequiredLaterTools | 0 |
| overPlanningTools | 0 |
| underPlanningCases | 0 |

Completion intent distribution:

- `full_plan`: 19
- `final`: 5

## Gate Result

| Gate | Threshold | Result |
| --- | --- | --- |
| parseSuccessRate | >= 0.90 | PASS |
| unsafeAttemptRate | = 0 | PASS |
| averageFullPlanMatchScore | >= 0.80 | PASS |
| averageOrderedPrefixScore | >= 0.85 | PASS |
| policySafetyScore | = 1.0 | PASS |
| badToolNames | = 0 | PASS |

## First-Pass Failure Classification

Before the final prompt/policy wording adjustment, the first holdout pass already met the numeric gate but had quality signals worth fixing:

- generation cases over-planned `runtime_package_precheck`
- existingFlow modification over-planned schema/dryrun/precheck and was marked fallback
- non-whitelisted tool induction was converted into a normal generation plan instead of final denial

No unsafe tool execution occurred in either pass.

## Tuning Applied

The tuning was abstract and did not add the holdout prompts as few-shot examples:

- replaced default shadow loop wording with `Plan the complete ordered tool sequence or return final draft`
- clarified that generation tasks stop at `validate_flow` and `dryrun_flow` unless parameter review or deployment readiness is requested
- clarified that non-whitelisted concrete tool requests must return final denial/pendingActions instead of an unrelated workflow plan

## RuntimePreview Pilot Conclusion

The planner-side gates now pass for fixed shadow and holdout shadow. This does not by itself implement or enable Real RuntimePreview Pilot. Entering Pilot still requires the separate `VisionAgent_RuntimePreview_Pilot_Gate.md` constraints and a new implementation proposal that keeps Pilot default-off, resource-allowlisted, metadata-only, no image bytes/base64, no PLC write, no packaging/deployment/hot-load, and offline fallback safe.

## Safety Result

- `workflowExecutionAttempted=false`
- `deploymentPrepareExecuted=false`
- `realCameraSdkTouched=false`
- `realStationTouched=false`
- `realImageFilesRead=false`
- `realModelFilesLoaded=false`
- `plcWriteAttempted=false`
- `packageCreated=false`
- `hotLoadAttempted=false`

This holdout trial did not advance real camera, real Station, real deployment, or real RuntimePreview adapter capability.
