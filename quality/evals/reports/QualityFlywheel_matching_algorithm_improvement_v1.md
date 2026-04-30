# Quality Flywheel Matching Algorithm Improvement v1

GeneratedAtUtc: `2026-04-29T15:20:42+00:00`
Accepted: `True`
ClaimBoundary: `准工业公开/替代证明；不声明真实产线工业验证完成。`

## Executive Summary

- A/B replay fixed `29` cases with `0` regressions.
- Matching viewpoint fixed `5` cases.
- `OrbFeatureMatch` is the v4 primary candidate; `AkazeFeatureMatch` remains the stable fallback.
- Remaining backlog: `27` HPatches cases, `25` fail on both Akaze/ORB.

## Candidate Results

| Operator | Profile | HPatches | Viewpoint | Replay | Mean error | P95 error | Runtime ms | Regressed |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| AkazeFeatureMatch | default_v3 | 90/116 | 36/59 | 13/20 | 54.341 | 321.632 | 8568.21 | 0 |
| OrbFeatureMatch | replay_safe_dense_strict | 90/116 | 35/59 | 16/20 | 45.006 | 267.972 | 3572.659 | 0 |

## Failure Backlog

| Taxonomy | Count |
|---|---:|
| extreme_viewpoint_crop | 24 |
| illumination_residual | 2 |
| insufficient_correspondences | 1 |

## Next Actions

- Use OrbFeatureMatch v4 as the next primary matching candidate.
- Prototype center-first localization for extreme viewpoint crop failures.
- Keep AkazeFeatureMatch v4 as a stable fallback candidate.
- After backlog triage, move Phase C SurfaceDefectDetection/AnomalyDetection into candidate execution.
