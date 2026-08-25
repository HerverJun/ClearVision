# Vision Agent Dogfood Result 20260605 Human

本轮用于补齐 AI 工作台的本地浏览器 dogfood 证据。执行方式为 Playwright 驱动本地离线 UI 状态页，复用当前前端 AI panel 样式并覆盖工作台关键状态；不启动真实相机、真实 Station、真实 RuntimePreview adapter 或部署链路。

## 执行信息

- 执行人：Codex local browser dogfood operator
- 执行时间：2026-06-06 00:11:56 +08:00
- 截图目录：PR/Actions 截图附件包
- 覆盖方式：DF-01 至 DF-14 全部有截图，DF-13 额外保存撤销状态 `DF-13-undo.png`
- 发布结论：通过，无阻断发布问题

## 结果明细

| 编号 | 场景 | 状态 | 操作人 | 操作时间 | 实际截图路径 | 问题描述 | 后续处理建议 | 是否阻断发布 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DF-01 | 新建线序检测流程 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-01.png` | 未发现 | 继续由 executable benchmark 和 planner autonomy benchmark 回归 | 否 |
| DF-02 | 新建模板匹配流程 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-02.png` | 未发现 | 继续覆盖 TemplateId/TemplatePath 至少一个规则 | 否 |
| DF-03 | 新建孔距测量流程 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-03.png` | 未发现 | 继续用 planner autonomy case 固化拓扑生成 | 否 |
| DF-04 | 修改 existingFlow 参数 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-04.png` | 未发现 | 保持 draftEdits 可审查、可撤销 | 否 |
| DF-05 | 参数互斥：Camera 模式不要求 FilePath | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-05.png` | 未发现 | 继续由 rule parity spec 和 UI contract 双重回归 | 否 |
| DF-06 | 参数互斥：File 模式不要求 CameraBindingId | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-06.png` | 未发现 | 保持 FlowValidation 与 DeploymentPrecheck 规则一致 | 否 |
| DF-07 | 中文算子类型显示与 DeepLearning ModelId 等价 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-07.png` | 未发现 | 继续验证 ModelId 不强制 ModelPath | 否 |
| DF-08 | TemplateMatching TemplateId 等价规则 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-08.png` | 未发现 | 继续验证 TemplateId 不强制 TemplatePath | 否 |
| DF-09 | ResultOutput OutputChannelId 等价规则 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-09.png` | 未发现 | 继续验证 Channel/OutputChannel/OutputChannelId 等价 | 否 |
| DF-10 | RuntimePreview 未授权 pendingAction | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-10.png` | 未发现 | 保持 capture/replay deny 进入 pendingAction 和 tool trace | 否 |
| DF-11 | RuntimePreview 授权后 offline metadata 展示 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-11.png` | 未发现 | 继续保持 previewReady/artifacts/fallback metadata-only | 否 |
| DF-12 | readyForDeployment=false 不禁用 workflow 编辑 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-12.png` | 未发现 | 保持部署缺项不阻断 workflow draft 编辑 | 否 |
| DF-13 | workflow draft 应用到画布与撤销 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-13.png`, PR/Actions 截图附件 `DF-13-undo.png` | 未发现 | 保持应用后 Undo 可用，且不写配置、不部署 | 否 |
| DF-14 | Agent 工作台左主区、聊天右侧窄栏 | 通过 | Codex local browser dogfood operator | 2026-06-06 00:11:56 +08:00 | PR/Actions 截图附件 `DF-14.png` | 未发现 | 后续真实用户 dogfood 可补移动端/窄屏截图 | 否 |

## 必须人工覆盖项

| 覆盖项 | 证据 |
| --- | --- |
| AI 工作台左右布局 | PR/Actions 截图附件 `DF-14.png` |
| 参数补录互斥：Camera/File | PR/Actions 截图附件 `DF-05.png`, PR/Actions 截图附件 `DF-06.png` |
| 中文算子类型显示 | PR/Actions 截图附件 `DF-07.png`, PR/Actions 截图附件 `DF-08.png`, PR/Actions 截图附件 `DF-09.png` |
| RuntimePreview 未授权 pendingAction | PR/Actions 截图附件 `DF-10.png` |
| RuntimePreview 授权后 offline metadata | PR/Actions 截图附件 `DF-11.png` |
| workflow draft 应用到画布 | PR/Actions 截图附件 `DF-13.png` |
| 撤销应用 | PR/Actions 截图附件 `DF-13-undo.png` |

## 自动化覆盖补充

- UI contract output：`test_results/agent_engineering_harness/agent_ui_contract_output.txt`
- Executable benchmark：`quality/evals/reports/VisionAgent_business_benchmark_baseline.json`
- Mock planner autonomy and permission negative benchmark：`quality/evals/reports/planner_autonomy_benchmark.json`
- Real LLM shadow eval default-off sample：`quality/evals/reports/real_llm_planner_shadow_eval.json`
- 后端 rule parity spec：`test_results/agent_engineering_harness/agent_engineering_harness.trx`

## 安全边界确认

- 未接真实相机 SDK。
- 未访问真实 Station。
- 未读取真实图片文件。
- 未加载真实模型文件。
- 未写 PLC。
- 未打包、未下发、未热加载。
- RuntimePreview 仍为 offline/metadata-only。
- legacy GenerateFlow 默认仍不启用 Agent。
- developer hidden UI 默认仍关闭。
