# Vision Agent RuntimePreview v1.3 Manifest Dry-Run Evidence

Generated at: 2026-06-06

## Scope

RuntimePreview v1.3 upgrades the v1.2 metadata-only governance platform into a pre-release review desk. The new scope is manifest-level dry-run evidence for package review, not package creation.

Still prohibited:
- Real camera SDK access.
- Real Station access.
- Real image file reads.
- Real vision model file loads.
- PLC writes.
- Real package file creation.
- Real packaging, deployment, delivery, or hot-load.
- Real RuntimePreview adapter.
- Agent shell, cmd, PowerShell, or system-command tool.
- Acme.Product.* additions.

Allowed:
- Metadata-only manifest dry-run.
- Redacted flow corpus.
- Package readiness bridge v2.
- Developer-only pre-release review desk.
- Governance store v3 JSONL metadata persistence and export.
- Agent explanation benchmark v2.

## Architecture Audit

Current v1.2 to v1.3 gap:
- Package readiness answered whether a draft could proceed toward packaging, but did not generate a manifest-level dependency contract.
- Scenario corpus was representative but still synthetic; it lacked station type, workflow kind, manifest risk, and engineer action fields.
- Governance store persisted sessions, audit, session reports, deploy readiness, and package readiness, but not manifest dry-run streams.
- Console could run scenario/package readiness, but did not provide a manifest dry-run review surface or manifestId lookup.
- Agent explanation benchmark could explain readiness and package blocking, but Markdown status could be empty for v1.3-style corpus fields.

Enough metadata for manifest dry-run:
- Workflow draft hash and operator graph.
- Operator types and temp ids.
- Camera binding ids.
- Model ids/catalog handles.
- Template ids/catalog handles.
- Output channel ids.
- Runtime package precheck missing resources.
- Readiness and package readiness summaries.

Fields that must remain metadata-only and redacted:
- Camera IP, device network path, Station address, PLC address.
- Image bytes/base64 and image paths.
- Model file paths and template file paths.
- API keys, Authorization, Bearer, x-api-key, BaseUrl query/userinfo.
- Real package paths or binary package content.

## Manifest Dry-Run Behavior Matrix

| Input | Output | Allowed | Forbidden |
| --- | --- | --- | --- |
| workflow draft | workflowDraftHash, operatorCount, operatorTypes | yes | no real workflow execution |
| operator graph | operatorTrace, dependencyTrace | yes | no package artifact |
| resource handles | camera/model/template/output dependencies | yes, metadata handles only | no real path/IP/device address |
| package readiness report | packageReviewAllowed, blockedReasons, riskLevel | yes | no package creation |
| runtime package precheck | missingDependencies, pendingActions | yes | no deployment prepare beyond precheck |
| dangerous/denied input | deny evidence only | yes | no manifest artifact generation |

Generated report fields:
- manifestId
- workflowDraftHash
- operatorCount
- operatorTypes
- resourceDependencies
- modelDependencies
- templateDependencies
- cameraBindings
- outputChannels
- missingDependencies
- blockedReasons
- riskLevel
- packageReviewAllowed
- packageCreated=false
- deploymentExecuted=false
- metadataOnly=true
- realResourcesTouched=false

## Redacted Flow Corpus

The v1.3 corpus contains 20 redacted, production-like metadata cases:
- RP-RF-001 wire sequence complete flow.
- RP-RF-002 remote-control defect detection.
- RP-RF-003 template positioning plus measurement.
- RP-RF-004 hole distance measurement.
- RP-RF-005 terminal color order.
- RP-RF-006 missing camera.
- RP-RF-007 missing template.
- RP-RF-008 missing model.
- RP-RF-009 missing output channel.
- RP-RF-010 PLC/Station deny.
- RP-RF-011 dangerous path.
- RP-RF-012 allowlist mismatch.
- RP-RF-013 multi-camera flow.
- RP-RF-014 multi-model flow.
- RP-RF-015 missing parameter.
- RP-RF-016 package manifest blocked.
- RP-RF-017 workflow editable but package blocked.
- RP-RF-018 runtime package precheck blocked.
- RP-RF-019 template plus hole distance.
- RP-RF-020 direct deploy request denied.

Each case carries caseId, stationType, workflowKind, businessPurpose, workflowDraftHash, operatorSummary, expectedReadiness, expectedPackageReadiness, expectedManifestRisk, expectedEngineerAction, and redactionStatus.

Report artifacts:
- quality/evals/reports/runtime_preview_redacted_flow_corpus.json
- quality/evals/reports/runtime_preview_redacted_flow_corpus.md

## Package Readiness Bridge v2

Package readiness now distinguishes three concepts:
- readyForPackage: static precheck/readiness status.
- packageReviewAllowed: manifest dry-run dependency contract is clean enough for review.
- packageCreated: always false in v1.3.

Bridge v2 also links manifestDryRunReportId and outputs:
- blockedReason
- packageRiskLevel
- packageReviewExplanation
- dependencyTrace
- operatorContract
- resourceContract

If workflowDraftAllowed=true but packageReviewAllowed=false, the report states that engineers may keep editing the draft while dependency, output, parameter, or policy issues block package review.

## Pilot Console Productization

The independent developer-only console is now a pre-release review desk:
- RuntimePreview Pre-release Review Desk title and page marker.
- Redacted Flow Corpus selector and "Run pre-release chain" action.
- RuntimePackage manifest dry-run panel.
- Package Readiness Bridge v2 panel.
- Governance lookup by sessionId, reportId, caseId, and manifestId.
- Report export and governance export surfaces.
- All displayed payloads pass through the existing RuntimePreview redactor.

Normal users do not see this console by default.

## Governance Store v3

JSONL storage remains the persistence format. v3 adds:
- storageVersion=jsonl.v3
- recordType=manifest_dry_run_report
- runtime_package_manifest_dry_run_reports.jsonl
- SaveManifestDryRunReport
- LoadManifestDryRunReports
- lookup by manifestId/reportId/sessionId
- ManifestDryRunReportCount in index summary
- ManifestDryRunReports in governance export
- manifest stream cleanup and corruption counting

SQLite remains a future boundary; this release keeps local metadata JSONL only.

## Agent Explanation v2

Agent explanation v2 uses redacted corpus cases and emits engineer-facing review text:
- Status is always populated.
- Missing resource explanations name the metadata dependency class.
- Package risk explains why package review is blocked or allowed.
- Affected operators and blocked reasons are included.
- ManifestRisk and PackageReviewAllowed are explicit fields.

Markdown Status column is fixed by using expectedReadiness/status fallbacks for v1.3 case fields.

## Quality Results

Local quality suite: pass.

| Gate | Result |
| --- | --- |
| Backend Agent tests | 333 / 333 passed, minimum 320 |
| AI endpoint regression | 25 / 25 passed, minimum 25 |
| UI contract tests | 111 / 111 passed, minimum 110 |
| Business benchmark | 55 / 55 accepted |
| Scenario evidence | 20 / 20 accepted |
| Redacted flow corpus | 20 / 20 accepted |
| Package readiness sample | 20 cases, 7 ready, 13 blocked |
| Manifest dry-run sample | 20 cases, 7 review allowed, 13 blocked |
| Agent explanation v2 | 20 / 20 accepted |
| Planner autonomy | 21 / 21 accepted |

## Artifact Set

New/updated artifact reports:
- quality/evals/reports/runtime_preview_redacted_flow_corpus.json
- quality/evals/reports/runtime_preview_redacted_flow_corpus.md
- quality/evals/reports/runtime_package_manifest_dry_run.sample.json
- quality/evals/reports/runtime_package_manifest_dry_run.sample.md
- quality/evals/reports/runtime_preview_package_readiness_report.sample.json
- quality/evals/reports/runtime_preview_package_readiness_report.sample.md
- quality/evals/reports/runtime_preview_agent_explanation_benchmark.json
- quality/evals/reports/runtime_preview_agent_explanation_benchmark.md
- quality/evals/reports/runtime_preview_governance_export_sample.json
- quality/evals/reports/runtime_preview_governance_export_sample.md
- quality/evals/reports/VisionAgent_business_benchmark_baseline.json
- quality/evals/reports/VisionAgent_business_benchmark_baseline.md

## CI Evidence

Latest remote CI evidence must be updated after pushing this v1.3 commit and observing a successful Vision Agent Quality Suite run. The local suite already produces the required artifact files and trx/txt evidence.

## Safety Statement

RuntimePreview v1.3 did not add real camera SDK access, real Station access, real image file reads, real model file loads, PLC writes, real package file creation, real packaging, real deployment, hot-load, or a Real RuntimePreview adapter. Manifest dry-run remains metadata-only and packageCreated=false.
