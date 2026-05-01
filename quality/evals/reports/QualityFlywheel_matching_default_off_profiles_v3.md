# Matching Default-Off Profiles v3

GeneratedAtUtc: `2026-05-01T06:08:52+00:00`
ClaimBoundary: `Public HPatches evidence only; these profiles are opt-in candidates and do not change product defaults.`

## Profile Gate

- Status: `default-off-candidates-ready`
- Product default change: `False`
- Primary candidate: `OrbFeatureMatch/replay_safe_dense_strict`
- Fallback candidate: `AkazeFeatureMatch/default_v3`
- Release gate status: `blocked-missing-field-replay`

## Candidates

| Operator | Profile | Status | Full pass delta | P95 pos delta | P95 corner delta | Runtime delta ms | Replay pass | Decision |
|---|---|---|---:|---:|---:|---:|---:|---|
| AkazeFeatureMatch | default_v3 | default_off_ready_no_accuracy_delta | 0 | 0 | 0 | -2489.317 | 13/20 | keep-default-off-neutral-candidate |
| OrbFeatureMatch | replay_safe_dense_strict | default_off_ready_metric_gain_runtime_tradeoff | 0 | -32.337 | -16.952 | 377.073 | 16/20 | keep-default-off-metric-gain-candidate |

## Required Before Default-On

- run release/field replay with the candidate profile explicitly enabled
- show no full pass, P95 position, or P95 corner regression
- meet the signed runtime budget for ORB replay_safe_dense_strict
