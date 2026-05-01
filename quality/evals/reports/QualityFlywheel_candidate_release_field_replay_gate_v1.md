# Candidate Release/Field Replay Gate v1

GeneratedAtUtc: `2026-05-01T06:09:01+00:00`
GateStatus: `standards-signed-replay-required`
ProductDefaultChange: `False`
DefaultOnReady: `False`
ClaimBoundary: `Signed candidate gate standards only; no real release/field replay packet is attached and no product default changes are made.`

## Signed Standards

| Operator | Profile | Standard | Current evidence | Field replay |
|---|---|---|---|---|
| AnomalyDetection | mvtec_lite_v2 | anomaly_mvtec_lite_v2_fp_acceptance_2026_05_01 | True | True |
| OrbFeatureMatch | replay_safe_dense_strict | orb_replay_safe_dense_strict_runtime_budget_2026_05_01 | True | True |

## Checks

### AnomalyDetection / mvtec_lite_v2

| Check | Actual | Limit | Pass |
|---|---:|---:|---|
| fp_delta_within_signed_budget | 3 | <= 3 | True |
| normal_false_positive_rate_within_signed_budget | 0.0909 | <= 0.1 | True |
| precision_floor | 0.9583 | >= 0.95 | True |
| recall_delta_floor | 0.7471 | >= 0.1 | True |
| critical_false_positive_review_required | 0 | <= 0 | True |

### OrbFeatureMatch / replay_safe_dense_strict

| Check | Actual | Limit | Pass |
|---|---:|---:|---|
| runtime_delta_ms_per_case_within_budget | 4.7134 | <= 5 | True |
| runtime_delta_percent_within_budget | 21.812 | <= 25 | True |
| candidate_mean_runtime_within_budget | 26.3227 | <= 30 | True |
| full_pass_delta_not_negative | 0 | >= 0 | True |
| p95_position_delta_not_worse | -32.3368 | <= 0 | True |
| p95_corner_delta_not_worse | -16.9519 | <= 0 | True |

## Required Replay Packet

| Area | Fields |
|---|---|
| Minimum scope | candidate profile explicitly enabled, pinned baseline replay from the same build and hardware class, sanitized release/field manifest without raw customer paths, per-case pass/fail, FP/FN, runtime, and fallback diagnostics |
| Anomaly | normalImageCount, imageFalsePositive, criticalFalsePositiveCount, imagePrecision, imageRecall, fallbackModeResult |
| ORB | hardwareProfileId, caseCount, baselineMeanRuntimeMsPerCase, candidateMeanRuntimeMsPerCase, runtimeDeltaMsPerCase, runtimeDeltaPercent, p95PositionErrorPxDelta, p95CornerErrorPxDelta |
