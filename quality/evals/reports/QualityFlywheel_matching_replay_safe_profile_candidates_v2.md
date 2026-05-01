# Matching Replay-Safe Profile Candidates v2

GeneratedAtUtc: `2026-04-30T11:24:57+00:00`
ClaimBoundary: `Public HPatches profile triage only; no default profile is promoted unless replay, pass count, P95 position, and P95 corner all avoid regression.`

## Profile Gate

- Status: `promotion-ready-default-off`
- Product default change: `False`
- Primary candidate: `OrbFeatureMatch/center_only_projection_v1`
- Fallback candidate: `AkazeFeatureMatch/center_only_projection_v1`
- Release gate status: `blocked-missing-field-replay`

## Candidate Decisions

| Operator | Profile | Full pass | Replay pass delta | P95 pos delta | P95 corner delta | Runtime delta ms | Decision |
|---|---|---:|---:|---:|---:|---:|---|
| OrbFeatureMatch | center_only_projection_v1 | 112/116 | 2 (18/20) | -265.411 | 0 | -7.578 | promote-candidate |
| AkazeFeatureMatch | center_only_projection_v1 | 114/116 | 6 (19/20) | -319.617 | 0 | -81.453 | promote-candidate |
| OrbFeatureMatch | replay_safe_high_ratio | 90/116 | 0 (16/20) | 15.295 | -1.758 | 40.845 | hold-position-regression |
| OrbFeatureMatch | high_ratio_ransac6 | 90/116 | 0 (16/20) | 15.295 | -0.214 | -1.863 | hold-position-regression |
| OrbFeatureMatch | dense_ransac5 | 89/116 | -1 (15/20) | 0 | 0.973 | -2.756 | reject-replay-regression |
| OrbFeatureMatch | mid_ratio_ransac6 | 89/116 | -1 (15/20) | 11.184 | 2.441 | -5.226 | reject-replay-regression |
| AkazeFeatureMatch | partial_plane_low_detector_threshold | 89/116 | 0 (13/20) | -4.37 | -4.038 | 637.815 | reject-full-pass-regression |

## Baselines

| Operator | Full pass | P95 position | P95 corner | Runtime ms | Source |
|---|---:|---:|---:|---:|---|
| AkazeFeatureMatch | 90/116 | 321.632 | 9.247 | 6867.832 | quality/evals/reports/AkazeFeatureMatch_hpatches_candidate_v4.json |
| OrbFeatureMatch | 90/116 | 267.972 | 8.454 | 2962.178 | quality/evals/reports/OrbFeatureMatch_hpatches_candidate_v4.json |
