# G10C-FOLLOWUP：坐标方向、精度报告与 PointList SpatialContext 最终收口

> 阶段：Geometry/Spatial
> 状态：`DONE`
> 目标：收紧 G10C 已确认的 TransformMode/frame、PointList sidecar、Points/Image SpatialContext、AccuracyReport 与 World2D 单位契约缺口。

## Preflight

- Initial SHA：`d1c3bfa74dfbfdbbd063c46bc43b632b78b90ba1`
- 开始时间：`2026-07-03 15:14:15 +08:00`
- 分支：`codex初稿`
- 本地 HEAD：`d1c3bfa74dfbfdbbd063c46bc43b632b78b90ba1`
- `origin/codex初稿`：`d1c3bfa74dfbfdbbd063c46bc43b632b78b90ba1`
- GitHub `refs/heads/codex初稿`：`d1c3bfa74dfbfdbbd063c46bc43b632b78b90ba1`
- 工作树：preflight 时干净

## 本轮范围

[x] PixelToWorld/WorldToPixel frame direction fail-closed。
[x] Planar/RayPlane 共用稳定错误前缀：`SPATIAL_FRAME_DIRECTION_INVALID`。
[x] Points/Image SpatialContext 分开解析，Points 优先，Image sidecar 保持 Image context ownership。
[x] PointList 端口启用 port-aware SpatialContext sidecar 传播。
[x] World2D 增加 Meter/Centimeter/Micrometer 可序列化单位。
[x] AccuracyReport 增加 `RoundTripFrame`、`RoundTripSpatialTransformCount`，并使用真实 round-trip unit。
[x] World2D neutral-plane UI/Playwright 最终覆盖。
[x] 完整 Product/Desktop/UI/build/publish/audit 验证。

## 修改文件

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Calibration/SpatialContextV1.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Calibration/SpatialCalibrationTransformService.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PixelToWorldTransformOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/Calibration/SpatialContextV1Tests.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/Operators/PixelToWorldTransformOperatorTests.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/Services/FlowExecutionServiceTests.cs`
- `ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/nodePreviewInspector.js`
- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/node-preview.spec.ts`
- `ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/node-preview-inspector.test.mjs`
- `docs/ai/operator-knowledge/operator_knowledge_cards.json`
- `docs/ai/operator-knowledge/operator_knowledge_graph.json`
- `docs/ai/operator-knowledge/operator_knowledge_graph_report.md`
- `docs/进行中/Studio2/goals/G10C-FOLLOWUP.md`
- `docs/进行中/Studio2/goals/G10C.md`
- `docs/进行中/Studio2/goals/G11A.md`
- `TODO.md`

## 契约记录

- mode/frame policy：PixelToWorld 禁止 World2D 输入和 ImageFull/RoiLocal/Undistorted 输出；WorldToPixel 禁止非 World2D 输入，非法组合 fail closed。
- Points/Image context ownership：坐标计算优先 `PointsSpatialContext`；缺失时允许 `ImageSpatialContext` 兼容 fallback 并记录 diagnostic；Image 输出 sidecar 仅采用 Image context 或 legacy fallback。
- PointList sidecar E2E：`TransformedPointsSpatialContext` 可通过 PointList 连接传播为 `PointsSpatialContext`；binding 的 SourceOperatorId/OutputPortId/OutputName 仍为权威。
- AccuracyReport frame parity：报告新增 `RoundTripFrame`、`RoundTripUnit`、`RoundTripSpatialTransformCount`，避免 WorldToPixel 使用 `world_unit` 占位。
- unit contract：`SpatialUnitV1` additive 增加 Meter/Centimeter/Micrometer；World2D 不再用 Unitless 代替物理单位。
- World2D neutral plane：`FrameKind=World2D`、`CoordinateSpace=world.2d.neutral-plane` 或 world metadata 明确时跳过 input/output image candidate，强制 neutral SVG；Scene 信息区显示 FrameId、Unit、World bounds、WorldToSceneScale；Annotated PNG 明确不作为 World2D Scene 底图。
- operator knowledge：因 Product full 发现旧知识图缺少 PixelToWorld `InputFrame`/`OutputFrame` metadata，本轮重新生成 operator knowledge graph/cards/report；生成器存在 `System.Collections.Immutable` 版本冲突 warning，但命令成功且回归通过。

## 验证

- `dotnet build "ClearVision.Product/src/ClearVision.Product.Infrastructure/ClearVision.Product.Infrastructure.csproj" -c Debug --nologo` - PASS, 0 warnings/errors.
- `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" -FullyQualifiedName PixelToWorldTransformOperatorTests,SpatialContextV1Tests,FlowExecutionServiceTests -NoRestore` - PASS, 73 tests.
- `npm run test:unit`（`ClearVision.Product/tests/ClearVision.Product.UI.Tests`）- PASS, 611 tests.
- `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" -FullyQualifiedName ExecutionObservationProjectorTests -NoBuild -NoRestore` - PASS, 30 tests.
- `& "./scripts/run-tests-calibration-regression.ps1" -NoRestore` - PASS, 104 tests.
- `& "./scripts/run-tests-phase42-regression.ps1" -NoBuild -NoRestore` - PASS, 121 tests.
- `& "./scripts/run-tests-services-regression.ps1" -NoBuild -NoRestore` - PASS, 62 tests.
- `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" -NoBuild -NoRestore` - PASS, 3185 passed, 4 skipped.
- `& "./scripts/run-dotnet-test-serial.ps1" -Project "ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" -NoBuild -NoRestore` - PASS, 466 tests.
- `npm run test:preview-smoke`（`ClearVision.Product/tests/ClearVision.Product.UI.Tests`）- PASS.
- `npx playwright test tests/e2e/node-preview.spec.ts`（`ClearVision.Product/tests/ClearVision.Product.UI.Tests`）- PASS, 13 tests.
- Station Debug build - PASS.
- Station Release build - PASS.
- Desktop Debug build - PASS on single rerun after an earlier transient MSBuild file-lock failure.
- Desktop Release publish to `.tmp/publish-check/g10c-followup` - PASS.
- publish asset/source/dev artifact audit - PASS, 144 files; no `.map`/`.ts`/`.tsx`/`.vue`, no package config, no `node_modules/tests`; `wwwroot/src` remains existing publish shape.
- `git diff --check` - PASS.
- `./scripts/scan-secrets.ps1` - PASS.
- large-file audit - PASS for this change set; large cache/artifact files reported under `.dotnet_cli_home` and `artifacts/publish/test_build/Acme.Product.Desktop.exe` were pre-existing/unrelated.
- untracked/scratch audit - PASS; `.tmp/publish-check/g10c-followup` removed; Playwright report removed.
- process audit - PASS after `dotnet build-server shutdown`; no residual `dotnet` or `testhost`; user Chrome processes remain unrelated.
- 完整 GitHub CI：NOT RUN.
- 真实 WebView2：NOT PERFORMED.

## 回填区

- 状态：`DONE`
- 完成时间：`2026-07-03 15:48:14 +08:00`
- Initial SHA：`d1c3bfa74dfbfdbbd063c46bc43b632b78b90ba1`
- Final SHA：同一提交无法在 tracked 文件中自包含自身 Git SHA；本轮以提交后 `git rev-parse HEAD` 核对值为权威。
- 远端 SHA：同一提交无法在 tracked 文件中自包含自身 Git SHA；本轮以 push 后 `git ls-remote origin refs/heads/codex初稿` 核对值为权威。
- blockers：`NONE`
