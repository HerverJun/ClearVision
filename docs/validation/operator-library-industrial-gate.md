# Operator Library Industrial Gate

The industrial gate remains the Release / Manual automated core. It is serial, reuses the repository test runner, and selects tests only through the authoritative `TestClassification` Traits defined by `quality/test-gates.json`.

## Commands

Run the full profile:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" -Profile industrial
```

The industrial profile runs, in order:

- classification governance;
- OperatorLibrary package smoke;
- measurement Regression, Accuracy, Determinism, Stability, and acceptance Performance;
- calibration Regression and Integration;
- detection Regression, Accuracy, Stability, and acceptance Performance;
- matching Determinism and Stability;
- preprocessing Robustness;
- virtual PLC Release / Manual evidence.

The `quick` profile is a fast industrial diagnostic containing governance, package smoke, regression, and PR-safe PLC coverage. It is not a Release / Manual substitute.

Run selected gates:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" `
  -Gate governance,measurement-accuracy,matching-stability
```

Preview without execution:

```powershell
& ".\scripts\run-operator-library-industrial-gate.ps1" -Profile industrial -DryRun
```

The preferred release entry, including the explicit manual boundary, is:

```powershell
& ".\scripts\run-test-quality-lane.ps1" `
  -Lane ReleaseManual `
  -AcknowledgeManualRequirements
```

That entry runs the industrial profile first and then the authoritative `product-release-manual` aggregate gate, so Release / Manual reports include every classified external-resource test rather than only the industrial subset.

## Outputs

Each industrial run writes:

```text
test_results/operator-library-industrial-gate/<yyyyMMdd-HHmmss>/
```

with:

- `logs/*.log`;
- `trx/*.trx`;
- `performance-reports/*`;
- `governance/test-governance.json` and `.md`;
- `summary.json` and `summary.md`.

Each classified child gate also writes a `*.gate.json` beside its TRX. The top-level summary records every command, duration, exit code, TRX, and performance report.

## Result Validation

Every gate uses a minimum existence check of one test. This only prevents empty filters and missing/renamed gate memberships; fixed test counts are not treated as quality evidence. Test quality is enforced by the classification governance rules and the declared oracle/evidence types.

The run fails when:

- governance detects missing or invalid classification;
- a Trait filter resolves to no test;
- any TRX is missing, malformed, or contains failed/error/timeout/aborted results;
- a performance report is missing, stale, incomplete, or over budget;
- virtual PLC prerequisites cannot start or the declared PLC tests fail.

## Dependencies

- Invoke scripts from the current PowerShell shell; do not use `powershell.exe -File` around the serial runner.
- The first run should build and restore current sources. Use `-NoBuild -NoRestore` only after the same projects and package smoke assembly were built successfully.
- OperatorLibrary smoke consumes the packed package rather than a project reference to the product implementation.
- Virtual PLC execution starts isolated Modbus and MC/FINS simulators and declares the `VirtualPlc` resource requirement.
- Physical PLC, camera, device, package/source identity, model/assets, SBOM/delivery evidence, and human approval remain outside the automated industrial profile and must be acknowledged by the Release / Manual lane.

## Failure Triage

1. Open `summary.md` and locate the first failed gate.
2. Inspect `logs/<gate>.log` and the corresponding TRX/`*.gate.json`.
3. For a governance failure, inspect `governance/test-governance.md`; do not weaken an oracle or change a lane just to make it green.
4. For performance, compare the fresh report and declared profile variables.
5. For PLC, verify simulator ports and external resource declarations before rerunning.
