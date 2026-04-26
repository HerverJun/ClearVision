# AnomalyDetection MVTec AD Lite Tuning Notes

Generated: 2026-04-26

## Result

The MVTec AD Lite baseline was strengthened by optimizing coreset selection and moving the default runner parameters to a denser patch grid.

| Variant | Max side | Patch / stride | Coreset | Pixel sample stride | Image AUROC | Pixel AUROC | Runtime |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Initial Lite | 128 | 32 / 32 | 0.02 | 2 | 0.5575 | 0.4933 | ~5.5s |
| Dense patches before coreset optimization | 128 | 16 / 16 | 0.02 | 4 | 0.6609 | 0.6703 | ~41.2s |
| Dense patches after coreset optimization | 128 | 16 / 16 | 0.02 | 2 | 0.6609 | 0.6709 | ~5.9s |

## Change

- `SimplePatchCoreDetector.SelectCoreset` now maintains each candidate feature's minimum distance to the selected coreset.
- This preserves the farthest-first selection semantics while avoiding repeated scans over the selected set.
- `AnomalyDetectionMvtecRunner` defaults changed from `PatchSize=32, PatchStride=32` to `PatchSize=16, PatchStride=16`.

## Interpretation

The stronger baseline improves the public-data evidence, especially pixel-level AUROC. It is still a handcrafted `lab_gradient_stats` feature baseline, not a replacement for the planned PatchCore-Deep / ONNX embedding route.
