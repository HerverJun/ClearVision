# DeepLearning COCO Real Model Baseline

EvidenceKind: `public-benchmark-real-model`
EvidencePurpose: `InferenceSmokeOnly`
Accepted: `False`
Precision disposition: `FAIL` (smoke-only, zero AP50/precision/recall, checksum mismatch, zero thresholds, no approved delivery manifest)
GeneratedAtUtc: `2026-04-30T01:18:36.0094256+00:00`
Dataset: `COCO 2017 real validation images`
DatasetKind: `COCO real-image inference with ONNX Runtime model outputs; annotation-seeded tensors are not used.`

## Summary

| Metric | Value |
| --- | ---: |
| Cases | 20 |
| Processed cases | 20 |
| Processing failed cases | 0 |
| Matched cases | 0 |
| Unmatched cases | 20 |
| Ground truth boxes | 90 |
| True positives | 0 |
| False positives | 20 |
| False negatives | 90 |
| Precision@0.50 | 0 |
| Recall@0.50 | 0 |
| AP50 | 0 |
| Min Precision@0.50 | 0 |
| Min Recall@0.50 | 0 |
| Min AP50 | 0 |
| Mean matched IoU | 0 |
| Runtime ms | 435.848 |
| Session create ms | 10.457 |
| Real ONNX inference | True |
| Annotation seeded | False |

## Model

| Field | Value |
| --- | --- |
| ModelId | `clearvision_yolov8_constant_smoke` |
| Model artifact | `generated-smoke-fixture` |
| Model SHA256 | `4c80a803ef8e2a3b931dfcfe77b0ef91a7f95b1735680e5a700b52cc488f3cbd` |
| Expected SHA256 | `` |
| SHA256 matched | `False` |
| Provider | `CPUExecutionProvider` |
| CandidateVersion | `baseline` |
| Profile | `real_model_hard_nms_045` |
| Git SHA / dirty | `376174d830621d284c0d5da0b40a9b6c219a9150` / `True` |
| Dataset index checksum | `79a914cd475c7beb0e600b44412382c202e708a5c6c746d892ac1aab67e1a072` |
| Replay status | `unavailable: COCO annotation/image payload is absent from this checkout` |

## Claim Boundary

- This report uses real ONNX Runtime model outputs. Annotation-seeded tensors are not used.
- COCO public benchmark evidence is not real production-site validation or sign-off.
- AP50/precision/recall are frozen from the supplied model artifact and must not be compared to annotation-seeded proof as model accuracy.

## Cases

| Case | Categories | Passed | ProcessingError | Size | GT | Detections | TP | FP | FN | Best IoU | Runtime ms | Output shape | Failure |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| coco2017_val_139 | person | False | False | 640x426 | 2 | 1 | 0 | 1 | 2 | 0 | 65.896 | 1x7x32 | FP=1, FN=2, detections=1, gt=2 |
| coco2017_val_724 | car | False | False | 375x500 | 1 | 1 | 0 | 1 | 1 | 0 | 21.467 | 1x7x32 | FP=1, FN=1, detections=1, gt=1 |
| coco2017_val_785 | person | False | False | 640x425 | 1 | 1 | 0 | 1 | 1 | 0 | 18.18 | 1x7x32 | FP=1, FN=1, detections=1, gt=1 |
| coco2017_val_872 | person | False | False | 621x640 | 2 | 1 | 0 | 1 | 2 | 0 | 20.303 | 1x7x32 | FP=1, FN=2, detections=1, gt=2 |
| coco2017_val_885 | person | False | False | 640x427 | 8 | 1 | 0 | 1 | 8 | 0 | 18.202 | 1x7x32 | FP=1, FN=8, detections=1, gt=8 |
| coco2017_val_1000 | person | False | False | 640x480 | 12 | 1 | 0 | 1 | 12 | 0 | 25.242 | 1x7x32 | FP=1, FN=12, detections=1, gt=12 |
| coco2017_val_1268 | person | False | False | 640x427 | 4 | 1 | 0 | 1 | 4 | 0 | 25.55 | 1x7x32 | FP=1, FN=4, detections=1, gt=4 |
| coco2017_val_1296 | person | False | False | 427x640 | 2 | 1 | 0 | 1 | 2 | 0 | 21.151 | 1x7x32 | FP=1, FN=2, detections=1, gt=2 |
| coco2017_val_1353 | person | False | False | 375x500 | 6 | 1 | 0 | 1 | 6 | 0 | 16.528 | 1x7x32 | FP=1, FN=6, detections=1, gt=6 |
| coco2017_val_1490 | person | False | False | 640x315 | 1 | 1 | 0 | 1 | 1 | 0 | 17.009 | 1x7x32 | FP=1, FN=1, detections=1, gt=1 |
| coco2017_val_1532 | car | False | False | 640x480 | 7 | 1 | 0 | 1 | 7 | 0 | 18.044 | 1x7x32 | FP=1, FN=7, detections=1, gt=7 |
| coco2017_val_1584 | person | False | False | 612x612 | 11 | 1 | 0 | 1 | 11 | 0 | 17.988 | 1x7x32 | FP=1, FN=11, detections=1, gt=11 |
| coco2017_val_1761 | person | False | False | 427x640 | 5 | 1 | 0 | 1 | 5 | 0 | 17.356 | 1x7x32 | FP=1, FN=5, detections=1, gt=5 |
| coco2017_val_2006 | person | False | False | 640x480 | 3 | 1 | 0 | 1 | 3 | 0 | 18.778 | 1x7x32 | FP=1, FN=3, detections=1, gt=3 |
| coco2017_val_2153 | person | False | False | 640x480 | 4 | 1 | 0 | 1 | 4 | 0 | 18.429 | 1x7x32 | FP=1, FN=4, detections=1, gt=4 |
| coco2017_val_2261 | person | False | False | 640x427 | 1 | 1 | 0 | 1 | 1 | 0 | 17.382 | 1x7x32 | FP=1, FN=1, detections=1, gt=1 |
| coco2017_val_2299 | person | False | False | 500x302 | 13 | 1 | 0 | 1 | 13 | 0 | 18.182 | 1x7x32 | FP=1, FN=13, detections=1, gt=13 |
| coco2017_val_2431 | person | False | False | 457x640 | 2 | 1 | 0 | 1 | 2 | 0 | 20.118 | 1x7x32 | FP=1, FN=2, detections=1, gt=2 |
| coco2017_val_2473 | person | False | False | 640x427 | 4 | 1 | 0 | 1 | 4 | 0 | 19.372 | 1x7x32 | FP=1, FN=4, detections=1, gt=4 |
| coco2017_val_2532 | person | False | False | 480x640 | 1 | 1 | 0 | 1 | 1 | 0 | 20.671 | 1x7x32 | FP=1, FN=1, detections=1, gt=1 |
