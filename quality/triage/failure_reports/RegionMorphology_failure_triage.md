# Region/Morphology Failure Triage

Date: 2026-04-24

## Fixed In This Slice

| Area | Symptom | Root Cause | Resolution |
|---|---|---|---|
| Empty contour | Empty Region visualization could request contours from a 0x0 bounding box | `Region.GetContourPoints()` did not short-circuit empty regions | Return an empty point list immediately |
| RegionComplement | Negative Y runs could cause later valid rows to be ignored | Row scan advanced only when the current sorted run matched the scan row | Clip to explicit image bounds first, group valid runs by row |
| RegionUnion | Duplicate input contract was implicit only | No idempotence test locked the expected output | Added duplicate-input golden-style test |
| Region boolean empty inputs | Empty Region boolean behavior was not explicitly covered for all operators | Existing tests focused mainly on complement and morphology empty inputs | Added empty union/intersection/difference test |
| Morphology generator | First real runner pass exposed 47 Ellipse kernel mismatches | Python generator approximated ellipse area instead of matching OpenCV-style discrete rasterization | Aligned ellipse row bounds with OpenCV `MORPH_ELLIPSE`; rerun reached 900/900 pass |

## Runner Result

| Metric | Value |
|---|---:|
| Total cases | 900 |
| Passed | 900 |
| Failed | 0 |
| Region boolean cases | 400 |
| Morphology cases | 500 |

## Matrix Result

| Metric | Value |
|---|---:|
| Operators tracked | 155 |
| C-level operators | 13 |
| Operators with golden evidence | 9 |
| C-level operators without golden evidence | 4 |
| Region/Morphology card TODO count | 5 per generated card |

The matrix confirms that this slice closed runner evidence for all Region/Morphology C-level operators, but it also exposes a remaining documentation/source metadata gap: generated operator cards still retain TODO placeholders.

## Current Expected Failure Boundaries

| Operator | Boundary | Expected Behavior |
|---|---|---|
| RegionUnion | Empty + empty | Success, empty output, area 0 |
| RegionUnion | Same region twice | Success, identical runs and area |
| RegionIntersection | Disjoint regions | Success, empty output, area 0 |
| RegionDifference | Same region twice | Success, empty output, area 0 |
| RegionComplement | Empty region with explicit bounds | Success, full image region |
| RegionComplement | Full region with explicit bounds | Success, empty output |
| RegionComplement | Runs outside explicit bounds | Out-of-bounds rows/columns ignored |
| RegionErosion | Kernel larger than region | Success, empty output |
| RegionOpening | Kernel larger than region | Success, empty output |
| RegionClosing | Small gap within kernel reach | Success, bridge can be closed |
| RegionSkeleton | Thin line | Success, endpoints preserved |
| RegionSkeleton | Cross shape | Success, 8-connected skeleton |

## Residual Risks

- Region boolean operators still have naive same-row lookup paths in intersection and difference; dense fragmented masks need performance profiling.
- Morphology dilation can produce coordinates outside the original image domain by design. Downstream clipping must be explicit.
- Skeleton endpoint and branch counts are discrete 8-neighborhood diagnostics, not subpixel geometry.
- Field samples are not yet part of the P0 evidence pack.

## Next Triage Inputs

- Use repeated runner executions to identify runtime and allocation variance.
- Backfill Region/Morphology card/source TODO placeholders reported by `operator_quality_matrix.md`.
- Start the next C-level golden slice with `ArcCaliper`; follow with `ContourExtrema` and `PhaseClosure`.
- Add field-derived masks once stable failure samples are available.
