# P2 Matching Residual Baseline

GeneratedAtUtc: `2026-04-29T03:29:51.0563385+00:00`

## Summary

CaseCount: 72
Passed: 72
Failed: 0
RuntimeMs: 1838.195

## Operators

| Operator | Cases | Passed | Failed | RuntimeMsAvg | MemoryBytesAvg |
|---|---:|---:|---:|---:|---:|
| LocalDeformableMatching | 24 | 24 | 0 | 29.374 | 9876710 |
| PlanarMatching | 24 | 24 | 0 | 30.527 | 285681 |
| ShapeMatching | 24 | 24 | 0 | 16.69 | 39562881 |

## Scenarios

| Scenario | Cases | Passed | Failed | RuntimeMsAvg |
|---|---:|---:|---:|---:|
| Blank scene no-match contract | 8 | 8 | 0 | 25.336 |
| Blank scene rejection contract | 4 | 4 | 0 | 5.637 |
| Direct pose oracle | 8 | 8 | 0 | 11.08 |
| Feature homography identity oracle | 8 | 8 | 0 | 71.243 |
| Local deformation oracle | 8 | 8 | 0 | 86.096 |
| Low-feature no-match contract | 8 | 8 | 0 | 1.743 |
| Missing input failure contract | 4 | 4 | 0 | 0.151 |
| Missing template contract | 4 | 4 | 0 | 0.485 |
| Parameter validation contract | 8 | 8 | 0 | 0.125 |
| Perspective homography oracle | 8 | 8 | 0 | 17.438 |
| Rotation-scale oracle | 4 | 4 | 0 | 27.153 |
