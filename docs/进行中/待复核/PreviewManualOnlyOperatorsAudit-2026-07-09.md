# 预览手动执行算子审计（2026-07-09）

## 审计范围

本轮只做静态审计统计，未修改业务代码、预览逻辑或测试代码。检索覆盖前端预览协调器、旧预览面板、预览工作台 owner、属性面板触发点、后端预览端点、执行准入、执行服务、UI/后端测试、算子目录和进行中文档。

重点检索关键词已覆盖：`autoPreviewAllowed`、`HIGH_COST_OPERATOR_TYPE_HINTS`、`HIGH_COST_TEXT_HINTS`、`getOperatorPreviewCostPolicy`、`isLiveCameraAcquisitionNode`、`manual preview`、`手动预览`、`刷新预览`、`high cost`、`previewCost`、`preview eligibility`、`no-image-output`、`side-effect`、`external I/O`、`persistent side effects`。

表格中的路径缩写：

- `previewCoordinator.js` = `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewCoordinator.js`
- `previewPanelCapabilityOwner.mjs` = `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanelCapabilityOwner.mjs`
- `previewPanel.js` = `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanel.js`
- `PreviewNodeEndpoints.cs` = `ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewNodeEndpoints.cs`
- `ExecutionAdmissionService.cs` = `ClearVision.Product/src/ClearVision.Product.Core/Services/ExecutionAdmissionService.cs`
- `OperatorPreviewService.cs` = `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorPreviewService.cs`
- `FlowExecutionService.cs` = `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`

## 核心结论

当前真正把自动预览短路成“需手动预览”的前端代码只有 `getOperatorPreviewCostPolicy()` 两类：高成本规则和 `ImageAcquisition + SourceType=camera + 已绑定相机`。自动请求在 `requestActivePreview()` 中遇到 `autoPreviewAllowed=false` 会直接置为 idle，不会发后端预览请求。

无图像输出不是同一个概念：`getCanvasPreviewEligibility()` 返回 `no-image-output` 只说明没有画布/卡片图像资格，不等于自动预览不会执行。报告仍按用户要求列入 D 类，但在“当前是否自动预览”中标为“是，但无图像画布资格”。

副作用/I/O 现在主要由后端准入拦截：`ExecutionAdmissionService` 会阻断 NodePreview 和 OperatorPreview 中的外部 I/O、文件、数据库、PLC/通信、标定加载/求解，以及 `ImageAcquisition SourceType=Camera`。因此这些不是“必须手动后就能真实执行”，而是自动和手动真实预览都应被阻断或改为 dry-run。

另有隐藏风险：`/api/autotune/flow-node/preview` 目前未检索到 `ExecutionAdmissionService` 准入调用，它会把 FlowData 转成 preview entity 后通过 `FlowNodePreviewService` 执行到目标节点。若旧入口或专用分析入口仍可达，含上游副作用节点的流程需要补准入。

## 统计摘要

- A 类直接高成本命中：正式算子 10 个；另有 `OnnxInference` 是 legacy alias，`TemplateMatch`/`FeatureMatch` 是提示词或基类/别名性质，不是独立正式算子。
- B 类 metadata/text 高成本命中：22 个，包含多处 `ai`、`feature`、`matching` 子串误命中。
- C 类运行参数命中：`ImageAcquisition` 的 camera 模式 1 类；file 模式允许自动预览。
- D 类无图像输出：按 `docs/operator_catalog.json` 的正式元数据统计为 59 个。
- E 类副作用/I/O 准入阻断：`AlwaysBlockedSideEffectTypes` 正式算子 18 个 + `ModbusRtuCommunication` legacy alias；`ResultOutput SaveToFile=true` 是条件阻断。
- F 类其他隐藏/状态逻辑：自动预览 checkbox、旧面板折叠、节点 disabled、缺项目、输入图过大、缺输入/采集源、AutoSafeParallel 状态/副作用名单、`/autotune/flow-node/preview` 缺准入。

## 审计总表

| 算子类型 | 中文名/显示名 | 所属分类 | 当前是否自动预览 | 为什么被禁用/跳过 | 代码来源 | 是否建议后续恢复自动预览 | 建议理由 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| ImageAcquisition | 图像采集 | C 运行参数 / F 状态/并行风险 | File 是；Camera 否 | SourceType=camera 且 CameraId/CameraBindingId 非空触发真实取帧；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:492-500,511-533；ExecutionAdmissionService.cs:239-249<br>FlowExecutionService.cs:43-77,502-575 | 需要产品决策 | File 模式可继续自动；Camera 模式应保持手动或受控自动采帧。 | 高 |
| AkazeFeatureMatch | AKAZE特征匹配 | A 直接高成本 | 否（auto 被前端置 idle） | node.type 命中 AkazeFeatureMatch,FeatureMatch | previewCoordinator.js:22-35,343-382,1326-1338 | 需要技术验证 | 匹配成本依图像、模板和参数变化较大，建议带 debounce 与超时灰度恢复。 | 中 |
| AnomalyDetection | 异常检测 | A 直接高成本 / F 状态/并行风险 | 否（auto 被前端置 idle） | node.type 命中 AnomalyDetection；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:22-35,343-382,1326-1338<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | AI/ONNX/缺陷检测仍可能占用 GPU/CPU 和模型资源，可先做节流/缓存验证。 | 高 |
| DeepLearning | 深度学习 | A 直接高成本 / F 状态/并行风险 | 否（auto 被前端置 idle） | node.type 命中 DeepLearning；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:22-35,343-382,1326-1338<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | AI/ONNX/缺陷检测仍可能占用 GPU/CPU 和模型资源，可先做节流/缓存验证。 | 高 |
| OcrRecognition | OCR 识别 | A 直接高成本 / D 无图像输出 | 否（auto 被前端置 idle） | node.type 命中 OcrRecognition；metadata outputPorts 无 Image | previewCoordinator.js:22-35,343-382,1326-1338<br>previewCoordinator.js:284-314 | 需要技术验证 | OCR 没有图像输出但可能调用识别模型，适合先做小图/超时验证。 | 中 |
| OrbFeatureMatch | ORB特征匹配 | A 直接高成本 | 否（auto 被前端置 idle） | node.type 命中 FeatureMatch | previewCoordinator.js:22-35,343-382,1326-1338 | 需要技术验证 | 特征匹配成本依图像和特征数量变化，建议带缓存和超时灰度恢复。 | 中 |
| PlanarMatching | Planar Matching | A 直接高成本 | 否（auto 被前端置 idle） | node.type 命中 PlanarMatching | previewCoordinator.js:22-35,343-382,1326-1338 | 需要技术验证 | 平面匹配依赖特征提取和单应性估计，恢复前需要性能阈值。 | 中 |
| SemanticSegmentation | 语义分割 | A 直接高成本 / F 状态/并行风险 | 否（auto 被前端置 idle） | node.type 命中 SemanticSegmentation；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:22-35,343-382,1326-1338<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | ONNX 分割通常消耗模型资源，需验证节流、取消和缓存。 | 高 |
| ShapeMatching | 旋转尺度模板匹配 | A 直接高成本 | 否（auto 被前端置 idle） | node.type 命中 ShapeMatching | previewCoordinator.js:22-35,343-382,1326-1338 | 需要技术验证 | 旋转/尺度搜索成本受参数范围影响，建议分阶段恢复。 | 中 |
| SurfaceDefectDetection | 表面缺陷检测 | A 直接高成本 | 否（auto 被前端置 idle） | node.type 命中 SurfaceDefectDetection | previewCoordinator.js:22-35,343-382,1326-1338 | 需要技术验证 | 缺陷检测可能含复杂纹理/模型流程，需性能和资源验证。 | 高 |
| TemplateMatching | 模板匹配 | A 直接高成本 | 否（auto 被前端置 idle） | node.type 命中 TemplateMatch,TemplateMatching | previewCoordinator.js:22-35,343-382,1326-1338 | 需要技术验证 | 经典模板匹配可先在小图/低频参数变更场景灰度恢复。 | 中 |
| BlobLabeling | 连通域标注 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 feature | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 更像子串误命中，未见副作用准入阻断。 | 低 |
| BoxFilter | 候选框过滤 (Bounding Box) | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 后处理筛选通常轻量，适合作为第一批恢复。 | 低 |
| BoxNms | 候选框抑制 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | NMS 是后处理，建议恢复结构化自动预览并保留超时。 | 低 |
| CaliperTool | 卡尺工具 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 命中来自文本子串，卡尺预览应回归自动。 | 低 |
| DetectionSequenceJudge | 检测顺序判定 | B 文本高成本 / D 无图像输出 | 否（auto 被前端置 idle） | metadata 命中 ai；metadata outputPorts 无 Image | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314 | 建议恢复 | 检测序列判定是结构化后处理，适合自动刷新摘要。 | 低 |
| DualModalVoting | Dual Modal Voting | B 文本高成本 / D 无图像输出 | 否（auto 被前端置 idle） | metadata 命中 ai,deep learning；metadata outputPorts 无 Image | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314 | 建议恢复 | 投票融合本身偏结构化，建议恢复但保留输入规模限制。 | 低 |
| EdgePairDefect | 边缘对缺陷 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai,defect | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 名称含 defect 但不是后端副作用，应以耗时数据决定。 | 低 |
| FrequencyFilter | Frequency Filter | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 误命中概率高，可先恢复自动图像预览。 | 低 |
| GeometricTolerance | 几何公差 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai,feature | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 几何公差不应因文本子串进入高成本手动名单。 | 低 |
| GlcmTexture | GLCM Texture Features | B 文本高成本 / D 无图像输出 | 否（auto 被前端置 idle） | metadata 命中 feature；metadata outputPorts 无 Image | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314 | 建议恢复 | 结构化特征输出可自动预览，必要时限制 ROI/图像尺寸。 | 低 |
| HistogramAnalysis | 直方图分析 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 直方图分析属于轻量统计，建议恢复。 | 低 |
| LineMeasurement | 直线测量 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 feature | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 测量类命中文本规则不合理，建议恢复。 | 低 |
| LocalDeformableMatching | Local Deformable Matching | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 matching | previewCoordinator.js:36-47,326-366,1326-1338 | 需要技术验证 | 局部形变匹配可能较重，需性能数据后决定。 | 中 |
| NPointCalibration | N Point Calibration | B 文本高成本 / D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata 命中 ai；metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59 | 保持手动 | 标定求解/落盘应受控，预览应做 dry-run 或草稿工作台。 | 高 |
| PPFEstimation | PPF点对特征 | B 文本高成本 / D 无图像输出 | 否（auto 被前端置 idle） | metadata 命中 ai,feature；metadata outputPorts 无 Image | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314 | 建议恢复 | 可先恢复结构化摘要，超大点云另设阈值。 | 低 |
| PPFMatch | PPF表面匹配 | B 文本高成本 / D 无图像输出 | 否（auto 被前端置 idle） | metadata 命中 matching；metadata outputPorts 无 Image | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314 | 需要技术验证 | 3D 匹配可能重，恢复前需点云规模和超时策略。 | 中 |
| ParallelLineFind | 平行线查找 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 定位几何类不应由 `ai` 子串拦截。 | 低 |
| PhaseClosure | Phase Closure | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 当前像误命中，建议恢复并观察耗时。 | 低 |
| QuadrilateralFind | 四边形查找 | B 文本高成本 | 否（auto 被前端置 idle） | metadata 命中 ai | previewCoordinator.js:36-47,326-366,1326-1338 | 建议恢复 | 几何查找不应由文本子串阻断自动预览。 | 低 |
| RansacPlaneSegmentation | RANSAC平面分割 | B 文本高成本 / D 无图像输出 | 否（auto 被前端置 idle） | metadata 命中 segmentation；metadata outputPorts 无 Image | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314 | 需要技术验证 | RANSAC 点云规模敏感，需点数阈值和超时。 | 中 |
| StereoCalibration | Stereo Calibration | B 文本高成本 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata 命中 ai；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:36-47,326-366,1326-1338<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 双目标定读取样本并求解，需工作台或 dry-run。 | 高 |
| TranslationRotationCalibration | 平移旋转标定 | B 文本高成本 / D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata 命中 ai；metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:36-47,326-366,1326-1338<br>previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 标定数据写入/求解应受控，建议 dry-run 展示参数有效性。 | 高 |
| CalibrationLoader | Calibration Loader | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 加载本机文件属于外部 I/O，应改 dry-run 显示路径/格式检查。 | 高 |
| CameraCalibration | Camera Calibration | E 副作用/I/O准入 | 否/条件否（后端准入） | AlwaysBlockedSideEffectTypes 阻断真实预览执行 | ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 会读取样本目录并可能写标定结果，必须受控。 | 高 |
| DatabaseWrite | 数据库写入 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 真实预览会写数据库；后续只应 dry-run SQL/参数摘要。 | 高 |
| FisheyeCalibration | Fisheye Calibration | E 副作用/I/O准入 | 否/条件否（后端准入） | AlwaysBlockedSideEffectTypes 阻断真实预览执行 | ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59 | 保持手动 | 会读取标定图片并写 bundle，适合独立工作台。 | 高 |
| HandEyeCalibration | Hand-Eye Calibration | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 标定求解高风险且无图像输出，应 dry-run 或手动。 | 高 |
| HttpRequest | HTTP 请求 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 真实预览会访问外部/内网 HTTP；只应 dry-run 请求摘要。 | 高 |
| ImageSave | 图像保存 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 真实预览会创建目录/写图片；可做输出路径 dry-run。 | 高 |
| MitsubishiMcCommunication | Mitsubishi MC Communication | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | PLC 通信可能真实读写设备，预览只能 dry-run。 | 高 |
| ModbusCommunication | Modbus Communication | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | Modbus 可能真实读写 PLC，预览只能 dry-run。 | 高 |
| ModbusRtuCommunication | legacy alias | E 副作用/I/O准入 / legacy alias | 否/条件否（后端准入） | AlwaysBlockedSideEffectTypes 阻断真实预览执行；legacy alias -> ModbusCommunication | ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575<br>OperatorTypeAliasResolver.cs:5-12；OperatorMetadataMigrationTests.cs:53-63 | 保持手动 | legacy alias 仍映射到 Modbus，按 PLC I/O 处理。 | 高 |
| MqttPublish | MQTT Publish | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 发布消息是外部副作用，只应 dry-run topic/payload。 | 高 |
| OmronFinsCommunication | 欧姆龙FINS通信 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | FINS 通信可能真实读写 PLC，预览只能 dry-run。 | 高 |
| ResultOutput | 结果输出 | E 副作用/I/O准入 | 否/条件否（后端准入） | SaveToFile=true 时阻断；false 时仍无 Image 输出端口 | ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | SaveToFile=false 可普通结构化预览；SaveToFile=true 必须继续阻断或 dry-run。 | 中 |
| SerialCommunication | 串口通信 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 串口收发会接触真实设备，预览只能 dry-run。 | 高 |
| SiemensS7Communication | 西门子S7通信 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | S7 通信可能真实读写 PLC，预览只能 dry-run。 | 高 |
| TcpCommunication | TCP通信 | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | TCP 通信会连接外部端点，预览只能 dry-run。 | 高 |
| TextSave | Text Save | D 无图像输出 / E 副作用/I/O准入 | 否/条件否（后端准入） | metadata outputPorts 无 Image；AlwaysBlockedSideEffectTypes 阻断真实预览执行 | previewCoordinator.js:284-314<br>ExecutionAdmissionService.cs:79-100,222-237；PreviewNodeEndpoints.cs:167-175；OperatorPreviewService.cs:49-59<br>FlowExecutionService.cs:43-77,502-575 | 保持手动 | 真实预览会创建/追加文本文件；只应 dry-run 路径和内容摘要。 | 高 |
| CycleCounter | 循环计数器 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 状态计数类预览需隔离上下文或 dry-run。 | 中 |
| ForEach | ForEach 循环 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 子图循环可能放大成本和副作用，需先补准入。 | 中 |
| FrameAveraging | 帧平均 | F 状态/并行风险 | 当前前端未必阻断 | AutoParallelBlockedOperatorTypes 标记状态/重算风险 | FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 帧缓存/状态语义需确认，不能简单放开。 | 中 |
| FrameChangeTrigger | 帧变化触发 | F 状态/并行风险 | 当前前端未必阻断 | AutoParallelBlockedOperatorTypes 标记状态/重算风险 | FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 触发类算子有时序语义，预览需模拟触发而非真实触发。 | 中 |
| OnnxInference | legacy alias | F 状态/并行风险 / legacy alias | 当前前端未必阻断 | AutoParallelBlockedOperatorTypes 标记状态/重算风险；legacy alias -> DeepLearning | FlowExecutionService.cs:43-77,502-575<br>OperatorTypeAliasResolver.cs:5-12；OperatorMetadataMigrationTests.cs:53-63 | 需要技术验证 | legacy alias 不在正式目录，按 DeepLearning 策略处理。 | 中 |
| ScriptOperator | 脚本算子 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 脚本可能包含任意逻辑，预览应沙箱或 dry-run。 | 中 |
| TimerStatistics | 计时统计 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 状态统计类预览应隔离运行态。 | 中 |
| TriggerModule | 触发模块 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 触发输出不能在预览中产生生产语义。 | 中 |
| VariableIncrement | 变量递增 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 会改变变量语义，应 dry-run 或隔离变量 session。 | 中 |
| VariableWrite | 变量写入 | D 无图像输出 / F 状态/并行风险 | 是，但无图像画布资格 | metadata outputPorts 无 Image；AutoParallelBlockedOperatorTypes 标记状态/重算风险 | previewCoordinator.js:284-314<br>FlowExecutionService.cs:43-77,502-575 | 需要技术验证 | 变量写入是状态副作用，应阻断真实写入或 dry-run。 | 中 |
| Aggregator | 数据聚合 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 不是 manual-only，主要缺图像画布资格；可补结构化自动预览体验。 | 低 |
| ArrayIndexer | 数组索引器 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 不是 manual-only，建议只优化结构化摘要。 | 低 |
| Comment | 注释 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 注释无需真实预览图，可保持结构化展示。 | 低 |
| Comparator | 数值比较 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 适合自动结构化预览，不需要手动限制。 | 低 |
| ConditionalBranch | 条件分支 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 可展示条件结果摘要，无需图像画布。 | 低 |
| Delay | 延时 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 延时不适合真实等待式预览，建议 dry-run 展示配置。 | 低 |
| EdgeIntersection | 边线交点 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出点/角度可自动结构化展示。 | 低 |
| EuclideanClusterExtraction | 欧氏聚类分割 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 点云类无图像画布，需产品决定 3D/表格预览形式。 | 低 |
| GeoMeasurement | 几何测量 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出距离/角度摘要即可自动展示。 | 低 |
| HandEyeCalibrationValidator | Hand-Eye Calibration Validator | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 验证器输出报告/误差，需结构化预览而非图像卡片。 | 低 |
| JsonExtractor | JSON 提取器 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 可自动展示提取值，不需要手动限制。 | 低 |
| LineLineDistance | 线线距离 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出测量值，适合结构化自动预览。 | 低 |
| LogicGate | 逻辑门 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 逻辑结果可自动展示。 | 低 |
| MathOperation | 数值计算 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 数值结果可自动展示。 | 低 |
| PixelStatistics | 像素统计 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 统计值可自动展示，图像画布不是必要条件。 | 低 |
| PointAlignment | 点位对齐 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出偏移量，适合结构化预览。 | 低 |
| PointCorrection | 点位修正 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出修正参数，适合结构化预览。 | 低 |
| PointLineDistance | 点线距离 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出距离/垂足，适合结构化预览。 | 低 |
| PointSetTool | 点集工具 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 点集输出需要表格/覆盖层策略。 | 低 |
| PositionCorrection | 位置修正 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 输出位姿/补偿值，适合结构化预览。 | 低 |
| ResultJudgment | Result Judgment | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | OK/NG 结果应自动结构化展示。 | 低 |
| RoiTransform | ROI跟踪 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | ROI 跟踪结果需要覆盖层/结构化展示。 | 低 |
| StatisticalOutlierRemoval | 统计滤波 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 点云输出不适合图像卡片，需 3D/摘要预览。 | 低 |
| Statistics | Statistics | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 统计结果可自动结构化展示。 | 低 |
| StringFormat | 字符串格式化 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 字符串结果可自动展示。 | 低 |
| TryCatch | 异常捕获 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 可展示 try/catch 分支状态，不需要图像。 | 低 |
| TypeConvert | Type Convert | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 类型转换结果可自动展示。 | 低 |
| UnitConvert | 单位换算 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 单位换算结果可自动展示。 | 低 |
| VariableRead | 变量读取 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 读取结果可自动展示，但需注意变量 session 语义。 | 低 |
| VoxelDownsample | 体素下采样 | D 无图像输出 | 是，但无图像画布资格 | metadata outputPorts 无 Image | previewCoordinator.js:284-314 | 需要产品决策 | 点云输出需 3D/摘要预览，不是手动限制问题。 | 低 |

## HIGH_COST_OPERATOR_TYPE_HINTS 直接命中

`HIGH_COST_OPERATOR_TYPE_HINTS` 位于 `previewCoordinator.js:22-35`。按当前正式算子目录和节点类型字符串核对，直接命中的正式算子是：

| hint | 当前实际命中 | 核对结论 |
|---|---|---|
| DeepLearning | DeepLearning | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:74`。 |
| OnnxInference | OnnxInference | 枚举仍存在，但是 legacy alias，映射到 DeepLearning；不在正式 156 算子目录中，见 `OperatorTypeAliasResolver.cs:5-12`、`OperatorMetadataMigrationTests.cs:53-63`。 |
| OcrRecognition | OcrRecognition | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:250`。 |
| TemplateMatch | TemplateMatching | 没有独立正式 `TemplateMatch` 枚举；`TemplateMatchOperator.cs` 暴露 `OperatorType.TemplateMatching`，目录见 `docs/OPERATOR_CATALOG.md:142`。 |
| TemplateMatching | TemplateMatching | 正式存在。 |
| ShapeMatching | ShapeMatching | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:141`。 |
| PlanarMatching | PlanarMatching | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:139`。 |
| AkazeFeatureMatch | AkazeFeatureMatch | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:135`。 |
| FeatureMatch | AkazeFeatureMatch、OrbFeatureMatch | 没有独立正式 `FeatureMatch` 算子；该 hint 会按子串命中 AKAZE 和 ORB 特征匹配，目录见 `docs/OPERATOR_CATALOG.md:135,138`。 |
| SemanticSegmentation | SemanticSegmentation | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:76`。 |
| SurfaceDefectDetection | SurfaceDefectDetection | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:77`。 |
| AnomalyDetection | AnomalyDetection | 正式存在，目录见 `docs/OPERATOR_CATALOG.md:73`。 |

补充：`HIGH_COST_TEXT_HINTS` 位于 `previewCoordinator.js:36-47`，使用子串匹配。当前 metadata 口径下会额外命中 22 个 B 类算子，其中 `ai` 会误伤若干包含该字母组合或 AI 分类文案的算子，`feature`/`matching` 也会误伤部分轻量特征/测量算子。

## ImageAcquisition 专项

- SourceType=file：前端 `isLiveCameraAcquisitionNode()` 不命中，`getOperatorPreviewCostPolicy()` 返回 `autoPreviewAllowed=true`。`validatePreviewPrerequisites()` 只要求 FilePath 存在；缺 FilePath 时返回“请先配置文件路径”一类 idle 错误，不是手动预览策略。
- SourceType=camera 且有 CameraId/CameraBindingId：前端 `isLiveCameraAcquisitionNode()` 返回 true，`autoPreviewAllowed=false`，自动请求不会发出，提示点击手动预览。代码来源：`previewCoordinator.js:492-500,343-359,1326-1338`。
- SourceType=camera 但未选择相机：前置校验优先返回缺相机，单测覆盖见 `preview-coordinator-memory.test.mjs:330-355`。
- 后端当前更严格：`ExecutionAdmissionService.TryCreateViolation()` 会阻断 NodePreview/OperatorPreview 中 `ImageAcquisition SourceType=Camera`，原因是访问本地相机硬件，见 `ExecutionAdmissionService.cs:239-249`。这意味着真实相机预览不应简单放开；若产品需要“受控自动”，应设计采样频率、权限、设备占用、取消、缓存和相机状态门禁。

建议：file 模式继续允许自动预览；camera 模式保持手动或做受控自动（例如仅使用已授权模拟帧/最近帧、显式用户开启、冷却时间、只读不触发外设、可取消）。

## 副作用算子专项

当前后端明确阻断的副作用/I/O 类型来自 `ExecutionAdmissionService.AlwaysBlockedSideEffectTypes`：`HttpRequest`、`TextSave`、`ImageSave`、`DatabaseWrite`、`TcpCommunication`、`SerialCommunication`、`ModbusCommunication`、`ModbusRtuCommunication`、`SiemensS7Communication`、`MitsubishiMcCommunication`、`OmronFinsCommunication`、`MqttPublish`、`CameraCalibration`、`FisheyeCalibration`、`StereoCalibration`、`NPointCalibration`、`TranslationRotationCalibration`、`HandEyeCalibration`、`CalibrationLoader`。`ResultOutput SaveToFile=true` 是条件阻断。

分组建议：

- 保持阻断真实执行：`ImageSave`、`TextSave`、`DatabaseWrite`、`HttpRequest`、`MqttPublish`、`TcpCommunication`、`SerialCommunication`、`ModbusCommunication`、`SiemensS7Communication`、`MitsubishiMcCommunication`、`OmronFinsCommunication`、`ModbusRtuCommunication`、`ImageAcquisition SourceType=camera`。
- 可做安全 dry-run：`ImageSave`/`TextSave` 展示目标路径和文件名；`DatabaseWrite` 展示连接/表/字段映射但不连接；`HttpRequest` 展示 method/url/body 摘要但不发送；`MqttPublish` 展示 topic/payload；PLC/TCP/串口展示目标地址、寄存器/命令摘要；`ResultOutput SaveToFile=true` 展示将写入格式和目标。
- 可恢复普通自动预览：`ResultOutput SaveToFile=false`、纯结构化后处理类（如 `DetectionSequenceJudge`、`BoxFilter`、`BoxNms`）在确认没有上游副作用后可恢复自动结构化预览。
- 需要技术验证或专用工作台：标定类、点云/3D 匹配类、脚本类、变量写入/计数/触发类。

隐藏缺口：`/api/autotune/flow-node/preview` 在 `AutoTuneEndpoints.cs:171-183` 直接调用 `FlowNodePreviewService.PreviewWithMetricsAsync()`，后者在 `FlowNodePreviewService.cs:76-91` 设置 `BreakAtOperatorId` 后执行 debug flow，未见 `ExecutionAdmissionService.ValidateFlowAsync()`。如果该入口仍可被 UI 或 API 使用，副作用算子可能绕过 NodePreview 的准入。

## 其他隐藏跳过逻辑

- 用户关闭工作台自动预览 checkbox：`previewPanelCapabilityOwner.mjs:762-779`；手动预览按钮走 `requestManualPreview()`，见 `previewPanelCapabilityOwner.mjs:782-815`。
- 旧卡片式面板关闭自动预览或折叠：`previewPanel.js:199-220`。
- 旧面板节点 disabled 时跳过：`previewPanel.js:204-206,222-226,410-414`。
- 缺项目不执行预览：`previewCoordinator.js:1341-1363`。
- 输入图过大不执行预览：`previewCoordinator.js:1372-1391`。
- 缺采集源/缺 FilePath/缺 CameraId 时不执行预览：`previewCoordinator.js:511-533,1393-1413`。
- 工作台把高成本/手动类 idle 错误显示为“需手动预览”：`previewPanelCapabilityOwner.mjs:140-173`。
- 属性面板参数变更触发旧预览面板自动预览：`propertyPanel.js:2034-2042,2151-2155,2174-2178`。

## 测试覆盖现状

已有覆盖：

- 高成本算子自动预览不执行、手动执行：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/preview-coordinator-memory.test.mjs:303-328`。
- ImageAcquisition 缺相机优先于手动策略：`preview-coordinator-memory.test.mjs:330-355`。
- 相机模式 `autoPreviewAllowed=false` smoke：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/preview-regression.smoke.mjs:136-147`。
- 预览工作台手动按钮、取消、stale 清理和 camera-binding manual-required UI：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/flow-layout-vm.spec.ts:682-857`。
- PreviewNode 后端副作用阻断：`ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/PreviewNodeEndpointsTests.cs:1249-1282`。
- OperatorPreviewService 副作用阻断：`ClearVision.Product/tests/ClearVision.Product.Tests/Services/OperatorPreviewServiceAdmissionTests.cs:16-37`。
- inline 正式/实时执行副作用准入已有部分测试：`InspectionServiceSingleRunTests.cs:236-245,1382-1390`、`InspectionServiceRealtimeTests.cs:125`。

测试缺口：

- 未见自动预览 checkbox toggle 后“不再请求 / 恢复请求”的真实行为测试；现有多为渲染文案断言。
- 高成本策略只覆盖了 `TemplateMatching` 一类，未参数化覆盖全部 A 类 direct hits 和 B 类 text-only false positives。
- 未见基于真实 operator catalog 的 policy 快照测试，无法防止 `HIGH_COST_TEXT_HINTS` 继续误伤轻量算子。
- 未见 `ImageAcquisition SourceType=file` 明确允许自动预览的单测。
- 未见 `ImageAcquisition SourceType=camera` 前端 manual 提示与后端 admission blocked 的端到端一致性测试。
- 未见 `/api/autotune/flow-node/preview` 的副作用准入测试。
- 副作用准入测试只抽样了 `HttpRequest`/`TextSave` 等，未参数化覆盖 AlwaysBlockedSideEffectTypes 全量列表和 `ResultOutput SaveToFile=true/false`。
- 无图像输出 D 类缺少工作台结构化预览 E2E 截图/布局覆盖。

## 下一轮改造建议

第一批可恢复自动预览：

- 明显 text-only 误伤：`LineMeasurement`、`GeometricTolerance`、`CaliperTool`、`BlobLabeling`、`HistogramAnalysis`、`ParallelLineFind`、`QuadrilateralFind`、`FrequencyFilter`、`PhaseClosure`、`GlcmTexture`。
- 轻量后处理：`BoxFilter`、`BoxNms`、`DetectionSequenceJudge`、`DualModalVoting`、`EdgePairDefect`。
- D 类纯结构化算子：不需要“恢复”执行，重点是让工作台自动展示结构化摘要，不再用无图像资格制造“像没预览”的体验。

必须保留手动或后端阻断：

- `ImageAcquisition SourceType=camera`，除非做明确的受控自动采帧设计。
- 所有外部 I/O 和持久化副作用：保存、写库、HTTP、MQTT、TCP、串口、PLC。
- 标定类真实读取/写入和求解路径。

应改成安全 dry-run：

- `ImageSave`、`TextSave`、`ResultOutput SaveToFile=true`、`DatabaseWrite`、`HttpRequest`、`MqttPublish`、PLC/TCP/串口通信、`CalibrationLoader`、标定类、变量写入/递增/计数/触发/脚本类。

需要新增测试：

- `getOperatorPreviewCostPolicy()` 参数化单测：A 类 direct、B 类误伤、ImageAcquisition file/camera。
- catalog 快照测试：列出当前 manual-only direct/text 命中，作为后续恢复的变更审查依据。
- PreviewNode/OperatorPreview admission 参数化测试：全量 AlwaysBlockedSideEffectTypes、`ResultOutput SaveToFile=true/false`、`ImageAcquisition camera/file`。
- `/api/autotune/flow-node/preview` admission 测试：含上游 `ImageSave`/`HttpRequest`/PLC 节点时必须拒绝或 dry-run。
- UI/E2E：自动预览 checkbox 开关、camera manual-required、no-image structured preview、恢复第一批算子的自动刷新截图。
