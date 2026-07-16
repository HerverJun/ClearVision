# Operator Precision Phase 5 Comparison

- Supplemental harness source SHA (both reports): `f7e9eea4022734ece1be602f36f97aa44e3226da`
- Dataset manifest SHA: `4a7518093f3e73db759c356c47a082bb13b69fa53e93b88b51ad7e7b6744e321`
- Generated input/truth SHA: `85389261aa17bb99ecd70c51dfcb18545a0a417417b0b16ef33f07c42174ae72`
- Seed: `20260715`
- Model SHA: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `c9e4e703eb2bdeab37496422a47243afc424d84769fa2ecb7dd75787361c86a8`
- Harness SHA: `0d8e14ae658d301b5b4f852f7b803fd03b2a0d879b7567f1406f476fb9359472` (commit `f7e9eea4022734ece1be602f36f97aa44e3226da`; dirty=False)
- Identity check: baseline and after used the same generated input/truth, harness, model, preprocessing identity, seed and runtime environment.

> Supplemental kernel-level synthetic mathematical and preprocessing-contract evidence only. This is not executable historical-product evidence, complete formal operator-path evidence, E4, end-to-end field accuracy, Release Ready, Field Verified, commercial-grade, or production-site evidence.

| Domain | Baseline | Evaluated path | RMSE improvement | P95 error improvement | Failure delta | Ambiguity delta | P95 latency | Allocation | Budget | Adopted | Conformance |
|---|---|---|---:|---:|---:|---:|---:|---:|---|---|---|
| Caliper | `LegacyGradientCentroid` | `IntegratedGaussianDerivative` | -379.49822% | -274.822118% | 0 | -0.0296296296296296 | 0.048834 ms | 27013 B/case | False | False | RejectedIntegrationRegression |
| Circle | `AlgebraicL2` | `ProductionOrthogonalWelsch` | 39.479997% | 67.959834% | 0 | -0.278571428571429 | 0.308434 ms | 198270 B/case | Passed | False | ExactAccuracy |
| Line | `L2` | `ProductionWelsch` | 86.854588% | 87.780361% | 0 | -0.4 | 0.163048 ms | 115850 B/case | Passed | False | ExactAccuracy |

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
