# TemplateMatching Homography Bridge Baseline

GeneratedAtUtc: `2026-04-29T03:21:10.8246963+00:00`
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
| Runtime ms | 57.269 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Max ms | Avg bytes | Public/Alternative Dataset |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TemplateMatching | 24 | 24 | 0 | 2.386 | 47.315 | 48998 | HPatches-style synthetic homography bridge |

## Cases

| Case | Sequence | Template | Passed | Pos Error Px | Score | Norm Score | Runtime Ms | Error |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| TemplateMatching_illumination_translation_0000 | illumination_translation | Source | True | 0 | 0.999973 | 0.999987 | 47.315 | - |
| TemplateMatching_illumination_translation_0001 | illumination_translation | Source | True | 0 | 0.999908 | 0.999954 | 0.461 | - |
| TemplateMatching_illumination_translation_0002 | illumination_translation | Source | True | 0 | 0.999978 | 0.999989 | 0.682 | - |
| TemplateMatching_illumination_translation_0003 | illumination_translation | Source | True | 0 | 0.999923 | 0.999961 | 0.458 | - |
| TemplateMatching_illumination_translation_0004 | illumination_translation | Source | True | 0 | 0.999964 | 0.999982 | 0.337 | - |
| TemplateMatching_illumination_translation_0005 | illumination_translation | Source | True | 0 | 0.999844 | 0.999922 | 0.469 | - |
| TemplateMatching_viewpoint_translation_0000 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.422 | - |
| TemplateMatching_viewpoint_translation_0001 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.34 | - |
| TemplateMatching_viewpoint_translation_0002 | viewpoint_translation | WarpedScene | True | 0 | 0.999998 | 0.999999 | 0.333 | - |
| TemplateMatching_viewpoint_translation_0003 | viewpoint_translation | WarpedScene | True | 0 | 1 | 1 | 0.318 | - |
| TemplateMatching_viewpoint_translation_0004 | viewpoint_translation | WarpedScene | True | 0 | 0.999999 | 1 | 0.323 | - |
| TemplateMatching_viewpoint_translation_0005 | viewpoint_translation | WarpedScene | True | 0 | 1 | 1 | 0.486 | - |
| TemplateMatching_homography_shear_0000 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.292 | - |
| TemplateMatching_homography_shear_0001 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.405 | - |
| TemplateMatching_homography_shear_0002 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.42 | - |
| TemplateMatching_homography_shear_0003 | homography_shear | WarpedScene | True | 0 | 0.999998 | 0.999999 | 0.597 | - |
| TemplateMatching_homography_shear_0004 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.563 | - |
| TemplateMatching_homography_shear_0005 | homography_shear | WarpedScene | True | 0 | 1 | 1 | 0.442 | - |
| TemplateMatching_homography_perspective_0000 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.421 | - |
| TemplateMatching_homography_perspective_0001 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.494 | - |
| TemplateMatching_homography_perspective_0002 | homography_perspective | WarpedScene | True | 0 | 0.999997 | 0.999998 | 0.506 | - |
| TemplateMatching_homography_perspective_0003 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.454 | - |
| TemplateMatching_homography_perspective_0004 | homography_perspective | WarpedScene | True | 0 | 0.999999 | 1 | 0.393 | - |
| TemplateMatching_homography_perspective_0005 | homography_perspective | WarpedScene | True | 0 | 1 | 1 | 0.338 | - |
