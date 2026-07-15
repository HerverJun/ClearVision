# Operator Precision Phase 5 Comparison

- Baseline SHA: `ce266626e0bec0a8cd4a68c11b176df95e8cb482`
- After SHA: `9df0fa73db4b2a94a748f377147ded3511162746`
- Dataset manifest SHA: `ec29b22e0bbda301a0fef4b23375413d7a569240f8c4abf06d45318493938920`
- Generated input/truth SHA: `85389261aa17bb99ecd70c51dfcb18545a0a417417b0b16ef33f07c42174ae72`
- Seed: `20260715`
- Model SHA: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `c9e4e703eb2bdeab37496422a47243afc424d84769fa2ecb7dd75787361c86a8`
- Harness SHA: `58a1fba586e60e33a822b29175d3069423c956d62d9855f6c037870a36e6120e` (commit `9df0fa73db4b2a94a748f377147ded3511162746`; dirty=False)
- Identity check: baseline and after used the same generated input/truth, harness, model, preprocessing identity, seed and runtime environment.

> Kernel-level synthetic mathematical and preprocessing-contract evidence only. This is not E4, end-to-end field accuracy, Release Ready, Field Verified, commercial-grade, or production-site evidence.

| Domain | Baseline | Evaluated path | RMSE improvement | P95 error improvement | Failure delta | Ambiguity delta | P95 latency | Allocation | Budget | Adopted | Conformance |
|---|---|---|---:|---:|---:|---:|---:|---:|---|---|---|
| Caliper | `LegacyGradientCentroid` | `IntegratedGaussianDerivative` | -379.49822% | -274.822118% | 0 | -0.0296296296296296 | 0.043705 ms | 27013 B/case | False | False | RejectedIntegrationRegression |
| Circle | `AlgebraicL2` | `ProductionOrthogonalWelsch` | 39.479997% | 67.959834% | 0 | -0.278571428571429 | 0.302913 ms | 198270 B/case | Passed | True | ExactAccuracy |
| Line | `L2` | `ProductionWelsch` | 86.854588% | 87.780361% | 0 | -0.4 | 0.173719 ms | 115850 B/case | Passed | True | ExactAccuracy |

## Rejected candidates

- **Caliper / GaussianDerivative formal integration:** The validation-selected localizer regressed end-to-end test RMSE/P95 when seeded by the formal detector and pair selection; its allocation also exceeded the written diagnostic budget, so it remains out of the formal operator.
- **Caliper / Quadratic:** No repeatable accuracy improvement over the legacy baseline.
- **Caliper / Erf:** No accuracy improvement and materially higher P95 latency/allocation.
- **MeasurementUncertainty / Covariance as calibrated confidence:** 68%/95% calibration did not improve; retained only as UncalibratedCovariance evidence.
- **Anomaly / ONNX embedding as default:** Traditional mode remains the compatibility default; manifest/model/fingerprint binding is a fail-closed governance upgrade.

## Uncertainty and anomaly conclusions

- Residual heuristic coverage: 68%=0.896428571428571, 95%=0.953571428571429.
- Raw covariance coverage: 68%=0.55, 95%=0.785714285714286. It remains `UncalibratedCovariance`.
- Anomaly traditional mode remains the default. ONNX preprocessing reference RMSE is 0, and mismatched identity is required to fail closed.

## Reproduction

```powershell
& ".\\scripts\\run-operator-precision-benchmark.ps1" -Profile acceptance -Label after -ResultsDirectory "quality\\evals\\reports"
& ".\\scripts\\compare-operator-precision-benchmarks.ps1"
```
