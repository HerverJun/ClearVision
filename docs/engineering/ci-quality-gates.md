# CI And Test Quality Lanes

ClearVision uses one test-classification source and three cumulative quality lanes:

```text
PR -> Nightly -> Release / Manual
```

A green earlier lane is a prerequisite, not a substitute for a later lane.

## Authoritative Classification

The authoritative source is `ClearVision.Testing.TestClassificationAttribute` on every xUnit test class or method. The attribute is converted into xUnit Traits during discovery, so local scripts, CI filters, TRX output, and governance reports select the same metadata.

Every formal xUnit test declares:

- `Domain`
- `Purpose`
- `Lane`
- `EvidenceType`
- `OracleType`
- `ResourceRequirement`
- `ExpectedDuration`
- `FlakyPolicy`
- `Owner`

Optional `Suites`, `SeedControl`, and `PerformanceProfile` traits support named regression suites and semantic governance without maintaining test-class lists in scripts.

`quality/test-gates.json` defines gate names, projects, Trait filters, and lane entry sets. It contains no method or class list. Renaming a classified test therefore preserves gate membership; deleting or mistyping the final matching classification makes governance fail because the gate resolves to zero tests.

## Semantic Rules

| Purpose | Required evidence |
| --- | --- |
| Regression | Contract, historical behavior, error code, compatibility path, or confirmed bug oracle. |
| Accuracy | Independent mathematical truth, annotated ground truth, or governed golden data. `IsSuccess` alone is invalid. |
| Determinism | Equal output for equal input, parameters, environment, and controlled seed. `SeedControl` is mandatory. |
| Stability | Statistical or metamorphic bounds over noise, scale, bit depth, boundary, perturbation, or multi-seed inputs. |
| Robustness | Behavior-preserving or fail-closed evidence across adverse inputs and transformations. |
| Performance | Declared data scale, warmup, measured iterations, statistic, environment/profile, and budget. |
| Integration | Explicit resource/system boundary evidence. External resources must be declared. |
| Smoke | Package/install/instantiation evidence; it does not claim numerical accuracy. |

The source scanner also rejects parameterless `new Random()` and `Random.Shared` in formal test sources.

## Governance

Run from the repository root:

```powershell
& ".\scripts\run-test-governance.ps1" -FailOnWarning
```

The Roslyn-based audit scans every `[Fact]` and `[Theory]`, validates all required fields, enforces semantic constraints, evaluates every filter in `quality/test-gates.json`, and writes JSON/Markdown reports. It fails closed for:

- unclassified or multiply classified tests;
- method/class classifications that xUnit would merge into contradictory Traits;
- Accuracy without an independent oracle or with only a success assertion;
- Determinism without declared seed control;
- Performance in PR or without a performance profile and budget oracle;
- external resources in PR;
- unknown projects, duplicate gate names, invalid filters, empty gates, or lane conflicts;
- lane definitions that reference missing gates;
- uncontrolled random sources.

Gate `minimumTotalTests` is an existence guard only. It is not a quality score and is intentionally `1`; the semantic metadata and oracle rules provide the quality evidence.

## PR Lane

The PR lane contains fast, deterministic, blocking tests without device/model/database dependencies:

- build and static governance;
- `Lane=Pr` Product and Desktop tests;
- core contract/regression tests;
- the independent calibration mathematical oracle representative;
- Stage 1/2 regression through `Suite=Stage12Regression`;
- OperatorLibrary package smoke through `Suite=OperatorLibrarySmoke`;
- FrontendV2 and UI contract unit tests.

Run locally after required package/UI dependencies are ready:

```powershell
& ".\scripts\run-test-quality-lane.ps1" -Lane Pr
```

For focused development, `-SkipUi` and `-SkipOperatorLibrarySmoke` are explicit local diagnostics only. A final PR result must not use those switches.

PR does not run performance profiles, Playwright E2E, real/virtual PLC resources, Docker databases, physical devices, or large-model evaluation.

## Nightly Lane

Nightly first re-runs PR prerequisites, then adds:

- full Nightly Accuracy;
- multi-round Determinism with explicit seed policy;
- noise/perturbation Stability and preprocessing Robustness;
- Nightly Integration and full image-contract coverage;
- standard measurement/detection performance profiles and remaining performance tests;
- heavier model, dataset, quality-suite, and UI E2E work owned by scheduled CI.

```powershell
& ".\scripts\run-test-quality-lane.ps1" -Lane Nightly
```

CI schedules the Nightly lane daily. Measurement and detection performance scripts still own their warmup/measurement environment and validate fresh structured reports.

## Release / Manual Lane

Release / Manual runs the full industrial profile with acceptance performance budgets, package smoke, complete semantic gates, calibration integration, matching/preprocessing evidence, and virtual PLC gates. It then runs the authoritative `product-release-manual` aggregate so every classified external-resource test, including Docker database coverage, is represented in the structured lane report. Physical PLC/camera/device evidence, model/assets/SBOM/delivery checks, package/source identity, and human sign-off remain explicit manual responsibilities.

```powershell
& ".\scripts\run-test-quality-lane.ps1" `
  -Lane ReleaseManual `
  -AcknowledgeManualRequirements
```

Without `-AcknowledgeManualRequirements`, the lane exits with a pending/manual code even if all automated gates pass. The acknowledgement is an assertion that the external evidence was actually reviewed, not a bypass.

Release tags are additionally bound to the exact approved SHA through the repository variable `CV_RELEASE_MANUAL_APPROVED_SHA`. Tag publication fails when the variable is absent or differs from `github.sha`.

## Reports And Exit Codes

`run-classified-test-gate.ps1` writes:

- one TRX file;
- one `*.gate.json` summary containing gate name, filter, lane, counters, duration, and exit code.

`run-test-quality-lane.ps1` writes timestamped logs, TRX files, governance/performance reports, `summary.json`, and `summary.md`. Any failed automated step returns non-zero; unacknowledged manual requirements return `3`.

## Serial Test Rule

All .NET execution goes through `scripts/run-dotnet-test-serial.ps1`. Never run multiple `dotnet test` processes for the same `.csproj`; after a successful build, follow-up runs may use `-NoBuild -NoRestore`.
