# Vision Agent Real RuntimePreview RFC

## Status

Draft. This RFC defines future boundaries only. It does not implement a Real RuntimePreview adapter.

## Entry Criteria

Real RuntimePreview Pilot may start only after all of the following pass:

- fixed real LLM shadow eval
- holdout real LLM shadow eval
- permission negative benchmark
- model config regression
- RuntimePreview v1.0 session/broker/audit/report regression
- artifact/source/report/audit/session secret scan
- latest Vision Agent Quality Suite CI artifact
- manual approval from the internal owner

## Minimal Future Adapter Boundary

A future adapter may only be considered if it:

- is default-off
- requires admin/developer enablement
- requires resource allowlist handles
- requires explicit session authorization
- is read-only
- returns metadata first
- never returns image bytes/base64 by default
- falls back offline on failure
- keeps `workflowDraftAllowed=true` on preview failure

## Read-Only Sampling Conditions

Any future image/frame sampling must:

- use an approved resource handle, not a raw path or camera id
- require explicit session authorization
- be single-session scoped
- have timeout and cancellation
- not persist raw bytes unless a separate artifact gate is approved
- redact all logs and reports

## Model Loading Conditions

Any future model loading must:

- use approved model handles only
- never accept raw `ModelPath` or `ModelCatalogPath` from Agent output
- verify model metadata before loading
- run in isolated read-only mode
- record audit events before and after load
- fail closed and fallback offline

## Station Isolation Conditions

Any future Station interaction must:

- be read-only
- use a Station allowlist
- never write station config
- never package, deploy, downlink, or hot-load
- record a separate Station audit event
- fail closed when Station status is unknown

## PLC Write Ban

PLC writes remain forbidden. RuntimePreview must not write PLC tags, coils, registers, DB values, or output channels. PLC-related preview may only display redacted metadata.

## Network Permissions

Network access must be explicit, scoped, and audited. Agent tools must not gain shell/cmd/powershell/system command capabilities. A future adapter must not create broad network clients outside the approved RuntimePreview adapter boundary.

## Human Confirmation Points

Required confirmations:

- enabling real pilot mode
- selecting approved resource handles
- starting any real read-only sampling
- accepting any non-offline fallback result
- exporting any preview report

## Rollback Strategy

- disable pilot config
- remove resource handles from allowlist
- force offline fallback
- archive audit/report evidence
- keep workflow draft editing enabled

## Failure Modes

| Failure | Required behavior |
| --- | --- |
| permission missing | deny with pending action |
| allowlist missing | not_ready with pending action |
| dangerous path/model/image/plc/station field | denied, no artifact |
| adapter timeout | offline fallback |
| redaction failure | fail closed |
| Station unavailable | fail closed |
| model metadata mismatch | fail closed |

## Explicit Non-Goals

This RFC does not authorize:

- real camera SDK integration
- real Station access
- real image file read
- real model file load
- PLC write
- packaging
- deployment/downlink
- hot-load
- Real RuntimePreview adapter implementation
- Agent shell/cmd/powershell/system command tool
- `Acme.Product.*`
