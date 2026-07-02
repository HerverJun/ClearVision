import test from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_RECT_PARAM_KEYS,
  REGION_RECT_PARAM_KEYS,
  cancelRectangleDraft,
  clampRectToBounds,
  commitRectangleDraft,
  createRectangleDraftSession,
  hitTestRectHandle,
  hitTestRectangle,
  imageToScreenPoint,
  imageToScreenRect,
  nudgeRect,
  normalizeRectFromPoints,
  rectFromParams,
  rectToParams,
  redoRectangleDraft,
  resizeRectByHandle,
  screenToImageRect,
  screenToImagePoint,
  translateRect,
  undoRectangleDraft,
  validateRectangleGeometry
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiGeometry.mjs';
import { getOperatorRoiConfig } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiEditorSupport.mjs';
import RoiEditorPanel from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiEditorPanel.js';
import {
  ImageCanvas,
  buildOverlayRenderCommands
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/imageCanvas.js';

test('normalizeRectFromPoints handles reverse drag direction', () => {
  assert.deepEqual(
    normalizeRectFromPoints({ x: 30, y: 40 }, { x: 10, y: 20 }),
    { x: 10, y: 20, width: 20, height: 20 }
  );
});

test('clampRectToBounds keeps rect inside image and enforces min size', () => {
  assert.deepEqual(
    clampRectToBounds({ x: -5, y: 90, width: 30, height: 20 }, { width: 100, height: 100 }, 1),
    { x: 0, y: 80, width: 30, height: 20 }
  );
});

test('screenToImagePoint converts viewport coordinates back to image space', () => {
  assert.deepEqual(
    screenToImagePoint({ x: 210, y: 120 }, { scale: 2, offset: { x: 10, y: 20 } }),
    { x: 100, y: 50 }
  );
});

test('image and screen coordinate conversion round-trips points and rectangles with DPI scale', () => {
  const viewport = { scale: 1.5, offset: { x: 12, y: 9 } };
  const imagePoint = { x: 32, y: 24 };
  const screenPoint = imageToScreenPoint(imagePoint, viewport);

  assert.deepEqual(screenPoint, { x: 60, y: 45 });
  assert.deepEqual(screenToImagePoint(screenPoint, viewport), imagePoint);

  const imageRect = { x: 10, y: 12, width: 20, height: 8 };
  const screenRect = imageToScreenRect(imageRect, viewport);
  assert.deepEqual(screenRect, { x: 27, y: 27, width: 30, height: 12 });
  assert.deepEqual(screenToImageRect(screenRect, viewport), imageRect);
});

test('validateRectangleGeometry rejects non-finite and degenerate rectangles', () => {
  assert.deepEqual(
    validateRectangleGeometry({ x: 4, y: 5, width: 12, height: 8 }, { width: 64, height: 64 }),
    { valid: true, errors: [] }
  );

  const invalid = validateRectangleGeometry(
    { x: Number.NaN, y: 5, width: 0, height: Number.POSITIVE_INFINITY },
    { width: 64, height: 64 }
  );

  assert.equal(invalid.valid, false);
  assert.ok(invalid.errors.includes('non-finite'));
  assert.ok(invalid.errors.includes('width'));
  assert.ok(invalid.errors.includes('height'));
});

test('resizeRectByHandle updates size from south-east handle', () => {
  assert.deepEqual(
    resizeRectByHandle(
      { x: 10, y: 10, width: 20, height: 20 },
      'se',
      { x: 40, y: 45 },
      { width: 100, height: 100 },
      1
    ),
    { x: 10, y: 10, width: 30, height: 35 }
  );
});

test('hit testing finds rectangle body and DPI-aware resize handles', () => {
  const rect = { x: 10, y: 10, width: 20, height: 16 };

  assert.equal(hitTestRectangle({ x: 15, y: 15 }, rect), true);
  assert.equal(hitTestRectangle({ x: 4, y: 15 }, rect), false);
  assert.equal(
    hitTestRectHandle({ x: 32, y: 28 }, rect, { scale: 2, offset: { x: 0, y: 0 } }, 10),
    'se'
  );
  assert.equal(
    hitTestRectHandle({ x: 31, y: 18 }, rect, { scale: 2, offset: { x: 0, y: 0 } }, 10),
    'e'
  );
});

test('translateRect keeps moved rectangle within bounds', () => {
  assert.deepEqual(
    translateRect(
      { x: 80, y: 85, width: 20, height: 15 },
      { x: 10, y: 10 },
      { width: 100, height: 100 },
      1
    ),
    { x: 80, y: 85, width: 20, height: 15 }
  );
});

test('nudgeRect clamps keyboard movement inside image bounds', () => {
  assert.deepEqual(
    nudgeRect(
      { x: 1, y: 1, width: 10, height: 10 },
      { x: -5, y: -3 },
      { width: 64, height: 64 }
    ),
    { x: 0, y: 0, width: 10, height: 10 }
  );
});

test('rectangle draft history supports local undo redo and cancel', () => {
  let session = createRectangleDraftSession(
    { x: 4, y: 5, width: 12, height: 8 },
    { width: 64, height: 64 }
  );

  session = commitRectangleDraft(session, { x: 10, y: 5, width: 12, height: 8 }, {
    previousRect: session.current
  });
  session = commitRectangleDraft(session, { x: 14, y: 9, width: 12, height: 8 }, {
    previousRect: session.current
  });

  assert.deepEqual(session.current, { x: 14, y: 9, width: 12, height: 8 });
  assert.equal(session.past.length, 2);

  session = undoRectangleDraft(session);
  assert.deepEqual(session.current, { x: 10, y: 5, width: 12, height: 8 });

  session = redoRectangleDraft(session);
  assert.deepEqual(session.current, { x: 14, y: 9, width: 12, height: 8 });

  session = cancelRectangleDraft(session);
  assert.deepEqual(session.current, { x: 4, y: 5, width: 12, height: 8 });
  assert.equal(session.past.length, 0);
  assert.equal(session.future.length, 0);
});

test('rectFromParams supports BoxFilter region parameter names', () => {
  assert.deepEqual(
    rectFromParams({
      RegionX: 12,
      RegionY: 14,
      RegionW: 30,
      RegionH: 18
    }, REGION_RECT_PARAM_KEYS),
    { x: 12, y: 14, width: 30, height: 18 }
  );
});

test('rectToParams can write back BoxFilter region parameter names', () => {
  assert.deepEqual(
    rectToParams({ x: 6, y: 8, width: 20, height: 16 }, REGION_RECT_PARAM_KEYS),
    { RegionX: 6, RegionY: 8, RegionW: 20, RegionH: 16 }
  );
});

test('getOperatorRoiConfig enables ROI editor for BoxFilter region mode', () => {
  const config = getOperatorRoiConfig({
    type: 'BoxFilter',
    parameters: [
      { name: 'FilterMode', value: 'Region' },
      { name: 'RegionX', value: 0 },
      { name: 'RegionY', value: 0 },
      { name: 'RegionW', value: 100 },
      { name: 'RegionH', value: 80 }
    ]
  });

  assert.equal(config.supported, true);
  assert.equal(config.editable, true);
  assert.deepEqual(config.rectParamKeys, REGION_RECT_PARAM_KEYS);
});

test('getOperatorRoiConfig keeps BoxFilter ROI editor readonly outside region mode', () => {
  const config = getOperatorRoiConfig({
    type: 'BoxFilter',
    parameters: [
      { name: 'FilterMode', value: 'Score' }
    ]
  });

  assert.equal(config.supported, true);
  assert.equal(config.editable, false);
  assert.match(config.readonlyMessage, /Region/);
  assert.deepEqual(DEFAULT_RECT_PARAM_KEYS, {
    x: 'X',
    y: 'Y',
    width: 'Width',
    height: 'Height'
  });
});

test('refreshFromOperator re-applies ROI state instead of only syncing the old overlay', async () => {
  const panel = {
    currentConfig: { editable: false },
    applyState() {
      this.currentConfig = { editable: true };
      return Promise.resolve();
    }
  };

  await RoiEditorPanel.prototype.refreshFromOperator.call(panel);

  assert.equal(panel.currentConfig.editable, true);
});

test('ImageCanvas addOverlay accepts stable ids while preserving legacy generated ids', () => {
  const canvas = {
    overlays: [],
    invalidate() {
      this.invalidated = true;
    }
  };

  const stable = ImageCanvas.prototype.addOverlay.call(canvas, 'rectangle', 1, 2, 3, 4, {
    id: 'scene-rect',
    groupId: 'scene',
    readOnly: true,
    selectable: false,
    layer: 'roi',
    zOrder: 5
  });
  const legacy = ImageCanvas.prototype.addOverlay.call(canvas, 'circle', 5, 6, 7, 8);

  assert.equal(stable.id, 'scene-rect');
  assert.equal(stable.groupId, 'scene');
  assert.equal(stable.readOnly, true);
  assert.equal(stable.selectable, false);
  assert.match(legacy.id, /^overlay_/);
  assert.equal(canvas.invalidated, true);
});

test('ImageCanvas overlay groups update scene overlays without clearing editable ROI overlays', () => {
  const canvas = {
    overlays: [
      { id: 'editable-roi', type: 'rectangle', editable: true, x: 1, y: 1, width: 10, height: 10 }
    ],
    selectedOverlay: 'editable-roi',
    addOverlay: ImageCanvas.prototype.addOverlay,
    invalidate() {
      this.invalidated = true;
    }
  };

  const added = ImageCanvas.prototype.setOverlayGroup.call(canvas, 'scene', [
    { id: 'scene-circle', type: 'circle', x: 20, y: 20, width: 10, height: 10, readOnly: true }
  ]);

  assert.equal(added.length, 1);
  assert.ok(canvas.overlays.some(overlay => overlay.id === 'editable-roi'));
  assert.ok(canvas.overlays.some(overlay => overlay.id === 'scene-circle'));
  assert.equal(canvas.selectedOverlay, 'editable-roi');

  ImageCanvas.prototype.clearOverlayGroup.call(canvas, 'scene');
  assert.deepEqual(canvas.overlays.map(overlay => overlay.id), ['editable-roi']);
});

test('ImageCanvas render commands sort by layer zOrder and stable id', () => {
  const commands = buildOverlayRenderCommands([
    { id: 'b', type: 'rectangle', layer: 'scene', zOrder: 2, visible: true, x: 0, y: 0, width: 1, height: 1 },
    { id: 'roi', type: 'rectangle', layer: 'roi', zOrder: 1, visible: true, x: 0, y: 0, width: 1, height: 1 },
    { id: 'a', type: 'circle', layer: 'scene', zOrder: 2, visible: true, x: 0, y: 0, width: 1, height: 1 },
    { id: 'hidden', type: 'point', layer: 'scene', zOrder: 0, visible: false, x: 0, y: 0 }
  ]);

  assert.deepEqual(commands.map(command => command.id), ['roi', 'a', 'b']);
});
