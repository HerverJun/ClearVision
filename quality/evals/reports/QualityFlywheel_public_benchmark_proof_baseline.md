# Quality Flywheel Public Benchmark Proof Baseline

GeneratedAtUtc: `2026-04-29T00:00:00Z`
Accepted: `Yes`

## Summary

- Operators: 10
- Accepted: 10
- Public benchmark proof rows: 7
- Golden/protocol bridge rows: 3
- Real industrial validation complete: 0

## Operators

| Operator | Dataset | Proof | Accepted | Cases | Primary Metrics | Thresholds |
|---|---|---|---|---:|---|---|
| AnomalyDetection | mvtec_ad_lite | public-benchmark | Yes | 120 | ImageAuroc=0.66092, PixelAuroc=0.670852 | ImageAuroc >= 0.5, PixelAuroc >= 0.5, Failed <= 0.0 |
| SurfaceDefectDetection | kolektorsdd2 | public-benchmark | Yes | 1004 | ImageAuroc=0.772432, PixelF1=0.269226 | ImageAuroc >= 0.7, PixelF1 >= 0.2, Failed <= 0.0 |
| EdgeDetection | bsds500 | public-benchmark | Yes | 200 | BoundaryF1=0.505735, BoundaryRecall=0.757494 | BoundaryF1 >= 0.49562, Failed <= 0.0 |
| CameraCalibration | opencv_calibration_samples | public-benchmark | Yes | 3 | ReprojectionRmsPx=0.367839, MaxReprojectionErrorPx=0.470042 | ReprojectionRmsPx <= 1.0, Failed <= 0.0 |
| DeepLearning | coco2017 | public-benchmark | Yes | 20 | AP50=0.0, PrecisionAt50=0.0, RecallAt50=0.0 | AP50 >= 0.0, PrecisionAt50 >= 0.0, RecallAt50 >= 0.0 |
| SemanticSegmentation | voc-style-protocol-bridge | golden | Yes | 36 | MeanIoU=1.0, PixelAccuracy=1.0 | MeanIoU >= 0.98, PixelAccuracy >= 0.98, Failed <= 0.0 |
| ShapeMatching | semisynthetic-geometric-shape-scenes | golden | Yes | 36 | F1=1.0, MeanPositionErrorPx=0.025387 | F1 >= 0.98, MeanPositionErrorPx <= 0.025895, Failed <= 0.0 |
| TemplateMatching | hpatches-style-homography-bridge | golden | Yes | 24 | P95PositionErrorPx=0.0, MeanPositionErrorPx=0.0 | P95PositionErrorPx <= 1.5, Failed <= 0.0 |
| AkazeFeatureMatch | hpatches | public-benchmark | Yes | 80 | PassRate=0.6625, P95PositionErrorPx=367.763511, MeanInliers=312.125 | PassRate >= 0.6, P95PositionErrorPx <= 500.0 |
| OrbFeatureMatch | hpatches | public-benchmark | Yes | 80 | PassRate=0.625, P95PositionErrorPx=301.251294, MeanInliers=262.8375 | PassRate >= 0.25, P95PositionErrorPx <= 700.0 |

## Replay Seeds

- Replay cases: 183
- Replay command: `python quality/tools/run_public_benchmark_proof.py --validate-only`

## Claim Boundary

Do not claim real industrial validation complete from public datasets or semisynthetic protocol bridges.
