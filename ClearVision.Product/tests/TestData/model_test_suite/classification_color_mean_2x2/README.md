# Classification color mean 2x2

Deterministic ONNX fixture for the unified `DeepLearning` classification path.
It averages the RGB channels of a `1x3x2x2` tensor, scales the three logits,
and applies Softmax. Solid red, green, and blue inputs therefore resolve to the
matching class without external weights.

Regenerate with `generate_model.py` after installing the Python `onnx` package.
