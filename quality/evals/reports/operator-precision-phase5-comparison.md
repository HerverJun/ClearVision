# Operator Precision Phase 5 Comparison

- Baseline SHA: `ce266626e0bec0a8cd4a68c11b176df95e8cb482`
- After SHA: `97d25440bf540b3543ddbd687377daa7cdab2685`
- Dataset SHA: `32495a03d7706969d9e33b18ef609d837844da6678bff9d08a5debbe02f379f5`
- Seed: `20260715`
- Model SHA: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `d4bf2ecf122f0e5b95c255812f7cea809cb333ad6fe7d37d1b5853c80fa4bde2`
- Identity check: baseline and after used the same dataset, model, preprocessing identity, seed and runtime environment.

> Synthetic mathematical and preprocessing-contract evidence only. This is not E4, Release Ready, Field Verified, commercial-grade, or production-site accuracy evidence.

| Domain | Baseline | Production winner | RMSE improvement | P95 error improvement | Failure delta | Ambiguity delta | P95 latency | Allocation | Conformance |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| Caliper | `LegacyGradientCentroid` | `ProductionGaussianDerivative` | 94.780834% | 97.114241% | 0 | -0.05 | 0.050118 ms | 5904 B/case | NoDegradation |
| Circle | `AlgebraicL2` | `ProductionOrthogonalWelsch` | 41.697092% | 58.420071% | 0 | -0.279166666666667 | 0.28348 ms | 195938 B/case | ExactAccuracy |
| Line | `L2` | `ProductionWelsch` | 88.185166% | 90.223009% | 0 | -0.4 | 0.158075 ms | 115465 B/case | ExactAccuracy |

## Rejected candidates

- **Caliper / Quadratic:** No repeatable accuracy improvement over the legacy baseline.
- **Caliper / Erf:** No accuracy improvement and materially higher P95 latency/allocation.
- **MeasurementUncertainty / Covariance as calibrated confidence:** 68%/95% calibration did not improve; retained only as UncalibratedCovariance evidence.
- **Anomaly / ONNX embedding as default:** Traditional mode remains the compatibility default; manifest/model/fingerprint binding is a fail-closed governance upgrade.

## Uncertainty and anomaly conclusions

- Residual heuristic coverage: 68%=0.891666666666667, 95%=0.954166666666667.
- Raw covariance coverage: 68%=0.566666666666667, 95%=0.791666666666667. It remains `UncalibratedCovariance`.
- Anomaly traditional mode remains the default. ONNX preprocessing reference RMSE is 0, and mismatched identity is required to fail closed.

## Reproduction

```powershell
& ".\\scripts\\run-operator-precision-benchmark.ps1" -Profile acceptance -Label after -ResultsDirectory "quality\\evals\\reports"
& ".\\scripts\\compare-operator-precision-benchmarks.ps1"
```
