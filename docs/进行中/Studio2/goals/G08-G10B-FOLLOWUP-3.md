# G08-G10B-FOLLOWUP-3: Spatial 通用传播与 Scene 有界诊断收口

> 阶段: Geometry/Spatial
> 状态: `DONE`
> Initial SHA: `64235d6d925189b9d2fc2ebf153bdc53b0e4a872`
> 分支: `codex初稿`
> 开始时间: `2026-07-03 12:13:19 +08:00`
> 结论: 本轮只收口 Follow-up-2 遗留的 SpatialContext 通用传播、源输出端口隔离和 Scene diagnostics 有界性；未执行 G10C、PixelToWorld 或 World2D Scene。

## 范围

- 删除 SpatialContext 传播对目标算子类型的硬编码，所有 Image target port 使用同一个 port-aware resolver。
- 以 `${TargetInputPortName}SpatialContext` 写入目标输入 scoped key；Absent 保持 legacy 成功，Invalid/Ambiguous/Malformed 在目标 executor 前受控失败。
- 按当前连接的源输出端口隔离 sidecar 解析，其他端口的 malformed 或 binding mismatch 不污染当前连接。
- 保持 sequential、AutoSafeParallel、debug、cached upstream 的 fail-closed 语义一致。
- 将 `ExecutionVisualSceneProjector.MaxDiagnostics` 作为最终 Scene DTO diagnostics 硬上限。
- 补充真实 PropertyPanel/RoiEditor 生命周期 Playwright 覆盖。

## 架构红线

- 禁止执行 G10C、PixelToWorld 或 World2D。
- 禁止通过目标 `OperatorType` 硬编码 SpatialContext 能力。
- 禁止为每个算子增加独立 sidecar 传播逻辑。
- 禁止依赖 Dictionary 或 connection 枚举顺序。
- 禁止放宽 Global Variable index ResultPath。
- 禁止新建第二 Canvas renderer。
- 禁止修改 Project schema、Runtime Package、Station、AgentRun/EventStore/Workspace Snapshot。

## 验证计划

- UI focused: `canvas-core.test.mjs`, `node-preview-inspector.test.mjs`, PropertyPanel/RoiEditor lifecycle unit, `roi-editor.spec.ts`, `node-preview.spec.ts`。
- UI full/smoke: `npm run test:unit`, `npm run test:preview-smoke`。
- Product focused/full: `FlowExecutionServiceTests`, `RoiManagerOperatorTests`, `SpatialContextV1Tests`, ResultPath/Project Global Variable focused tests, generic image target/multi-image/sequential/parallel/debug/cache focused tests, Product 完整串行测试。
- Desktop focused/full: `ExecutionObservationProjectorTests`, Preview endpoints/artifacts/continuous preview, Studio2/BuildFromPlan architecture guards, 300 primitive/diagnostic budget tests, Desktop 完整串行测试。
- Build/publish/audit: Desktop Debug build, Station Debug/Release build, Desktop Release publish 到 `.tmp/publish-check/g08-g10b-followup-3`, publish audit, `git diff --check`, secret/large/untracked/scratch/process audit, cleanup publish scratch。

## 状态回填

- G08-G10B-FOLLOWUP-3: `DONE`
- G10C: `READY`
- G11A+: `LOCKED`
- 未执行 G10C

## 验证结果

- 完整 GitHub CI: `NOT RUN`
- 真实 WebView2: `NOT PERFORMED`
- Product focused sidecar 回归: `PASS`（199 tests）
- Desktop focused Scene/Preview/Architecture 回归: `PASS`（90 tests）
- Product full serial: `PASS`（3164 passed, 4 skipped）
- Desktop full serial: `PASS`（464 tests）
- UI focused/unit/smoke/Playwright: `PASS`
- Desktop Debug build: `PASS`
- Station Debug/Release build: `PASS`
- Desktop Release publish: `PASS`
- Publish audit: `PASS`（144 files；no source maps/TS/TSX/Vue/package config；`wwwroot/src` JS/CSS 与 7 个 PDB 保持现有发布形态）
- `git diff --check`: `PASS`
- secret/large/untracked/scratch/process audit: `PASS`

## 完成回填

- 完成时间: `2026-07-03 12:45:08 +08:00`
- Final SHA: `915f97f79e6f299ff217afa927ae6b217cad6299`
- 远端 SHA: `915f97f79e6f299ff217afa927ae6b217cad6299`
- 修改文件:
  - `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/Observation/ExecutionObservationProjector.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Tests/Services/FlowExecutionServiceTests.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ExecutionObservationProjectorTests.cs`
  - `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/roi-editor.spec.ts`
  - `TODO.md`
  - `docs/进行中/Studio2/goals/G08-G10B-FOLLOWUP-3.md`
  - `docs/进行中/Studio2/goals/G10C.md`
- generic Image sidecar 传播证据: `FlowExecutionServiceTests` 覆盖非 RoiManager Image target、`BackgroundSpatialContext`、多 Image target scoped sidecar、legacy absent sidecar。
- malformed 跨端口隔离证据: `FlowExecutionServiceTests` 覆盖 Image/Mask 双端口 malformed 隔离、binding key/source port disagreement fail-closed、连接顺序反转确定性。
- sequential/parallel/debug/cache fail-closed 证据: targeted FlowExecutionService 回归覆盖 sequential、AutoSafeParallel、debug cached upstream 在当前端口 malformed sidecar 时目标 executor 前失败。
- Scene diagnostics 上限证据: `ExecutionObservationProjectorTests` 覆盖 300 CircleData 定位诊断、63/64/65 边界、最终 diagnostics 上限 64 与 aggregate truncation diagnostic。
- 生命周期 Playwright 证据: `roi-editor.spec.ts` 覆盖未释放拖拽时图片切换、图片消失、切换节点、编辑器销毁、迟到旧 preview 不覆盖新 preview。
- focused/full tests、build、publish、audit: 见“验证结果”；完整 GitHub CI `NOT RUN`，真实 WebView2 `NOT PERFORMED`。
- TODO 状态: Follow-up-3 `DONE`；G10C `READY`；G11A+ `LOCKED`；当前执行项推进到 G10C。
- 三方 SHA 一致性: 提交并 push 后在最终报告回填。
- blockers: `NONE`
