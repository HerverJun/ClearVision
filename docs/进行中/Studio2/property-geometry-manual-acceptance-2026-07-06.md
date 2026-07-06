# Property Geometry Manual Acceptance - 2026-07-06

## Preconditions

- Studio2 property panel capability is enabled.
- A flow contains image-producing upstream nodes so the ROI editor can resolve a preview image.
- For guarded features, set the matching startup flag before launching the app.

## Checks

1. `RoiManager` rectangle
   - Select a `RoiManager` node with `Shape=Rectangle`.
   - Draw or drag the ROI in the geometry editor.
   - Commit and confirm `X/Y/Width/Height` update in the parameter form and persist to the node.

2. `RoiManager` circle
   - Select `Shape=Circle`.
   - Drag center/radius handles.
   - Commit and confirm `CenterX/CenterY/Radius` update.

3. `RoiManager` polygon
   - Select `Shape=Polygon`.
   - Move vertices and commit.
   - Confirm `PolygonPoints` remains valid JSON and reflects image coordinates.

4. `TemplateMatching` search ROI
   - Select a `TemplateMatching` node.
   - Draw a search rectangle and commit.
   - Confirm `UseRoi=true` and `RoiX/RoiY/RoiWidth/RoiHeight` update.
   - Confirm `OriginX/OriginY` are unchanged.

5. `CircleMeasurement` guarded search ring
   - With `Studio:CircleSearchV2ToolEnabled=false`, select `Method=CaliperFitV2`; the geometry editor must not expose editable Circle Search V2 controls.
   - With the flag enabled and `Method=CaliperFitV2`, edit the ring and confirm `SearchCenterX/SearchCenterY/MinRadius/NominalRadius/MaxRadius` update.
   - With another method, confirm the Circle Search V2 editor remains hidden.

6. `PolarUnwrap`
   - Select a `PolarUnwrap` node.
   - Edit annulus or arc handles.
   - Commit and confirm center, radii, and angle parameters update without changing unrelated parameters.

7. `NPointCalibration`
   - With `Studio:NPointCalibrationWorkbenchEnabled=false`, confirm the point workbench is not editable.
   - With the flag enabled, move image points and confirm `PointPairs` JSON preserves `WorldX/WorldY` and enabled state.

8. `CaliperTool.SearchRegion`
   - Select a `CaliperTool` node with no `SearchRegion` connection.
   - Draw and commit a rectangle.
   - Confirm a `RectangleRegion` node is created, connected from its `Rectangle` output to `CaliperTool.SearchRegion`, and populated with `X/Y/Width/Height`.
   - Edit again and confirm the existing `RectangleRegion` is updated instead of creating a duplicate.
   - If `SearchRegion` is already connected to a non-`RectangleRegion` node, confirm commit does not overwrite that connection.

9. Legacy migrated controls
   - File picker parameters still send `PickFileCommand` and receive `FilePickedEvent`.
   - Camera binding parameters still load camera bindings and write the selected binding.
   - Slider, color, boolean, enum, and numeric parameters still render and write normally.

10. Preview ownership
    - With the standalone preview panel enabled, the property geometry editor must reuse the preview coordinator and not claim preview resources.
    - With the preview panel disabled, the property geometry editor may use its fallback preview resources.
