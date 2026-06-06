# Vision Agent RuntimePreview Pilot v0.7 Design

## Scope

This is a metadata-only RuntimePreview Pilot skeleton. It is default-off and exists to test configuration, allowlist resolution, adapter routing, policy trace, UI display, and regression gates. It does not connect to real cameras, Station, image files, model files, PLC, packaging, deployment, downlink, or hot-load.

## Configuration

`RuntimePreviewPilotConfig` is stored under `AppConfig.Runtime.RuntimePreviewPilot` and is normalized during `AppConfig.Normalize()`:

- `enabled=false`
- `mode=metadata_only`
- `allowedCameraBindingIds=[]`
- `allowedModelIds=[]`
- `allowedTemplateIds=[]`
- `allowedFlowIds=[]`
- `allowedResourceRoots=[]`
- `maxPreviewArtifacts=8`
- `maxMetadataBytes=16384`
- `fallbackToOffline=true`
- `denyExternalPath=true`
- `denyImageBytes=true`

Migration behavior:

- old configs missing `runtimePreviewPilot` receive safe defaults
- wildcard tokens such as `*`, `all`, and `any` are removed by normalization
- path-like tokens, traversal tokens, and encoded image markers are not valid allowlist keys
- validator rejects disabled `denyExternalPath` or `denyImageBytes`

## Resolver

`RuntimePreviewResourceAllowlistResolver` evaluates the tool request, workflow draft, operator parameters, and `VisionAgentToolContext.RuntimePreviewPilot`.

Decision fields:

- `allowed`
- `reasonCode`
- `resourceType`
- `resourceId`
- `normalizedKey`
- `missingResources`
- `trace`

Covered decisions:

- camera allowlist hit/miss/empty
- model allowlist hit/miss/empty
- template allowlist hit/miss/empty
- flow allowlist hit/miss/empty
- unknown RuntimePreview tool deny
- external path deny
- path traversal deny
- image bytes / encoded image payload deny
- Station field deny
- PLC field deny
- real image file source deny
- real model/template path deny

The resolver only reads JSON values already present in the Agent request. It does not read files, enumerate cameras, contact Station, load models, or touch PLC.

## Adapter Routing

Registered adapters:

- `OfflineRuntimePreviewAdapter`
- `PilotRuntimePreviewAdapter`

Routing rules:

- pilot disabled and no explicit adapter: use `offline_runtime_preview`
- pilot disabled and explicit `pilot_runtime_preview`: use `offline_runtime_preview`
- pilot enabled and no explicit adapter: use `pilot_runtime_preview`
- pilot enabled and explicit `pilot_runtime_preview`: use `pilot_runtime_preview`
- explicit unknown adapter: controlled adapter-not-found failure
- adapter exception with `fallbackToOffline=true`: use offline fallback

`PilotRuntimePreviewAdapter` reuses the offline adapter to generate deterministic metadata after allowlist approval, then wraps the result as:

- `adapterName=pilot_runtime_preview`
- `previewMode=metadata_only`
- `permissionDecision`
- `resourceTrace`
- `fallback`
- `artifacts`
- `issues`
- `pendingActions`

All artifacts remain `metadataOnly=true`, `binaryIncluded=false`, and `byteLength=0`.

## Policy

Existing Agent gates remain authoritative:

- `RuntimePreviewConsent=false` denies `capture_test_frame` and `replay_flow_with_frame`
- missing `RuntimePreview` permission denies preview tools
- `ConfigWrite` remains permanently denied
- `DeploymentPrepare` remains limited to `runtime_package_precheck`

Pilot-specific decisions are additional:

- allowlist hit: metadata-only pilot result
- allowlist miss: deny, pendingAction, fallback info
- dangerous resource: deny, pendingAction, no real fallback execution
- adapter failure: offline fallback when enabled

Preview failure never changes `workflowDraftAllowed`; draft editing remains allowed.

## Agent Result Shape

`validationPreview.runtimePreview` now carries:

```json
{
  "adapterName": "pilot_runtime_preview",
  "previewMode": "metadata_only",
  "permissionDecision": {
    "allowed": true,
    "reasonCode": "runtime_preview_pilot_metadata_only_allowed",
    "runtimePreviewConsent": true,
    "pilotEnabled": true,
    "metadataOnly": true,
    "effectiveAdapterName": "pilot_runtime_preview",
    "allowlistCounts": {
      "camera": 1,
      "model": 1,
      "template": 1,
      "flow": 1,
      "resourceRoot": 1
    }
  },
  "resourceTrace": {
    "allowed": true,
    "reasonCode": "runtime_preview_resources_allowlisted",
    "resourceType": "workflow",
    "missingResources": [],
    "trace": []
  },
  "fallback": {
    "used": false
  },
  "artifacts": [
    {
      "artifactType": "operator_result_metadata",
      "metadataOnly": true,
      "binaryIncluded": false,
      "byteLength": 0
    }
  ]
}
```

Denied example:

```json
{
  "adapterName": "pilot_runtime_preview",
  "previewMode": "metadata_only",
  "previewReady": false,
  "workflowDraftAllowed": true,
  "permissionDecision": {
    "allowed": false,
    "reasonCode": "runtime_preview_camera_not_allowlisted"
  },
  "fallback": {
    "used": true,
    "fallbackAdapterName": "offline_runtime_preview"
  },
  "pendingActions": [
    {
      "actionType": "RuntimePreviewPilotAllowlistReview"
    }
  ]
}
```

## UI

AI Workbench RuntimePreview section displays:

- Adapter
- Mode
- Permission
- Resource trace
- Missing resources
- Pending actions
- Artifact metadata
- Fallback reason

Developer-only status shows:

- pilot enabled state
- allowlist counts
- metadata-only flag
- `realResourcesTouched=false`

The developer section is hidden by default. RuntimePreview UI display redacts path-like strings, IP/BaseUrl-like strings, authorization/key markers, and encoded image markers.

## Safety Boundary

This skeleton intentionally does not add:

- real camera SDK
- real Station access
- real image file read
- real vision model file load
- PLC write
- package/export/deploy/hot-load/downlink
- image bytes/base64 output
- arbitrary path read
- Agent shell/cmd/powershell/system command tool
- `Acme.Product.*`
