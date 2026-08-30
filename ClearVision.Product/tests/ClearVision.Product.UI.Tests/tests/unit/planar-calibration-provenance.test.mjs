import test from 'node:test';
import assert from 'node:assert/strict';

function createStorage() {
  const values = new Map();
  return {
    getItem(key) { return values.has(key) ? values.get(key) : null; },
    setItem(key, value) { values.set(key, String(value)); },
    removeItem(key) { values.delete(key); }
  };
}

const postedMessages = [];
globalThis.window = {
  sessionStorage: createStorage(),
  localStorage: createStorage(),
  addEventListener() {},
  removeEventListener() {},
  chrome: {
    webview: {
      addEventListener() {},
      removeEventListener() {},
      postMessage(message) { postedMessages.push(structuredClone(message)); }
    }
  }
};
globalThis.alert = () => {};

const { PlanarScaleOffsetCalibWizard } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/calibration/planarScaleOffsetCalibWizard.js'
);

function createWizard(options = {}) {
  const settings = {
    projectId: 'project-planar-1',
    expectedPersistenceRevision: 17,
    sessionId: 'session-planar-1',
    assetId: 'asset-planar-1',
    getCameraBindingId: () => 'camera-binding-1',
    ...options
  };
  const wizard = Object.create(PlanarScaleOffsetCalibWizard.prototype);
  Object.assign(wizard, {
    cameraManager: null,
    captureFrame: null,
    getCameraBindingId: settings.getCameraBindingId,
    getProjectId: settings.getProjectId || null,
    projectId: settings.projectId ?? null,
    getExpectedPersistenceRevision: settings.getExpectedPersistenceRevision || null,
    expectedPersistenceRevision: settings.expectedPersistenceRevision ?? null,
    getSessionId: settings.getSessionId || null,
    sessionId: settings.sessionId ?? null,
    assetId: settings.assetId,
    currentStep: 1,
    points: [],
    solveResult: null,
    solveArtifact: null,
    solveContext: null
  });
  wizard.els = {
    btnSolve: { innerHTML: '', disabled: false },
    btnNext: { disabled: false, textContent: '' },
    inpAssetId: { value: 'asset-planar-1', disabled: false },
    saveContextHint: { textContent: '' }
  };
  return wizard;
}

test('planar solve binds project context and formal save posts only the server artifact reference', () => {
  postedMessages.length = 0;
  const wizard = createWizard();
  wizard.points = [
    { pixelX: 1, pixelY: 2, physicalX: 3, physicalY: 4 },
    { pixelX: 5, pixelY: 6, physicalX: 7, physicalY: 8 },
    { pixelX: 9, pixelY: 10, physicalX: 11, physicalY: 12 }
  ];

  wizard.solveCalibration();
  const solve = postedMessages.findLast(message => message.messageType === 'planar2d:solve');
  assert.ok(solve);
  assert.equal(solve.payload.projectId, 'project-planar-1');
  assert.equal(solve.payload.sessionId, 'session-planar-1');
  assert.equal(solve.payload.assetId, 'asset-planar-1');
  assert.equal(solve.payload.cameraBindingId, 'camera-binding-1');
  assert.equal(solve.payload.points.length, 3);

  wizard.solveResult = {
    accepted: true,
    result: { forgedClientAuthority: true },
    fileName: 'forged-local-path.json'
  };
  wizard.solveArtifact = { artifactId: 'planar-solve-artifact-1' };
  wizard.solveContext = {
    projectId: 'project-planar-1',
    sessionId: 'session-planar-1',
    assetId: 'asset-planar-1',
    cameraBindingId: 'camera-binding-1'
  };
  wizard.saveCalibration();

  const save = postedMessages.findLast(message => message.messageType === 'planar2d:save');
  assert.ok(save);
  assert.deepEqual(save.payload, {
    solveArtifactId: 'planar-solve-artifact-1',
    projectId: 'project-planar-1',
    expectedPersistenceRevision: 17,
    assetId: 'asset-planar-1',
    sessionId: 'session-planar-1',
    cameraBindingId: 'camera-binding-1'
  });
  assert.equal(Object.hasOwn(save.payload, 'result'), false);
  assert.equal(Object.hasOwn(save.payload, 'fileName'), false);
  assert.equal(Object.hasOwn(save.payload, 'contentHash'), false);
});

test('planar solve remains display-only without project context', () => {
  postedMessages.length = 0;
  const alerts = [];
  const originalAlert = globalThis.alert;
  globalThis.alert = message => alerts.push(String(message));

  try {
    const wizard = createWizard({ projectId: null, expectedPersistenceRevision: null });
    wizard.solveResult = { accepted: true };
    wizard.solveArtifact = { artifactId: 'unscoped-artifact' };
    wizard.solveContext = {
      projectId: '',
      sessionId: 'session-planar-1',
      cameraBindingId: 'camera-binding-1'
    };

    wizard.refreshFormalSaveState();
    assert.equal(wizard.els.btnNext.disabled, true);
    assert.equal(wizard.els.inpAssetId.disabled, true);
    assert.match(wizard.els.saveContextHint.textContent, /没有工程上下文/);
    wizard.saveCalibration();

    assert.equal(postedMessages.some(message => message.messageType === 'planar2d:save'), false);
    assert.ok(alerts.some(message => message.includes('没有工程上下文')));
  } finally {
    globalThis.alert = originalAlert;
  }
});
