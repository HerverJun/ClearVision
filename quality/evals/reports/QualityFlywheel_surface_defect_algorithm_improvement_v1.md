# Quality Flywheel SurfaceDefectDetection Improvement v1

GeneratedAtUtc: `2026-05-01T03:24:21+00:00`
Accepted: `True`

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
- Improved metric cases: `10`
- Regressed cases: `0`
- Worse metric cases: `8`

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

- Keep v1 profile as the replay-gated SurfaceDefectDetection candidate.
- Next tuning should target residual low-contrast misses and undersegmentation before lowering global thresholds further.
- Move AnomalyDetection into candidate execution after this SurfaceDefectDetection evidence chain stays green.
