# Vision Agent RuntimePreview v1.2 Scenario Corpus + Package Readiness Bridge

Date: 2026-06-06

## Scope

RuntimePreview v1.2 keeps the v1.1 metadata-only boundary and upgrades the readiness layer into a scenario-driven pilot preparation platform. This work adds a richer scenario corpus, metadata-only package readiness bridge, independent developer Pilot Console, hardened governance export/index behavior, and Agent explanation benchmark.

It still does not implement a Real RuntimePreview adapter and does not touch real cameras, Station, image files, model files, PLC, package creation, deployment, or hot-load paths.

## Architecture Audit

v1.1 already had session, audit, report archive, simulated harness, deploy readiness report, and a developer-hidden console. The main v1.2 gaps were:

- Scenario evidence was still demo-sized and centered on 8 synthetic cases, so it did not represent common business readiness failures.
- Deploy readiness answered "can this be considered for deployment" but did not explicitly answer "why can this not be packaged or released now."
- Pilot Console was still attached to AI Settings, which made it harder to present as a developer governance console and easier to confuse with AI model configuration.
- JSONL governance persistence lacked explicit storage/index/export metadata for package reports and corruption recovery evidence.
- Agent benchmarks checked tool plans and permission gates, but not whether the Agent could explain missing resources, deny reasons, allowlist mismatch, and package risk in engineer-facing terms.

The convergence is:

- Governance services own metadata session, audit, replay, package readiness, export, and scenario reports.
- Resource and permission brokers stay between endpoints, console, and future adapter boundaries.
- The independent Pilot Console calls only metadata endpoints.
- RuntimePackage Readiness Bridge consumes existing readiness/precheck outputs and never creates a package.

## Scenario Corpus

`RuntimePreviewScenarioCorpusService` exposes a redacted metadata corpus with 15 cases. Reports are generated at:

- `quality/evals/reports/runtime_preview_scenario_corpus.json`
- `quality/evals/reports/runtime_preview_scenario_corpus.md`

| Case | Scenario | Expected status | Risk |
| --- | --- | --- | --- |
| RP-SC-001 | wire_sequence | passed | low |
| RP-SC-002 | terminal_color_order | passed | low |
| RP-SC-003 | template_matching | passed | low |
| RP-SC-004 | hole_distance | passed | low |
| RP-SC-005 | remote_control_detection | passed | low |
| RP-SC-006 | missing_camera | not_ready | missing_camera_binding |
| RP-SC-007 | missing_template | not_ready | missing_template |
| RP-SC-008 | missing_model | not_ready | missing_model |
| RP-SC-009 | dangerous_path | denied | dangerous_resource |
| RP-SC-010 | plc_station_deny | denied | plc_station_denied |
| RP-SC-011 | precheck_blocked | not_ready | precheck_not_ready |
| RP-SC-012 | allowlist_mismatch | not_ready | allowlist_mismatch |
| RP-SC-013 | multi_operator_flow | passed | medium |
| RP-SC-014 | missing_parameter | not_ready | missing_parameter |
| RP-SC-015 | draft_editable_package_blocked | not_ready | draft_allowed_package_blocked |

Each case records `caseId`, `scenario`, `workflowDraftHash`, `expectedStatus`, `expectedRisk`, `expectedPendingActions`, and `businessExplanation`. Workflow drafts use metadata handles only. No case reads a real image, model file, device path, Station address, PLC address, or network endpoint.

## Package Readiness Bridge

`RuntimePreviewPackageReadinessBridge` connects workflow draft, resource handles, readiness, simulated report, and `runtime_package_precheck` into `RuntimePreviewPackageReadinessReport`.

| Input | Behavior |
| --- | --- |
| Ready metadata workflow | `readyForPackage=true`, `packageBlocked=false`, `packageCreated=false`, `deploymentExecuted=false` |
| Missing camera/template/model/parameter | `readyForPackage=false`, `packageBlocked=true`, missing resources and pending actions are surfaced |
| Denied path/PLC/Station intent | `readyForPackage=false`, `packageBlocked=true`, dangerous denial is preserved, no artifact is produced |
| Allowlist mismatch | workflow draft remains editable, package readiness blocks release |
| Precheck not ready | package is blocked with `workflowDraftAllowed=true` |

Sample reports:

- `quality/evals/reports/runtime_preview_package_readiness_report.sample.json`
- `quality/evals/reports/runtime_preview_package_readiness_report.sample.md`

Latest local sample: 15 cases, 6 ready for package, 9 package-blocked, `packageCreated=false`, `deploymentExecuted=false`, `realResourcesTouched=false`.

## Independent Pilot Console

RuntimePreview Pilot Console is now an independent developer-only settings section instead of being rendered inside the AI model settings tab.

Capabilities:

- Catalog-driven metadata allowlist selection.
- Scenario corpus selection and run action.
- Session list, replay, and report export surfaces.
- Package/deploy readiness generation.
- Governance index, lookup, and export controls.
- Agent explanation benchmark panel.
- Diff and confirmation surfaces for allowlist updates.

Visibility remains gated by admin/developer state. Ordinary users do not see the console. Settings page save semantics for AI, PLC, Station, and Camera tabs remain isolated.

## Governance Store Hardening

`RuntimePreviewGovernanceStore` now carries:

- `schemaVersion`
- `storageVersion`
- record type counts
- index summary
- corruption recovery count
- retention cleanup support
- export manifest
- package readiness report stream
- lookup by `sessionId`, `reportId`, and `caseId`

JSONL remains the storage format. The boundary is kept behind store/archive interfaces so a future SQLite implementation can replace the file implementation without changing endpoints or console code.

Governance export sample:

- `quality/evals/reports/runtime_preview_governance_export_sample.json`
- `quality/evals/reports/runtime_preview_governance_export_sample.md`

Latest local export: 15 sessions, 105 audit events, 15 session reports, 15 deploy readiness reports, 15 package readiness reports, 1 intentionally corrupt line recovered, redaction passed.

## Agent Explanation Benchmark

`RuntimePreviewAgentExplanationService` evaluates whether each scenario can be explained to an industrial vision engineer:

- why ready / not_ready / denied
- which resources are missing
- why allowlist mismatch blocks package readiness
- why `workflowDraftAllowed=true` can coexist with `packageBlocked=true`
- why no deployment/package action is allowed
- what the engineer should verify next

Reports:

- `quality/evals/reports/runtime_preview_agent_explanation_benchmark.json`
- `quality/evals/reports/runtime_preview_agent_explanation_benchmark.md`

Latest local result: 15 / 15 accepted, metadata-only, `realResourcesTouched=false`.

## Quality Results

Local quality suite result on 2026-06-06:

| Gate | Result |
| --- | --- |
| backend Agent tests | 291 / 291 passed |
| AI endpoint regression | 22 / 22 passed |
| UI contract tests | 90 / 90 passed |
| executable business benchmark | 45 / 45 accepted |
| planner autonomy + permission negative benchmark | 21 / 21 accepted |
| scenario corpus | 15 cases, accepted |
| package readiness sample | 15 cases, accepted |
| Agent explanation benchmark | 15 / 15 accepted |
| artifact/source/report/session/audit scan | forbiddenHitCount=0, redactionPass=true |

Artifact scan fields:

- scanPolicyVersion: `2026-06-06.runtime-preview-v1.2-governance-scan.v2`
- sourceFilesScanned: 3333
- reportsScanned: 11
- auditReportsScanned: 2
- sessionReportsScanned: 14
- forbiddenHitCount: 0
- redactionPass: true

## Safety Statement

RuntimePreview v1.2 did not advance real resource integration. The implementation forbids real camera SDK access, real Station access, real image file reads, real model file loading, PLC writes, real `.cvpkg` creation, package/deploy/hot-load execution, and Real RuntimePreview adapter work. All new reports and console flows are metadata-only.
