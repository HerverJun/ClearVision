# P2 Matching Residual Baseline

GeneratedAtUtc: `2026-04-26T16:41:58.7658772+00:00`

## Summary

CaseCount: 72
Passed: 72
Failed: 0
RuntimeMs: 1932.475

## Operators

| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryBytesAvg |
|---|---:|---:|---:|---:|---:|
| LocalDeformableMatching | 24 | 24 | 0 | 28.791 | 9876733 |
| PlanarMatching | 24 | 24 | 0 | 30.356 | 285161 |
| ShapeMatching | 24 | 24 | 0 | 21.372 | 39562277 |

## Scenarios

| Scenario | Cases | Passed | Failed | RuntimeMsAvg |
|---|---:|---:|---:|---:|
| Blank scene no-match contract | 8 | 8 | 0 | 26.923 |
| Blank scene rejection contract | 4 | 4 | 0 | 5.045 |
| Direct pose oracle | 8 | 8 | 0 | 11.138 |
| Feature homography identity oracle | 8 | 8 | 0 | 70.254 |
| Local deformation oracle | 8 | 8 | 0 | 84.403 |
| Low-feature no-match contract | 8 | 8 | 0 | 1.732 |
| Missing input failure contract | 4 | 4 | 0 | 0.154 |
| Missing template contract | 4 | 4 | 0 | 0.412 |
| Parameter validation contract | 8 | 8 | 0 | 0.117 |
| Perspective homography oracle | 8 | 8 | 0 | 18.208 |
| Rotation-scale oracle | 4 | 4 | 0 | 51.957 |
