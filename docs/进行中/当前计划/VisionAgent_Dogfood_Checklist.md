# Vision Agent Workbench Dogfood Checklist

本文档用于内部 dogfood 验证 AI 工作台是否达到“工程师好用、规则可信、质量可度量”的可用状态。检查范围只覆盖 Agent 工作台产品化体验、参数规则一致性、offline metadata-only RuntimePreview 与质量证据，不推进真实相机、真实 Station 或真实部署。

## 执行约束

- 不接真实相机 SDK。
- 不访问真实 Station。
- 不读取真实图片文件。
- 不加载真实模型文件。
- 不写 PLC。
- 不打包、不下发、不热加载。
- RuntimePreview 保持 offline/metadata-only。
- legacy GenerateFlow 默认仍不启用 Agent。
- developer hidden UI 默认仍关闭。

## 状态枚举

- 未执行
- 通过
- 阻断
- 非阻断问题

## 失败证据约定

- 截图目录：PR/Actions 截图附件包
- UI contract 输出：`test_results/agent_engineering_harness/agent_ui_contract_output.txt`
- 后端 TRX：`test_results/agent_engineering_harness/agent_engineering_harness.trx`
- benchmark 报告：`quality/evals/reports/`

## Checklist

| 编号 | 场景 | 操作步骤 | 预期结果 | 是否阻断发布 | 状态 | 执行人 | 执行时间 | 证据路径 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DF-01 | 新建线序检测流程 | 打开 AI 工作台；输入线序检测需求；生成 workflow draft；查看 validation preview 与 tool trace。 | 生成包含采集、线序/检测、结果输出的 draft；缺失资源进入 pendingAction；workflow 可编辑。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-01.png`；`quality/evals/reports/planner_autonomy_benchmark.json` | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-02 | 新建模板匹配流程 | 输入模板匹配需求，模板资源保持未绑定；生成 draft。 | TemplateMatching 节点存在；TemplatePath/TemplateId 至少一个规则生效；缺资源不阻断 workflow draft。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-02.png`；`quality/evals/reports/VisionAgent_business_benchmark_baseline.json` | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-03 | 新建孔距测量流程 | 输入孔距测量需求；要求两个孔定位与距离测量。 | draft 包含圆/孔定位与距离测量链路；dryrun 为结构级模拟；无真实图片读取。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-03.png`；`quality/evals/reports/planner_autonomy_benchmark.json` | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-04 | 修改 existingFlow 参数 | 载入已有流程；让 Agent 将采集模式改为 File 或调整输出通道；应用 draftEdits。 | 只修改目标参数；拓扑不被无关重写；应用后画布状态可见。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-04.png`；UI console log | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-05 | 参数补录：Camera 模式不要求 FilePath | 在 ImageAcquisition 设置 `SourceType=Camera`，不填 FilePath。 | FilePath disabled/not required；CameraId/CameraBindingId 至少一个进入缺失项。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-05.png`；rule parity spec report | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-06 | 参数补录：File 模式不要求 CameraBindingId | 在 ImageAcquisition 设置 `SourceType=File`，填写 FilePath，不填 CameraBindingId。 | FilePath required 且已满足；CameraId/CameraBindingId disabled/not required。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-06.png`；`agent_ui_contract_output.txt` | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-07 | DeepLearning ModelId 不要求 ModelPath | 在 DeepLearning 节点填写 ModelId，不填 ModelPath。 | ModelPath 不再被单独要求；ModelPath/ModelId/ModelCatalogPath 至少一个规则满足。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-07.png`；后端 parity TRX | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-08 | TemplateMatching TemplateId 不要求 TemplatePath | 在 TemplateMatching 节点填写 TemplateId，不填 TemplatePath。 | TemplatePath 不再被单独要求；TemplatePath/TemplateId 至少一个规则满足。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-08.png`；后端 parity TRX | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-09 | ResultOutput OutputChannelId 不要求 Channel | 在 ResultOutput 节点填写 OutputChannelId，不填 Channel。 | Channel/OutputChannel/OutputChannelId 等价规则满足；不出现重复必填提示。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-09.png`；rule parity spec report | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-10 | RuntimePreview 未授权时提示 pendingAction | 关闭 RuntimePreview consent；请求预览。 | capture/replay 被 policy deny；toolTrace 和 pendingActions 显示授权待处理；workflow draft 仍可编辑。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-10.png`；`planner_autonomy_benchmark.json` permission cases | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-11 | RuntimePreview 授权后显示 offline metadata | 打开 RuntimePreview consent；请求预览。 | 显示 adapterName、previewMode、artifacts、previewReady、fallback；内容为 offline metadata-only。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-11.png`；RuntimePreview UI contract 输出 | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-12 | readyForDeployment=false 时不禁用 workflow 编辑 | 构造缺 targetStationId 或缺部署资源的 precheck。 | Deployment action 禁用或提示待补录；workflow editing 仍 enabled。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-12.png`；`agent_ui_contract_output.txt` | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-13 | workflow draft 应用到画布后可撤销 | 生成 draft；点击应用到画布；执行撤销。 | 画布恢复应用前状态；AI 面板中的 preview/apply 状态不残留错误。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-13.png`；UI console log | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |
| DF-14 | Agent 工作台左侧主区、聊天右侧窄栏可用 | 打开 AI 工作台布局；在桌面与窄屏视口分别检查主工作区、聊天栏、参数审核、validation/toolTrace/RuntimePreview。 | 左侧主工作区承载 Agent 操作；右侧聊天栏窄栏可用；文本不溢出，关键按钮可点击。 | 是 | 未执行 | - | - | PR/Actions 截图附件 `DF-14-desktop.png`；PR/Actions 截图附件 `DF-14-mobile.png` | 首轮结果见 `VisionAgent_Dogfood_Result_20260605.md`。 |

## 发布前通过标准

- DF-01 到 DF-14 全部通过，或失败项有明确修复单与降级策略。
- `python quality/tools/run_quality_suite.py --suite agent_engineering_harness_suite --run` 通过。
- `quality/evals/reports/VisionAgent_business_benchmark_baseline.json`、`quality/evals/reports/planner_autonomy_benchmark.json` 与 `quality/evals/reports/real_llm_planner_shadow_eval.json` 均可下载/查看。
- 明确确认本轮未推进真实相机、真实 Station 或真实部署能力。
