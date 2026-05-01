# Quality Flywheel SurfaceDefectDetection Improvement v2

GeneratedAtUtc: `2026-05-01T04:56:01+00:00`
Accepted: `False`

## Result

| Metric | Baseline | Candidate |
|---|---:|---:|
| Pixel F1 | 0.2692 | 0.2692 |
| Image AUROC | 0.7724 | 0.7724 |
| Image F1 | 0.5671 | 0.5671 |
| FP/normal | 0.1398 | 0.1398 |

## A/B Replay

- Status: `candidate-executed`
- Replay cases: `20`
- Improved metric cases: `0`
- Regressed cases: `0`
- Worse metric cases: `0`

## Component Rule Selector

- Status: `accepted-rule-found`
- Accepted rules: `1088` / `11520`

## Taxonomy

| Taxonomy | Count |
|---|---:|
| low_contrast_defect_miss | 15 |
| mask_overgrowth_false_positive | 1 |
| oversegmentation_false_positive | 2 |
| small_defect_miss | 2 |
| texture_noise_false_positive | 123 |
| undersegmentation_false_negative | 46 |

## Next Actions

- Keep product defaults unchanged until the fixed component-rule gate passes on validation and test.
- Promote only when texture_noise_false_positive decreases, low_contrast_defect_miss does not increase, and PixelF1 stays at or above baseline.
- Use the exported component telemetry distribution to choose the next compact-noise rule; do not lower the global manual threshold.
- If a selector rule passes, convert it into a default-off SurfaceDefectDetection profile and replay on test.
