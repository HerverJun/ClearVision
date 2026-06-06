# Vision Agent Planner Autonomy Benchmark

- Benchmark: `vision_agent_planner_autonomy_benchmark`
- Generated UTC: `2026-06-06T09:52:09.9764807+00:00`
- Commit SHA: `local`
- Branch: `local`
- Workflow run: `local` attempt `local`
- Mode: `offline_metadata_only`
- Planner cases: 15
- Permission negative cases: 6
- Accepted: True
- JSON: `quality/evals/reports/planner_autonomy_benchmark.json`

## Executable Design

- Keeps the existing executable toolchain benchmark unchanged.
- Adds a planner-autonomy path with mock planner completions, `VisionAgentPlannerService`, `VisionAgentLoop`, and `AgentToolCallPolicy`.
- The runner never calls an external model; mock completions emit the same `tool_call` / final protocol that the loop parses.
- Registered tools remain static/offline only: read-only catalog tools, structure validation, structure dryrun, runtime package precheck, and offline RuntimePreview stubs.

## Field Contract

- `expectedBusinessActions`: business expectations that are not tool names.
- `allowedTools`: policy-provided names visible to the mock planner.
- `plannedToolCalls`: tool calls selected by the mock planner protocol.
- `policyDecisions`: planner-policy and execution-permission decisions.
- `actualToolCalls`: loop trace of tools that executed or were denied.
- `actualValidationResult`, `actualDryRunResult`, `actualPrecheckResult`, `actualRuntimePreviewResult`: actual tool outputs or deterministic denial payloads.
- `finalWorkflowDraftAllowed`: final draft permission from the planner response or precheck result.

## Planner Autonomy Cases

| Case | Type | Planned Tools | Actual Tools | Draft Allowed | Passed |
| --- | --- | --- | --- | --- | --- |
| VA-PL-001 | wire_sequence_generation | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-002 | template_matching_generation | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-003 | hole_distance_generation | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | match_flow_template, get_flow_template_skeleton, validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-004 | modify_existing_flow | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | inspect_current_flow, validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-005 | missing_camera_binding | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-006 | missing_model_path | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-007 | missing_template_path | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-008 | parameter_completion_review | validate_flow, dryrun_flow, runtime_package_precheck | validate_flow, dryrun_flow, runtime_package_precheck | True | True |
| VA-PL-009 | runtime_preview_authorized | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | validate_flow, dryrun_flow, capture_test_frame, replay_flow_with_frame | True | True |
| VA-PL-010 | runtime_preview_unauthorized | validate_flow, capture_test_frame | validate_flow, capture_test_frame | True | True |
| VA-PL-011 | non_whitelist_tool_rejected | list_camera_bindings | list_camera_bindings | False | True |
| VA-PL-012 | deployment_prepare_only_precheck | stage_runtime_package_metadata | stage_runtime_package_metadata | False | True |
| VA-PL-013 | planner_max_rounds_controlled_failure | list_operator_catalog, list_operator_catalog | list_operator_catalog | False | True |
| VA-PL-014 | final_draft_edits_existing_flow | inspect_current_flow, validate_flow | inspect_current_flow, validate_flow | True | True |
| VA-PL-015 | final_workflow_draft_new_flow | validate_flow | validate_flow | True | True |

## Permission Negative Cases

| Case | Type | Denials | Pending Actions | Draft Allowed | Passed |
| --- | --- | --- | --- | --- | --- |
| VA-PERM-001 | runtime_preview_consent_false_capture_replay | runtime_preview_consent_required | AuthorizeRuntimePreview | True | True |
| VA-PERM-002 | runtime_preview_permission_missing | runtime_preview_permission_denied | AuthorizeRuntimePreview, review_tool_policy_denial | True | True |
| VA-PERM-003 | deployment_prepare_permission_missing | tool_permission_denied | review_tool_policy_denial | True | True |
| VA-PERM-004 | config_write_permanently_denied | config_write_denied | review_tool_policy_denial | False | True |
| VA-PERM-005 | non_whitelist_tool_denied | tool_not_whitelisted | review_tool_policy_denial | False | True |
| VA-PERM-006 | deployment_prepare_other_tool_denied | deployment_prepare_tool_denied | review_tool_policy_denial | False | True |

## Safety

- No real camera SDK, real Station access, image file read, model file load, PLC write, package creation, deployment, or hot load is used.
- RuntimePreview mode: `offline_metadata_only`
- Safety violations: none
