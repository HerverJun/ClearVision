# RegionUnion Baseline

Date: 2026-04-24

## Scope

This baseline covers the P0 Region boolean contract for `RegionUnion`.

## Contract

`RegionUnion` accepts two `Region` RLE inputs and emits:

- `Region`: merged RLE union
- `Area`: exact foreground pixel count
- `Region1Area` / `Region2Area`: source diagnostics
- `ProcessingTimeMs`: runtime diagnostic

## Golden Dimensions

The synthetic generator covers:

- empty region
- full image region
- single pixel region
- edge-touching region
- multi-connected region
- inner-hole region
- thin region
- crossing regions
- contained region
- disjoint region
- duplicate input
- tiny ROI

Default generation:

```powershell
python quality/synthetic/generators/region_generator.py --count 400
```

This emits 100 cases per Region boolean operator, including `RegionUnion`.

## Acceptance

- Empty + empty returns an empty region and does not throw in visualization.
- Duplicate input is idempotent: output area and runs equal the input.
- Area is exact for overlap cases: `A union B = A + B - intersection`.
- Component count, bbox, and mask IoU are evaluated by `quality/evals/metrics/morphology_metrics.py`.

## Evidence

- `Acme.Product.Tests.Operators.Phase42RegionProcessingOperatorTests`: 21 tests passed.
- 400 Region cases generated in smoke output, including 100 `RegionUnion` cases.
- Metrics smoke run produced `Passed=true` for a generated `RegionUnion` case.
- .NET golden runner executed 100 `RegionUnion` cases through `RegionUnionOperator`: 100 passed, 0 failed.
- Aggregate evidence is included in `quality/evals/reports/RegionMorphology_baseline.json`.

## Residual Risk

- Current implementation sorts input runs on each execution; row-index optimization is still open for very dense fragmented regions.
- Evidence is synthetic-only in this slice. Field data and public dataset evidence remain future work.
