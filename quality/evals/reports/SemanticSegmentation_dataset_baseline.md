# SemanticSegmentation Dataset Baseline

EvidenceKind: `dataset`
GeneratedAtUtc: `2026-04-27T11:36:45.2894272+00:00`
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
| Runtime ms | 724.817 |

## Scenarios

| Scenario | Cases | Passed | Failed | Pixels | Pixel accuracy | mIoU | Dice | Boundary IoU | Avg ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| class_absent | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 9.373 |
| multi_class_regions | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 9.21 |
| nested_regions | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 10.286 |
| single_region | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 72.744 |
| small_object | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 9.184 |
| thin_boundary | 6 | 6 | 0 | 29376 | 1 | 1 | 1 | 1 | 10.007 |

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
| SemanticSegmentation_single_region_0000 | single_region | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface | 392.112 | - |
| SemanticSegmentation_multi_class_regions_0000 | multi_class_regions | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 2.709 | - |
| SemanticSegmentation_thin_boundary_0000 | thin_boundary | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 2.03 | - |
| SemanticSegmentation_small_object_0000 | small_object | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 1.881 | - |
| SemanticSegmentation_class_absent_0000 | class_absent | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface | 1.933 | - |
| SemanticSegmentation_nested_regions_0000 | nested_regions | True | 32x24 | 64x48 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 2.11 | - |
| SemanticSegmentation_single_region_0001 | single_region | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 3.865 | - |
| SemanticSegmentation_multi_class_regions_0001 | multi_class_regions | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 4.179 | - |
| SemanticSegmentation_thin_boundary_0001 | thin_boundary | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.839 | - |
| SemanticSegmentation_small_object_0001 | small_object | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 7.505 | - |
| SemanticSegmentation_class_absent_0001 | class_absent | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 3.263 | - |
| SemanticSegmentation_nested_regions_0001 | nested_regions | True | 48x32 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 3.561 | - |
| SemanticSegmentation_single_region_0002 | single_region | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 4.985 | - |
| SemanticSegmentation_multi_class_regions_0002 | multi_class_regions | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.745 | - |
| SemanticSegmentation_thin_boundary_0002 | thin_boundary | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.354 | - |
| SemanticSegmentation_small_object_0002 | small_object | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 7.796 | - |
| SemanticSegmentation_class_absent_0002 | class_absent | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 5.11 | - |
| SemanticSegmentation_nested_regions_0002 | nested_regions | True | 64x48 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 5.557 | - |
| SemanticSegmentation_single_region_0003 | single_region | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface | 7.408 | - |
| SemanticSegmentation_multi_class_regions_0003 | multi_class_regions | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 8.262 | - |
| SemanticSegmentation_thin_boundary_0003 | thin_boundary | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 11.64 | - |
| SemanticSegmentation_small_object_0003 | small_object | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 7.698 | - |
| SemanticSegmentation_class_absent_0003 | class_absent | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface | 7.247 | - |
| SemanticSegmentation_nested_regions_0003 | nested_regions | True | 80x60 | 96x48 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 12.62 | - |
| SemanticSegmentation_single_region_0004 | single_region | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 10.622 | - |
| SemanticSegmentation_multi_class_regions_0004 | multi_class_regions | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 10.644 | - |
| SemanticSegmentation_thin_boundary_0004 | thin_boundary | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 14.83 | - |
| SemanticSegmentation_small_object_0004 | small_object | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, contaminant | 11.02 | - |
| SemanticSegmentation_class_absent_0004 | class_absent | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface | 10.082 | - |
| SemanticSegmentation_nested_regions_0004 | nested_regions | True | 96x72 | 64x64 | RGB | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 22.468 | - |
| SemanticSegmentation_single_region_0005 | single_region | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 17.471 | - |
| SemanticSegmentation_multi_class_regions_0005 | multi_class_regions | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 23.718 | - |
| SemanticSegmentation_thin_boundary_0005 | thin_boundary | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 22.347 | - |
| SemanticSegmentation_small_object_0005 | small_object | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, contaminant | 19.202 | - |
| SemanticSegmentation_class_absent_0005 | class_absent | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface | 28.604 | - |
| SemanticSegmentation_nested_regions_0005 | nested_regions | True | 128x96 | 96x64 | BGR | 1 | 1 | 1 | 1 | background, surface, scratch, contaminant | 15.4 | - |
