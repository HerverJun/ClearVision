import test from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_RECT_PARAM_KEYS,
  CIRCLE_PARAM_KEYS,
  POLAR_ANNULUS_ARC_PARAM_KEYS,
  REGION_RECT_PARAM_KEYS,
  cancelRectangleDraft,
  clampRectToBounds,
  commitRectangleDraft,
  computeClockwiseAngleSpanDegrees,
  createRectangleDraftSession,
  angleDegreesFromCenter,
  pointFromAngleDegrees,
  hitTestRectHandle,
  hitTestRectangle,
  hitTestCircle,
  hitTestCircleHandle,
  hitTestAnnulus,
  hitTestAnnulusHandle,
  imageToScreenPoint,
  imageToScreenRect,
  normalizeAngleDegrees,
  normalizeAnnulusGeometry,
  normalizeCircleGeometry,
  nudgeRect,
  normalizeRectFromPoints,
  rectFromParams,
  rectToParams,
  redoRectangleDraft,
  resizeAnnulusByHandle,
  resizeCircleByHandle,
  resizeRectByHandle,
  screenToImageRect,
  screenToImagePoint,
  translateAnnulus,
  translateCircle,
  translateRect,
  undoRectangleDraft,
  validateAnnulusGeometry,
  validateCircleGeometry,
  validateRectangleGeometry
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiGeometry.mjs';
import {
  geometryFromParams,
  geometryToParams,
  getOperatorRoiConfig
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/roiEditorSupport.mjs';
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

test('circle geometry normalizes validates translates resizes and hit-tests with DPI handles', () => {
  const bounds = { width: 100, height: 80 };
  const circle = normalizeCircleGeometry({ centerX: 50, centerY: 40, radius: 18 }, bounds);

  assert.deepEqual(circle, { kind: 'circle', centerX: 50, centerY: 40, radius: 18 });
  assert.deepEqual(validateCircleGeometry(circle, bounds), { valid: true, errors: [] });
  assert.equal(hitTestCircle({ x: 60, y: 40 }, circle), true);
  assert.equal(hitTestCircleHandle({ x: 68, y: 40 }, circle, { scale: 2 }, 10), 'radius');

  assert.deepEqual(
    translateCircle(circle, { x: 10, y: -5 }, bounds),
    { kind: 'circle', centerX: 60, centerY: 35, radius: 18 }
  );
  assert.deepEqual(
    resizeCircleByHandle(circle, 'radius', { x: 74, y: 40 }, bounds),
    { kind: 'circle', centerX: 50, centerY: 40, radius: 24 }
  );

  const invalid = validateCircleGeometry({ centerX: 10, centerY: 10, radius: Number.NaN });
  assert.equal(invalid.valid, false);
  assert.ok(invalid.errors.includes('non-finite'));
  assert.ok(invalid.errors.includes('radius'));
});

test('annulus and arc geometry use clockwise image-space degree rules and fail closed', () => {
  assert.equal(normalizeAngleDegrees(-90), 270);
  assert.equal(computeClockwiseAngleSpanDegrees(350, 10), 20);
  assert.equal(computeClockwiseAngleSpanDegrees(0, 360, { allowFullCircle: true }), 360);
  assert.equal(angleDegreesFromCenter({ x: 10, y: 10 }, { x: 10, y: 20 }), 90);
  assert.deepEqual(pointFromAngleDegrees({ x: 10, y: 10 }, 5, 180), { x: 5, y: 10 });

  const bounds = { width: 100, height: 100 };
  const annulus = normalizeAnnulusGeometry({
    centerX: 50,
    centerY: 50,
    innerRadius: 10,
    outerRadius: 30,
    startAngle: 350,
    endAngle: 410
  }, bounds);

  assert.equal(annulus.kind, 'arc');
  assert.equal(annulus.startAngle, 350);
  assert.equal(annulus.spanDegrees, 60);
  assert.deepEqual(validateAnnulusGeometry(annulus, bounds), { valid: true, errors: [] });
  assert.equal(hitTestAnnulus({ x: 75, y: 50 }, annulus), true);
  assert.equal(hitTestAnnulusHandle({ x: 80, y: 50 }, annulus, { scale: 2 }, 10), 'outerRadius');

  const moved = translateAnnulus(annulus, { x: -5, y: 4 }, bounds);
  assert.equal(moved.centerX, 45);
  assert.equal(moved.centerY, 54);

  const resized = resizeAnnulusByHandle(annulus, 'innerRadius', { x: 65, y: 50 }, bounds);
  assert.equal(resized.innerRadius, 15);

  const invalid = validateAnnulusGeometry({
    centerX: 50,
    centerY: 50,
    innerRadius: 20,
    outerRadius: 10,
    startAngle: 0,
    endAngle: 90
  });
  assert.equal(invalid.valid, false);
  assert.ok(invalid.errors.includes('outerRadius'));
});

test('geometry parameter adapters round-trip circle and PolarUnwrap annulus arc params', () => {
  const circleConfig = getOperatorRoiConfig({
    type: 'RoiManager',
    parameters: [
      { name: 'Shape', value: 'Circle' },
      { name: 'CenterX', value: 40 },
      { name: 'CenterY', value: 30 },
      { name: 'Radius', value: 12 }
    ]
  });

  assert.equal(circleConfig.editable, true);
  assert.deepEqual(circleConfig.geometryAdapter.paramKeys, CIRCLE_PARAM_KEYS);
  const circle = geometryFromParams({
    CenterX: 40,
    CenterY: 30,
    Radius: 12
  }, circleConfig, { width: 100, height: 100 });
  assert.deepEqual(circle, { kind: 'circle', centerX: 40, centerY: 30, radius: 12 });
  assert.deepEqual(geometryToParams({ kind: 'circle', centerX: 41.4, centerY: 30.5, radius: 12.6 }, circleConfig), {
    CenterX: 41,
    CenterY: 31,
    Radius: 13
  });

  const polarConfig = getOperatorRoiConfig({
    type: 'PolarUnwrap',
    parameters: [
      { name: 'CenterX', value: 50 },
      { name: 'CenterY', value: 50 },
      { name: 'InnerRadius', value: 10 },
      { name: 'OuterRadius', value: 25 },
      { name: 'StartAngle', value: 45 },
      { name: 'EndAngle', value: 180 }
    ]
  });

  assert.equal(polarConfig.shape, 'Arc');
  assert.equal(polarConfig.editable, true);
  assert.deepEqual(polarConfig.geometryAdapter.paramKeys, POLAR_ANNULUS_ARC_PARAM_KEYS);
  const arc = geometryFromParams({
    CenterX: 50,
    CenterY: 50,
    InnerRadius: 10,
    OuterRadius: 25,
    StartAngle: 45,
    EndAngle: 180
  }, polarConfig, { width: 100, height: 100 });
  assert.equal(arc.kind, 'arc');
  assert.equal(arc.spanDegrees, 135);
  assert.deepEqual(geometryToParams(arc, polarConfig), {
    CenterX: 50,
    CenterY: 50,
    InnerRadius: 10,
    OuterRadius: 25,
    StartAngle: 45,
    EndAngle: 180
  });

  const invalidPolar = getOperatorRoiConfig({
    type: 'PolarUnwrap',
    parameters: [
      { name: 'InnerRadius', value: 20 },
      { name: 'OuterRadius', value: 10 }
    ]
  });
  assert.equal(invalidPolar.editable, false);
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

test('ImageCanvas render commands preserve bounded annulus and arc fields', () => {
  const [command] = buildOverlayRenderCommands([
    {
      id: 'polar-arc',
      type: 'arc',
      visible: true,
      x: 50,
      y: 40,
      innerRadius: 10,
      outerRadius: 25,
      startAngle: 45,
      endAngle: 135,
      spanDegrees: 90
    }
  ]);

  assert.equal(command.type, 'arc');
  assert.equal(command.innerRadius, 10);
  assert.equal(command.outerRadius, 25);
  assert.equal(command.startAngle, 45);
  assert.equal(command.spanDegrees, 90);
});
