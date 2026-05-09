# Model Repository

This directory is the model and feature-library catalog entry point for ClearVision. The canonical index is `models/model_catalog.json`.

The repository only carries lightweight test assets by default so CI and demos can resolve `ModelId` values without committing production model binaries.

- `SemanticSegmentation` uses catalog entries to resolve ONNX segmentation models.
- `AnomalyDetection` uses catalog entries to resolve embedding models or feature libraries.
- `DeepLearning` real-model evaluations use manifests that record `modelId`, `modelSha256`, license, class/label contract, input shape, preprocessing and postprocessing.
- Production ONNX weights stay outside git unless their license and size are explicitly approved.

## Release Gate

Every external model attached to a release or field package must include these fields in its manifest or generated catalog entry:

| Field | Requirement |
| --- | --- |
| `modelSha256` | SHA-256 of the exact model binary. |
| `license` | SPDX expression or reviewed upstream license note. |
| `labelsContract` | Class order, label names and any metadata-name fallback. |
| `providerFallback` | CPU/GPU execution-provider fallback policy. |
| `datasetVersion` | Dataset or sample-pack version used for acceptance. |
| `hardwareProfile` | CPU/GPU/driver profile used for the report. |
| `reportId` | Linked quality, smoke or release-gate report. |

`object_detection/coco_yolo_real_model_manifest.template.json` is the template for the DeepLearning COCO real-model runner. When landing a real model:

1. Put the ONNX model outside the repo or in an ignored path.
2. Fill `modelSha256`, `source`, `license`, `labelsContract` and IO schema.
3. Run `quality/tools/DeepLearningCocoRealModelRunner` with `--model` pointing to the local artifact.
4. Keep `AnnotationSeeded=false` in the report, and do not describe public COCO results as production-line sign-off.

## Path Rules

`model_catalog.json` supports:

- absolute paths,
- paths relative to `models/`,
- paths relative to the repository root.

Real business models and large feature assets should be deployed outside the repository, then mounted through an absolute path or a deployment-generated catalog.
