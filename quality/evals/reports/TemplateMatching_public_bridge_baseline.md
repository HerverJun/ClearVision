# TemplateMatching Homography Bridge Baseline

GeneratedAtUtc: `2026-04-26T15:30:30.9223855+00:00`
Dataset: `HPatches-style synthetic homography bridge`
DatasetKind: `In-repo public-protocol proxy for planar homography and illumination evidence`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 24 |
| Passed | 24 |
| Failed | 0 |
| Position tolerance px | 1.5 |
| Mean position error px | 0 |
| P95 position error px | 0 |
| Runtime ms | 51.469 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Max ms | Avg bytes | Public/Alternative Dataset |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TemplateMatching | 24 | 24 | 0 | 2.145 | 44.347 | 49328 | HPatches-style synthetic homography bridge |

## Cases

| Case | Sequence | Template | Passed | Pos Error Px | Score | Norm Score | Runtime Ms | Error |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| TemplateMatching_illumination_translation_0000 | illumination_translation | Source | True | 0 | 0.999973 | 0.999987 | 44.347 | - |
| TemplateMatching_illumination_translation_0001 | illumination_translation | Source | True | 0 | 0.999908 | 0.999954 | 0.388 | - |
| TemplateMatching_illumination_translation_0002 | illumination_translation | Source | True | 0 | 0.999978 | 0.999989 | 0.518 | - |
| TemplateMatching_illumination_translation_0003 | illumination_translation | Source | True | 0 | 0.999923 | 0.999961 | 0.358 | - |
| TemplateMatching_illumination_translation_0004 | illumination_translation | Source | True | 0 | 0.999964 | 0.999982 | 0.312 | - |
| TemplateMatching_illumination_translation_0005 | illumination_translation | Source | True | 0 | 0.999844 | 0.999922 | 0.321 | - |
| TemplateMatching_viewpoint_translation_0000 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.307 | - |
| TemplateMatching_viewpoint_translation_0001 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.417 | - |
| TemplateMatching_viewpoint_translation_0002 | viewpoint_translation | WarpedScene | True | 0 | 0.999998 | 0.999999 | 0.399 | - |
| TemplateMatching_viewpoint_translation_0003 | viewpoint_translation | WarpedScene | True | 0 | 1 | 1 | 0.31 | - |
| TemplateMatching_viewpoint_translation_0004 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.306 | - |
| TemplateMatching_viewpoint_translation_0005 | viewpoint_translation | WarpedScene | True | 0 | 1 | 1 | 0.314 | - |
| TemplateMatching_homography_shear_0000 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.274 | - |
| TemplateMatching_homography_shear_0001 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.296 | - |
| TemplateMatching_homography_shear_0002 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.305 | - |
| TemplateMatching_homography_shear_0003 | homography_shear | WarpedScene | True | 0 | 0.999998 | 0.999999 | 0.299 | - |
| TemplateMatching_homography_shear_0004 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.375 | - |
| TemplateMatching_homography_shear_0005 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.233 | - |
| TemplateMatching_homography_perspective_0000 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.218 | - |
| TemplateMatching_homography_perspective_0001 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.218 | - |
| TemplateMatching_homography_perspective_0002 | homography_perspective | WarpedScene | True | 0 | 0.999997 | 0.999998 | 0.234 | - |
| TemplateMatching_homography_perspective_0003 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.227 | - |
| TemplateMatching_homography_perspective_0004 | homography_perspective | WarpedScene | True | 0 | 0.999999 | 1 | 0.211 | - |
| TemplateMatching_homography_perspective_0005 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.282 | - |
