# ClearVision Operator Precision Benchmark

- Label: `baseline`
- Source SHA: `ce266626e0bec0a8cd4a68c11b176df95e8cb482`
- Dataset: `clearvision-operator-precision-synthetic-v1` `1.0.0` manifest SHA `ec29b22e0bbda301a0fef4b23375413d7a569240f8c4abf06d45318493938920` generated-data SHA `85389261aa17bb99ecd70c51dfcb18545a0a417417b0b16ef33f07c42174ae72`
- Seed: `20260715`
- Model SHA: `5ee8135365428e700eba8c3eb3cd66b0d6e697f9f5df9d23fcb931655378fc90`
- Preprocess fingerprint: `c9e4e703eb2bdeab37496422a47243afc424d84769fa2ecb7dd75787361c86a8`
- Harness: `9df0fa73db4b2a94a748f377147ded3511162746` / source SHA `58a1fba586e60e33a822b29175d3069423c956d62d9855f6c037870a36e6120e` / dirty `False`
- Environment: `.NET 8.0.19` / `Microsoft Windows 10.0.22000` / `X64`

> Synthetic mathematical and preprocessing-contract evidence only. This report is not field validation, release readiness, E4, or commercial-grade accuracy evidence.

## Metrics

| Domain | Algorithm | Split | Cases | Bias | RMSE | P95 error | Failure | Ambiguity | Outlier | Latency P50/P95 ms | Allocation B/case |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Caliper | LegacyGradientCentroid | validation | 51 | -0.462176 | 2.802441 | 1.070159 | 0 | 0.058824 | 0.058824 | 0.006912 / 0.007526 | 1313 |
| Caliper | LegacyGradientCentroid | test | 135 | -0.898781 | 3.605459 | 13.56042 | 0 | 0.02963 | 0.066667 | 0.006322 / 0.007379 | 1312 |
| Caliper | Quadratic | validation | 51 | -0.528271 | 2.829877 | 0.950598 | 0 | 0.058824 | 0.058824 | 0.006248 / 0.006345 | 1313 |
| Caliper | Quadratic | test | 135 | -0.938508 | 3.604106 | 13.501152 | 0 | 0.02963 | 0.074074 | 0.006213 / 0.006591 | 1312 |
| Caliper | GaussianDerivative | validation | 51 | 0.022006 | 0.15182 | 0.232792 | 0 | 0.039216 | 0 | 0.027168 / 0.032165 | 2881 |
| Caliper | GaussianDerivative | test | 135 | -0.415253 | 2.447117 | 0.487604 | 0 | 0.066667 | 0.037037 | 0.018699 / 0.030695 | 2880 |
| Caliper | Erf | validation | 51 | -0.548954 | 2.809191 | 0.307398 | 0 | 0.058824 | 0.039216 | 2.421879 / 2.514728 | 2748833 |
| Caliper | Erf | test | 135 | -0.91949 | 3.618579 | 13.719656 | 0 | 0.02963 | 0.081481 | 2.415094 / 2.488149 | 2748832 |
| Circle | AlgebraicL2 | validation | 50 | -0.407089 | 2.775364 | 6.550431 | 0 | 0.42 | 0 | 0.013808 / 0.014508 | 6193 |
| Circle | AlgebraicL2 | test | 140 | -0.187351 | 1.994612 | 3.922396 | 0 | 0.392857 | 0 | 0.013805 / 0.01493 | 6192 |
| Circle | OrthogonalHuber | validation | 50 | -0.133727 | 1.331119 | 2.798473 | 0 | 0.18 | 0.042917 | 0.249262 / 0.275849 | 205492 |
| Circle | OrthogonalHuber | test | 140 | 0.072336 | 1.2727 | 1.69618 | 0 | 0.157143 | 0.045313 | 0.247879 / 0.269077 | 204176 |
| Circle | OrthogonalWelsch | validation | 50 | -0.105224 | 1.18884 | 2.043661 | 0 | 0.12 | 0.054792 | 0.258314 / 0.298332 | 189447 |
| Circle | OrthogonalWelsch | test | 140 | 0.070351 | 1.207139 | 1.256742 | 0 | 0.114286 | 0.054762 | 0.268151 / 0.283841 | 197036 |
| Line | L2 | validation | 50 | 0.026238 | 0.589197 | 1.352649 | 0 | 0.4 | 0 | 0.025634 / 0.027687 | 2745 |
| Line | L2 | test | 140 | 0.004843 | 0.621696 | 1.457463 | 0 | 0.4 | 0 | 0.025479 / 0.027732 | 2744 |
| Line | Huber | validation | 50 | -0.01714 | 0.100035 | 0.239098 | 0 | 0 | 0.076 | 0.111275 / 0.131411 | 110751 |
| Line | Huber | test | 140 | -0.011852 | 0.164644 | 0.320176 | 0 | 0.007143 | 0.081548 | 0.109044 / 0.116983 | 115747 |
| Line | Welsch | validation | 50 | -0.012074 | 0.070418 | 0.121553 | 0 | 0 | 0.078667 | 0.14022 / 0.175234 | 113320 |
| Line | Welsch | test | 140 | -0.003164 | 0.081725 | 0.178097 | 0 | 0 | 0.086429 | 0.139771 / 0.150878 | 114547 |
| MeasurementUncertainty | ResidualHeuristic | test | 280 | 0.030242 | 0.907434 | 1.25193 | 0 | 0.082143 | 0.06343 | 0.000031 / 0.000036 | 0 |
| MeasurementUncertainty | Covariance | test | 280 | 0.030242 | 0.907434 | 1.25193 | 0 | 0.082143 | 0.06343 | 0.000002 / 0.000019 | 0 |
| Anomaly | TraditionalLabGradient | test | 48 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00691 / 0.009213 | 1129 |
| Anomaly | OnnxManifestPreprocess | test | 48 | 0 | 0 | 0 | 0 | 0 | 0 | 0.073025 / 0.087691 | 3489 |
| AnomalyPreprocess | ManifestDeclaredRgbFloat01 | contract | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0.069585 / 0.082677 | 3491 |

## Decisions

| Domain | Baseline | Winner | Baseline score | Winner score | Adopted | Reason |
|---|---|---|---:|---:|---|---|
| Caliper | LegacyGradientCentroid | GaussianDerivative | 4.460835 | 0.776769 | True | Candidate reduced combined RMSE/P95 without material failure-rate regression. |
| Circle | AlgebraicL2 | OrthogonalWelsch | 13.525795 | 4.432501 | True | Candidate reduced combined RMSE/P95 without material failure-rate regression. |
| Line | L2 | Welsch | 5.941846 | 0.191971 | True | Candidate reduced combined RMSE/P95 without material failure-rate regression. |
| MeasurementUncertainty | ResidualHeuristic | ResidualHeuristic | 0.214657 | 0.214657 | False | Covariance did not improve 68%/95% coverage calibration. |
| Anomaly | TraditionalLabGradient | TraditionalLabGradient | 0 | 0 | False | Traditional mode remains the compatibility default; manifest binding is a fail-closed identity upgrade, not a default feature switch. |

## Reproduction

```powershell
& "./scripts/run-operator-precision-benchmark.ps1" -Profile acceptance -Label baseline
```
