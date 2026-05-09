# ClearVision

ClearVision is a .NET 8 industrial vision workflow platform. It combines a Windows desktop Studio, local runtime APIs, Station sync, operator execution, quality evidence, and a separately packable `Acme.OperatorLibrary`.

## Current Status

- Main app: `Acme.Product`, `net8.0-windows`, WinForms + WebView2 + local ASP.NET Core endpoints.
- Operator package: `Acme.OperatorLibrary`, package baseline `1.0.2`, MIT license expression.
- Toolchain: `.NET SDK 9.0.300` is pinned by `global.json` with conservative `latestFeature` roll-forward.
- Operator catalog: 155 public operator types are the formal package/catalog surface. Legacy aliases and internal-only operators are excluded from package indexing.
- Quality posture: functional maturity and evidence maturity are tracked separately. The current catalog can show broad functional coverage, but field replay, datasets, and real production sign-off remain separate release gates.

## Key Entry Points

- [Project overview](./docs/项目总览.md)
- [Docs index](./docs/README.md)
- [Current debug closure plan](./docs/进行中/当前计划/debug计划.md)
- [Operator quality matrix](./quality/evals/reports/operator_quality_matrix.md)
- [Operator package README](./Acme.OperatorLibrary/README.md)
- [CI and quality gates](./docs/engineering/ci-quality-gates.md)
- [Evidence and temporary artifacts](./docs/engineering/evidence-artifacts.md)
- [Compatibility matrix](./docs/engineering/compatibility-matrix.md)

## Common Commands

```powershell
dotnet --info
dotnet restore Acme.Product/Acme.Product.sln
dotnet build Acme.Product/Acme.Product.sln --configuration Debug
& "./scripts/run-dotnet-test-serial.ps1" -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" -Verbosity minimal
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release
& "./Acme.OperatorLibrary/pack.ps1" -Configuration Release -RunSmokeTest
```

Use `./scripts/run-dotnet-test-serial.ps1` for targeted .NET tests so test filters for the same project are merged into one process.
