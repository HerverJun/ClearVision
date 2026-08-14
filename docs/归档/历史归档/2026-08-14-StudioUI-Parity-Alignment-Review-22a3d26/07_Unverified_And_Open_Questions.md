# Unverified and Open Questions

## Required NOT_VERIFIED Items

| Item | Status | Blocking reason | Required evidence |
| --- | --- | --- | --- |
| Real Windows 125% DPI | NOT PERFORMED | Current WebView2 run used scale 1 / 100% | Physical/logged Windows 125% run at 1920x1080 plus compact/comfortable screenshots |
| Independent no-Node target | NOT PERFORMED | Current machine has development toolchain | Publish/package on isolated target with no Node installed |
| Real Camera | NOT PERFORMED | Harness image only | Discovery, bind, single/continuous preview, trigger, failure/reconnect, calibration |
| Real PLC | NOT PERFORMED | No device/session | Connect/test/read-write/permission/error/recovery evidence |
| Real TCP client/server | NOT PERFORMED | No external peer | Profile, connect/listen, send/receive, timeout/reconnect evidence |
| Real Station | NOT PERFORMED | Local projection only | Package deploy, commands, unknown outcome, active identity, Station result ingestion |
| Real AI provider/model | NOT PERFORMED | Harness/session evidence only | Clarify/plan/build/apply/recover against authenticated provider and failure matrix |
| Remote CI | BLOCKED / NOT RUN | No authenticated run evidence in this audit | Clean remote branch run with artifact retention |
| Production soak | NOT PERFORMED | No production environment | Long-running execution, SSE reconnect, owner disposal, memory/handle/resource metrics |
| Product-owner signoff | NOT GRANTED | Explicit F10 state | Written acceptance of gaps/fallbacks and Legacy retirement decision |
| Additional targeted .NET group | NOT COMPLETED | Child task exceeded 10-minute threshold without result | Re-run via repository serial script; record exact classes/count/log |

## Endpoint Ownership Questions

### `POST /api/operators/{type}/preview`

Endpoint exists at `ApiEndpoints.cs:1694`, but no Legacy or Next UI caller was established. Do not classify as migration loss until the backend/product owner identifies its intended consumer or formally retires it.

### `POST /api/images/upload`

Endpoint exists at `ApiEndpoints.cs:2178`, but no Legacy or Next UI caller was established. Determine whether it belongs to an external client, a removed prototype, or a future Studio file-load workflow.

## Approved Deferred Capabilities

These are explicitly deferred, not migrated and not retired:

| Capability | Current boundary | Re-entry condition |
| --- | --- | --- |
| AI attachment resource | AgentRun resource store absent | resource ID/version, permission, operation identity, expire/recover/reconcile contract |
| CV model artifact | model registry/Project asset owner unresolved | publish/version/hash/download/revoke contract |
| TemplateMatching artifact | Flow template endpoint is not image template artifact authority | artifact identity/version/permission/reconcile contract |
| Calibration asset projection | asset -> numeric Scale/Offset projection incomplete | source asset/unit/node/revision projection |
| N-point advanced workflow | basic draft/solve/save retained | unified observation/asset projection plus overlay/import/export semantics |
| Generic AutoTune | only frozen Line Sequence scenario exposed | operator-by-operator target/input/parameter/admission/reconcile approval |
| Line Sequence AI follow-up | no approved cross-capability composer | one AI session/queue owner and operation identity |
| Database advanced | Legacy Admin maintenance authority retained | Admin policy, operation ID, revision/backup identity, audit, mutual exclusion, unknown outcome |
| Runtime Preview Pilot | Legacy console/backend retained | product vs internal ownership and approved Next surface/retirement decision |

## Partial Capabilities To Close

1. N-point advanced workflow: basic capture/edit/solve/candidate/formal asset save works; advanced templates/import/export/overlay remain absent.
2. Global Variables runtime values: owner exists, but current migration source labels evidence partial; run real save/reload/value conflict and disposal paths.
3. Continuous inspection protection/recovery: Runtime owns the behavior, but Next needs real state projection evidence for missing-material and consecutive-NG cases.
4. Startup cutover/rollback: `NEXT_DEFAULT` and local rollback evidence exist; production acceptance and Legacy retirement do not.

## Questions For Product / Architecture Owners

1. Is independent local image loading still a supported Studio user task, or should it receive a documented retirement decision?
2. Which node types retain Legacy subgraph semantics, and is double-click still the approved entry?
3. Should Operator recommendation be restored for all supporting operators or only an explicit allowlist?
4. Which Station commands require confirmation, second-person approval, or typed target confirmation?
5. What is the secure replacement for Station token reveal/copy: one-time reveal, OS clipboard lease, downloadable bootstrap file, or external provisioning?
6. Is Runtime Preview Pilot a supported admin product surface or an internal developer console? Until answered, it cannot be retired silently.
7. Are persistent FPS/memory/project/version indicators required for engineering diagnostics at 1080p/125%, or should Diagnostics own them?

## Completion Statement

The capability-group inventory is fully mapped: `UNMAPPED_LEGACY_CAPABILITIES=0`. The audit is complete as an evidence/accounting exercise, while product parity remains `PARTIAL` because confirmed gaps, approved Legacy fallbacks and real-environment acceptance debt remain.
