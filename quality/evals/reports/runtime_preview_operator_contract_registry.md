# RuntimePreview Operator Contract Registry Final

- Generated UTC: `2026-08-01T07:46:37.041668+00:00`
- Commit: `local`
- Branch: `local`
- Run: `local` attempt `local`
- Metadata only: `True`
- Real resources touched: `False`
- Accepted: `True`

| Id | Scenario / Type | Status / Decision | Risk / Notes |
| --- | --- | --- | --- |
| ImageAcquisition | ImageAcquisition | true | camera_metadata |
| TemplateMatching | TemplateMatching | true | template_dependency |
| CircleMeasurement | CircleMeasurement | true | measurement |
| MeasureDistance | MeasureDistance | true | measurement |
| DeepLearning | DeepLearning | true | deep_learning_review; engineer_approval_required |
| ResultOutput | ResultOutput | true | output_contract |
| ResultJudgment | ResultJudgment | true | judgment |
| BlobAnalysis | BlobAnalysis | true | blob |
| Thresholding | Thresholding | true | threshold |
| EdgeDetection | EdgeDetection | true | edge |
| ShapeMatching | ShapeMatching | true | template_dependency |
| SemanticSegmentation | SemanticSegmentation | true | deep_learning_review; engineer_approval_required |
| SurfaceDefectDetection | SurfaceDefectDetection | true | deep_learning_review; engineer_approval_required |
| ModbusCommunication | ModbusCommunication | true | plc_write_forbidden |
| HttpRequest | HttpRequest | true | network_write_forbidden |
| ScriptOperator | ScriptOperator | true | system_command_forbidden |

Safety boundary: no real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, hot-load, or Real RuntimePreview adapter.
