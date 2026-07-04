import test from 'node:test';
import assert from 'node:assert/strict';

globalThis.window = globalThis.window || {};

const { PropertyPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
const { CalibrationDraftWorkbench } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/calibrationDraftWorkbench.js');

function createDeferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function createPanelWithImageLoader(loader) {
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    inputImageBase64Load: null,
    loadImageUrlAsBase64: loader
  });
  return panel;
}

function createCircleMeasurementPanel(method = 'CaliperFitV2', searchCenterMode = 'ImageCenter', circleSearchV2ToolEnabled = undefined) {
  const parameters = [
    { name: 'Method', value: method, dataType: 'enum' },
    { name: 'MinRadius', value: 20, dataType: 'int' },
    { name: 'MaxRadius', value: 40, dataType: 'int' },
    { name: 'Dp', value: 1, dataType: 'double' },
    { name: 'MinDist', value: 50, dataType: 'double' },
    { name: 'Param1', value: 100, dataType: 'double' },
    { name: 'Param2', value: 30, dataType: 'double' },
    { name: 'SearchCenterMode', value: searchCenterMode, dataType: 'enum' },
    { name: 'SearchCenterX', value: 100, dataType: 'double' },
    { name: 'SearchCenterY', value: 90, dataType: 'double' },
    { name: 'NominalRadius', value: 30, dataType: 'double' },
    { name: 'CaliperCount', value: 96, dataType: 'int' },
    { name: 'ProfileSampleCount', value: 129, dataType: 'int' },
    { name: 'AveragingThickness', value: 5, dataType: 'double' }
  ];
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    currentOperator: {
      type: 'CircleMeasurement',
      parameters
    },
    circleSearchV2ToolEnabled,
    escapeHtml: PropertyPanel.prototype.escapeHtml,
    escapeAttribute: PropertyPanel.prototype.escapeAttribute
  });
  return { panel, parameters };
}

function createNPointPanel(nPointCalibrationWorkbenchEnabled = undefined) {
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    currentOperator: {
      id: 'npoint-node',
      type: 'NPointCalibration',
      parameters: [
        {
          name: 'PointPairs',
          value: '[{"ImageX":10,"ImageY":20,"WorldX":1,"WorldY":2,"Enabled":true}]',
          dataType: 'string'
        }
      ]
    },
    nPointCalibrationWorkbenchEnabled,
    escapeHtml: PropertyPanel.prototype.escapeHtml,
    escapeAttribute: PropertyPanel.prototype.escapeAttribute
  });
  return panel;
}

test('PropertyPanel deduplicates concurrent cached image base64 loads', async () => {
  const calls = [];
  const deferred = createDeferred();
  const panel = createPanelWithImageLoader((url) => {
    calls.push(url);
    return deferred.promise;
  });

  const first = panel.loadInputImageUrlAsBase64('/images/latest');
  const second = panel.loadInputImageUrlAsBase64('/images/latest');

  assert.equal(first, second);
  await Promise.resolve();
  assert.deepEqual(calls, ['/images/latest']);

  deferred.resolve('AQID');

  assert.equal(await first, 'AQID');
  assert.equal(await second, 'AQID');
  assert.equal(panel.inputImageBase64Load, null);
});

test('PropertyPanel keeps newer cached image load when an older load resolves', async () => {
  const deferredA = createDeferred();
  const deferredB = createDeferred();
  const calls = [];
  const panel = createPanelWithImageLoader((url) => {
    calls.push(url);
    return url.endsWith('/a') ? deferredA.promise : deferredB.promise;
  });

  const first = panel.loadInputImageUrlAsBase64('/images/a');
  const second = panel.loadInputImageUrlAsBase64('/images/b');

  assert.notEqual(first, second);
  await Promise.resolve();
  assert.deepEqual(calls, ['/images/a', '/images/b']);
  assert.equal(panel.inputImageBase64Load.sourceKey, '/images/b');

  deferredA.resolve('A');
  assert.equal(await first, 'A');
  assert.equal(panel.inputImageBase64Load.sourceKey, '/images/b');

  deferredB.resolve('B');
  assert.equal(await second, 'B');
  assert.equal(panel.inputImageBase64Load, null);
});

test('PropertyPanel releases cached image in-flight state after loader failure', async () => {
  const panel = createPanelWithImageLoader(() => {
    throw new Error('load failed');
  });

  await assert.rejects(
    panel.loadInputImageUrlAsBase64('/images/failure'),
    /load failed/
  );

  assert.equal(panel.inputImageBase64Load, null);
});

test('PropertyPanel gates CircleMeasurement parameters by Method without deleting hidden values', () => {
  const { panel, parameters } = createCircleMeasurementPanel('CaliperFitV2');
  const v2Names = panel.getParametersForRender('CircleMeasurement', parameters).map(param => param.name);

  assert.ok(v2Names.includes('SearchCenterMode'));
  assert.ok(v2Names.includes('NominalRadius'));
  assert.ok(!v2Names.includes('Dp'));
  assert.ok(!v2Names.includes('Param1'));

  parameters.find(param => param.name === 'Method').value = 'HoughCircle';
  const legacyNames = panel.getParametersForRender('CircleMeasurement', parameters).map(param => param.name);

  assert.ok(legacyNames.includes('Dp'));
  assert.ok(legacyNames.includes('Param1'));
  assert.ok(!legacyNames.includes('SearchCenterMode'));
  assert.ok(!legacyNames.includes('NominalRadius'));
  assert.equal(parameters.find(param => param.name === 'NominalRadius').value, 30);
});

test('PropertyPanel leaves CircleMeasurement generic when Circle Search V2 feature is off', () => {
  const { panel, parameters } = createCircleMeasurementPanel('CaliperFitV2', 'ImageCenter', false);
  const names = panel.getParametersForRender('CircleMeasurement', parameters).map(param => param.name);
  const groups = panel.groupParameters(parameters);

  assert.ok(names.includes('Dp'));
  assert.ok(names.includes('SearchCenterMode'));
  assert.deepEqual(Object.keys(groups), ['基本参数']);
  assert.equal(panel.renderCircleMeasurementWorkloadHint('CircleMeasurement', parameters), '');
  assert.equal(panel.isReadonlyCircleMeasurementParameter('SearchCenterX'), false);
});

test('PropertyPanel honors startup Circle Search V2 feature flag off', () => {
  const previousStartup = globalThis.window.__CLEARVISION_STARTUP__;
  globalThis.window.__CLEARVISION_STARTUP__ = {
    featureFlags: {
      'Studio:CircleSearchV2ToolEnabled': false
    }
  };

  try {
    const { panel, parameters } = createCircleMeasurementPanel('CaliperFitV2', 'ImageCenter', true);
    const names = panel.getParametersForRender('CircleMeasurement', parameters).map(param => param.name);

    assert.ok(names.includes('Dp'));
    assert.ok(names.includes('SearchCenterMode'));
    assert.equal(panel.renderCircleMeasurementWorkloadHint('CircleMeasurement', parameters), '');
  } finally {
    globalThis.window.__CLEARVISION_STARTUP__ = previousStartup;
  }
});

test('PropertyPanel mounts NPoint draft workbench only when the feature flag is enabled', () => {
  const previousStartup = globalThis.window.__CLEARVISION_STARTUP__;
  try {
    globalThis.window.__CLEARVISION_STARTUP__ = {
      featureFlags: {
        'Studio:NPointCalibrationWorkbenchEnabled': true
      }
    };
    assert.equal(createNPointPanel(true).shouldMountNPointCalibrationWorkbench(), true);

    globalThis.window.__CLEARVISION_STARTUP__ = {
      featureFlags: {
        'Studio:NPointCalibrationWorkbenchEnabled': false
      }
    };
    assert.equal(createNPointPanel(true).shouldMountNPointCalibrationWorkbench(), false);
    assert.equal(createNPointPanel(false).shouldMountNPointCalibrationWorkbench(), false);
  } finally {
    globalThis.window.__CLEARVISION_STARTUP__ = previousStartup;
  }
});

test('CalibrationDraftWorkbench initializes from legacy PointPairs as ephemeral draft state', () => {
  const workbench = Object.create(CalibrationDraftWorkbench.prototype);
  Object.assign(workbench, {
    getProjectId: () => 'project-1',
    getOperator: () => ({
      id: 'node-1',
      type: 'NPointCalibration',
      parameters: [
        { name: 'CalibrationMode', value: 'Perspective' },
        { name: 'CalibrationUnit', value: 'um' },
        { name: 'PointPairs', value: '[{"ImageX":10,"ImageY":20,"WorldX":1,"WorldY":2,"Enabled":false}]' }
      ]
    })
  });

  const session = CalibrationDraftWorkbench.prototype.createSessionFromOperator.call(workbench);

  assert.match(session.sessionId, /^calibration-draft-/);
  assert.equal(session.projectId, 'project-1');
  assert.equal(session.targetNodeId, 'node-1');
  assert.equal(session.mode, 'Perspective');
  assert.equal(session.unit, 'um');
  assert.equal(session.dirty, false);
  assert.equal(session.status, 'Draft');
  assert.equal(session.candidateBundle, null);
  assert.deepEqual(
    session.samples.map(sample => ({
      pixelX: sample.pixelX,
      pixelY: sample.pixelY,
      worldX: sample.worldX,
      worldY: sample.worldY,
      enabled: sample.enabled,
      source: sample.source
    })),
    [
      {
        pixelX: 10,
        pixelY: 20,
        worldX: 1,
        worldY: 2,
        enabled: false,
        source: 'Imported'
      }
    ]
  );
});

test('PropertyPanel groups CaliperFitV2 controls and locks image-center coordinates', () => {
  const { panel, parameters } = createCircleMeasurementPanel('CaliperFitV2', 'ImageCenter');
  const rendered = panel.getParametersForRender('CircleMeasurement', parameters);
  const groups = panel.groupParameters(rendered);

  assert.deepEqual(Object.keys(groups).slice(0, 3), ['检测方法', '搜索几何', '卡尺采样']);
  assert.equal(panel.isReadonlyCircleMeasurementParameter('SearchCenterX'), true);
  assert.equal(panel.isReadonlyCircleMeasurementParameter('SearchCenterY'), true);
  assert.equal(panel.isReadonlyCircleMeasurementParameter('NominalRadius'), false);
  panel.circleSearchV2ImageBounds = { width: 240, height: 180 };
  assert.equal(panel.resolveCircleMeasurementDisplayValue('SearchCenterX', 100), 119.5);
  assert.equal(panel.resolveCircleMeasurementDisplayValue('SearchCenterY', 90), 89.5);

  parameters.find(param => param.name === 'SearchCenterMode').value = 'Explicit';
  assert.equal(panel.isReadonlyCircleMeasurementParameter('SearchCenterX'), false);
});

test('PropertyPanel renders bounded CaliperFitV2 workload hint', () => {
  const { panel, parameters } = createCircleMeasurementPanel('CaliperFitV2');
  const html = panel.renderCircleMeasurementWorkloadHint('CircleMeasurement', parameters);

  assert.match(html, /Sampling work:/);
  assert.match(html, /61,920/);
});
