# CI And Quality Gates

This document records the current CI responsibility split after the debug-plan closure.

## Pull Request Lane

| Gate | Responsibility |
| --- | --- |
| Secret scan | Reject committed credentials and known sensitive values. |
| Text encoding scan | Check active user-facing text for invalid UTF-8 and common mojibake fragments. |
| Restore/build | Validate solution restore and Debug build. |
| Product tests | Run through `scripts/run-dotnet-test-serial.ps1` with TRX and coverage collection. |
| Desktop tests | Run through the serial runner with TRX and coverage collection. |
| Detection gates | Regression, accuracy, stability and performance smoke profiles. |
| Operator package smoke | Locked restore, pack, install package into smoke tests, collect TRX/coverage. |
| UI unit + Playwright | Run `npm run test:unit` before Playwright. |
| Format/static analysis | `dotnet format --verify-no-changes` and warning-as-error build in PR code-quality job. |

## Release Or Manual Lane

- `workflow_dispatch` can run with an explicit version and detection performance profile.
- `scripts/run-operator-library-industrial-gate.ps1 -Profile quick` is the quick industrial gate.
- Full industrial profile remains a release/nightly gate because it may run heavier quality suites.
- Desktop CI zip is a build artifact. Field portable delivery must use `scripts/package-portable-deployment.ps1` or an equivalent package process that carries deployment notes, SBOM and third-party notices.

## Coverage Trend

Coverage is collected as an artifact first, not yet a high threshold blocker. Product/Desktop coverage should be trended over time, with priority on:

- runtime execution,
- Station sync and endpoint permissions,
- persistence and configuration,
- result/event backpressure paths.

## Test Runner Rule

Never start multiple `dotnet test` processes for the same `.csproj`. Use `scripts/run-dotnet-test-serial.ps1`, pass multiple `-FullyQualifiedName` values together, and use `-NoBuild -NoRestore` after a project has already built successfully in the session.
