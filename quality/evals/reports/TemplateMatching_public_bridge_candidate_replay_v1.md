# TemplateMatching Homography Bridge Baseline

GeneratedAtUtc: `2026-04-30T02:21:24.7094453+00:00`
Dataset: `HPatches-style synthetic homography bridge`
DatasetKind: `In-repo public-protocol proxy for planar homography and illumination evidence`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Position tolerance px | 1.5 |
| Mean position error px | 0 |
| P95 position error px | 0 |
| Runtime ms | 53.428 |
| Candidate version | v1 |
| Profile | homography_bridge_ncc_v1 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Max ms | Avg bytes | Public/Alternative Dataset |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TemplateMatching | 20 | 20 | 0 | 2.671 | 45.81 | 52697 | HPatches-style synthetic homography bridge |

## Cases

| Case | Sequence | Template | Passed | Pos Error Px | Score | Norm Score | Runtime Ms | Error |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| TemplateMatching_illumination_translation_0000 | illumination_translation | Source | True | 0 | 0.999973 | 0.999987 | 45.81 | - |
| TemplateMatching_illumination_translation_0001 | illumination_translation | Source | True | 0 | 0.999908 | 0.999954 | 0.542 | - |
| TemplateMatching_illumination_translation_0002 | illumination_translation | Source | True | 0 | 0.999978 | 0.999989 | 0.606 | - |
| TemplateMatching_illumination_translation_0003 | illumination_translation | Source | True | 0 | 0.999923 | 0.999961 | 0.37 | - |
| TemplateMatching_illumination_translation_0004 | illumination_translation | Source | True | 0 | 0.999964 | 0.999982 | 0.391 | - |
| TemplateMatching_illumination_translation_0005 | illumination_translation | Source | True | 0 | 0.999844 | 0.999922 | 0.356 | - |
| TemplateMatching_viewpoint_translation_0000 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.364 | - |
| TemplateMatching_viewpoint_translation_0001 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.405 | - |
| TemplateMatching_viewpoint_translation_0002 | viewpoint_translation | WarpedScene | True | 0 | 0.999998 | 0.999999 | 0.396 | - |
| TemplateMatching_viewpoint_translation_0003 | viewpoint_translation | WarpedScene | True | 0 | 1 | 1 | 0.417 | - |
| TemplateMatching_viewpoint_translation_0004 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.388 | - |
| TemplateMatching_viewpoint_translation_0005 | viewpoint_translation | WarpedScene | True | 0 | 1 | 1 | 0.404 | - |
| TemplateMatching_homography_shear_0000 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.381 | - |
| TemplateMatching_homography_shear_0001 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.435 | - |
| TemplateMatching_homography_shear_0002 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.453 | - |
| TemplateMatching_homography_shear_0003 | homography_shear | WarpedScene | True | 0 | 0.999998 | 0.999999 | 0.396 | - |
| TemplateMatching_homography_shear_0004 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.474 | - |
| TemplateMatching_homography_shear_0005 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.345 | - |
| TemplateMatching_homography_perspective_0000 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.254 | - |
| TemplateMatching_homography_perspective_0001 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.241 | - |
