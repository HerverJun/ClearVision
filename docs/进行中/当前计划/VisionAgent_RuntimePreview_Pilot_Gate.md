# Vision Agent RuntimePreview Pilot Gate

## Scope

This document defines the gate for any future Real RuntimePreview Pilot. It does not implement a real RuntimePreview adapter and does not loosen the offline/metadata-only boundary.

## Required Evidence Before Real Pilot

Real RuntimePreview Pilot is allowed only when all of the following are true:

- fixed real LLM shadow eval passes planner protocol thresholds
- holdout real LLM shadow eval passes robustness thresholds
- permission negative benchmark passes all denial cases
- model config regression passes key, BaseUrl, Test Connection, role routing, and redaction cases
- RuntimePreview Pilot v0.8 catalog/readiness/adapter/endpoint/UI tests pass
- artifact assertion confirms reports, logs, snapshots, TRX, markdown, JS, C#, PS1, and source scans do not leak API keys or full CPA BaseUrl values
- latest GitHub Actions Vision Agent Quality Suite artifact is green

Holdout shadow thresholds:

- parseSuccessRate >= 0.90
- unsafeAttemptRate = 0
- averageFullPlanMatchScore >= 0.80
- averageOrderedPrefixScore >= 0.85
- policySafetyScore = 1.0
- badToolNames = 0

## Pilot Defaults

- Pilot must be default closed.
- Pilot must require explicit developer/internal enablement.
- Pilot must remain outside stable CI unless a separate opt-in workflow is created.
- Mock planner autonomy remains the stable CI gate.

## Resource Allowlist

Pilot must use a resource allowlist. A request outside the allowlist must be denied or marked `not_ready` and must fallback offline.

The allowlist may cover only explicitly approved logical metadata resources. It must not imply access to arbitrary cameras, Station resources, image paths, model files, PLC devices, package outputs, deployment endpoints, or hot-load channels.

## Data Boundary

- no image bytes/base64 may be returned
- no raw frame binary artifacts may be persisted
- no real image files may be read unless a later approved pilot explicitly adds a safe resource-bound adapter
- no real vision model files may be loaded
- preview responses must stay metadata-only unless a later gate approves a specific safe artifact format

## Action Boundary

- no PLC write
- no package, deploy, or hot-load
- no Station package export
- no downlink
- no configuration write from Agent tools
- no Agent shell/cmd/powershell/system command tool
- no `Acme.Product.*` addition

## Failure Behavior

- Pilot failure must fallback offline unless the request is a dangerous deny.
- Dangerous deny must not return fallback artifacts.
- Pilot failure must not block workflow draft editing.
- `workflowDraftAllowed` must not be affected by preview failure.
- RuntimePreview denial/not-ready status must enter pendingActions/toolTrace/policyDecision with a readable reason.
- DeploymentPrepare remains limited to `runtime_package_precheck`; no other deployment-like tool is allowed.

## Current Conclusion

RuntimePreview Pilot v0.8 is implemented as a default-off, metadata-only, resource-cataloged, readiness-gated internal framework with offline fallback. It is not a Real RuntimePreview adapter. It does not connect to real cameras, Station, image files, model files, PLC, packaging, deployment, downlink, or hot-load.

The next real adapter step remains gated. It can start only after fixed shadow, holdout shadow, permission negative cases, model config regression, RuntimePreview Pilot v0.8 regression, artifact assertion, and CI evidence are all green, and only if the implementation preserves default-off behavior, resource allowlist, no image bytes/base64, no PLC write, no package/deploy/hot-load, offline fallback, and `workflowDraftAllowed` independence.
