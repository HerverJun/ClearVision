# AnomalyDetection MVTec AD Lite Sweep v1

GeneratedAtUtc: `2026-04-29T16:07:57+00:00`
SelectedProfile: `max192_dense_stride8`

| Profile | Image AUROC | Pixel AUROC | Image F1 | TP | FP | FN | Max side | Patch / stride | Runtime ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| max192_dense_stride8 | 0.9178 | 0.8692 | 0.7746 | 55 | 0 | 32 | 192 | 16 / 8 | 30423.3 |
| max160_patch12_stride6 | 0.9025 | 0.8577 | 0.7429 | 52 | 1 | 35 | 160 | 12 / 6 | 45192.8 |
| max160_dense_stride8 | 0.8885 | 0.8334 | 0.5854 | 36 | 0 | 51 | 160 | 16 / 8 | 17370.8 |
| patch12_stride6 | 0.8795 | 0.8361 | 0.6615 | 43 | 0 | 44 | 128 | 12 / 6 | 22142.4 |
| dense_stride8_coreset05 | 0.8029 | 0.7924 | 0.5968 | 37 | 0 | 50 | 128 | 16 / 8 | 17335.5 |
| dense_stride8 | 0.7328 | 0.7749 | 0.4037 | 22 | 0 | 65 | 128 | 16 / 8 | 9991.2 |
| baseline_default | 0.6609 | 0.6709 | 0.0879 | 4 | 0 | 83 | 128 | 16 / 16 | 5593.5 |

Select highest Image AUROC, then Pixel AUROC, Image F1, and lower runtime.
