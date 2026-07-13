# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ClearVision is an industrial machine-vision inspection platform for .NET on Windows. It combines a WinForms + WebView2 desktop "Studio", an embedded ASP.NET Core local API, a flow-execution runtime, a large library of vision operators (OpenCV / ONNX / OCR / PLC), field "Station" sync, and a separately packable operator NuGet library.

The primary application solution lives in `ClearVision.Product/`. `ClearVision.OperatorLibrary/` is a standalone NuGet project that reuses operator source via MSBuild linked-compile items.

## SDK and toolchain

- `global.json` pins SDK **9.0.300** (`rollForward: latestFeature`). Projects target **net8.0** / **net8.0-windows**; running requires the .NET 8 Desktop Runtime + WebView2 Runtime.
- Do not rely on a bare `dotnet` from PATH — a machine may have both `C:\Program Files\dotnet` and `%LOCALAPPDATA%\Microsoft\dotnet`. Use `scripts/dotnet.ps1`, which reads `global.json` and selects the correct host. `scripts/dotnet.ps1 -InstallIfMissing` provisions the pinned SDK and runtimes.
- Packages are centrally versioned (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`) and restored with lock files. Restore with `--locked-mode`; do not add `<PackageVersion>` inline in a csproj.

## Common commands

Build (from repo root):
```powershell
& ".\scripts\dotnet.ps1" restore .\ClearVision.Product\ClearVision.Product.sln --locked-mode
& ".\scripts\dotnet.ps1" build   .\ClearVision.Product\ClearVision.Product.sln --configuration Debug --no-restore
```

Run the desktop app:
```powershell
& ".\ClearVision.Product\src\ClearVision.Product.Desktop\bin\Debug\net8.0-windows\win-x64\ClearVision.Product.Desktop.exe"
```

Desktop build/publish builds StudioUI automatically. After running `npm ci` in `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI`, use `-p:SkipStudioUiInstall=true` to skip only the redundant install step; do not skip the StudioUI build unless current validated assets already exist in the Desktop `obj` tree.

### Tests — always serialize per project

**Never run more than one `dotnet test` against the same `.csproj` at once.** Use the serial runner, which merges multiple filters into one invocation and holds a per-project lock:
```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName FlowExecutionServiceTests,ConnectionPoolManagerTests `
  -NoBuild -NoRestore -Verbosity minimal
```
- Invoke it from the current shell with `& "./scripts/..."`; do **not** wrap it in `powershell.exe -File` (leaks child processes).
- After the project has already built in this session, add `-NoBuild -NoRestore` to follow-up runs.
- Prefer the fixed preset scripts when they match the task: `run-tests-services-regression.ps1`, `run-tests-plc-regression.ps1`, `run-tests-desktop-endpoints.ps1`, `run-tests-detection-regression.ps1`, `run-tests-measurement-performance.ps1`, `run-tests-phase42-regression.ps1`, and the other `run-tests-*.ps1` under `scripts/`.

StudioUI quality gates (from `ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI`):
```powershell
npm ci
npm run lint
npm run typecheck
npm run test:unit
npm run build
```

UI/Playwright tests live in `ClearVision.Product/tests/ClearVision.Product.UI.Tests` (`npm run test:unit`, `npm run test:preview-smoke`, `npx playwright test`).

OperatorLibrary pack + smoke:
```powershell
& ".\ClearVision.OperatorLibrary\pack.ps1" -Configuration Release -RunSmokeTest
```

Quality suites (Python):
```powershell
python .\quality\tools\run_quality_suite.py --suite quick_contract_suite --run
```

### CI parity checks

CI enforces these on PRs — run them before pushing if you touched the relevant area:
- `dotnet format <sln> --verify-no-changes` (formatting is a hard gate)
- `dotnet build <sln> -warnaserror` (static analysis gate)
- `scripts/scan-secrets.ps1`, `scripts/check-text-encoding.ps1`, `scripts/check-diff-hygiene.ps1`

## Architecture

### Backend layering (`ClearVision.Product/src/`)

- **`ClearVision.Product.Core`** — domain layer: entities (`Operator`, `Parameter`), the `OperatorType` enum, operator base/interfaces (`OperatorBase`, `IOperatorExecutor`), metadata attributes (`OperatorMetaAttribute`, `InputPort`/`OutputPort`/`OperatorParam`), repository interfaces. No infrastructure dependencies.
- **`ClearVision.Product.Application`** — use cases: commands/queries, DTOs, AutoMapper profiles, application services.
- **`ClearVision.Product.Infrastructure`** — concrete implementations: **all vision operators live in `Infrastructure/Operators/`**, plus repositories (EF Core / SQLite), `OperatorFactory`, `OperatorMetadataScanner`, cameras, AI flow generation. DI composition is in `Infrastructure/DependencyInjection/VisionRuntimeServiceCollectionExtensions.cs` — `AddVisionRuntimeCoreServices` is the shared registration used by **both** Desktop and Station.
- **`ClearVision.Product.Runtime` / `.Runtime.Abstractions`** — flow execution host, runtime package export/load/validate for shipping flows to Stations.
- **`ClearVision.Product.Desktop`** — Windows entry point (`Program.cs`, `MainForm.cs`), WebView2 host, and the embedded ASP.NET Core server. HTTP handlers are minimal-API style in `Endpoints/` (`ApiEndpoints.cs`, `StationEndpoints.cs`, `PlcEndpoints.cs`, etc.); SignalR hubs in `Hubs/`. The server auto-binds a port in the 5000–5010 range.
- **`ClearVision.Product.Station` / `.Station.Simulator`** — field-side sync/run surface and its simulator.
- **`ClearVision.PlcComm`** — industrial protocol comms (Modbus, S7, MC, FINS, TCP/serial).

### Operator system (the core extension point)

Operators are **attribute-driven**. Each operator class in `Infrastructure/Operators/` derives from `OperatorBase`, overrides `OperatorType` and `ExecuteCoreAsync`, and declares its ports/parameters via attributes (`[OperatorMeta]`, `[InputPort]`, `[OutputPort]`, `[OperatorParam]`). At runtime:
1. `OperatorMetadataScanner` reflects over the operator assembly to build the metadata catalog.
2. `OperatorFactory` uses that metadata to instantiate operators with the right ports/params — it **fails hard** if metadata is missing for a type (no fail-open fallback).
3. Executors are registered as `IOperatorExecutor` singletons in `VisionRuntimeServiceCollectionExtensions`.

When adding or changing an operator you typically touch: the operator class, the `OperatorType` enum (`Core/Enums/OperatorEnums.cs`), and DI registration. The **git pre-commit hook** (`.githooks/pre-commit`) auto-regenerates the operator catalog (`算子资料/算子目录.{json,md}`) via `scripts/OperatorDocGenerator` whenever operator sources, attributes, `OperatorEnums.cs`, or the generator change. Install hooks with `scripts/install-githooks.ps1`.

### Frontend

- **Legacy** (`wwwroot/`, served at `/index.html`) remains the formal Desktop entry and rollback baseline. It is vanilla JS/CSS with source under `wwwroot/src/`.
- **StudioUI** (`StudioUI/`) is the independent Vue 3 + TypeScript + Vite rebuild line. During F01 Prompt 1 its assets may be built into output/publish `wwwroot/studio/`, but it is not a Desktop startup choice until Prompt 2.
- The retired migration prototype and `/v2` route are not compatibility targets and must not be recreated.

`ClearVision.Product/UI.md` documents DOM IDs, CSS class names, HTTP endpoints, WebMessage types, and `data-*` attributes that the **legacy** JS depends on — treat those as a contract and do not rename/remove them without updating the JS.

### Studio ↔ Station flow

Studio builds/executes flows locally, exports a **runtime package** (`Runtime/RuntimePackageExporter`), and syncs it to field Stations, which run inspections and report health/results/audit back. Operators can also be shipped independently as the `ClearVision.OperatorLibrary` NuGet package.

## Repository conventions

- **Docs must stay in sync with code.** `DEVELOPMENT_RULES.md` requires updating the relevant doc entry when you change code/config/CI, runtime or operator contracts, Station sync, deployment, or quality gates. The doc entry points are `docs/README.md` / `docs/导航.md`; in-progress work is under `docs/进行中/`, archived under `docs/归档/`. Do not reference the old "active 待办总表" path.
- **Temp/scratch output** for ad-hoc `dotnet publish` or packaging verification goes under `./.tmp/publish-check/` (disposable) or outside the repo — never new unignored temp dirs in the repo root.
- **Secrets** (AI provider keys, etc.) are never committed; configure them in local config or environment. The secret scanner runs in CI.
- Much of the codebase and docs are in **Chinese**; match the surrounding language when editing comments and docs. XML doc comments are expected on public APIs and complex lifecycle logic (concurrency, cancellation, `ImageWrapper` lifetime, Station sync, persistence recovery).

## Quality vs. evidence maturity

The project deliberately separates "feature exists" from "release evidence is sufficient." Public datasets, semi-synthetic samples, field-substitute replay, and dry-run smoke tests are **not** equivalent to real production sign-off. Do not describe simulated/replayed validation as real-hardware or real-line acceptance.
