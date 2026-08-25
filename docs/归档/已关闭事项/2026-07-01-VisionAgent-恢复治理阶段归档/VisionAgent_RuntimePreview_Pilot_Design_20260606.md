# Vision Agent RuntimePreview Pilot v0.8 Design

## Scope

RuntimePreview Pilot v0.8 upgrades the v0.7 metadata-only skeleton into a configurable, auditable internal pilot framework. It remains default-off and offline/metadata-only. It does not implement a real RuntimePreview adapter and does not connect to real cameras, Station, image files, model files, PLC, packaging, deployment, downlink, or hot-load.

## v0.7 Audit

Reviewed and reused existing pieces instead of replacing them:

- `RuntimePreviewPilotConfig` under `AppConfig.Runtime.RuntimePreviewPilot`
- `RuntimePreviewPilotConfigValidator`
- `RuntimePreviewPermissionGate`
- `RuntimePreviewResourceAllowlistResolver`
- `PilotRuntimePreviewAdapter`
- `OfflineRuntimePreviewAdapter`
- AI workbench RuntimePreview summary UI
- Settings API / AI settings tab structure
- `agent_engineering_harness_suite`

The audit found v0.7 already had a safe default-off config, permission gate, allowlist resolver, pilot adapter skeleton, offline fallback, and UI redaction. v0.8 adds catalog discovery, a separate readiness gate, hidden Settings management UI, and endpoint/UI/backend regression coverage.

## Configuration

`RuntimePreviewPilotConfig` is normalized during `AppConfig.Normalize()`:

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

Validation rejects unsafe allowlist keys:

- wildcard or broad tokens: `*`, `all`, `any`
- path-like tokens, traversal, slashes, drive markers
- URL markers and IP-like values
- `base64`, `token`, `apikey`, `api-key`
- disabled `denyExternalPath` or `denyImageBytes`

## Resource Catalog

`RuntimePreviewPilotResourceCatalog` builds safe metadata-only catalog items from:

- `AppConfig.Cameras` with source `app_config`
- `AiConfigStore` model summaries with source `ai_config_store`
- current workflow draft logical ids with source `workflow_draft`
- fixture fallback only when no items exist, with source `fixture` and `safeForPilot=false`

Each catalog item contains:

- `id`
- `displayName`
- `resourceType`
- `source`
- `metadataOnly`
- `safeForPilot`
- `reasonCode`
- `redacted`
- `metadata`

The catalog intentionally does not return real paths, IP/BaseUrl values, API keys, tokens, base64, image bytes, Station addresses, PLC addresses, or model file paths.

## Readiness Gate

`RuntimePreviewPilotReadinessGate` evaluates:

- pilot config
- catalog
- workflow draft
- `toolName`
- arguments
- `VisionAgentToolContext`

It returns:

- `status`: `ready`, `not_ready`, or `denied`
- `canRunMetadataPilot`
- `workflowDraftAllowed`
- `issues`
- `blockingIssues`
- `missingResources`
- `unsafeFindings`
- `allowlistCoverage`
- `resourceTrace`
- `pendingActions`
- `fallback`
- real-resource flags, all false

Decision rules:

- pilot disabled or missing allowlist: `not_ready`
- non-`metadata_only` mode: `denied`
- external path, file path, model path, template path, Station, PLC, image bytes/base64: `denied`
- metadata-only request with allowlisted resources: `ready`
- `not_ready` keeps `workflowDraftAllowed=true` and returns `RuntimePreviewPilotReadinessReview`

The Settings UI readiness probe uses `runtime_preview_metadata` so frontend source does not hard-code `capture_test_frame` or `replay_flow_with_frame`.

## Endpoints

Minimal Settings endpoints:

- `GET /api/settings/runtime-preview-pilot/config`
- `PUT /api/settings/runtime-preview-pilot/config`
- `GET /api/settings/runtime-preview-pilot/catalog`
- `POST /api/settings/runtime-preview-pilot/readiness`

`PUT` normalizes and validates config before saving. The endpoints return metadata-only summaries and no secrets. They do not affect PLC, Station, Camera, or general settings save semantics.

## Adapter Behavior

`PilotRuntimePreviewAdapter` now runs readiness first:

- `ready`: returns `pilot_runtime_preview` metadata-only result
- `not_ready`: returns offline fallback metadata plus pending actions
- dangerous `denied`: returns deny evidence with no fallback artifact
- exception: falls back to `offline_runtime_preview` when configured

All adapter results include:

- `permissionDecision`
- `resourceTrace`
- `readiness`
- `fallback`
- `issues`
- `pendingActions`

All resource-touch flags remain false:

- `binaryIncluded=false`
- `capturedRealFrame=false`
- `loadedModelFiles=false`
- `accessedHardware=false`
- `stationTouched=false`

## Developer UI

The AI Settings tab has a developer-hidden RuntimePreview Pilot panel. It is visible only when `localStorage.cv_ai_agent_dev_ui=true`.

Controls:

- enabled
- metadata-only mode display
- offline fallback
- max artifacts / metadata bytes
- allowlist inputs for camera, model, template, flow, resource root
- refresh catalog
- save config
- run readiness

Display:

- catalog rows with source, resource type, safe flag, redacted flag
- readiness status
- `blockingIssues`
- `missingResources`
- `pendingActions`
- `resourceTrace`
- fallback summary
- allowlist/catalog counts

All visible values are sanitized; no keys, full BaseUrl, IP-like identifiers, paths, or base64 markers are shown.

## Safety Boundary

RuntimePreview Pilot v0.8 intentionally does not add:

- real camera SDK
- real Station access
- real image file read
- real vision model file load
- PLC write
- package/export/deploy/hot-load/downlink
- image bytes/base64 output
- arbitrary path read
- Agent shell/cmd/powershell/system command tool
- legacy non-ClearVision product namespace
