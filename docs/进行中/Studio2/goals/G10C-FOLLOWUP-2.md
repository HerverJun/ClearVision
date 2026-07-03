# G10C-FOLLOWUP-2：RoundTrip 与 PointList 世界单位权威收口

> 阶段：Geometry/Spatial
> 状态：`DONE`
> 目标：修复 G10C-FOLLOWUP 遗留的 AccuracyReport 真实 round-trip、PointList input unit、UnitScale 一致性、Image/Points context 隔离与 synthetic image sidecar 契约。

## Preflight

- Initial SHA：`0b96b0a1f78a785c3ee28f0135eaecc490bcaf97`
- 开始时间：`2026-07-03 16:30:28 +08:00`
- 分支：`codex初稿`
- 本地 HEAD：`0b96b0a1f78a785c3ee28f0135eaecc490bcaf97`
- `origin/codex初稿`：`0b96b0a1f78a785c3ee28f0135eaecc490bcaf97`
- GitHub `refs/heads/codex初稿`：`0b96b0a1f78a785c3ee28f0135eaecc490bcaf97`
- 工作树：preflight 时干净

## 本轮范围

[x] AccuracyReport 使用真实 PixelToWorld/WorldToPixel round-trip 数学链路，Planar/RayPlane 共用单一入口。
[x] WorldToPixel 消费 `PointsSpatialContext` 的 World2D frame/unit 权威，支持 mm/m/cm/μm。
[x] 建立唯一 WorldUnitContract，UnitScale 与已知物理单位冲突时 fail closed。
[x] Points/Image/legacy context 分别解析、分别产生 outcome；Image context 不参与点坐标推断。
[x] synthetic visualization 不输出伪业务 ImageFull SpatialContext。
[x] PixelToWorldTransform operator version 按仓库规则最小合法升级，并重生成 catalog/knowledge/中英文镜像资料。
[x] focused/full 验证与发布审计。

## 修改文件

- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Calibration/SpatialCalibrationTransformService.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PixelToWorldTransformOperator.cs`
- `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/Operators/PixelToWorldTransformOperatorTests.cs`
- `ClearVision.Product/tests/ClearVision.Product.Tests/Services/FlowExecutionServiceTests.cs`
- `docs/ai/operator-knowledge/operator_knowledge_graph.json`
- `docs/ai/operator-knowledge/operator_knowledge_graph_report.md`
- `docs/CATALOG.md`
- `docs/CHANGELOG.md`
- `docs/OPERATOR_CATALOG.md`
- `docs/operators/*`
- `docs/算子资料/*`
- `算子资料/*`
- `docs/进行中/Studio2/goals/G10C-FOLLOWUP.md`
- `docs/进行中/Studio2/goals/G10C-FOLLOWUP-2.md`
- `TODO.md`

## 契约记录

- AccuracyReport 不再只改标签：Planar/RayPlane 均按实际 SpatialContext -> CalibrationSource -> World2D -> CalibrationSource -> 原 frame 链路计算 round-trip，失败点不跳过；`RoundTripErrors.Count` 与输入点数不一致时 fail closed。
- WorldToPixel 在存在 `PointsSpatialContext` 时以其 World2D frame/unit 为输入点权威，按 context unit 转为内部毫米后再执行 calibration inverse；非 World2D Points context fail closed。
- `WorldUnitContract` 作为唯一单位入口，支持 `mm/cm/m/um/µm/μm` 固定物理比例；显式 `UnitScale` 与已知单位不一致时返回 `SPATIAL_UNIT_INCOMPATIBLE`，不再出现数值和标签分离。
- Points/Image/legacy SpatialContext 分别解析：Points malformed 阻止坐标转换；有合法 Points context 时 Image malformed 只省略 Image sidecar 并输出 diagnostic；legacy 仅在 scoped 缺失时 fallback，且 scoped/legacy 冲突比较 frame、binding 与 transform chain。
- 无真实输入图的 synthetic visualization 仍可输出 Image 预览，但不输出业务 ImageFull SpatialContext sidecar，并记录 `SYNTHETIC_IMAGE_SPATIAL_CONTEXT_OMITTED`。
- Flow PointList 规范化显式保留 `Position`、`Point2f`、`Point3f`、`Point3d` 的 `X/Y/Z`，下游 PixelToWorld 可解析 Flow-normalized dictionaries/JsonElement/tuple 形态。
- PixelToWorldTransform operator 版本升级为 `1.0.1`，operator catalog、version history、中英文镜像资料和 AI operator knowledge graph 已由生成器重建；最终 OperatorDocGenerator 运行无 source-hash/version warning。

## 验证

- Product focused：`PixelToWorldTransformOperatorTests,SpatialContextV1Tests,FlowExecutionServiceTests` PASS（83/83）。
- Calibration regression：`./scripts/run-tests-calibration-regression.ps1 -NoBuild -NoRestore` PASS（111/111）。
- Services regression：`./scripts/run-tests-services-regression.ps1 -NoBuild -NoRestore` PASS（65/65）。
- Phase42 regression：`./scripts/run-tests-phase42-regression.ps1 -NoBuild -NoRestore` PASS（128/128）。
- Product full serial：PASS（3195 passed, 4 skipped）。一次中间 full run 因 OCR/CircleMeasurement/ColorMeasurement 性能长尾失败；失败类隔离重跑 PASS（9/9），随后 Product full serial 重跑 PASS。
- Desktop full serial：`ClearVision.Product.Desktop.Tests.csproj` PASS（466/466）。
- UI unit：`npm run test:unit` PASS（611/611）。
- Preview smoke：`npm run test:preview-smoke` PASS。
- Playwright：`npx playwright test tests/e2e/node-preview.spec.ts --reporter=list` PASS（13/13）。
- Operator docs：`dotnet run --project scripts/OperatorDocGenerator/OperatorDocGenerator.csproj -- .` PASS，无 source-hash/version warning。
- Operator knowledge graph：`dotnet run --project quality/tools/OperatorKnowledgeGraphRunner/OperatorKnowledgeGraphRunner.csproj -- ...` PASS（156 cards, 1843 edges）；仅有既有 `System.Collections.Immutable` 版本冲突 warning。
- Desktop Debug build：PASS。
- Station Debug build：PASS。
- Station Release build：PASS。
- Desktop Release publish：`.tmp/publish-check/g10c-followup-2` PASS。
- Publish audit：入口 exe 与 `wwwroot` 存在；`.ts/.tsx/.vue/.map/package*.json/vite/tsconfig` 等常见 dev/source artifact 计数 0；`wwwroot/src` 保持既有 JS/CSS 发布形态。
- Scratch cleanup：`.tmp/publish-check/g10c-followup-2` 已清理。
- `git diff --check` PASS（仅 CRLF 提示）。
- Secret scan PASS。
- Large-file audit PASS：本变更集无新增/修改超过 50MB 的文件；仓库内既有大文件为缓存/bin/obj/artifact 类。
- Untracked/scratch audit PASS：仅目标新卡片为预期 untracked；publish scratch 已清理。
- Process audit PASS：`dotnet build-server shutdown` 后无 `dotnet`/`testhost`/`node` 残留；仅用户 Chrome 进程存在。
- 提交前最终执行：remote divergence check。

## 未执行

- 完整 GitHub CI：NOT RUN
- 真实 WebView2：NOT PERFORMED

## 回填区

- 状态：`DONE`
- 完成时间：`2026-07-03 18:09:48 +08:00`
- Initial SHA：`0b96b0a1f78a785c3ee28f0135eaecc490bcaf97`
- Final SHA：提交后以最终报告中的 `git rev-parse HEAD` 核对值为权威。
- 远端 SHA：push 后以最终报告中的 `git ls-remote origin refs/heads/codex初稿` 核对值为权威。
- blockers：NONE
