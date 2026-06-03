# Operator Library Industrial Gate

This document defines the one-command validation gate for the operator library industrial-readiness push. The gate is intentionally serial: every .NET test step goes through the repository runner so the same `.csproj` is not tested by concurrent `dotnet test` processes.

## Commands

Run the full industrial profile from the repository root:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1"
```

The default `industrial` profile runs these gates in order:

- `operator-library-smoke`
- `measurement-regression`
- `measurement-accuracy`
- `measurement-stability`
- `measurement-performance`
- `calibration`
- `detection-regression`
- `detection-performance`
- `plc`

The `calibration` and `detection-regression` steps intentionally run their regression subsets. Broader integration or stability sweeps should be launched explicitly after their external prerequisites are available.

Run the faster smoke profile:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" -Profile quick
```

Run selected gates only:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" `
  -Gate operator-library-smoke,measurement-regression
```

Preview commands without executing tests:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" -Profile industrial -DryRun
```

## Outputs

Each real run writes a timestamped directory under:

```text
test_results/operator-library-industrial-gate/<yyyyMMdd-HHmmss>/
```

The run directory contains:

- `logs/*.log`: complete console output for each gate.
- `trx/*.trx`: one TRX file per selected .NET gate.
- `performance-reports/*`: measurement and detection performance budget reports generated during this run.
- `summary.json`: machine-readable gate summary.
- `summary.md`: human-readable gate summary.

If any selected gate returns a non-zero exit code, the top-level script exits non-zero. By default it continues after failures so the summary captures all selected gates. Use `-FailFast` to stop after the first failed gate. The summary also lists every collected TRX and performance report path.

## Dependencies

- Use the current PowerShell shell. Do not wrap `run-dotnet-test-serial.ps1` with `powershell.exe -File`.
- The .NET SDK is selected by the repository `global.json`.
- Existing environment variables are respected. If unset and available, the script uses repository-local `.dotnet-home` and `.dotnet/.nuget/packages`.
- Performance gates honor existing `CV_MEASUREMENT_PERF_*` and `CV_DETECTION_PERF_*` variables. The top-level script can also set `-PerfGateProfile standard|acceptance|auto`.
- Performance reports can be redirected with `-ReportDirectory` on the performance child scripts, `CV_MEASUREMENT_PERF_REPORT_DIR`, `CV_DETECTION_PERF_REPORT_DIR`, or generic `CV_PERF_REPORT_DIR`. The top-level industrial gate sets this to its timestamped `performance-reports` directory so it does not overwrite the tracked `ClearVision.Product/test_results/*_performance_budget_report.*` baseline files.
- The PLC gate delegates to `run-tests-plc-regression.ps1` and still depends on the communication simulators or test environment expected by those tests.

## NoBuild And NoRestore

For the first full validation run, omit `-NoBuild` and `-NoRestore` so every project can restore and build normally.

This is the default behavior for the top-level gate. A plain `run-operator-library-industrial-gate.ps1` invocation restores and builds before testing, so it validates the current worktree rather than reusing stale binaries.

After the same project has built successfully in the current session, follow-up runs may use:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" `
  -Gate operator-library-smoke `
  -NoBuild `
  -NoRestore
```

Do not use `-NoBuild` after changing source, project files, package references, `global.json`, or generated compile inputs. Do not use `-NoRestore` after dependency or NuGet cache changes.

## Result Validation

The serial test runner supports minimum result validation with `-ResultsDirectory`, `-LogFileName`, and `-MinimumTotalTests`. The industrial gate passes these for every selected gate. After `dotnet test` succeeds, the runner parses the expected TRX and fails the step if:

- the TRX file is missing or malformed;
- total or executed tests are below the gate minimum;
- passed tests are below the required minimum;
- TRX counters report failed, error, timed out, or aborted tests.

The minimums are intentionally conservative: performance and PLC gates require at least one executed/passed test, while class-filtered regression gates require at least the number of selected test classes. This blocks empty-filter or stale-output false positives without overfitting the exact number of test methods.

## Failure Triage

1. Open the run `summary.md` and identify the failed gate and exit code.
2. Inspect `logs/<gate>.log`; every log includes the command preview and serial runner output.
3. If the failure is from `dotnet test`, inspect the corresponding TRX in the run root and rerun the failed gate through the same serial runner path.
4. If a performance gate fails, check the `CV_MEASUREMENT_PERF_*` or `CV_DETECTION_PERF_*` environment variables and compare the generated performance budget report under the run root to the historical baseline.
5. If the PLC gate fails, verify the expected simulator, serial port, network, or PLC communication prerequisites before rerunning.

## AGENTS Constraints

The top-level gate and child gates use `run-dotnet-test-serial.ps1` for .NET test execution. The top-level gate passes `-ReturnExitCode` to child scripts so one child runner cannot terminate the whole orchestration before the summary is written. Running the existing child scripts directly without `-ReturnExitCode` preserves their previous command-line behavior.
