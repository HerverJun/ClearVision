# ClearVision Operator Precision Benchmark

- Label: `baseline`
- Source SHA: `ce266626e0bec0a8cd4a68c11b176df95e8cb482`
- Dataset: `clearvision-operator-precision-synthetic-v1` `1.0.0` SHA `32495a03d7706969d9e33b18ef609d837844da6678bff9d08a5debbe02f379f5`
- Seed: `20260715`
- Model SHA: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `d4bf2ecf122f0e5b95c255812f7cea809cb333ad6fe7d37d1b5853c80fa4bde2`
- Environment: `.NET 8.0.19` / `Microsoft Windows 10.0.22000` / `X64`

> Synthetic mathematical and preprocessing-contract evidence only. This report is not field validation, release readiness, E4, or commercial-grade accuracy evidence.

## Metrics

| Domain | Algorithm | Bias | RMSE | P95 error | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Allocation B/case |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Caliper | LegacyGradientCentroid | -0.77612 | 3.38878 | 13.557994 | 0 | 0.05 | 0.066667 | 0.006727 / 0.006969 | 1312 |
| Caliper | Quadratic | -0.807945 | 3.391749 | 13.339212 | 0 | 0.05 | 0.070833 | 0.00618 / 0.007139 | 1312 |
| Caliper | GaussianDerivative | -0.224813 | 1.839532 | 0.502213 | 0 | 0.05 | 0.020833 | 0.006826 / 0.023574 | 2880 |
| Caliper | Erf | -0.805674 | 3.398419 | 13.676655 | 0 | 0.05 | 0.066667 | 2.435834 / 2.464258 | 2748832 |
| Circle | AlgebraicL2 | -0.24216 | 2.233493 | 5.081622 | 0 | 0.4 | 0 | 0.014366 / 0.015176 | 6192 |
| Circle | OrthogonalHuber | 0.046189 | 1.369758 | 2.247046 | 0 | 0.1625 | 0.045095 | 0.242436 / 0.253847 | 204724 |
| Circle | OrthogonalWelsch | 0.059174 | 1.302191 | 2.112935 | 0 | 0.120833 | 0.05434 | 0.265758 / 0.270863 | 194695 |
| Line | L2 | -0.004857 | 0.64176 | 1.538505 | 0 | 0.4 | 0 | 0.02599 / 0.026886 | 2744 |
| Line | Huber | -0.015482 | 0.146046 | 0.303077 | 0 | 0.008333 | 0.080243 | 0.11054 / 0.120904 | 115682 |
| Line | Welsch | -0.002829 | 0.075823 | 0.150419 | 0 | 0 | 0.08434 | 0.13917 / 0.146493 | 114159 |
| MeasurementUncertainty | ResidualHeuristic | 0.008815 | 0.925984 | 1.597435 | 0 | 0.0875 | 0.061198 | 0.000031 / 0.000037 | 0 |
| MeasurementUncertainty | Covariance | 0.008815 | 0.925984 | 1.597435 | 0 | 0.0875 | 0.061198 | 0.000002 / 0.000038 | 0 |
| Anomaly | TraditionalLabGradient | 0 | 0 | 0 | 0 | 0 | 0 | 0.008955 / 0.010313 | 1129 |
| Anomaly | OnnxManifestPreprocess | 0 | 0 | 0 | 0 | 0 | 0 | 0.026901 / 0.038321 | 2697 |
| AnomalyPreprocess | ManifestDeclaredRgbFloat01 | 0 | 0 | 0 | 0 | 0 | 0 | 0.026468 / 0.028409 | 2699 |

## Decisions

| Domain | Baseline | Winner | Baseline score | Winner score | Adopted | Reason |
|---|---|---|---:|---:|---|---|
| Caliper | LegacyGradientCentroid | GaussianDerivative | 17.446773 | 2.841745 | True | Candidate reduced combined RMSE/P95 without material failure-rate regression. |
| Circle | AlgebraicL2 | OrthogonalWelsch | 11.315115 | 4.623459 | True | Candidate reduced combined RMSE/P95 without material failure-rate regression. |
| Line | L2 | Welsch | 6.180265 | 0.226242 | True | Candidate reduced combined RMSE/P95 without material failure-rate regression. |
| MeasurementUncertainty | ResidualHeuristic | ResidualHeuristic | 0.2093 | 0.2093 | False | Covariance did not improve 68%/95% coverage calibration. |
| Anomaly | TraditionalLabGradient | TraditionalLabGradient | 0 | 0 | False | Traditional mode remains the compatibility default; manifest binding is a fail-closed identity upgrade, not a default feature switch. |

## Reproduction

```powershell
& "./scripts/run-operator-precision-benchmark.ps1" -Profile acceptance -Label baseline
```
