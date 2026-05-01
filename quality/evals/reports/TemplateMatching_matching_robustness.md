# TemplateMatching Matching Robustness Report

Generated from: `quality\evals\reports\TemplateMatching_baseline.json`

| Scenario | Cases | Passed | Failed | Position P95 Px | Position Max Px |
|---|---:|---:|---:|---:|---:|
| edge_domain | 9 | 9 | 0 | 0.0000 | 0.0000 |
| fixed_scale_boundary | 9 | 9 | 0 | 0.0000 | 0.0000 |
| gradient_domain | 9 | 9 | 0 | 0.0000 | 0.0000 |
| illumination_shift | 9 | 9 | 0 | 0.0000 | 0.0000 |
| low_texture | 9 | 9 | 0 | 0.0000 | 0.0000 |
| mask_constraint | 9 | 9 | 0 | 0.0000 | 0.0000 |
| method_contract | 9 | 9 | 0 | 0.0000 | 0.0000 |
| multi_match_nms | 9 | 9 | 0 | 0.0000 | 0.0000 |
| repeated_texture | 9 | 9 | 0 | 0.0000 | 0.0000 |
| roi_constraint | 9 | 9 | 0 | 0.0000 | 0.0000 |
| roi_mask_constraint | 9 | 9 | 0 | 0.0000 | 0.0000 |
| sqdiff_contract | 9 | 9 | 0 | 0.0000 | 0.0000 |
| translation_gray | 9 | 9 | 0 | 0.0000 | 0.0000 |

## Boundary Checks

- `low_texture` expects `IsMatch=false` with an insufficient-texture failure reason.
- `fixed_scale_boundary` expects `IsMatch=false`, locking the fixed-scale/no-rotation limitation.
- ROI and Mask scenarios verify that stronger decoys outside the allowed search area are not returned.
- Multi-match scenarios verify `MaxMatches` and IoU NMS distinctness.
