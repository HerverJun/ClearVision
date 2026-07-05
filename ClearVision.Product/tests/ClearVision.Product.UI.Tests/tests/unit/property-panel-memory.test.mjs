import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

globalThis.window = globalThis.window || {};

const { PropertyPanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
const { CalibrationDraftWorkbench } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/calibrationDraftWorkbench.js');
const httpClient = (await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js')).default;

function createDeferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function readRepoText(relativeUrl) {
  return readFileSync(new URL(relativeUrl, import.meta.url), 'utf8');
}

class PreviewHostFakeElement {
  constructor(id = '') {
    this.id = id;
    this._innerHTML = '';
    this.textContent = '';
    this.style = {};
    this.disabled = false;
    this.attributes = new Map();
    this.listeners = new Map();
  }

  set innerHTML(value) {
    this._innerHTML = String(value ?? '');
  }

  get innerHTML() {
    return this._innerHTML;
  }

  set src(value) {
    this.setAttribute('src', value);
  }

  get src() {
    return this.getAttribute('src') || '';
  }

  setAttribute(name, value) {
    this.attributes.set(String(name), String(value));
  }

  getAttribute(name) {
    return this.attributes.get(String(name)) ?? null;
  }

  removeAttribute(name) {
    this.attributes.delete(String(name));
  }

  addEventListener(type, listener) {
    if (!this.listeners.has(type)) {
      this.listeners.set(type, []);
    }
    this.listeners.get(type).push(listener);
  }

  querySelectorAll() {
    return [];
  }
}

class PreviewHostFakeContainer extends PreviewHostFakeElement {
  constructor(id = 'container') {
    super(id);
    this.elementsById = new Map();
    this.elementsByRole = new Map();
  }

  set innerHTML(value) {
    this._innerHTML = String(value ?? '');
    this.elementsById = new Map();
    this.elementsByRole = new Map();

    const ids = Array.from(this._innerHTML.matchAll(/id="([^"]+)"/g)).map(match => match[1]);
    ids.forEach(id => {
      const element = new PreviewHostFakeElement(id);
      if (id === 'btn-preview-open-output') {
        element.disabled = true;
      }
      this.elementsById.set(id, element);
    });

    const outputImage = this.elementsById.get('preview-output-image');
    if (outputImage) {
      this.elementsByRole.set('preview-output-image', outputImage);
    }
  }

  get innerHTML() {
    return this._innerHTML;
  }

  querySelector(selector) {
    const idMatch = String(selector).match(/^#(.+)$/);
    if (idMatch) {
      return this.elementsById.get(idMatch[1]) || null;
    }

    const roleMatch = String(selector).match(/^\[data-role="([^"]+)"\]$/);
    if (roleMatch) {
      return this.elementsByRole.get(roleMatch[1]) || null;
    }

    return null;
  }
}

function createPreviewCoordinatorHarness(initialState) {
  const listeners = new Set();
  let state = initialState;
  let previewRequests = 0;

  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      listener(state);
      return () => listeners.delete(listener);
    },
    requestActivePreview() {
      previewRequests += 1;
    },
    listenerCount: () => listeners.size,
    previewRequestCount: () => previewRequests
  };
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

test('PropertyPanel preview resource opt-out destroys legacy PreviewPanel and ROI instances', () => {
  const destroyed = [];
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    previewResourcesEnabled: false,
    previewPanel: {
      destroy() {
        destroyed.push('preview');
      }
    },
    roiEditorPanel: {
      destroy() {
        destroyed.push('roi');
      }
    },
    container: {
      querySelector() {
        throw new Error('disabled preview resources must not query mount containers');
      }
    }
  });

  PropertyPanel.prototype.initPreviewPanel.call(panel);
  PropertyPanel.prototype.initRoiEditorPanel.call(panel);

  assert.deepEqual(destroyed, ['preview', 'roi']);
  assert.equal(panel.previewPanel, null);
  assert.equal(panel.roiEditorPanel, null);
});

test('PropertyPanel reuses an external PreviewPanel host without duplicate subscriptions', () => {
  const operator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const previewCoordinator = createPreviewCoordinatorHarness({
    activeNodeId: 'node-1',
    nodeType: 'Thresholding',
    title: 'Threshold',
    status: 'idle',
    presenter: {
      statusText: '等待预览',
      inputImageSrc: null,
      outputImageSrc: null
    },
    outputData: {},
    request: {
      projectId: 'project-1',
      nodeId: 'node-1',
      flowRevision: 3,
      parameterSnapshot: [],
      requestKey: 'request-1'
    }
  });
  const propertyContainer = new PreviewHostFakeContainer('property-root');
  const previewContainer = new PreviewHostFakeContainer('preview-root');
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    container: propertyContainer,
    previewContainer,
    previewResourcesEnabled: true,
    previewPanel: null,
    roiEditorPanel: null,
    calibrationDraftWorkbench: null,
    currentOperator: operator,
    previewCoordinator,
    onOpenPreviewImage() {},
    validateCurrentOperator() {
      return true;
    }
  });

  PropertyPanel.prototype.initPreviewPanel.call(panel);
  const firstPreviewPanel = panel.previewPanel;

  assert.ok(firstPreviewPanel);
  assert.equal(previewCoordinator.listenerCount(), 1);
  assert.equal((previewContainer.innerHTML.match(/data-role="preview-output-image"/g) || []).length, 1);

  PropertyPanel.prototype.initPreviewPanel.call(panel);

  assert.equal(panel.previewPanel, firstPreviewPanel);
  assert.equal(previewCoordinator.listenerCount(), 1);

  PropertyPanel.prototype.clear.call(panel);

  assert.equal(panel.previewPanel, firstPreviewPanel);
  assert.equal(previewCoordinator.listenerCount(), 1);
  assert.equal((previewContainer.innerHTML.match(/data-role="preview-output-image"/g) || []).length, 1);

  PropertyPanel.prototype.destroy.call(panel);
  assert.equal(previewCoordinator.listenerCount(), 0);
  assert.match(previewContainer.innerHTML, /请选择一个算子/);
});

test('PropertyPanel clears external preview host when switching to a library selection', () => {
  const canvasOperator = {
    id: 'node-1',
    type: 'Thresholding',
    title: 'Threshold',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  const secondCanvasOperator = {
    id: 'node-2',
    type: 'Morphology',
    title: 'Morphology',
    parameters: [{ name: 'Operation', value: 'Open' }]
  };
  const previewCoordinator = createPreviewCoordinatorHarness({
    activeNodeId: 'node-1',
    nodeType: 'Thresholding',
    title: 'Threshold',
    status: 'success',
    presenter: {
      statusText: '预览完成',
      inputImageSrc: 'data:image/png;base64,input',
      outputImageSrc: 'data:image/png;base64,old-output'
    },
    outputData: { Score: 0.98 },
    request: {
      projectId: 'project-1',
      nodeId: 'node-1',
      flowRevision: 3,
      parameterSnapshot: [],
      requestKey: 'request-1'
    }
  });
  const propertyContainer = new PreviewHostFakeContainer('property-root');
  const previewContainer = new PreviewHostFakeContainer('preview-root');
  const panel = Object.create(PropertyPanel.prototype);
  Object.assign(panel, {
    container: propertyContainer,
    previewContainer,
    previewResourcesEnabled: true,
    previewPanel: null,
    roiEditorPanel: null,
    calibrationDraftWorkbench: null,
    currentOperator: canvasOperator,
    previewCoordinator,
    onOpenPreviewImage() {},
    onChangeCallback() {},
    inputImageBase64Load: null,
    pendingRecommendation: null,
    recommendedFieldNames: new Set(),
    recommendationSupportedOperators: new Set(),
    circleSearchV2ToolEnabled: false,
    nPointCalibrationWorkbenchEnabled: false,
    validateCurrentOperator() {
      return true;
    }
  });

  PropertyPanel.prototype.initPreviewPanel.call(panel);

  assert.equal(previewCoordinator.listenerCount(), 1);
  assert.equal(
    previewContainer.querySelector('#preview-output-image')?.getAttribute('src'),
    'data:image/png;base64,old-output'
  );

  panel.currentOperator = {
    isLibrarySelection: true,
    type: 'Thresholding',
    displayName: 'Thresholding',
    category: '图像处理',
    description: 'Library entry',
    parameters: [{ name: 'Threshold', value: 128 }]
  };
  PropertyPanel.prototype.render.call(panel);

  assert.equal(previewCoordinator.listenerCount(), 0);
  assert.equal(panel.previewPanel, null);
  assert.match(previewContainer.innerHTML, /算子库条目无运行预览/);
  assert.doesNotMatch(previewContainer.innerHTML, /preview-output-image/);
  assert.doesNotMatch(previewContainer.innerHTML, /old-output/);
  assert.doesNotMatch(previewContainer.innerHTML, /operator-preview-output-item/);
  assert.doesNotMatch(previewContainer.innerHTML, /operator-result-diagnostic/);

  panel.currentOperator = secondCanvasOperator;
  PropertyPanel.prototype.initPreviewPanel.call(panel);

  assert.equal(previewCoordinator.listenerCount(), 1);
  assert.equal((previewContainer.innerHTML.match(/data-role="preview-output-image"/g) || []).length, 1);

  PropertyPanel.prototype.destroy.call(panel);
  assert.equal(previewCoordinator.listenerCount(), 0);
});

test('flow editor layout keeps variables in the toolbar and preview outside PropertyPanel', () => {
  const indexSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/index.html');
  const appSource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/app.js');
  const propertySource = readRepoText('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/propertyPanel.js');
  const rightSidebar = indexSource.match(/<aside class="[^"]*\bsidebar\b[^"]*\bright\b[^"]*"[\s\S]*?<\/aside>/)?.[0] || '';

  assert.match(indexSource, /class="toolbar-global-variable-host"[\s\S]*id="global-variable-panel"/);
  assert.match(indexSource, /未打开工程/);
  assert.match(indexSource, /id="operator-rail"/);
  assert.match(indexSource, /class="sidebar left inspector-pane"[\s\S]*属性检查器/);
  assert.doesNotMatch(rightSidebar, /panel-title">全局变量/);
  assert.match(rightSidebar, /preview-sidebar-panel/);
  assert.match(rightSidebar, /预览工作台/);
  assert.doesNotMatch(rightSidebar, /property-sidebar-panel/);
  assert.match(appSource, /function shouldLegacyPropertyPanelOwnSidebarPreview\(\)/);
  assert.match(appSource, /previewResourcesEnabled:\s*!isPreviewPanelCapabilityEnabled\(\)/);
  assert.match(appSource, /previewContainer:\s*ownsPreviewSidebar[\s\S]*document\.getElementById\('preview-panel'\)/);
  assert.match(propertySource, /shouldMountInternalPreviewContainer/);
  assert.match(propertySource, /resetPreviewForNonCanvasSelection/);
  assert.match(propertySource, /this\.previewPanel\?\.container === container/);
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

test('CalibrationDraftWorkbench formal save posts candidate and records saved asset', async () => {
  const originalPost = httpClient.post;
  const calls = [];
  let savedResponse = null;
  const statusMessages = [];
  const workbench = Object.create(CalibrationDraftWorkbench.prototype);
  Object.assign(workbench, {
    formalSaveInProgress: false,
    session: {
      sessionId: 'draft-1',
      imageIdentity: 'image-hash',
      candidateBundleJson: '{"schemaVersion":2}',
      diagnostics: [],
      status: 'Solved'
    },
    getProjectId: () => 'project-1',
    getProject: () => ({ id: 'project-1', persistenceRevision: 11 }),
    getOperator: () => ({ id: 'node-1' }),
    renderStatus: message => statusMessages.push(message),
    onFormalSaveSuccess: response => {
      savedResponse = response;
    }
  });

  httpClient.post = async (url, body) => {
    calls.push({ url, body });
    return {
      projectId: 'project-1',
      persistenceRevision: 12,
      asset: {
        assetId: 'asset-1',
        projectRevision: 12,
        contentHash: 'sha256:abc'
      },
      assets: {
        calibrationAssets: [],
        spatialAssets: []
      }
    };
  };

  try {
    await CalibrationDraftWorkbench.prototype.formalSaveCandidate.call(workbench);
  } finally {
    httpClient.post = originalPost;
  }

  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, '/projects/project-1/calibration-assets/from-draft');
  assert.equal(calls[0].body.expectedPersistenceRevision, 11);
  assert.equal(calls[0].body.sessionId, 'draft-1');
  assert.equal(calls[0].body.targetNodeId, 'node-1');
  assert.equal(calls[0].body.candidateBundleJson, '{"schemaVersion":2}');
  assert.equal(workbench.session.status, 'FormalSaved');
  assert.equal(workbench.session.formalAssetId, 'asset-1');
  assert.equal(workbench.session.formalAssetRevision, 12);
  assert.equal(savedResponse.persistenceRevision, 12);
  assert.ok(statusMessages.some(message => String(message || '').includes('Saved asset-1')));
});

test('CalibrationDraftWorkbench formal save displays backend failure reason', async () => {
  const originalPost = httpClient.post;
  const statusMessages = [];
  const workbench = Object.create(CalibrationDraftWorkbench.prototype);
  Object.assign(workbench, {
    formalSaveInProgress: false,
    session: {
      sessionId: 'draft-1',
      imageIdentity: 'image-hash',
      candidateBundleJson: '{"schemaVersion":2}',
      diagnostics: [],
      status: 'Solved'
    },
    getProjectId: () => 'project-1',
    getProject: () => ({ id: 'project-1', persistenceRevision: 11 }),
    getOperator: () => ({ id: 'node-1' }),
    renderStatus: message => statusMessages.push(message),
    onFormalSaveSuccess: () => {
      throw new Error('should not save');
    }
  });

  httpClient.post = async () => {
    throw new Error('PSV019: calibration candidate checksum mismatch.');
  };

  try {
    await CalibrationDraftWorkbench.prototype.formalSaveCandidate.call(workbench);
  } finally {
    httpClient.post = originalPost;
  }

  assert.equal(workbench.session.status, 'FormalSaveFailed');
  assert.equal(workbench.session.diagnostics[0], 'PSV019: calibration candidate checksum mismatch.');
  assert.ok(statusMessages.includes('PSV019: calibration candidate checksum mismatch.'));
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
