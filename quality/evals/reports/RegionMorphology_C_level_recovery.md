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
- Added `.NET` golden runner at `quality/tools/RegionMorphologyGoldenRunner`.
- Added aggregate baseline output `quality/evals/reports/RegionMorphology_baseline.json`.
- Added runner report `quality/evals/reports/RegionMorphology_before_after_report.md`.
- Added operator quality matrix generator `quality/tools/generate_operator_quality_matrix.py`.
- Added matrix output `quality/evals/reports/operator_quality_matrix.md`.
- Added boundary tests for empty boolean inputs, duplicate union input, empty contour extraction, and complement vertical clipping.
- Fixed empty-region contour extraction so visualization paths can request contours safely.
- Fixed `RegionComplement` to ignore rows outside explicit image bounds and keep later valid rows.
- Aligned morphology generator ellipse kernels with OpenCV-style `MORPH_ELLIPSE` rasterization.
- Matrix now marks all 9 Region/Morphology operators as `HasGoldenTest=Yes` with 100 cases each.

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

The .NET runner executes generated cases through the real operator implementations and adds:

- `RuntimeMs`
- `MemoryAllocationBytes`
- per-operator pass/fail summary
- per-case metric details

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

Golden runner:

- Cases: 900
- Passed: 900
- Failed: 0
- Baseline: `quality/evals/reports/RegionMorphology_baseline.json`
- Report: `quality/evals/reports/RegionMorphology_before_after_report.md`

Operator quality matrix:

- Total operators: 155
- Level counts: A=115, B=27, C=13
- Golden test status: Yes=9, No=146
- C-level without golden evidence: 4 (`Comment`, `ContourExtrema`, `PhaseClosure`, `ArcCaliper`)
- Region/Morphology card TODO status: still `CardTodoCount=5` per generated card; this must be backfilled into the card/source metadata before QScore/Level promotion.

## Acceptance Mapping

- Empty Region boolean inputs no longer crash visualization.
- Explicit complement bounds now clip both X and Y, including negative rows.
- Synthetic golden case generation is deterministic by seed; generated cases are kept out of git by default.
- Generated cases now execute through the real .NET operators with 900/900 pass.
- Runtime and allocation observations are captured in the aggregate baseline.
- Region/Morphology quality state is now tracked by `operator_quality_matrix.md`.

## Remaining Work

- Add public or field dataset evidence.
- Backfill Region/Morphology card/source TODO placeholders that the matrix still reports as `CardTodoCount=5`.
- Use the matrix and runner baseline to decide QScore/Level updates.
- Add longer runtime trend capture across repeated runs.
