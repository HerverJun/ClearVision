# Matching Failure Backlog v1

GeneratedAtUtc: `2026-05-01T03:28:42+00:00`
ClaimBoundary: `准工业公开 HPatches/replay backlog；不是真实产线签核。`

## Summary

| Metric | Value |
|---|---:|
| Remaining cases | 4 |
| Both operators failed | 2 |
| Viewpoint cases | 3 |
| Illumination cases | 1 |

## Taxonomy

| Taxonomy | Count |
|---|---:|
| extreme_viewpoint_crop | 3 |
| insufficient_correspondences | 1 |

## Cases

| Severity | Case | Type | Taxonomy | Both failed | Best current | Akaze error | ORB error | Next action |
|---|---|---|---|---|---|---:|---:|---|
| P1 | v_astronautis_1_2 | viewpoint | extreme_viewpoint_crop | False | AkazeFeatureMatch | 0.231 | 307.23 | Prototype center-first localization gate: permit heavily cropped projected quadrilaterals only when center, inliers, reprojection, and area remain stable. |
| P1 | v_charing_1_2 | viewpoint | extreme_viewpoint_crop | False | AkazeFeatureMatch | 0.914 | 338.707 | Prototype center-first localization gate: permit heavily cropped projected quadrilaterals only when center, inliers, reprojection, and area remain stable. |
| P2 | v_churchill_1_2 | viewpoint | extreme_viewpoint_crop | True | OrbFeatureMatch | 192.562 | 143.408 | Prototype center-first localization gate: permit heavily cropped projected quadrilaterals only when center, inliers, reprojection, and area remain stable. |
| P2 | i_pool_1_2 | illumination | insufficient_correspondences | True | OrbFeatureMatch | 230.107 | 0.5 | Increase texture support or add fallback detector profile only if replay gate remains stable. |
