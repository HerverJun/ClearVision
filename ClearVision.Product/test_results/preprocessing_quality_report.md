# Preprocessing Quality Report

Generated (UTC): 2026-08-04T02:11:09.2529449Z

| Case | Operator | Metric | Before | After | Delta | Expectation |
|---|---|---|---:|---:|---:|---|
| reference_shapes_saltpepper | MedianBlur | MAE | 6.4106 | 0.1080 | -6.3026 | lower_is_better |
| reference_shapes_saltpepper | MedianBlur | PSNR | 16.0119 | 38.6025 | 22.5906 | higher_is_better |
| reference_shapes_frame_stack | FrameAveraging | MAE | 7.2520 | 7.1702 | -0.0818 | lower_is_better |
| reference_shapes_frame_stack | FrameAveraging | PSNR | 25.9923 | 29.4518 | 3.4595 | higher_is_better |
| real_wire_sequence | ClaheEnhancement | RMSContrast | 64.3921 | 67.1145 | 2.7224 | higher_is_better |
| real_wire_sequence | ClaheEnhancement | Entropy | 7.5997 | 7.7743 | 0.1746 | higher_is_better |
| real_wire_sequence | HistogramEqualization | RMSContrast | 64.3921 | 73.1772 | 8.7851 | higher_is_better |
| real_wire_sequence | HistogramEqualization | Sharpness | 1980.4888 | 2480.2525 | 499.7637 | higher_is_better |
| real_wire_sequence | ShadingCorrection | IlluminationCV | 0.4736 | 0.1110 | -0.3626 | lower_is_better |
| real_wire_sequence | ShadingCorrection | Sharpness | 1980.4888 | 1743.1593 | -237.3294 | higher_is_better |
