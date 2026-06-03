# Evidence And Temporary Artifacts

ClearVision uses a few recurring artifact directories. Keep them predictable so CI, local triage and field trials do not leak files into the repository root.

| Path | Purpose | Commit policy |
| --- | --- | --- |
| `.tmp/` | Disposable local scratch space. | Do not commit generated contents. |
| `.tmp/publish-check/` | Allowed ad hoc publish/package verification output. | Clean after verification unless explicitly needed. |
| `.tmp/station-alpha-trial/` | Alpha trial CSV, notes, preflight and summary output. | Attach to evidence review; do not commit routine runs. |
| `.tmp/plc-regression/` | Virtual PLC regression TRX and smoke evidence. | Attach to gate results; do not commit routine runs. |
| `artifacts/` | CI or benchmark output. | Commit only curated baseline reports. |
| `test_results/` | Local quality/performance reports. | Commit only reviewed baseline artifacts. |
| `logs/` | Local app/runtime logs. | Never commit raw site logs. |

## Scripts

- `scripts/run-station-alpha-trial.ps1` writes metrics, notes, preflight JSON and summary markdown under `.tmp/station-alpha-trial/`.
- `scripts/run-tests-plc-regression.ps1 -Virtual` writes default virtual-PLC evidence under `.tmp/plc-regression/<timestamp>/`.
- `scripts/run-operator-library-industrial-gate.ps1` writes summary and gate artifacts under its configured artifact directory.
- `scripts/check-text-encoding.ps1` guards active user-facing UTF-8 text and common mojibake fragments.

## Release Evidence

External release bundles should include:

- TRX and coverage reports for Product/Desktop/Operator smoke tests,
- OperatorLibrary `.nupkg` and `.snupkg`,
- `ClearVision.OperatorLibrary/SBOM.md`,
- `ClearVision.OperatorLibrary/SBOM.spdx.json`,
- `ClearVision.OperatorLibrary/THIRD-PARTY-NOTICES.md`,
- dependency report from `ClearVision.OperatorLibrary/analyze-deps.ps1`,
- model release-gate manifests when external ONNX artifacts are used.
