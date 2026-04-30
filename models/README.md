# 模型仓库 / Model Repository

该目录用于承载阶段3的模型与特征库索引，入口文件为 `models/model_catalog.json`。

当前仓库内默认附带的是轻量级测试资产，便于：

- 语义分割算子通过 `ModelId` 直接解析 ONNX 模型
- 异常检测算子通过 `ModelId` 解析特征库文件
- DeepLearning 真实模型评估通过 manifest 记录 `modelId`、`modelSha256`、license、classes、input shape、preprocess 和 postprocess；ONNX 权重文件不进 git
- 自动化测试和 Demo 文档引用统一模型索引

推荐目录结构：

```text
models/
  model_catalog.json
  segmentation/
  anomaly_detection/
  object_detection/
```

`object_detection/coco_yolo_real_model_manifest.template.json` 是 DeepLearning COCO real-model runner 的模板。落地真实模型时：

1. 将 ONNX 模型放在 repo 外部或 ignored 路径。
2. 填写 manifest 中的 `modelSha256`、`source`、`license` 和 IO schema。
3. 运行 `quality/tools/DeepLearningCocoRealModelRunner` 并通过 `--model` 指向本地模型。
4. 报告中必须保持 `AnnotationSeeded=false`，且不得把公开 COCO 结果写成真实产线签核。

`model_catalog.json` 中的 `path` 字段支持：

- 绝对路径
- 相对 `models/` 目录的路径
- 相对仓库根目录的路径

真实业务模型与大文件资产建议按需放置在仓库外部，再通过绝对路径或部署时生成的 catalog 挂载。
