# Compatibility Matrix

## Supported

| Area | Supported baseline |
| --- | --- |
| OS | Windows x64 for Desktop Studio and field portable packages. |
| .NET SDK | Exact `9.0.300` via `global.json`; use `scripts/dotnet.ps1` to avoid PATH-based SDK drift. |
| .NET runtimes | .NET 8 Core / ASP.NET / WindowsDesktop runtime for test and Desktop debug; `scripts/dotnet.ps1 -InstallIfMissing` installs `8.0.26` into the selected user-local dotnet host. |
| Product TFM | `net8.0-windows` for Desktop, `net8.0` for shared/runtime libraries. |
| UI runtime | WebView2 inside WinForms Desktop. |
| Database | SQLite for local Studio/Station persistence. |
| Operator package | `Acme.OperatorLibrary` package baseline `1.0.2`, `net8.0`. |
| Station sync | Opt-in outbound SignalR/HTTP from Station to Studio. |

## Conditional

| Area | Condition |
| --- | --- |
| GPU/ONNX providers | CPU fallback required; GPU provider and driver profile must be recorded in model release evidence. |
| PLC | Modbus virtual regression is local; MC/FINS virtual regression is opt-in; physical PLC validation remains field evidence. |
| Portable delivery | Use `scripts/package-portable-deployment.ps1` or equivalent release process, not the raw CI desktop zip, for site delivery. |
| Public datasets | Can support research or substitute evidence; cannot be described as production sign-off. |

## Not Supported By Default

| Area | Boundary |
| --- | --- |
| Station inbound HTTP server | Not part of the current Station sync design. |
| Image transfer over Station sync | Result summaries only; source/output images are not carried by Station DTOs. |
| Containerized desktop app | Not a supported release target. |
| Production model binaries in git | External or ignored paths only unless explicitly approved. |
