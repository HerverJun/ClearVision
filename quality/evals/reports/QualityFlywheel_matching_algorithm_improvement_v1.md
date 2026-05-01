# Quality Flywheel Matching Algorithm Improvement v1

GeneratedAtUtc: `2026-04-30T11:24:58+00:00`
Accepted: `True`
ClaimBoundary: `准工业公开/替代证明；不声明真实产线工业验证完成。`

## Executive Summary

- A/B replay fixed `37` cases with `0` regressions.
- Matching viewpoint fixed `10` cases.
- `OrbFeatureMatch` center_only_v1 is the primary candidate profile; `AkazeFeatureMatch` center_only_v1 remains the stable fallback profile.
- Replay-safe profile gate has `2` promotion-ready default-off profiles; release gate status is `blocked-missing-field-replay`.
- Remaining backlog: `4` HPatches cases, `2` fail on both Akaze/ORB.

## Candidate Results

| Operator | Profile | HPatches | Viewpoint | Replay | Mean error | P95 error | Runtime ms | Regressed |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| AkazeFeatureMatch | center_only_projection_v1 | 114/116 | 58/59 | 19/20 | 4.176 | 2.015 | 6786.379 | 0 |
| OrbFeatureMatch | center_only_projection_v1 | 112/116 | 56/59 | 18/20 | 7.27 | 2.562 | 2954.6 | 0 |

## Failure Backlog

| Taxonomy | Count |
|---|---:|
| extreme_viewpoint_crop | 3 |
| insufficient_correspondences | 1 |

## Next Actions

- Use OrbFeatureMatch center_only_v1 as the next primary matching candidate profile.
- Keep AllowCenterOnlyProjection default-off in product paths until release/field replay gate signs off.
- Keep AkazeFeatureMatch center_only_v1 as a stable fallback candidate profile.
- Treat QualityFlywheel_matching_replay_safe_profile_candidates_v2 as the promotion-ready profile gate; do not use it as product-default approval.
- After backlog triage, move Phase C SurfaceDefectDetection/AnomalyDetection into candidate execution.
