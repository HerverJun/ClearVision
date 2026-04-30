# SemanticSegmentation Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-30T02:07:37.9060079+00:00`
Dataset: `VOC-style semi-synthetic semantic segmentation protocol bridge`
DatasetKind: `Tier A protocol bridge for public/VOC-style semantic segmentation metrics; no external image pixels are stored.`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Passed | 20 |
| Failed | 0 |
| Total pixels | 41856 |
| Correct pixels | 41856 |
| Pixel accuracy | 1 |
| Mean IoU | 1 |
| Mean Dice | 1 |
| Mean boundary IoU | 1 |
| Runtime ms | 419.654 |
| Candidate version | v1 |
| Profile | protocol_bridge_exact_map_v1 |

## Scenarios

| Scenario | Cases | Passed | Failed | Pixels | Pixel accuracy | mIoU | Dice | Boundary IoU | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| class_absent | 3 | 3 | 0 | 5376 | 1 | 1 | 1 | 1 | 3.159 |
| multi_class_regions | 4 | 4 | 0 | 10176 | 1 | 1 | 1 | 1 | 4.864 |
| nested_regions | 3 | 3 | 0 | 5376 | 1 | 1 | 1 | 1 | 3.661 |
| single_region | 4 | 4 | 0 | 10176 | 1 | 1 | 1 | 1 | 88.424 |
| small_object | 3 | 3 | 0 | 5376 | 1 | 1 | 1 | 1 | 5.165 |
| thin_boundary | 3 | 3 | 0 | 5376 | 1 | 1 | 1 | 1 | 3.516 |

## Failure Boundaries

- `single_region` verifies large foreground masks and background separation.
- `multi_class_regions` verifies multiple positive classes and per-class IoU accounting.
- `thin_boundary` verifies 1-2 px structures and boundary-IoU sensitivity.
- `small_object` verifies small connected regions remain represented in masks.
- `class_absent` verifies missing classes do not create extra masks or denominator drift.
- `nested_regions` verifies overlapping overwrite order, class masks, and colored-map palette stability.
- This bridge records VOC-style segmentation metrics for SemanticSegmentation preprocessing, mask, and visualization paths; it is not production model accuracy evidence.

## Cases

| Case | Scenario | Passed | Size | Input | Order | Pixel accuracy | mIoU | Dice | Boundary IoU | Present classes | Runtime ms | Failure |
| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | ---: | --- |
| SemanticSegmentation_single_region_0000 | single_region | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface | 338.543 | - |
| SemanticSegmentation_multi_class_regions_0000 | multi_class_regions | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 2.443 | - |
| SemanticSegmentation_thin_boundary_0000 | thin_boundary | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 1.83 | - |
| SemanticSegmentation_small_object_0000 | small_object | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 1.811 | - |
| SemanticSegmentation_class_absent_0000 | class_absent | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface | 2.032 | - |
| SemanticSegmentation_nested_regions_0000 | nested_regions | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 1.827 | - |
| SemanticSegmentation_single_region_0001 | single_region | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 3.45 | - |
| SemanticSegmentation_multi_class_regions_0001 | multi_class_regions | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.831 | - |
| SemanticSegmentation_thin_boundary_0001 | thin_boundary | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.56 | - |
| SemanticSegmentation_small_object_0001 | small_object | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 5.83 | - |
| SemanticSegmentation_class_absent_0001 | class_absent | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 2.886 | - |
| SemanticSegmentation_nested_regions_0001 | nested_regions | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.188 | - |
| SemanticSegmentation_single_region_0002 | single_region | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 4.646 | - |
| SemanticSegmentation_multi_class_regions_0002 | multi_class_regions | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.21 | - |
| SemanticSegmentation_thin_boundary_0002 | thin_boundary | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.158 | - |
| SemanticSegmentation_small_object_0002 | small_object | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 7.855 | - |
| SemanticSegmentation_class_absent_0002 | class_absent | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 4.559 | - |
| SemanticSegmentation_nested_regions_0002 | nested_regions | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.969 | - |
| SemanticSegmentation_single_region_0003 | single_region | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface | 7.056 | - |
| SemanticSegmentation_multi_class_regions_0003 | multi_class_regions | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 7.97 | - |
