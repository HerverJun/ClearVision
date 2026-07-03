# G08-G10B-FOLLOWUP-2: Scene 定位与 Spatial fail-closed 最终收口

> 阶段: Geometry/Spatial
> 状态: `DONE`
> Initial SHA: `f9db425b1dc072ad23756f143ee722a481d9adad`
> 分支: `codex初稿`
> 结论: 本轮只收口 G08-G10B 联合 follow-up 残余缺口，未执行 G10C。

## 范围

- Scene primitive 与 Observation Detail 通过 `outputPortId + resultPathVersion + resultPath` 建立一一对应。
- ResultPath `[index]` 默认 fail closed，仅 Scene/Observation 只读定位显式启用。
- SpatialContext sidecar 区分 absent、matched、invalid binding、ambiguous binding、malformed。
- ImageCanvas/RoiEditor pointer 与图片切换生命周期集中取消，并防止 stale image load 覆盖新状态。
- Scene 有 primitives 但无有效尺寸时显示受控状态，不挂空白 canvas。

## 完成记录

- `Locatable` 与 `Addressable` 分离：数组/List item 容器可定位，但不能作为全局变量绑定。
- Global Variable SourceBinding schema/runtime 继续拒绝 index ResultPath。
- 未知 `IEnumerable` index resolver 不调用 `GetEnumerator()`。
- SpatialContext invalid/ambiguous/malformed 在目标 executor 前生成受控失败，错误码稳定。
- ImageCanvas `cancelAndReleaseActiveInteraction(reason)` 覆盖 clear、loadImage、模式切换、清除 editable overlay、destroy、pointercancel、lostpointercapture、window blur。
- `loadImage`/`loadImageFromBuffer` 使用 generation 防止旧请求晚到覆盖新图片。

## 状态回填

- G08-G10B-FOLLOWUP-2: `DONE`
- G10C: `READY`
- TODO 当前 Goal: `G10C`
- G11A+: `LOCKED`
- 未执行 G10C

## 验证摘要

- UI focused: `node-preview-inspector.test.mjs`, `canvas-core.test.mjs`, `roi-geometry.test.mjs`, PropertyPanel/ROI lifecycle unit PASS。
- UI full/smoke: `npm run test:unit`, `npm run test:preview-smoke`, `node-preview.spec.ts`, `roi-editor.spec.ts` PASS。
- Product focused/full: ResultPath、Global Variable、FlowExecutionService、RoiManager、SpatialContext focused PASS；Product 完整串行测试 PASS。
- Desktop focused/full: ExecutionObservationProjector、Preview endpoints/artifacts/continuous preview、Studio2/BuildFromPlan guards focused PASS；Desktop 完整串行测试 PASS。
- Build/publish/audit、CI/WebView2 状态以本轮最终报告为准。
