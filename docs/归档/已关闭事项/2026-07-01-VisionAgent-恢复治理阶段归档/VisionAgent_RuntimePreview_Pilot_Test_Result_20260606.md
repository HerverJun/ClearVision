# Vision Agent RuntimePreview Pilot v0.8 Test Result 20260606

## Summary

RuntimePreview Pilot v0.8 is implemented as a default-off, configurable, metadata-only pilot framework with a safe resource catalog, readiness gate, hidden developer UI, endpoint coverage, adapter fallback/deny behavior, and quality-suite regression gates.

## Backend Coverage

New and updated backend cases cover:

- old `AppConfig` migration to safe `RuntimePreviewPilotConfig` defaults
- config validator rejects wildcard/path/IP/URL/token-like allowlist values
- config validator rejects disabled `denyExternalPath` and `denyImageBytes`
- catalog exposes AppConfig/AiConfigStore/workflow draft metadata only
- catalog redacts unsafe identifiers and does not leak API keys or BaseUrl/path fragments
- readiness gate returns `ready`, `not_ready`, and `denied`
- readiness gate keeps `workflowDraftAllowed=true` for `not_ready`
- allowlist resolver supports the safe `runtime_preview_metadata` readiness tool
- allowlist resolver camera/model/template/flow/resource-root hit/miss/empty cases
- external path, path traversal, Station, PLC, and encoded image payload deny cases
- pilot adapter runs readiness before metadata result/fallback/deny
- allowlist miss returns `RuntimePreviewPilotReadinessReview`
- dangerous deny returns no fallback artifact
- adapter exception falls back offline and keeps draft editing allowed
- source guard keeps frontend free of `capture_test_frame` and `replay_flow_with_frame`

## Endpoint Coverage

`AiModelEndpointsTests` now covers RuntimePreview Pilot endpoints:

- `GET /api/settings/runtime-preview-pilot/config`
- invalid `PUT` rejects unsafe keys
- valid `PUT` saves normalized metadata-only config
- `GET /api/settings/runtime-preview-pilot/catalog` returns redacted metadata
- `POST /api/settings/runtime-preview-pilot/readiness` returns ready/not-ready/denied details
- no API key, full BaseUrl, IP-like value, or path fragment leaks in endpoint responses

## UI Coverage

UI contract cases cover:

- RuntimePreview adapter/mode/permission/readiness/fallback/resource trace display
- `workflowDraftAllowed=true` remains visible when preview is not ready
- pending actions and missing resources display
- developer pilot panel hidden by default
- developer panel visible only through the hidden developer flag
- pilot config, catalog, readiness controls render
- catalog/readiness values are sanitized
- no key appears in DOM text or captured console output
- frontend source has no RuntimePreview hardware/network/process tool entry

## Local Test Commands

Targeted RuntimePreview backend:

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName "ClearVision.Product.Tests.AI.VisionAgentRuntimePreviewAdapter.VisionAgentRuntimePreviewAdapterTests,ClearVision.Product.Tests.AI.VisionAgentRuntimePreview.VisionAgentRuntimePreviewTests" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal
```

Result: 42 total / 42 passed.

AI model + RuntimePreview Pilot endpoint regression:

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" `
  -FullyQualifiedName "ClearVision.Product.Desktop.Tests.AiModelEndpointsTests" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal
```

Result: 9 total / 9 passed.

UI contract:

```powershell
Set-Location "ClearVision.Product/tests/ClearVision.Product.UI.Tests"
npm run test:agent-ui-contract
```

Result: 58 total / 58 passed.

Full quality suite:

```powershell
python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run
```

Result:

- backend Agent tests: 203 total / 203 passed
- AI model endpoint regression: 9 total / 9 passed
- UI contract tests: 58 total / 58 passed
- executable business benchmark: 36 / 36 accepted
- planner autonomy + permission negative benchmark: 21 / 21 accepted

## Secret And Source Scan

Command:

```powershell
python quality/tools/assert_vision_agent_report_artifacts.py `
  --scan-source-files `
  --write-manifest quality/evals/reports/vision_agent_quality_artifact_manifest.json `
  --write-report quality/evals/reports/vision_agent_quality_artifact_manifest.md
```

Result:

- artifact files validated: 13
- reports validated: 4
- source files scanned: 3310
- forbidden key/IP/BaseUrl fragments: not found

## Gate Updates

`agent_engineering_harness_suite` gates:

- backend Agent tests minimum: 203
- UI contract tests minimum: 58
- AI endpoint tests minimum: 9
- business benchmark minimum: 36
- planner autonomy minimum: 15
- permission negative minimum: 6

Stable CI keeps real LLM shadow eval default-off. Manual fixed/holdout shadow eval remains planner-shadow evidence only.

## Safety Result

No forbidden real-resource capability was added:

- real camera SDK: not added
- real Station access: not added
- real image file read: not added
- real vision model file load: not added
- PLC write: not added
- package/deploy/hot-load/downlink: not added
- image bytes/base64 returned: false
- arbitrary path read: false
- RuntimePreview remains offline/metadata-only

## Pilot Conclusion

RuntimePreview Pilot v0.8 can proceed only as an internal, default-off, metadata-only pilot framework. It is not a Real RuntimePreview adapter and does not authorize real camera, Station, image, model, PLC, packaging, deployment, or hot-load work.
