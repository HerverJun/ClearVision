# Product Operator E2E Phase 5 Comparison

- Frozen executable product SHA: `ce266626e0bec0a8cd4a68c11b176df95e8cb482`
- After product SHA: `727414e2ca6bd5785aac3f2dbc68fb5b8badc369`
- Dataset generated SHA: `5d60098525547bf873b9e4618b3d7b5a08bf202164295dac9c2eff6f99c2507a`
- Harness program SHA: `572dfc2826d0eb597839442c0b2b2e49040348287fc01174288e18e803a57b9f`
- Old/current default conformance: aggregate accuracy, failure and diagnostic-summary fields match for Circle and Line validation/test rows; no per-case diagnostic fingerprint is claimed.
- Managed allocation is benchmark-thread only; process working-set/private-byte observations remain separate in the source reports.

| Domain | Baseline | Candidate | Test RMSE improvement | Test P95 improvement | Failure delta | P95 latency cost ms | Managed alloc cost B/case | Adopted | Reason |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| Circle | LegacyDefault | WelschOptIn | -8.204056% | -20.513562% | 0.013889 | -1.7503 | 173585.19 | False | RejectedByValidationAccuracyOrReliability |
| Line | L2Default | WelschOptIn | 0.985996% | 15.820713% | 0 | 0.7287 | 129262.09 | True | AcceptedOptInOnFormalProductPath |

## Claim boundary

Synthetic raster end-to-end product-operator evidence only. It is not E4, commercial-grade, field-accuracy, release-readiness, or production-site validation evidence.

## Reproduction

`& "./scripts/run-operator-product-e2e-evidence.ps1" -Profile acceptance -ResultsDirectory ".tmp/operator-product-e2e"`
