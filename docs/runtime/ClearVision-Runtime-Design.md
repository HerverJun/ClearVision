# ClearVision Runtime Design

## Boundary

- `Acme.Product.Desktop`
  - Studio only.
  - Owns WinForms host, WebView2, Kestrel, static `wwwroot`, auth/web endpoints, AI authoring entry points.
- `Acme.Product.Runtime.Abstractions`
  - Stable runtime package contracts and normalized runtime result DTOs.
- `Acme.Product.Runtime`
  - Runtime package export/load/validation.
  - Runtime host state machine, single-run/folder replay loop, bounded JSONL/image persistence.
  - Result normalization on top of shared `IFlowExecutionService`.
- `Acme.Product.Station`
  - Native WinForms operator console.
  - No WebView2, no Kestrel, no `wwwroot`, no Desktop reference.

## Shared execution

- Shared runtime DI now lives in `Acme.Product.Infrastructure.DependencyInjection.VisionRuntimeServiceCollectionExtensions`.
- Desktop keeps `AddVisionServices(...)` as a thin wrapper.
- Runtime/Station reuse the same `FlowExecutionService`, operator factory, variable context, and executor registrations.

## Package format

V1 package layout:

```text
runtime-package/
|- package.json
|- flow.json
|- runtime-profile.json
|- README.runtime.md
|- quality/
|  |- validation-report.json
|- field/
   |- station-profile.json
   |- trigger-profile.json
   |- result-mapping-profile.json
   |- model-assets.json
```

Validation rules:

- Required files must exist.
- `runtimeApiVersion` must be `1.0`.
- `entryFlow` must stay under the package root.
- `flowHash` must match `flow.json`.
- `exportAllowed` must be `true`.
- `pendingParameters` and `missingResources` must be empty.
- Secret-like parameters block export.

## Runtime host

- States: `Idle -> Loaded -> Running -> Stopping -> Loaded/Faulted`
- Execution modes:
  - single local image
  - folder replay
- Stop behavior:
  - linked cancellation token
  - delegates to `IFlowExecutionService.CancelExecutionAsync(...)`
  - bounded timeout via `runtime-profile.json`
- Persistence:
  - `%LocalAppData%/ClearVisionStation/runs/yyyyMMdd/runtime-results.jsonl`
  - `%LocalAppData%/ClearVisionStation/images/yyyyMMdd/NG|ERROR`

## Station MVP

- Load runtime package folder.
- Load last-good package pointer.
- Pick one image and run.
- Pick one folder and replay.
- Stop active replay.
- Show runtime state, last status, last timing, aggregate stats, recent results, bounded log buffer.

## Field schema drafts

- `field/station-profile.json`
  - `stationId`
  - `lineName`
- `field/trigger-profile.json`
  - `mode`
  - `intervalMs`
- `field/result-mapping-profile.json`
  - `okCode`
  - `ngCode`
  - `errorCode`
- `field/model-assets.json`
  - `assets[]`

Station MVP reads but may ignore these drafts.
