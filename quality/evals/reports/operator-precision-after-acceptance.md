# ClearVision Operator Precision Benchmark

- Label: `after`
- Source SHA: `97d25440bf540b3543ddbd687377daa7cdab2685`
- Dataset: `clearvision-operator-precision-synthetic-v1` `1.0.0` SHA `32495a03d7706969d9e33b18ef609d837844da6678bff9d08a5debbe02f379f5`
- Seed: `20260715`
- Model SHA: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `d4bf2ecf122f0e5b95c255812f7cea809cb333ad6fe7d37d1b5853c80fa4bde2`
- Environment: `.NET 8.0.19` / `Microsoft Windows 10.0.22000` / `X64`

> Synthetic mathematical and preprocessing-contract evidence only. This report is not field validation, release readiness, E4, or commercial-grade accuracy evidence.

## Metrics

| Domain | Algorithm | Bias | RMSE | P95 error | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Allocation B/case |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Caliper | LegacyGradientCentroid | -0.77612 | 3.38878 | 13.557994 | 0 | 0.05 | 0.066667 | 0.006663 / 0.006991 | 1312 |
| Caliper | Quadratic | -0.807945 | 3.391749 | 13.339212 | 0 | 0.05 | 0.070833 | 0.006077 / 0.006536 | 1312 |
| Caliper | GaussianDerivative | -0.224813 | 1.839532 | 0.502213 | 0 | 0.05 | 0.020833 | 0.007203 / 0.023798 | 2880 |
| Caliper | Erf | -0.805674 | 3.398419 | 13.676655 | 0 | 0.05 | 0.066667 | 2.449191 / 2.480086 | 2748832 |
| Circle | AlgebraicL2 | -0.24216 | 2.233493 | 5.081622 | 0 | 0.4 | 0 | 0.014445 / 0.0181 | 6192 |
| Circle | OrthogonalHuber | 0.046189 | 1.369758 | 2.247046 | 0 | 0.1625 | 0.045095 | 0.243158 / 0.266207 | 204724 |
| Circle | OrthogonalWelsch | 0.059174 | 1.302191 | 2.112935 | 0 | 0.120833 | 0.05434 | 0.261238 / 0.267277 | 194695 |
| Line | L2 | -0.004857 | 0.64176 | 1.538505 | 0 | 0.4 | 0 | 0.025574 / 0.026182 | 2744 |
| Line | Huber | -0.015482 | 0.146046 | 0.303077 | 0 | 0.008333 | 0.080243 | 0.109888 / 0.135139 | 115682 |
| Line | Welsch | -0.002829 | 0.075823 | 0.150419 | 0 | 0 | 0.08434 | 0.141873 / 0.160237 | 114159 |
| Caliper | ProductionGaussianDerivative | 0.00996 | 0.176866 | 0.391251 | 0 | 0 | 0.004167 | 0.014031 / 0.050118 | 5904 |
| Circle | ProductionOrthogonalWelsch | 0.059174 | 1.302191 | 2.112935 | 0 | 0.120833 | 0.05434 | 0.272268 / 0.28348 | 195938 |
| Line | ProductionWelsch | -0.002829 | 0.075823 | 0.150419 | 0 | 0 | 0.08434 | 0.154094 / 0.158075 | 115465 |
| MeasurementUncertainty | ResidualHeuristic | 0.008815 | 0.925984 | 1.597435 | 0 | 0.0875 | 0.061198 | 0.000032 / 0.000036 | 0 |
| MeasurementUncertainty | Covariance | 0.008815 | 0.925984 | 1.597435 | 0 | 0.0875 | 0.061198 | 0.000002 / 0.000037 | 0 |
| Anomaly | TraditionalLabGradient | 0 | 0 | 0 | 0 | 0 | 0 | 0.007325 / 0.008699 | 1129 |
| Anomaly | OnnxManifestPreprocess | 0 | 0 | 0 | 0 | 0 | 0 | 0.028705 / 0.035197 | 2857 |
| AnomalyPreprocess | ManifestDeclaredRgbFloat01 | 0 | 0 | 0 | 0 | 0 | 0 | 0.02946 / 0.034901 | 2859 |

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
& "./scripts/run-operator-precision-benchmark.ps1" -Profile acceptance -Label after
```
