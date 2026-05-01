# SurfaceDefectDetection Component Rule Selector v2

GeneratedAtUtc: `2026-05-01T04:56:01+00:00`
Status: `accepted-rule-found`
TelemetryCsv: `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_component_telemetry_v2.csv`
DistributionCsv: `quality/evals/reports/SurfaceDefectDetection_kolektorsdd2_component_distribution_v2.csv`

## Fixed Promotion Gate

| Gate | Baseline | Required |
|---|---:|---|
| texture_noise_false_positive | 55 | decrease |
| low_contrast_defect_miss | 14 | not increase |
| Pixel F1 | 0.320553 | not decrease |

## Selected Rule

- Rule: `{'areaMax': 8, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.0, 'ringProminenceMax': 24.0}`
- Pixel F1: `0.320672` (`+0.000119`)
- texture_noise_false_positive delta: `-2`
- low_contrast_defect_miss delta: `0`

## Top Rules

| Accepted | Texture noise | Low contrast | Pixel F1 | TP comps rejected | FP comps rejected | Rule |
|---|---:|---:|---:|---:|---:|---|
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 8, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 8, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.5, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 8, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 3.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 8, 'circularityMin': 0.6, 'fillRatioMin': 0.4, 'elongationMax': 2.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 8, 'circularityMin': 0.6, 'fillRatioMin': 0.4, 'elongationMax': 2.5, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 8, 'circularityMin': 0.6, 'fillRatioMin': 0.4, 'elongationMax': 3.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 12, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 12, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.5, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 12, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 3.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 12, 'circularityMin': 0.6, 'fillRatioMin': 0.4, 'elongationMax': 2.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 12, 'circularityMin': 0.6, 'fillRatioMin': 0.4, 'elongationMax': 2.5, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 12, 'circularityMin': 0.6, 'fillRatioMin': 0.4, 'elongationMax': 3.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 16, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.0, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 16, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 2.5, 'ringProminenceMax': 24.0}` |
| True | 53 | 14 | 0.320672 | 0 | 3 | `{'areaMax': 16, 'circularityMin': 0.55, 'fillRatioMin': 0.4, 'elongationMax': 3.0, 'ringProminenceMax': 24.0}` |
