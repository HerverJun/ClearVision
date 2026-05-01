# TemplateMatching Homography Bridge Baseline

GeneratedAtUtc: `2026-04-30T08:11:21.3318307+00:00`
Dataset: `HPatches-style synthetic homography bridge`
DatasetKind: `In-repo public-protocol proxy for planar homography and illumination evidence`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 32 |
| Passed | 32 |
| Failed | 0 |
| Position tolerance px | 1.5 |
| Mean position error px | 0.0338 |
| P95 position error px | 0.1934 |
| Runtime ms | 99.285 |
| Candidate version | v2 |
| Profile | homography_bridge_precision_v2 |

## Operators

| Operator | Cases | Passed | Failed | Avg ms | Max ms | Avg bytes | Public/Alternative Dataset |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TemplateMatching | 32 | 32 | 0 | 3.103 | 46.191 | 139124 | HPatches-style synthetic homography bridge |

## Cases

| Case | Sequence | Template | Passed | Pos Error Px | Angle Err | Scale Err | Pyramid Levels | Score | Norm Score | Subpixel X | Subpixel Y | Peak Curvature | Runtime Ms | Error |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| TemplateMatching_illumination_translation_0000 | illumination_translation | Source | True | 0.0053 | 0 | 0 | 1 | 0.999973 | 0.999987 | 0.003071 | -0.004287 | 0.113697 | 46.191 | - |
| TemplateMatching_illumination_translation_0001 | illumination_translation | Source | True | 0.0113 | 0 | 0 | 1 | 0.999908 | 0.999954 | 0.011178 | -0.001616 | 0.093099 | 0.406 | - |
| TemplateMatching_illumination_translation_0002 | illumination_translation | Source | True | 0 | 0 | 0 | 1 | 0.999978 | 0.999989 | -0.000003 | -0 | 0.55361 | 0.603 | - |
| TemplateMatching_illumination_translation_0003 | illumination_translation | Source | True | 0 | 0 | 0 | 1 | 0.999923 | 0.999961 | -0 | -0 | 0.537927 | 0.356 | - |
| TemplateMatching_illumination_translation_0004 | illumination_translation | Source | True | 0.0305 | 0 | 0 | 1 | 0.999964 | 0.999982 | 0.007298 | -0.029638 | 0.269762 | 0.337 | - |
| TemplateMatching_illumination_translation_0005 | illumination_translation | Source | True | 0.0036 | 0 | 0 | 1 | 0.999844 | 0.999922 | 0.003551 | -0.000738 | 0.098439 | 0.324 | - |
| TemplateMatching_viewpoint_translation_0000 | viewpoint_translation | WarpedScene | True | 0.0047 | 0 | 0 | 1 | 0.999999 | 1 | 0.003492 | -0.003117 | 0.113369 | 0.342 | - |
| TemplateMatching_viewpoint_translation_0001 | viewpoint_translation | WarpedScene | True | 0.0109 | 0 | 0 | 1 | 0.999999 | 1 | 0.010767 | -0.001979 | 0.093167 | 0.338 | - |
| TemplateMatching_viewpoint_translation_0002 | viewpoint_translation | WarpedScene | True | 0 | 0 | 0 | 1 | 0.999998 | 0.999999 | -0 | -0 | 0.553678 | 0.325 | - |
| TemplateMatching_viewpoint_translation_0003 | viewpoint_translation | WarpedScene | True | 0 | 0 | 0 | 1 | 1 | 1 | -0 | -0 | 0.538058 | 0.316 | - |
| TemplateMatching_viewpoint_translation_0004 | viewpoint_translation | WarpedScene | True | 0.0311 | 0 | 0 | 1 | 0.999999 | 1 | 0.007489 | -0.030217 | 0.269351 | 0.329 | - |
| TemplateMatching_viewpoint_translation_0005 | viewpoint_translation | WarpedScene | True | 0.0026 | 0 | 0 | 1 | 1 | 1 | 0.002164 | 0.001419 | 0.099216 | 0.308 | - |
| TemplateMatching_homography_shear_0000 | homography_shear | WarpedScene | True | 0.0036 | 0 | 0 | 1 | 1 | 1 | 0.003363 | -0.00122 | 0.078819 | 0.318 | - |
| TemplateMatching_homography_shear_0001 | homography_shear | WarpedScene | True | 0.0017 | 0 | 0 | 1 | 1 | 1 | 0.001694 | -0.000145 | 0.063806 | 0.311 | - |
| TemplateMatching_homography_shear_0002 | homography_shear | WarpedScene | True | 0.0174 | 0 | 0 | 1 | 1 | 1 | -0.005558 | 0.016483 | 0.424569 | 0.348 | - |
| TemplateMatching_homography_shear_0003 | homography_shear | WarpedScene | True | 0.0273 | 0 | 0 | 1 | 0.999998 | 0.999999 | 0.027313 | -0.001023 | 0.449701 | 0.339 | - |
| TemplateMatching_homography_shear_0004 | homography_shear | WarpedScene | True | 0.022 | 0 | 0 | 1 | 1 | 1 | 0.000249 | -0.021956 | 0.151799 | 0.42 | - |
| TemplateMatching_homography_shear_0005 | homography_shear | WarpedScene | True | 0.0074 | 0 | 0 | 1 | 1 | 1 | -0.006506 | 0.003445 | 0.056165 | 0.344 | - |
| TemplateMatching_homography_perspective_0000 | homography_perspective | WarpedScene | True | 0.0006 | 0 | 0 | 1 | 1 | 1 | 0.000606 | 0.000108 | 0.078736 | 0.321 | - |
| TemplateMatching_homography_perspective_0001 | homography_perspective | WarpedScene | True | 0.0016 | 0 | 0 | 1 | 1 | 1 | 0.001228 | -0.001066 | 0.062571 | 0.265 | - |
| TemplateMatching_homography_perspective_0002 | homography_perspective | WarpedScene | True | 0.0097 | 0 | 0 | 1 | 0.999997 | 0.999998 | 0.000579 | -0.009655 | 0.473845 | 0.254 | - |
| TemplateMatching_homography_perspective_0003 | homography_perspective | WarpedScene | True | 0.0244 | 0 | 0 | 1 | 1 | 1 | 0.002633 | 0.024222 | 0.48945 | 0.246 | - |
| TemplateMatching_homography_perspective_0004 | homography_perspective | WarpedScene | True | 0.034 | 0 | 0 | 1 | 0.999999 | 1 | 0.002966 | -0.033834 | 0.14054 | 0.261 | - |
| TemplateMatching_homography_perspective_0005 | homography_perspective | WarpedScene | True | 0.0068 | 0 | 0 | 1 | 1 | 1 | -0.006774 | -0.000016 | 0.05898 | 0.229 | - |
| TemplateMatching_pose_small_rotation_0000 | pose_small_rotation | Source | True | 0.1934 | 0 | 0 | 3 | 0.999979 | 0.999989 | -0.00018 | -0.193397 | 0.124955 | 8.273 | - |
| TemplateMatching_pose_small_rotation_0001 | pose_small_rotation | Source | True | 0.0341 | 0 | 0 | 3 | 0.999992 | 0.999996 | -0.003035 | -0.033917 | 0.089093 | 1.328 | - |
| TemplateMatching_pose_medium_rotation_0000 | pose_medium_rotation | Source | True | 0.2562 | 0 | 0 | 3 | 0.999945 | 0.999972 | -0.22484 | -0.122794 | 0.236096 | 3.088 | - |
| TemplateMatching_pose_medium_rotation_0001 | pose_medium_rotation | Source | True | 0.0477 | 0 | 0 | 3 | 0.999957 | 0.999978 | 0.019708 | -0.043408 | 0.371664 | 2.938 | - |
| TemplateMatching_pose_scale_0000 | pose_scale | Source | True | 0.0963 | 0 | 0 | 3 | 0.999969 | 0.999984 | -0.059091 | 0.075994 | 0.185877 | 0.701 | - |
| TemplateMatching_pose_scale_0001 | pose_scale | Source | True | 0.0505 | 0 | 0 | 3 | 0.999989 | 0.999994 | 0.017466 | 0.047342 | 0.062382 | 0.761 | - |
| TemplateMatching_pose_rotation_scale_0000 | pose_rotation_scale | Source | True | 0.0025 | 0 | 0 | 3 | 0.999991 | 0.999996 | -0.002496 | 0.000461 | 0.082972 | 12.412 | - |
| TemplateMatching_pose_rotation_scale_0001 | pose_rotation_scale | Source | True | 0.1439 | 0 | 0 | 3 | 0.999966 | 0.999983 | -0.072077 | -0.124558 | 0.310866 | 15.953 | - |
