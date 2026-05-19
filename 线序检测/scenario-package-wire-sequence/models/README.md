# Models

This scenario package now targets a two-wire terminal sequence contract:

- `Wire_Black`
- `Wire_Blue`

The recommended external model artifact name is:

- `wire-seq-yolo-nms-v1.3.onnx`

The repository does not commit the actual ONNX binary. This folder keeps only the
contract for package metadata and local deployment paths.

## Local placement

Place the trained model at:

- `线序检测/scenario-package-wire-sequence/models/wire-seq-yolo-nms-v1.3.onnx`

Or point `DeepLearning.ModelPath` to any valid external ONNX file.

The active 1.6 template sets `DeepLearning.OutputFormat=EndToEndNms`, so the
model must emit compact `[x1,y1,x2,y2,score,class]` detections rather than raw
YOLO anchor tensors.

## Label alignment

The model output class order must match [labels.txt](../labels/labels.txt):

1. `Wire_Blue`
2. `Wire_Black`

The inspection business order is still:

1. `Wire_Black`
2. `Wire_Blue`

Do not use the business order as the model class-order labels.

If the training/export label order changes, update these files together:

- `manifest.json`
- `rules/sequence-rule.v1.json`
- `template/terminal-wire-sequence.flow.template.json`
- `labels/labels.txt`
- `versions/<packageVersion>/release.json`
