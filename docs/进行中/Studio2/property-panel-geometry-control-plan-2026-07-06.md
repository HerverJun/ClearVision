# Property Panel Geometry Control Plan - 2026-07-06

## Scope

本轮只迁移属性面板里的图像几何编辑能力，不调整 AI、设置页、后端执行契约或既有 Legacy `PropertyPanel` 的行为。Studio2 属性面板继续复用现有 `RoiEditorPanel` 和 preview coordinator，不新增第二套 canvas。

## Capability Matrix

| Operator | Geometry | Write target | Status |
| --- | --- | --- | --- |
| `RoiManager` | Rectangle / Circle / Polygon | `X/Y/Width/Height`, `CenterX/CenterY/Radius`, `PolygonPoints` | 已迁移到 `PropertyPanelCapabilityOwner` |
| `TemplateMatching` | Rectangle search ROI | `UseRoi`, `RoiX/RoiY/RoiWidth/RoiHeight` | 已迁移；只编辑搜索 ROI |
| `CircleMeasurement` | Circle Search V2 | `SearchCenterX/SearchCenterY/MinRadius/NominalRadius/MaxRadius` | 受 `Studio:CircleSearchV2ToolEnabled` 与 `Method=CaliperFitV2` 保护 |
| `PolarUnwrap` | Annulus / arc | `CenterX/CenterY/InnerRadius/OuterRadius/StartAngle/EndAngle` | 已迁移 |
| `NPointCalibration` | Point sequence | `PointPairs` JSON | 受 `Studio:NPointCalibrationWorkbenchEnabled` 保护 |
| `CaliperTool` | Rectangle search region | `RectangleRegion -> CaliperTool.SearchRegion` connection | 已迁移；不写伪参数 |

## Implementation Notes

- `PropertyPanelCapabilityOwner` now owns `RoiEditorPanel` creation and teardown, receives `previewCoordinator`, preview resource fallback, image viewer callback, feature flags, and toast integration from `app.js`.
- Geometry commits write through `propertyAdapter.writeParameters` for direct-parameter operators.
- `CaliperTool.SearchRegion` is modeled as a real typed input. A new `RectangleRegion` operator emits a `PortDataType.Rectangle` dictionary with `X/Y/Width/Height`; the adapter creates or reuses that node and connects it to `SearchRegion`.
- `TemplateMatching` search ROI uses search-image coordinates and toggles `UseRoi`. `OriginX/OriginY` are deliberately not edited here because they must be template-image coordinates, and the current editor preview is the selected node input/search image.
- Existing legacy-only surfaces remain in `propertyPanel.js` and are covered by source-contract tests while Studio2 migration continues.

## Non-Goals And Guardrails

- No second ROI canvas.
- No fake `CaliperTool` rectangle parameters.
- No automatic conversion between search-image coordinates and template-image origin coordinates.
- No changes to backend AI, settings endpoints, authentication, or flow execution contracts outside the new `RectangleRegion` operator registration.

## Validation Matrix

| Area | Check |
| --- | --- |
| JS syntax | `node --check` for modified Studio2 files |
| ROI adapters | `roi-geometry.test.mjs` covers TemplateMatching, CaliperTool, existing direct-parameter geometry |
| Property panel owner | `property-panel-capability-owner.test.mjs` covers migrated controls, preview dependencies, and Caliper region binding |
| Backend operator | `RectangleRegionOperatorTests` covers operator type, output payload, and size validation |
| Git review | `git diff --check` plus manual diff review before staging |
