# FAQ

## Why does sequence mismatch happen when detections exist?

Use `DetectionSequenceJudge` with the configured `SortBy` and verify that `DeepLearning.OutputFormat` matches the exported ONNX model. The active template expects `EndToEndNms` output from the model.

## What should be tuned first on site?

Tune only these runtime parameters first:

- `DeepLearning.Confidence`

NMS score / IoU thresholds are owned by the exported ONNX model in the active template.

Do not treat these as runtime tuning targets:

- `ExpectedLabels`
- `ExpectedCount`
- model path
- labels path when the model has no metadata names
- `DetectionSequenceJudge.MinConfidence`

## Auto-tune Boundary

- Auto-tune for `wire-sequence-terminal` only changes:
  - `DeepLearning.Confidence`
- Do not auto-change:
  - `ExpectedLabels`
  - `ExpectedCount`
  - `ModelPath`
  - `LabelsPath` unless the model lacks metadata names

## Missing Assets

If preview or auto-tune returns `missing_model` / `missing_labels`:

1. Check whether `DeepLearning.ModelPath` is configured and points to the correct model.
2. If the model does not expose metadata names, configure `DeepLearning.LabelsPath` or place a matching `labels.txt` next to the model.
3. If the repository intentionally keeps model binaries out of source control, confirm the external delivery path first.
4. Do not continue tuning until the resource issue is resolved.

## How to release a new package version?

1. Register new artifact versions (template/model/rule/label).
2. Mark target artifact versions as active.
3. Generate a new manifest with package version update.
4. Add `versions/<newVersion>/release.json`.
