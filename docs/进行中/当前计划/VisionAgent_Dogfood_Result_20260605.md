# Vision Agent Dogfood First Pass - 2026-06-05

## 执行摘要

- 执行人：Codex
- 执行时间：2026-06-05
- 执行方式：半自动 dogfood。以 UI contract、后端 rule parity、executable business benchmark、mock planner autonomy benchmark、permission negative benchmark、shadow eval sample report 和源码契约检查作为证据。
- 结论：首轮覆盖项全部通过；未发现阻断发布问题。
- 后续优化：建议在下一轮补充真实浏览器人工截图到 PR/Actions 截图附件包，用于设计走查归档；这不是本轮发布阻断。

## 失败分级

- 阻断发布：无。
- 可后续优化：缺少人工截图归档；已有自动化证据覆盖行为契约。

## 结果表

| 编号 | 场景 | 状态 | 执行人 | 执行时间 | 证据路径 | 发布阻断等级 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DF-01 | 新建线序检测流程 | 通过 | Codex | 2026-06-05 | `quality/evals/reports/planner_autonomy_benchmark.json`；`quality/evals/reports/VisionAgent_business_benchmark_baseline.json` | 无 | 覆盖 wire sequence generation、validate、dryrun、precheck；缺资源保持 workflow draft 可编辑。 |
| DF-02 | 新建模板匹配流程 | 通过 | Codex | 2026-06-05 | `quality/evals/reports/VisionAgent_business_benchmark_baseline.json`；`quality/evals/reports/planner_autonomy_benchmark.json` | 无 | 覆盖 TemplateMatching draft 与 TemplatePath/TemplateId 至少一个规则。 |
| DF-03 | 新建孔距测量流程 | 通过 | Codex | 2026-06-05 | `quality/evals/reports/planner_autonomy_benchmark.json`；`quality/evals/reports/VisionAgent_business_benchmark_baseline.json` | 无 | 覆盖 hole distance generation；dryrun 为结构级模拟，未读取真实图片。 |
| DF-04 | 修改 existingFlow 参数 | 通过 | Codex | 2026-06-05 | `quality/evals/reports/planner_autonomy_benchmark.json`；`ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelApplyPreview.js` | 无 | 覆盖 modify existing flow 和 draftEdits；应用预览模块保留差异检查。 |
| DF-05 | 参数互斥：Camera 模式不要求 FilePath | 通过 | Codex | 2026-06-05 | `quality/evals/specs/vision_agent_parameter_rule_parity_cases.json`；`test_results/agent_engineering_harness/agent_engineering_harness.trx`；`test_results/agent_engineering_harness/agent_ui_contract_output.txt` | 无 | 后端 FlowValidation、precheck 与前端 effective rules 对齐。 |
| DF-06 | 参数互斥：File 模式不要求 CameraBindingId | 通过 | Codex | 2026-06-05 | `quality/evals/specs/vision_agent_parameter_rule_parity_cases.json`；`test_results/agent_engineering_harness/agent_ui_contract_output.txt` | 无 | FilePath required；CameraId/CameraBindingId disabled/not required。 |
| DF-07 | DeepLearning ModelId 不要求 ModelPath | 通过 | Codex | 2026-06-05 | `quality/evals/specs/vision_agent_parameter_rule_parity_cases.json`；`test_results/agent_engineering_harness/agent_engineering_harness.trx` | 无 | ModelPath/ModelId/ModelCatalogPath 至少一个规则通过。 |
| DF-08 | TemplateMatching TemplateId 不要求 TemplatePath | 通过 | Codex | 2026-06-05 | `quality/evals/specs/vision_agent_parameter_rule_parity_cases.json`；`test_results/agent_engineering_harness/agent_engineering_harness.trx` | 无 | TemplatePath/TemplateId 至少一个规则通过。 |
| DF-09 | ResultOutput OutputChannelId 不要求 Channel | 通过 | Codex | 2026-06-05 | `quality/evals/specs/vision_agent_parameter_rule_parity_cases.json`；`test_results/agent_engineering_harness/agent_engineering_harness.trx` | 无 | Channel/OutputChannel/OutputChannelId 等价规则通过；file/plc 缺失项由同一 spec 覆盖。 |
| DF-10 | RuntimePreview 未授权提示 pendingAction | 通过 | Codex | 2026-06-05 | `quality/evals/reports/planner_autonomy_benchmark.json`；`test_results/agent_engineering_harness/agent_ui_contract_output.txt` | 无 | RuntimePreviewConsent=false 与权限缺失均进入 policyDecision/toolTrace/pendingActions；workflow draft 不被阻断。 |
| DF-11 | RuntimePreview 授权后显示 offline metadata | 通过 | Codex | 2026-06-05 | `quality/evals/reports/planner_autonomy_benchmark.json`；`test_results/agent_engineering_harness/agent_ui_contract_output.txt` | 无 | 覆盖 adapterName、previewMode、artifacts、previewReady、fallback；仍为 offline metadata-only。 |
| DF-12 | readyForDeployment=false 时不禁用 workflow 编辑 | 通过 | Codex | 2026-06-05 | `test_results/agent_engineering_harness/agent_ui_contract_output.txt` | 无 | Deployment action 与 workflow editing 状态分离，workflow 仍可编辑。 |
| DF-13 | workflow draft 应用到画布后可撤销 | 通过 | Codex | 2026-06-05 | `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/ai/aiPanelApplyPreview.js`；`test_results/agent_engineering_harness/agent_ui_contract_output.txt` | 无 | 源码契约覆盖 apply snapshot、undo button、undo apply restore；建议下轮补人工截图。 |
| DF-14 | Agent 工作台左侧主区、聊天右侧窄栏可用 | 通过 | Codex | 2026-06-05 | `test_results/agent_engineering_harness/agent_ui_contract_output.txt`；`ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/shared/styles/ai-panel.css` | 无 | UI contract 覆盖左主区、右聊天栏、移动端 fallback、模块化职责。 |

## 本轮未推进范围

- 未接真实相机 SDK。
- 未访问真实 Station。
- 未读取真实图片文件。
- 未加载真实模型文件。
- 未写 PLC。
- 未打包、未下发、未热加载。
- RuntimePreview 保持 offline/metadata-only。
- legacy GenerateFlow 默认仍不启用 Agent。
- developer hidden UI 默认仍关闭。
