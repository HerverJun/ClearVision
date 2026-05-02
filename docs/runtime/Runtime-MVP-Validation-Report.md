# Runtime MVP Validation Report

Date: `2026-05-02`

## Scope closed in this pass

- Shared runtime DI extracted from Desktop.
- Added `Acme.Product.Runtime.Abstractions`, `Acme.Product.Runtime`, `Acme.Product.Station`.
- Implemented Runtime Package V1 export/load/validation.
- Implemented RuntimeHost single-run + folder replay + stop path.
- Added Station WinForms MVP for package load / single image / folder replay / stop / recent results / bounded log.
- Added architecture guards and runtime MVP tests.

## Commands executed

```powershell
dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore

& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "RuntimeMvpTests" `
  -NoRestore `
  -Verbosity minimal

dotnet build Acme.Product/src/Acme.Product.Station/Acme.Product.Station.csproj `
  --configuration Debug `
  --no-restore

Select-String -Path "Acme.Product/src/Acme.Product.Station/**/*.cs","Acme.Product/src/Acme.Product.Runtime/**/*.cs","Acme.Product/src/Acme.Product.Station/*.csproj","Acme.Product/src/Acme.Product.Runtime/*.csproj" `
  -Pattern "WebView2|Microsoft.Web.WebView2|wwwroot|Kestrel|WebApplication|MapVisionApiEndpoints|Acme.Product.Desktop"
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
