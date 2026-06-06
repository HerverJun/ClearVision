# Vision Agent RuntimePreview Pilot v0.7 Test Result 20260606

## Summary

RuntimePreview Pilot v0.7 skeleton is implemented as default-off, metadata-only, allowlist-gated, and offline-fallback capable. Local quality suite passed after raising minimum gates.

## Backend Coverage

New backend cases cover:

- old `AppConfig` migration to safe `RuntimePreviewPilotConfig` defaults
- config validator rejects wildcard/path-like allowlist tokens
- config validator rejects disabled `denyExternalPath` and `denyImageBytes`
- allowlist resolver camera hit
- allowlist resolver empty allowlist deny
- allowlist resolver missing camera deny
- allowlist resolver unknown/dangerous resource deny
- external path deny
- path traversal deny
- Station deny
- PLC deny
- encoded image payload deny
- pilot disabled routes to Offline
- pilot allowlist hit returns `pilot_runtime_preview` + `metadata_only`
- allowlist miss returns deny + pendingAction + fallback metadata
- external path deny does not leak path fragments
- adapter exception falls back Offline
- structural preview failure keeps `workflowDraftAllowed=true`
- source guard remains free of camera/Station/file/model/PLC/process/network access in RuntimePreview code

## UI Coverage

New UI contract cases cover:

- RuntimePreview adapter/mode/permission/fallback/resource trace display
- RuntimePreview pending actions display
- RuntimePreview artifact metadata display
- developer pilot status hidden by default
- developer pilot status visible only when developer UI is enabled
- allowlist counts display as counts only
- UI redacts path/IP/BaseUrl/key/encoded image fragments
- denied RuntimePreview still leaves workflow draft editable

## Local Test Commands

RuntimePreview targeted backend:

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName "ClearVision.Product.Tests.AI.VisionAgentRuntimePreviewAdapter.VisionAgentRuntimePreviewAdapterTests,ClearVision.Product.Tests.AI.VisionAgentRuntimePreview.VisionAgentRuntimePreviewTests" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal
```

Result:

- total: 40
- passed: 40
- failed: 0

UI contract:

```powershell
Set-Location "ClearVision.Product/tests/ClearVision.Product.UI.Tests"
npm run test:agent-ui-contract
```

Result:

- total: 55
- passed: 55
- failed: 0

Full quality suite:

```powershell
python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run
```

Result:

- backend Agent tests: 200 / 200 passed
- AI model endpoint regression: 8 / 8 passed
- UI contract tests: 55 / 55 passed
- executable business benchmark: 36 / 36 accepted
- planner autonomy benchmark: 21 / 21 accepted

## Gate Updates

`agent_engineering_harness_suite` was raised:

- backend Agent tests minimum: 200
- UI contract tests minimum: 55
- AI endpoint tests minimum: 8
- business benchmark minimum: 36
- planner autonomy minimum: 15
- permission negative minimum: 6

Stable CI still keeps fixed/holdout real LLM shadow eval default-off.

## Safety Result

No forbidden resource capability was added:

- realCameraSdkTouched=false
- realStationTouched=false
- realImageFilesRead=false
- realModelFilesLoaded=false
- plcWriteAttempted=false
- packageCreated=false
- hotLoadAttempted=false
- imageBytesReturned=false
- arbitraryPathRead=false
- workflowDraftAllowed remains true when preview fails

## Pilot Conclusion

RuntimePreview Pilot v0.7 skeleton is allowed to exist as a default-off metadata-only internal path. It is not a real RuntimePreview adapter and does not authorize real camera, Station, file, model, PLC, packaging, deployment, or hot-load work.
