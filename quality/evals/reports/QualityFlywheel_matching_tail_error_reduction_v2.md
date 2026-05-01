# Matching Tail Error Reduction v2

GeneratedAtUtc: `2026-04-30T08:29:18+00:00`
ClaimBoundary: `Public HPatches tail-error triage only; this is not production-line signoff.`

## Operator Tail Summary

| Candidate | HPatches pass | P95 position | P95 corner | Viewpoint failures | Large-viewpoint failures | Center-gate candidates |
|---|---:|---:|---:|---:|---:|---:|
| AkazeFeatureMatch | 90/116 (0.775862) | 321.632 | 9.247 | 23/59 | 22 | 16 |
| OrbFeatureMatch | 90/116 (0.775862) | 267.972 | 8.454 | 24/59 | 23 | 19 |
| PlanarMatching(ORB) | 70/116 (0.603448) | 114.786 | 10.31 | 44/59 | 21 | 13 |
| PlanarMatching(AKAZE) | 70/116 (0.603448) | 118.723 | 8.254 | 43/59 | 20 | 16 |

## Tail Buckets

### AkazeFeatureMatch

| Bucket | Cases | P95 position | Max position | P95 max corner | Mean inlier ratio | Mean reproj | Max reproj | Mean area ratio | Mean corners inside |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| extreme_viewpoint_crop | 22 | 378.274 | 400.798 | - | 0.937 | 0.809 | 5.089 | 1.068 | 0.636 |
| illumination_residual | 2 | 376.317 | 376.317 | - | 0.936 | 0.328 | 3.19 | 1.001 | 1 |
| insufficient_correspondences | 1 | 230.107 | 230.107 | - | 0 | - | - | 0 | 0 |
| projected_area_drift | 1 | 402.336 | 402.336 | - | 0.833 | 1.046 | 2.135 | 2.439 | 1 |

### OrbFeatureMatch

| Bucket | Cases | P95 position | Max position | P95 max corner | Mean inlier ratio | Mean reproj | Max reproj | Mean area ratio | Mean corners inside |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| extreme_viewpoint_crop | 22 | 279.156 | 307.23 | - | 0.957 | 1.115 | 5.6 | 1.069 | 0.636 |
| illumination_residual | 1 | 259.288 | 259.288 | - | 0.94 | 0.874 | 5.043 | 1.001 | 1 |
| insufficient_correspondences | 1 | 0.5 | 0.5 | - | 0 | - | - | 0 | 0 |
| projected_area_drift | 1 | 338.707 | 338.707 | - | 0.632 | 1.579 | 6.288 | 2.131 | 1 |
| reprojection_outlier | 1 | 358.203 | 358.203 | - | 0.902 | 0.988 | 8.913 | 1.008 | 0 |

### PlanarMatching(ORB)

| Bucket | Cases | P95 position | Max position | P95 max corner | Mean inlier ratio | Mean reproj | Max reproj | Mean area ratio | Mean corners inside |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| extreme_viewpoint_crop | 23 | 88.113 | 117.416 | 10.31 | 0.917 | 1.052 | 4.967 | 1.024 | 0.609 |
| illumination_residual | 1 | 1.215 | 1.215 | 3.477 | 0.953 | 0.603 | 2.872 | 1 | 1 |
| insufficient_correspondences | 1 | 1000000 | 1000000 | - | 0 | 0 | 0 | 0 | 0 |
| localization_tail | 2 | 109.78 | 109.78 | 4.575 | 0.981 | 0.876 | 4.968 | 0.736 | 4 |
| partial_viewpoint_crop | 16 | 157.526 | 157.526 | 10.483 | 0.978 | 1.108 | 5.586 | 0.756 | 2.25 |
| projected_area_drift | 3 | 890.566 | 890.566 | 3668.533 | 0.774 | 0.975 | 3.127 | 2.198 | 2.333 |

### PlanarMatching(AKAZE)

| Bucket | Cases | P95 position | Max position | P95 max corner | Mean inlier ratio | Mean reproj | Max reproj | Mean area ratio | Mean corners inside |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| extreme_viewpoint_crop | 22 | 87.944 | 118.723 | 7.811 | 0.949 | 0.718 | 6.426 | 0.914 | 0.591 |
| illumination_residual | 2 | 153.71 | 153.71 | 361.617 | 0.998 | 0.35 | 3.844 | 0.726 | 1.5 |
| insufficient_correspondences | 1 | 1000000 | 1000000 | - | 0 | 0 | 0 | 0 | 0 |
| localization_tail | 3 | 108.15 | 108.15 | 1.559 | 0.989 | 0.644 | 4.571 | 0.682 | 4 |
| partial_viewpoint_crop | 16 | 158.076 | 158.076 | 8.355 | 0.979 | 0.868 | 5.405 | 0.788 | 2.25 |
| projected_area_drift | 2 | 221.663 | 221.663 | 6.867 | 0.967 | 0.906 | 4.836 | 1.471 | 2 |

## Next Actions

- Use the populated P95CornerErrorPx and bucket-level max-corner evidence to evaluate replay-safe profile candidates.
- Treat extreme_viewpoint_crop as geometry-tail triage first; current center-gate candidates need replay regression checks before any relaxed pass gate.
- Keep replay regression at zero before promoting stricter ratio, looser RANSAC, or multi-hypothesis homography profiles.
