# G08-G10B-FOLLOWUP：Visual Geometry 与 SpatialContext 联合收口

> 阶段：Geometry/Spatial
> 状态：`DONE`
> 目标：统一修复 Visual Scene、Geometry 编辑和 SpatialContext 传播之间已确认的一致性缺口；本轮不执行 G10C。

## 本轮只读上下文

执行前只读取：

1. 根目录 `AGENTS.md`；
2. 根目录 `TODO.md` 的架构红线、当前执行项和本 Goal 行；
3. 本卡片；
4. 下列真实代码锚点；
5. 需要核实时再读取相关测试与已有 G08/G09/G10 完成卡，不篡改其历史 Initial/Final 记录。

### 代码锚点

- `ClearVision.Product/src/ClearVision.Product.Desktop/Observation/ExecutionVisualSceneProjector.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/RoiManagerOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/NPointCalibrationOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PolarUnwrapOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/v2/src/`
- `ClearVision.Product/tests/ClearVision.Product.Tests/`
- `ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/`
- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/`

## 本轮范围

- G08：Scene 底图 resolver、真实 ROI shape 投影、selectable 与 ResultPath 一致性。
- G09A：ROI 编辑 pointer lifecycle 与唯一 commit。
- G09B：geometry 参数 no-op round-trip 与运行时约束 parity。
- G09C：禁止伪造 World 坐标，PointSequence 空白点击不修改 `PointPairs`。
- G10B：按连接端口传播 `SpatialContext` sidecar。

## 明确不做

- 不执行 G10C。
- 不接入 PixelToWorld、World2D Scene 或正式 Calibration asset。
- 不新建第二 Canvas renderer。
- 不新建第二 Flow/Project command bus。
- 不修改 Project schema、Runtime Package、Station 或 AgentRun/EventStore/Workspace Snapshot/terminal/recovery。
- 不把 point image coordinate 当 world coordinate。

## 执行清单

- [x] 建立或收敛集中 Scene base-image resolver。
- [x] RoiManager Scene 按 Rectangle/Circle/Polygon/Mask/Crop 的真实 shape 投影。
- [x] selectable primitive 必须有生产 `ResultPathResolver` 可验证的 canonical ResultPath，否则不可选。
- [x] ROI 编辑统一受控 Pointer 生命周期，cancel 不 commit 且清理 capture/listener/timer/RAF。
- [x] `geometryFromParams` no-op round-trip 不静默改写业务参数。
- [x] RoiManager/PolarUnwrap 编辑约束与运行时一致，角度 raw provenance 不因打开编辑器丢失。
- [x] PointSequence 不再空白点击新增或伪造 `WorldX=imageX` / `WorldY=imageY`。
- [x] `SpatialContext` sidecar 由 port-aware resolver 精确按连接绑定传播。
- [x] 不修改 `ImageWrapper` 生命周期结构或持久化 spatial metadata。

## 验证清单

- [x] UI focused tests：ROI geometry、canvas core、node-preview-inspector、PropertyPanel/ROI editor unit、Playwright `roi-editor` / `node-preview`。
- [x] Product focused tests：RoiManager、SpatialContext、FlowExecutionService、NPointCalibration、PolarUnwrap、multi-level Crop、port-aware sidecar。
- [x] Desktop focused tests：ExecutionObservationProjector、Preview endpoints/artifact/continuous preview、Studio2 architecture guards、Scene shape/base-image/ResultPath。
- [x] `npm run test:unit`。
- [x] `npm run test:preview-smoke`。
- [x] Product 完整测试串行。
- [x] Desktop 完整测试串行。
- [x] Desktop Debug build。
- [x] Station Debug/Release build。
- [x] Desktop Release publish 到 `.tmp/publish-check/g08-g10b-unified-followup`。
- [x] publish asset/source/dev artifact audit。
- [x] `git diff --check`。
- [x] secret、大文件、untracked、scratch 和残留进程检查。
- [x] 清理 publish scratch。

## 完成条件

- [x] G08-G10B-FOLLOWUP 状态为 `DONE`。
- [x] G10C 状态恢复为 `READY`，但明确记录未执行 G10C。
- [ ] 本地 HEAD、`origin/codex初稿` 与 GitHub `refs/heads/codex初稿` 三方一致。

## 回填区

- 状态：`DONE`
- 开始时间：2026-07-03 08:48:21 +08:00
- 完成时间：2026-07-03 10:22:40 +08:00
- Initial SHA：`6a0a18bdd398dda71849bdc7715e01737f8ea4a0`
- Final SHA：提交后核对
- 远端 SHA：提交并 push 后核对
- 修改文件：
  - `ClearVision.Product/src/ClearVision.Product.Core/ResultPaths/ResultPathV1.cs`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/Observation/ExecutionVisualSceneProjector.cs`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiEditorSupport.mjs`
  - `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiGeometry.mjs`
  - `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/RoiManagerOperator.cs`
  - `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ExecutionObservationProjectorTests.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Tests/ProjectVariables/FlowExecutionProjectVariableBindingTests.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Tests/ResultPaths/ResultPathV1Tests.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Tests/Services/FlowExecutionServiceTests.cs`
  - `ClearVision.Product/tests/ClearVision.Product.Tests/TestData/ResultPathV1Conformance.json`
  - `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/roi-editor.spec.ts`
  - `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/roi-geometry.test.mjs`
  - `TODO.md`
  - `docs/进行中/Studio2/goals/G08.md`
  - `docs/进行中/Studio2/goals/G08-G10B-FOLLOWUP.md`
  - `docs/进行中/Studio2/goals/G09A.md`
  - `docs/进行中/Studio2/goals/G09B.md`
  - `docs/进行中/Studio2/goals/G09C.md`
  - `docs/进行中/Studio2/goals/G10A.md`
  - `docs/进行中/Studio2/goals/G10B.md`
  - `docs/进行中/Studio2/goals/G10C.md`
- 新增/变更契约：`ResultPathV1` 支持 canonical `[index]` segment；Scene primitive `selectable` 必须同时具备 outputPortId、ResultPathVersion=1 和 canonical ResultPath；Scene base image 只挂载尺寸匹配图像或 neutral plane；RoiManager Scene 投影真实 Rectangle/Circle/Polygon shape 并用独立 Crop Bounds；`FlowExecutionService` 按连接端口和 sidecar binding 精确传播 `SpatialContext` 到 input-scoped key（例如 `ImageSpatialContext`）；PointSequence 不再空白新增或伪造 world 坐标。
- active owner / 唯一写入口：`ImageCanvas` 仍是唯一 ROI/Scene canvas 渲染与交互内核；`PropertyPanel.handleRoiRectChanged` 仍只在 commit phase 写入 operator 参数；`ExecutionVisualSceneProjector` 只读投影 Scene；`FlowExecutionService` 只负责连接输入装配与 sidecar 传播，不新增 Project/Flow command bus。
- Legacy mounted / subscription / timer 状态：未新增第二 Canvas renderer、全局 undo bus、Project schema owner、Runtime/Station/AgentRun owner；Pointer cancel/destroy 释放 capture 并回滚 draft；publish scratch 已清理。
- 测试命令与结果：
  - `npm run test:unit` - PASS, 605 tests.
  - `npm run test:preview-smoke` - PASS.
  - `npx playwright test tests/e2e/node-preview.spec.ts tests/e2e/roi-editor.spec.ts --reporter=list` - PASS, 24 tests.
  - `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" -FullyQualifiedName ResultPathV1Tests,FlowExecutionProjectVariableBindingTests,ProjectGlobalVariableSchemaTests,FlowExecutionServiceTests,RoiManagerOperatorTests,SpatialContextV1Tests,NPointCalibrationOperatorTests,PolarUnwrapOperatorTests -NoBuild -NoRestore -Verbosity minimal` - PASS, 147 tests.
  - `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" -FullyQualifiedName ExecutionObservationProjectorTests,PreviewNodeEndpointsTests,PreviewArtifactStoreTests,ContinuousPreviewEndpointTests,Studio2ArchitectureGuardTests,BuildFromPlanArchitectureGuardTests -NoRestore -Verbosity minimal` - PASS, 85 tests.
  - Product full serial `ClearVision.Product.Tests` - PASS, 3150 passed / 4 skipped.
  - Desktop full serial `ClearVision.Product.Desktop.Tests` - PASS, 459 tests.
  - Desktop Debug build - PASS, 0 warnings / 0 errors.
  - Station Debug build - PASS, 0 warnings / 0 errors.
  - Station Release build - PASS, 0 warnings / 0 errors.
  - Desktop Release publish to `.tmp/publish-check/g08-g10b-unified-followup` - PASS.
  - Publish asset/source/dev artifact audit - PASS, 0 prohibited files/dirs; publish scratch removed.
  - `git diff --check` - PASS（仅 CRLF 工作区提示）。
  - `./scripts/scan-secrets.ps1 -Path "." -BaseRef HEAD -IncludeUntracked` - PASS.
  - Changed-file large-file scan - PASS, 0 files > 1 MB.
  - Process audit after `dotnet build-server shutdown` - PASS, 0 matching dotnet/node/app processes.
- 截图/Benchmark/Artifact：未新增截图或 benchmark；Release publish 仅用于 `.tmp/publish-check/g08-g10b-unified-followup` 审计且已清理。
- API / Project format / Runtime / Station / AgentRun 影响：无 Project schema、Runtime Package、Station、AgentRun/EventStore/Workspace Snapshot/terminal/recovery 变更；preview/observation shape 仅为 additive Scene DTO 行为；未执行 G10C、未接入 PixelToWorld/World2D Scene。
- 完整 GitHub CI：`NOT RUN`
- 真实 WebView2：`NOT PERFORMED`
- 技术债与非阻断事项：完整 GitHub CI 未运行；真实 WebView2 人工验证未执行；G10C 仍需后续按卡执行 PixelToWorld/World2D Scene。
- 阻断（无则写 `NONE`）：`NONE`
- 下一 Goal：`G10C`
