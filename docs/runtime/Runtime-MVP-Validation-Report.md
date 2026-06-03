# Runtime MVP Validation Report

Date: `2026-05-02`

## Scope closed in this pass

- Shared runtime DI extracted from Desktop.
- Added `ClearVision.Product.Runtime.Abstractions`, `ClearVision.Product.Runtime`, `ClearVision.Product.Station`.
- Implemented Runtime Package V1 export/load/validation.
- Implemented RuntimeHost single-run + folder replay + stop path.
- Added Station WinForms MVP for package load / single image / folder replay / stop / recent results / bounded log.
- Added architecture guards and runtime MVP tests.

## Commands executed

```powershell
dotnet build ClearVision.Product/ClearVision.Product.sln --configuration Debug --no-restore

& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName "RuntimeMvpTests" `
  -NoRestore `
  -Verbosity minimal

dotnet build ClearVision.Product/src/ClearVision.Product.Station/ClearVision.Product.Station.csproj `
  --configuration Debug `
  --no-restore

Select-String -Path "ClearVision.Product/src/ClearVision.Product.Station/**/*.cs","ClearVision.Product/src/ClearVision.Product.Runtime/**/*.cs","ClearVision.Product/src/ClearVision.Product.Station/*.csproj","ClearVision.Product/src/ClearVision.Product.Runtime/*.csproj" `
  -Pattern "WebView2|Microsoft.Web.WebView2|wwwroot|Kestrel|WebApplication|MapVisionApiEndpoints|ClearVision.Product.Desktop"
```

## Result

- Solution build: passed
- Runtime MVP tests: passed
- Station project build: passed
- Dependency red-line scan: passed with no matches

## Coverage delivered

- package export round-trip
- invalid package hash rejection
- Studio single-run vs Station single-run result consistency
- folder replay stop/idempotence
- Runtime/Station architecture guard

## Remaining non-MVP follow-ups

- real hardware camera/PLC adapters
- field profile activation beyond draft schema
- on-device performance baseline on target industrial PCs
