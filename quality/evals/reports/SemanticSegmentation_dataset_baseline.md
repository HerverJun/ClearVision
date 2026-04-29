# SemanticSegmentation Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-29T03:21:26.4419311+00:00`
Dataset: `VOC-style semi-synthetic semantic segmentation protocol bridge`
DatasetKind: `Tier A protocol bridge for public/VOC-style semantic segmentation metrics; no external image pixels are stored.`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 36 |
| Passed | 36 |
| Failed | 0 |
| Total pixels | 176256 |
| Correct pixels | 176256 |
| Pixel accuracy | 1 |
| Mean IoU | 1 |
| Mean Dice | 1 |
| Mean boundary IoU | 1 |
| Runtime ms | 695.89 |

## Scenarios

| Scenario | Cases | Passed | Failed | Pixels | Pixel accuracy | mIoU | Dice | Boundary IoU | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| class_absent | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 8.164 |
| multi_class_regions | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 9.01 |
| nested_regions | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 9.348 |
| single_region | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 70.803 |
| small_object | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 8.661 |
| thin_boundary | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 9.996 |

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
| SemanticSegmentation_single_region_0000 | single_region | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface | 382.014 | - |
| SemanticSegmentation_multi_class_regions_0000 | multi_class_regions | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 2.616 | - |
| SemanticSegmentation_thin_boundary_0000 | thin_boundary | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 1.925 | - |
| SemanticSegmentation_small_object_0000 | small_object | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 1.939 | - |
| SemanticSegmentation_class_absent_0000 | class_absent | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface | 1.846 | - |
| SemanticSegmentation_nested_regions_0000 | nested_regions | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 1.925 | - |
| SemanticSegmentation_single_region_0001 | single_region | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 3.573 | - |
| SemanticSegmentation_multi_class_regions_0001 | multi_class_regions | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.798 | - |
| SemanticSegmentation_thin_boundary_0001 | thin_boundary | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.925 | - |
| SemanticSegmentation_small_object_0001 | small_object | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 5.894 | - |
| SemanticSegmentation_class_absent_0001 | class_absent | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 3.101 | - |
| SemanticSegmentation_nested_regions_0001 | nested_regions | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.331 | - |
| SemanticSegmentation_single_region_0002 | single_region | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 4.872 | - |
| SemanticSegmentation_multi_class_regions_0002 | multi_class_regions | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.315 | - |
| SemanticSegmentation_thin_boundary_0002 | thin_boundary | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.194 | - |
| SemanticSegmentation_small_object_0002 | small_object | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 7.791 | - |
| SemanticSegmentation_class_absent_0002 | class_absent | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 4.758 | - |
| SemanticSegmentation_nested_regions_0002 | nested_regions | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.049 | - |
| SemanticSegmentation_single_region_0003 | single_region | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface | 7.06 | - |
| SemanticSegmentation_multi_class_regions_0003 | multi_class_regions | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 8.026 | - |
| SemanticSegmentation_thin_boundary_0003 | thin_boundary | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 11.175 | - |
| SemanticSegmentation_small_object_0003 | small_object | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 7.39 | - |
| SemanticSegmentation_class_absent_0003 | class_absent | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface | 7.137 | - |
| SemanticSegmentation_nested_regions_0003 | nested_regions | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 11.534 | - |
| SemanticSegmentation_single_region_0004 | single_region | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 9.878 | - |
| SemanticSegmentation_multi_class_regions_0004 | multi_class_regions | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 10.916 | - |
| SemanticSegmentation_thin_boundary_0004 | thin_boundary | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 15.17 | - |
| SemanticSegmentation_small_object_0004 | small_object | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 10.359 | - |
| SemanticSegmentation_class_absent_0004 | class_absent | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 9.907 | - |
| SemanticSegmentation_nested_regions_0004 | nested_regions | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 15.493 | - |
| SemanticSegmentation_single_region_0005 | single_region | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 17.421 | - |
| SemanticSegmentation_multi_class_regions_0005 | multi_class_regions | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 23.39 | - |
| SemanticSegmentation_thin_boundary_0005 | thin_boundary | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 22.584 | - |
| SemanticSegmentation_small_object_0005 | small_object | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 18.594 | - |
| SemanticSegmentation_class_absent_0005 | class_absent | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 22.233 | - |
| SemanticSegmentation_nested_regions_0005 | nested_regions | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 18.757 | - |
