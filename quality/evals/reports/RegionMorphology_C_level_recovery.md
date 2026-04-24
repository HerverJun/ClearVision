# Region/Morphology C-Level Recovery

Date: 2026-04-24

## Scope

P0 recovery slice for the C-level Region and Morphology operators:

- `RegionUnion`
- `RegionIntersection`
- `RegionDifference`
- `RegionComplement`
- `RegionErosion`
- `RegionDilation`
- `RegionOpening`
- `RegionClosing`
- `RegionSkeleton`

## Delivered

- Added `quality/synthetic/generators/region_generator.py`.
- Added `quality/synthetic/generators/morphology_generator.py`.
- Added `quality/evals/metrics/morphology_metrics.py`.
- Added boundary tests for empty boolean inputs, duplicate union input, empty contour extraction, and complement vertical clipping.
- Fixed empty-region contour extraction so visualization paths can request contours safely.
- Fixed `RegionComplement` to ignore rows outside explicit image bounds and keep later valid rows.
- Removed placeholder skeleton sections from the 9 P0 operator cards.

## Golden Case Volume

Generated output is intentionally not checked in. The generators are sized to meet the first recovery target:

```powershell
python quality/synthetic/generators/region_generator.py --count 400
python quality/synthetic/generators/morphology_generator.py --count 500
```

This creates 100 cases per operator for the 4 Region boolean operators and 5 Morphology operators. The output directory `quality/synthetic/cases/` is ignored to keep the working tree reviewable.

## Metrics

The metrics script evaluates:

- `AreaError`
- `ComponentCountError`
- `BBoxIoU`
- `MaskIoU`
- `EmptyRegionBehavior`

The generated case metadata also reserves runtime and allocation observations for runner integration.

## Verification

Targeted test run:

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "Acme.Product.Tests.Operators.Phase42RegionProcessingOperatorTests"
```

Result: 21 passed, 0 failed.

Generator and metrics checks:

- Region generator: 400 cases generated in smoke output.
- Morphology generator: 500 cases generated in smoke output.
- Smoke `expected.json` count: 900.
- Metrics smoke: generated `RegionUnion` and `RegionSkeleton` cases evaluated with `Passed=true`.

## Acceptance Mapping

- Empty Region boolean inputs no longer crash visualization.
- Explicit complement bounds now clip both X and Y, including negative rows.
- Region/Morphology cards no longer retain implementation/API/performance/use-case placeholders.
- Synthetic golden case generation is deterministic by seed; generated cases are kept out of git by default.

## Remaining Work

- Wire generated JSON cases into an automated .NET runner.
- Add public or field dataset evidence.
- Add memory allocation and runtime trend capture to the runner output.
