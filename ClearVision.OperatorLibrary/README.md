# ClearVision.OperatorLibrary

Industrial Vision Operator Library for ClearVision.

## Overview

`ClearVision.OperatorLibrary` packages the operator implementation layer as a standalone NuGet package while keeping source files in place under `ClearVision.Product/src/ClearVision.Product.Infrastructure`.

- Source sharing strategy: MSBuild linked compile items (`<Compile Include=... Link=... />`)
- Package model: single package (`ClearVision.OperatorLibrary`)
- Scope: image processing, measurement, calibration, communication, flow-control, AI

## Quick Start

1. Pack locally:

```powershell
./pack.ps1
```

2. Generated package output (default):

- `./nupkg/ClearVision.OperatorLibrary.1.0.3.nupkg`

3. Add local source in another project:

```xml
<packageSources>
  <add key="local-operator-library" value="path/to/ClearVision.OperatorLibrary/nupkg" />
</packageSources>
```

4. Reference package:

```xml
<PackageReference Include="ClearVision.OperatorLibrary" Version="1.0.3" />
```

5. Inspect a module index from package code:

```csharp
using ClearVision.OperatorLibrary.Modules;
using ClearVision.Product.Core.Enums;

var imageOperators = ClearVision.OperatorLibrary.ImageProcessing.Operators.Types;
var module = OperatorModuleCatalog.GetModule(OperatorType.MeanFilter);

Console.WriteLine($"{imageOperators.Count} image operators, MeanFilter module = {module}");
```

6. Run a representative operator directly:

```csharp
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

using var source = new Mat(64, 64, MatType.CV_8UC1, Scalar.Black);
using var inputImage = new ImageWrapper(source.Clone());

var op = new Operator("mean-filter-demo", OperatorType.MeanFilter, 0, 0);
op.AddParameter(new Parameter(Guid.NewGuid(), "KernelSize", "KernelSize", "", "int", 5));
op.AddParameter(new Parameter(Guid.NewGuid(), "BorderType", "BorderType", "", "int", 4));

var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);
var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
{
    ["Image"] = inputImage
});

Console.WriteLine(result.IsSuccess);
```

See also: `docs/参考资料/指南/ClearVision.OperatorLibrary-快速开始与质量验证.md`.

## Industrial Acceptance Scope

Package acceptance tests are not limited to smoke instantiation. The baseline now includes representative operators across all major modules:

- ImageProcessing: mean filter runtime boundary path, template matching, legacy morphology, region union, and FFT1D signal transform contracts
- Measurement: caliper success path and expected-count failure path
- Calibration: parameter validation and missing folder failure path
- Communication: Modbus validation boundary and RTU fail-fast path
- FlowControl: TryCatch passthrough contract
- AI: DeepLearning, SemanticSegmentation, and AnomalyDetection missing-asset gates plus SurfaceDefectDetection runtime diagnostics

Acceptance criteria: each representative operator must cover at least one normal path plus parameter, exception, or boundary behavior.

## Packaging Version & Traceability

The package no longer uses the fixed `*-local` version strategy.

- Default local version: `VersionPrefix` (`1.0.3` currently)
- CI version injection: pass `PackageVersion` (for example `1.0.3-ci.20260716.1`)
- Reproducibility metadata: `SourceRevisionId`, `RepositoryCommit`, `RepositoryBranch`, `PublishRepositoryUrl`, deterministic/CI build flags
- NuGet lock-file mode: the project sets `RestorePackagesWithLockFile=true`; `packages.lock.json` is checked in and CI/release restores use `--locked-mode`.
- Local package smoke restores use a temporary lock file under `.tmp/nuget-packages/operator-library-smoke` so fixed-version `.nupkg` hash changes do not rewrite the checked-in smoke-test lock file.
- Symbols: `.snupkg` is still generated for debugging compatibility
- Release package evidence: root package files include `THIRD-PARTY-NOTICES.md` and `SBOM.md`; release checklist and native runtime matrix live in `docs/operator-library/release-package-industrialization.md`.

`pack.ps1` supports explicit metadata injection:

```powershell
./pack.ps1 `
  -PackageVersion "1.0.3-ci.20260716.1" `
  -SourceRevisionId "a1b2c3d4" `
  -RepositoryBranch "main" `
  -RepositoryCommit "a1b2c3d4" `
  -RunSmokeTest
```

It also reads common CI environment variables (`CLEARVISION_OPERATORLIB_PACKAGE_VERSION`, `GITHUB_SHA`, `GITHUB_REF_NAME`, `BUILD_SOURCEVERSION`, `BUILD_SOURCEBRANCHNAME`) when parameters are omitted.
Package metadata includes project URL, repository traceability, license expression, release notes, README, SBOM, and third-party notices.

NuGet restore reproducibility workflow:

```powershell
cd ClearVision.OperatorLibrary
../scripts/dotnet.ps1 restore ./ClearVision.OperatorLibrary.csproj --use-lock-file
git diff -- packages.lock.json
../scripts/dotnet.ps1 restore ./ClearVision.OperatorLibrary.csproj --locked-mode
```

Dependency upgrade PRs must include the reviewed `packages.lock.json` diff; local restore without `--locked-mode` is only for intentionally updating that lock file.

## Tests, CI, and Benchmark Notes

Toolchain policy:

- Build with exact .NET SDK `9.0.300` from the repository `global.json` (`rollForward: disable`).
- Target framework remains `net8.0`.
- Direct `Microsoft.Extensions.*` dependencies are aligned to the repository net8-compatible package lane through `Directory.Packages.props`.
- Consumers should validate OpenCvSharp, ONNX Runtime, PaddleOCRSharp, HslCommunication, database, serial-port, and PLC dependencies in their deployment profile before treating the package as a thin abstractions-only dependency.

Use the package smoke/acceptance tests when validating NuGet packaging:

```powershell
./pack.ps1 -RunSmokeTest
```

For direct test execution, run the smoke test project serially from the repository root:

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.OperatorLibrary/tests/ClearVision.OperatorLibrary.SmokeTests/ClearVision.OperatorLibrary.SmokeTests.csproj"
```

Quality flywheel suites are described by manifests under `quality/evals/suites/` and executed serially by `quality/tools/run_quality_suite.py`:

```powershell
python quality/tools/run_quality_suite.py --suite quick_contract_suite --list
python quality/tools/run_quality_suite.py --suite quick_contract_suite --validate-only
python quality/tools/run_quality_suite.py --suite quick_contract_suite --dry-run
```

Benchmark evidence currently lives in two lanes:

- focused runner reports under `quality/evals/reports/*_baseline.json` and `*_baseline.md`
- product benchmark reports under `ClearVision.Product/test_results/*benchmark_report.md`

For ad hoc baseline performance checks:

```powershell
./scripts/dotnet.ps1 run --project scripts/BaselineBenchmark/BaselineBenchmark.csproj -- `
  --iterations 8 `
  --warmup 1 `
  --output docs/审计资料/报告/baseline_performance.json
```

Do not treat a single local benchmark run as a release gate by itself. Prefer comparing the generated report with the checked-in baseline evidence and the current `quality/evals/reports/operator_quality_matrix.md`.

## Notes

- This project is intentionally isolated from `ClearVision.Product.sln` default build graph.
- It does not change runtime behavior of the ClearVision main application.

## Phase 3.3 Compatibility Work

- Build profile constant: `CLEARVISION_OPERATORLIB_PACKAGE`
- Host-agnostic contracts/models: `ClearVision.OperatorLibrary/src/ClearVision.OperatorLibrary.Abstractions/*`
- Core adapters (guarded by `#if CLEARVISION_OPERATORLIB_PACKAGE`): `ClearVision.OperatorLibrary/src/ClearVision.OperatorLibrary.Abstractions/Adapters/CoreTypeAdapters.cs`
- Execution result abstractions preserve the host short-circuit flag (`ShouldShortCircuitFlow`) for trigger-style operators.
- Dependency analysis script:

```powershell
./analyze-deps.ps1
```

- Generated reports:
  - `./analysis/dependency-report.md`
  - `./analysis/dependency-report.json`

## Phase 3.4 Module Namespaces

The package exposes module-level namespace indexes:

- `ClearVision.OperatorLibrary.ImageProcessing`
- `ClearVision.OperatorLibrary.Measurement`
- `ClearVision.OperatorLibrary.Calibration`
- `ClearVision.OperatorLibrary.Communication`
- `ClearVision.OperatorLibrary.FlowControl`
- `ClearVision.OperatorLibrary.AI`

Use `Operators.Types` in each namespace to get grouped `OperatorType` lists.
