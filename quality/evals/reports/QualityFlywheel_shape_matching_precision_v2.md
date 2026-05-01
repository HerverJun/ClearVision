# Shape Matching Precision v2

GeneratedAtUtc: `2026-04-30T08:11:24+00:00`
Accepted: `True`
ClaimBoundary: `This is reproducible public-protocol and semi-synthetic precision evidence, not real production field sign-off.`

## Summary

| Metric | Value |
| --- | ---: |
| Operators | 4 |
| Total cases | 209 |
| Passed | 209 |
| Failed | 0 |
| Overall pass rate | 1.0 |

## Leaderboard

| Operator | Cases | Passed | Failed | Pos P95 px | Angle mean deg | Scale mean | Min score margin | Neg FP rate | Occlusion cases | Source |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TemplateMatching | 32 | 32 | 0 | 0.166175 | 0 | 0 | 0.249922 | - | 0 | quality/evals/reports/TemplateMatching_public_bridge_candidate_replay_v2.json |
| ShapeMatching | 36 | 36 | 0 | 0.183315 | 0.055556 | 0 | 0.551032 | 0 | 0 | quality/evals/reports/ShapeMatching_geometric_dataset_candidate_replay_v2.json |
| GradientShapeMatch | 117 | 117 | 0 | 1.08284 | 0.564103 | - | 1.73077 | 0 | 13 | quality/evals/reports/GradientShapeMatch_baseline.json |
| PyramidShapeMatch | 24 | 24 | 0 | 12.8062 | - | - | 16.8393 | 0 | 0 | quality/evals/reports/PyramidShapeMatch_contract_baseline.json |

## Pose Coverage

| Operator | Rotation cases | Rotation pass rate | Scale cases | Scale pass rate | Pyramid >=3 cases | Max pyramid levels | Position metric cases | Angle metric cases | Scale metric cases |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| TemplateMatching | 6 | 1 | 4 | 1 | 8 | 3 | 32 | 32 | 32 |
| ShapeMatching | 6 | 1 | 6 | 1 | 0 | 0 | 36 | 36 | 36 |
| GradientShapeMatch | 26 | 1 | 0 | - | 0 | 0 | 117 | 117 | 0 |
| PyramidShapeMatch | 0 | - | 0 | - | 0 | 0 | 14 | 0 | 0 |

## Profile Decision

- Primary: `ShapeMatching` / `geometric_dataset_precision_v2` - Best current source for pose labels with position, angle, scale, multi-target, origin, and blank-negative checks.
- Fallback: `GradientShapeMatch` / `contract_baseline` - Best current fallback evidence for rotation and occlusion-style shape scenes while ShapeMatching remains the scale-aware precision profile.
- TemplateMatching now includes bounded pose-search replay for small/medium rotation and 0.9..1.1 scale, with angle/scale error metrics reported from the v2 bridge.

## Gates

- All gates passed.
