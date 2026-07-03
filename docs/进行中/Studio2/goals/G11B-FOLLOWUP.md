# G11B-FOLLOWUP：Circle Search evidence、Scene 与 flag 权威收口

> 阶段：Vertical Product
> 状态：`DONE`
> 目标：收口 Circle Search V2 产品契约，避免 preview/前端二次运行 kernel，并补齐有界证据、Inspector、Scene identity 与 feature flag 权威。

## 本轮范围

[x] 单次 CaliperFitV2 执行可选地产生有界 profile evidence。
[x] profile evidence 使用版本化 DTO/Artifact，并限制 profile 数、单条 samples、总 samples 与 artifact bytes。
[x] Inspector 输出结构化、有限的 CaliperFitV2 summary，不展开原始 typed result。
[x] candidate/accepted/rejected Scene 按 CaliperIndex/Angle 均匀覆盖全圆。
[x] primitiveId 使用 CaliperIndex 作为稳定身份；ResultPath 保持真实列表 index。
[x] `Studio:CircleSearchV2ToolEnabled` 以服务端 startup flag 为权威，前端 registry 不能反向覆盖 false。
[x] 顺手收口 G11B 登记的内核诊断债：Auto polarity 预算、Huber convergence、MAD iteration-cap convergence。

## 明确未做

- 连续预览 debounce/cancel/latest-wins
- benchmark p50/p95
- 真实 WebView2 人工验证
- GitHub workflow 完整 CI
- Station、Project schema、Runtime Package、AgentRun 改动

## 执行清单

[x] `CircleCaliperFitV2Request` 增加 `IncludeProfileEvidence` 与 evidence 预算常量。
[x] `CircleCaliperFitV2Result` 增加 `ProfileEvidence`，采样 pass 内按全圆 deterministic index 收集 profile。
[x] `CircleMeasurement` 输出 `CaliperProfileEvidence` 并升级 operator version 至 `1.1.2`。
[x] `PreviewArtifactMaterializer` 将非空 profile evidence 强制转为 bounded `profile` artifact ref。
[x] `ExecutionObservationProjector` 将 typed V2 result 投影为 `caliperFitV2Summary`。
[x] `ExecutionVisualSceneProjector` 改为全圆均匀选择 Scene point primitive。
[x] `roiEditorSupport.mjs` 调整 startup flag 优先级。
[x] Operator docs/catalog 由 `OperatorDocGenerator` 重新生成。

## 验证清单

[x] Product focused：`CircleCaliperFitV2KernelTests,CircleMeasurementCaliperFitV2OperatorTests` PASS（30/30）。
[x] Desktop focused：`ExecutionObservationProjectorTests,PreviewArtifactStoreTests` PASS（48/48）。
[x] UI focused：`property-panel-memory.test.mjs` PASS（8/8）。
[x] JS syntax：`node --check src/features/flow-editor/roiEditorSupport.mjs` PASS。
[x] OperatorDocGenerator：PASS。
[x] `git diff --check`：PASS，仅 CRLF warning。
[ ] Product full serial：本阶段未运行。
[ ] Desktop full serial：本阶段未运行。
[ ] GitHub CI：NOT RUN。
[ ] 真实 WebView2：NOT PERFORMED。

## 完成条件

[x] G11B 保持 `DONE`。
[x] G11B-FOLLOWUP 为 `DONE`。
[x] G11C 为 `READY`。
[x] G12A+ 保持 `LOCKED`。
[x] TODO 当前 Goal 仍为 `G11C`。

## 回填区

- 状态：`DONE`
- 开始时间：`2026-07-04 01:10:00 +08:00`
- 完成时间：`2026-07-04 02:23:28 +08:00`
- Initial SHA：`e1b079df2e4d43cbba44dd4ba4037fe4ca249d50`
- Final SHA：提交自身 SHA 不写入 tracked 文件；以 push 后核对值为准
- 远端 SHA：提交自身 SHA 不写入 tracked 文件；以 push 后核对值为准
- 修改文件：CaliperFitV2 kernel、CircleMeasurement operator、Desktop observation/scene/artifact projection、Circle Search feature flag support、Product/Desktop/UI focused tests、operator generated docs/catalog、TODO 与 G11B-FOLLOWUP/G11C 卡片。
- 新增/变更契约：`caliper-circle-fit.v2.profile-evidence.v1` evidence DTO；`CaliperProfileEvidence` output；bounded `profile` artifact；Inspector `caliperFitV2Summary`；CaliperFitV2 Scene primitive identity 以 CaliperIndex 稳定，ResultPath 保持真实 list index；CircleMeasurement version `1.1.2`。
- active owner / 唯一写入口：无新增 preview owner；profile evidence 只由正式 CaliperFitV2 kernel 单次执行产生；Scene 仍由 `ExecutionVisualSceneProjector` 唯一投影；feature flag 以服务端 startup flags 为权威。
- Legacy mounted / subscription / timer 状态：未新增 timer、轮询或连续 preview；HoughCircle/FitEllipse、flag off 与旧工程行为保持兼容。
- 测试命令与结果：见验证清单。
- 截图/Benchmark/Artifact：Playwright screenshot NOT CAPTURED；benchmark 未新增；profile evidence 以 bounded `profile` artifact ref 覆盖。
- API / Project format / Runtime / Station / AgentRun 影响：仅 additive Desktop preview/observation/operator output contract；未修改 Project schema、Runtime Package、Station 或 AgentRun。
- 技术债与非阻断事项：G11C 仍需连续预览 debounce/cancel/latest-wins、性能 p50/p95、内存与保存重启兼容验证；完整 GitHub CI 与真实 WebView2 未执行。
- 阻断（无则写 `NONE`）：`NONE`
- 下一 Goal：`G11C`
