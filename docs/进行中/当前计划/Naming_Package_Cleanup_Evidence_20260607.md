# Naming And Package Artifact Cleanup Evidence 20260607

## Scope

This cleanup removes legacy Acme naming pollution and NuGet package artifact pollution from tracked repository content. It does not advance real camera SDK access, real Station access, real image/model/template reads, PLC writes, real package deployment, hot-load, or real package downlink capability.

## Scan Classes

| Class | Decision | Evidence |
| --- | --- | --- |
| Source/project/script/CI/README naming | Must use ClearVision naming | `ClearVision.OperatorLibrary` is used in `.sln`, `.csproj`, `pack.ps1`, README, CI, and package metadata. Active docs were normalized away from legacy product namespace wording. |
| Historical/deprecated references | Allow only with explicit allowlist | `quality/evals/allowlists/acme_naming_allowlist.json` allows only this report and the allowlist itself to mention Acme.Product, Acme.OperatorLibrary, or Acme.* for cleanup evidence. No product code path is allowlisted. |
| Build/package artifacts | Must be removed from git | Tracked `Acme.OperatorLibrary/nupkg/*.nupkg`, `Acme.OperatorLibrary/nupkg/*.snupkg`, and `ClearVision.OperatorLibrary/nupkg/.gitkeep` were removed. |

## Removed Tracked Package Artifacts

- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.1-local.nupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.1-local.snupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.2-ci.local.1778918632.nupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.2-ci.local.1778918632.snupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.2-industrial.local.1778918849.nupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.2-industrial.local.1778918849.snupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.2-local.nupkg`
- `Acme.OperatorLibrary/nupkg/Acme.OperatorLibrary.1.0.2-local.snupkg`

## Removed Temporary Snapshot

- `.tmp/AiFlowValidator.HEAD.cs` was removed because it was a tracked temporary snapshot with deprecated `Acme.Product.*` namespace references.

## Ignore Policy

`.gitignore` now blocks:

- `*.nupkg`
- `*.snupkg`
- `**/nupkg/`
- `**/packages/`

The existing `!**/packages.lock.json` exception remains in place so NuGet lock files stay trackable and reviewable.

## Source Guard

The UI contract source guard now checks `git ls-files` and fails if tracked content includes:

- `Acme.Product`
- `Acme.OperatorLibrary`
- any `Acme.*` fragment outside the allowlist
- `.nupkg`
- `.snupkg`
- files under tracked `nupkg/`
- files under tracked `packages/`

## Acceptance Snapshot

Post-cleanup state:

- `git ls-files` contains no `Acme.OperatorLibrary/nupkg/*.nupkg`.
- `git ls-files` contains no `Acme.OperatorLibrary/nupkg/*.snupkg`.
- Main source, project files, scripts, CI, README, and quality reports contain no `Acme.Product` or `Acme.OperatorLibrary`.
- Any remaining literal Acme text is confined to this cleanup report and the allowlist that explains why it is present.

## Local Validation

| Gate | Result |
| --- | --- |
| `dotnet build ClearVision.Product/ClearVision.Product.sln --no-restore` | Passed, 0 errors. |
| `dotnet build ClearVision.OperatorLibrary/ClearVision.OperatorLibrary.csproj --configuration Release --no-restore` | Passed, 0 errors. |
| `./ClearVision.OperatorLibrary/pack.ps1 -Configuration Release -RunSmokeTest` | Passed; generated `ClearVision.OperatorLibrary.*` package outputs locally, smoke tests `40/40`, then local ignored `nupkg` output was removed. |
| `python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run` | Passed; backend `593/593`, UI `195/195`, AI endpoint `42/42`. |
| `python quality/tools/assert_vision_agent_report_artifacts.py --scan-source-files ...` | Passed; `72` artifact files, `33` reports, `3383` source files scanned. |
| `git check-ignore` for package outputs | `.nupkg`, `.snupkg`, `nupkg/`, and `packages/` are ignored; `packages.lock.json` remains unignored. |
