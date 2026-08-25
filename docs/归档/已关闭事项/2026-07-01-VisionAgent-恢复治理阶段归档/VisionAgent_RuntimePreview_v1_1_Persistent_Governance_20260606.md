# Vision Agent RuntimePreview v1.1 Persistent Governance

Date: 2026-06-06

## Scope

RuntimePreview v1.1 keeps the v1.0 metadata-only safety boundary and upgrades governance from in-memory readiness checks to a persistent, replayable, auditable preparation layer. This work does not implement a Real RuntimePreview adapter and does not touch real resources.

## Architecture Audit

Current v1.0 responsibilities were split across session store, audit trail, report archive, readiness gate, permission broker, resource broker, simulated harness, and the developer-hidden settings panel. The main gaps were:

- Session/audit/report state was memory-only and could not be replayed after process restart.
- Readiness, simulation, report, and precheck evidence were available separately, but not as a single deploy-readiness chain.
- Pilot Console allowed basic session operations, but did not support catalog-driven allowlist changes, diff review, replay, export, retention cleanup, or scenario evidence.
- Artifact scanning recorded source/report counts, but v1.1 session/audit/report categories were not first-class evidence.

The v1.1 convergence keeps readiness and adapter logic separated. Governance owns session metadata, allowlist decisions, audit, replay, and reports; adapters remain metadata-only.

## Persistence Design

`RuntimePreviewGovernanceStore` persists append-only JSONL records:

| Stream | File | Contents |
| --- | --- | --- |
| Session | `runtime_preview_sessions.jsonl` | latest session state by sessionId |
| Audit | `runtime_preview_audit.jsonl` | append-only redacted audit events |
| Preview report | `runtime_preview_reports.jsonl` | metadata simulation reports |
| Deploy readiness | `runtime_preview_deploy_readiness_reports.jsonl` | metadata-only pre-deploy reports |

Default storage is local developer storage under the ClearVision application data area, or `CV_RUNTIME_PREVIEW_GOVERNANCE_STORE` when explicitly configured for tests. Stored payloads are rejected before write when they contain unredacted keys, auth headers, BaseUrl/IP fragments, image base64, path-like resource values, Station identifiers, or PLC address fragments.

Retention cleanup supports `retentionDays` and `maxSessions`, compacts JSONL streams, and appends a `retention_cleanup` audit event.

## Session Lifecycle

The lifecycle remains:

`create -> configure -> readiness -> authorize -> simulate -> complete`

Terminal alternatives:

`deny`, `fail`, `cancel`

Persisted session summaries include `sessionId`, `workflowDraftHash`, `pilotConfigRevision`, `catalogSnapshotId`, `readinessStatus`, `permissionStatus`, `auditEventIds`, `reportId`, `metadataOnly`, and `realResourcesTouched=false`.

## Broker Matrix

| Layer | Allows | Denies |
| --- | --- | --- |
| PermissionBroker endpoint gate | admin/developer metadata endpoints | non-admin access |
| PermissionBroker runtime gate | consent + RuntimePreview permission + metadata_only mode + ready resources | missing consent, missing RuntimePreview permission, disabled pilot, non-metadata mode, dangerous readiness |
| ResourceBroker | camera/model/template/flow/resourceRoot metadata handles | external paths, image bytes, Station/PLC values, unknown real resources |
| ReadinessGate | allowlisted metadata handles | missing allowlist, unsafe handles, resource mismatch |

## Pilot Console

The developer-hidden AI settings Console is now v1.1 and remains invisible for ordinary users. It supports:

- Catalog browser with selectable metadata handles.
- Catalog-driven allowlist draft and diff preview.
- Explicit save confirmation for allowlist changes.
- Readiness check.
- Metadata session create/simulate/cancel.
- Session replay.
- Report export preview.
- Deploy readiness report generation.
- Scenario evidence loading.
- Retention cleanup.

All displayed values pass through UI redaction; saved API keys and BaseUrl values are never rendered.

## Simulated Harness E2E

| Step | Evidence | Real resource touch |
| --- | --- | --- |
| create session | session record + `session_created` audit | false |
| catalog snapshot | metadata handles only | false |
| allowlist | redacted allowlist counts | false |
| readiness | ready/not_ready/denied with pending actions | false |
| authorize | permission decision | false |
| simulate preview | metadata artifact only | false |
| runtime package precheck | static precheck output only | false |
| deploy readiness report | combined report, no package | false |

`workflowDraftAllowed` remains true when preview or precheck is not ready.

## Scenario Evidence Set

The report runner writes:

- `quality/evals/reports/runtime_preview_scenario_evidence.json`
- `quality/evals/reports/runtime_preview_scenario_evidence.md`
- `quality/evals/reports/runtime_preview_deploy_readiness_report.sample.json`
- `quality/evals/reports/runtime_preview_deploy_readiness_report.sample.md`
- `quality/evals/reports/runtime_preview_governance_audit_sample.json`
- `quality/evals/reports/runtime_preview_governance_audit_sample.md`

Scenario set:

| Case | Scenario | Expected |
| --- | --- | --- |
| RP-SE-001 | wire_sequence | passed |
| RP-SE-002 | template_matching | passed |
| RP-SE-003 | hole_distance | passed |
| RP-SE-004 | remote_control_detection | passed |
| RP-SE-005 | missing_resource | not_ready |
| RP-SE-006 | dangerous_path | denied |
| RP-SE-007 | station_plc_deny | denied |
| RP-SE-008 | precheck_not_ready | not_ready |

## Deploy Readiness Matrix

Deploy readiness is a metadata-only aggregation of workflow draft hash, resource handles, readiness, permission, simulated preview, and `runtime_package_precheck`. It sets:

- `packageCreated=false`
- `deploymentExecuted=false`
- `realResourcesTouched=false`
- `deploymentBlocked=true` for not-ready or denied cases

Ready cases require readiness ready, previewReady true, and runtime package precheck ready.

## Session Replay Example

Replay returns a redacted timeline such as:

```json
{
  "sessionId": "rp_session_example",
  "timeline": [
    { "name": "session_created", "metadataOnly": true, "realResourcesTouched": false },
    { "name": "catalog_snapshot", "metadataOnly": true, "realResourcesTouched": false },
    { "name": "readiness", "status": "ready", "metadataOnly": true },
    { "name": "authorization", "status": "allowed", "metadataOnly": true },
    { "name": "simulated_preview", "status": "metadata_only", "metadataOnly": true }
  ]
}
```

## Tests And Gates

- Backend Agent suite target increased to 253.
- AI endpoint regression target increased to 19.
- UI contract target increased to 78.
- Scenario evidence report generation is an active quality suite entry.
- Artifact assertion now scans new RuntimePreview scenario/deploy readiness reports and continues source/report secret scanning.

## Safety Boundary

This v1.1 work did not advance real camera SDK integration, real Station access, real image file reads, real model file loading, PLC writes, packaging, deployment, hot-load, or Real RuntimePreview adapter implementation.
