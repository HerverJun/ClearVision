import test from 'node:test';
import assert from 'node:assert/strict';

function createMemoryStorage() {
  const values = new Map();

  return {
    getItem(key) {
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      values.set(key, String(value));
    },
    removeItem(key) {
      values.delete(key);
    },
    clear() {
      values.clear();
    }
  };
}

const sessionStorage = createMemoryStorage();
const localStorage = createMemoryStorage();

globalThis.window = {
  chrome: null,
  location: {
    protocol: 'http:',
    hostname: 'localhost',
    port: '5000'
  },
  sessionStorage,
  localStorage
};

globalThis.document = {
  createElement() {
    return {
      _textContent: '',
      set textContent(value) {
        this._textContent = String(value ?? '');
      },
      get textContent() {
        return this._textContent;
      },
      get innerHTML() {
        return this._textContent
          .replaceAll('&', '&amp;')
          .replaceAll('<', '&lt;')
          .replaceAll('>', '&gt;')
          .replaceAll('"', '&quot;');
      },
      set innerHTML(value) {
        this._textContent = String(value ?? '');
      },
      appendChild() {},
      addEventListener() {}
    };
  },
  getElementById() {
    return null;
  },
  body: {
    appendChild() {},
    removeChild() {}
  }
};

const { default: inspectionController } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionController.js'
);
const { default: httpClient } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/core/messaging/httpClient.js'
);
const { setCurrentProject } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectManager.js'
);
const { InspectionPanel } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/inspectionPanel.js'
);
const { AnalysisCardsPanel, renderDiagnosticsCardsHtml } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/inspection/analysisCardsPanel.js'
);

function createPanel({ projectId = 'project-current', isContinuous = false } = {}) {
  const calls = [];
  const panel = Object.create(InspectionPanel.prototype);

  Object.assign(panel, {
    projectId,
    isContinuous,
    selectedRunMode: 'camera',
    runtimeConfig: {
      applyProtectionRules: true,
      missingMaterialTimeoutSeconds: 120,
      stopOnConsecutiveNg: 0
    },
    consecutiveNgCount: 0,
    _lastProtectionMessage: '',
    analysisCardsPanel: {
      updateCards() {
        calls.push(['analysisUpdate']);
      },
      clear() {
        calls.push(['analysisClear']);
      }
    },
    syncAnalysisFlowContext() {
      calls.push(['syncAnalysisFlowContext']);
    },
    updateStatus(status, text) {
      calls.push(['status', status, text]);
    },
    setButtonsState(isRunning) {
      calls.push(['buttons', isRunning]);
    },
    updateProtectionNotice(message, level) {
      calls.push(['notice', message, level]);
    },
    armProtectionWatchdog(reason) {
      calls.push(['armProtectionWatchdog', reason]);
    },
    clearProtectionWatchdog() {
      calls.push(['clearProtectionWatchdog']);
    },
    updateCounters() {
      calls.push(['updateCounters']);
    },
    addRecentResult(result) {
      calls.push(['addRecentResult', result?.id]);
    },
    getAnalysisPayload() {
      calls.push(['getAnalysisPayload']);
      return null;
    }
  });

  return { panel, calls };
}

function createClassList() {
  const classes = new Set();

  return {
    toggle(value) {
      if (classes.has(value)) {
        classes.delete(value);
        return false;
      }

      classes.add(value);
      return true;
    },
    contains(value) {
      return classes.has(value);
    }
  };
}

function createAnalysisContainer() {
  const listeners = new Map();

  return {
    innerHTML: '',
    addEventListener(type, handler) {
      if (!listeners.has(type)) {
        listeners.set(type, new Set());
      }

      listeners.get(type).add(handler);
    },
    removeEventListener(type, handler) {
      listeners.get(type)?.delete(handler);
    },
    contains() {
      return true;
    },
    dispatch(type, event = {}) {
      Array.from(listeners.get(type) || []).forEach(handler => handler(event));
    },
    listenerCount(type) {
      return listeners.get(type)?.size || 0;
    }
  };
}

function createEventTarget() {
  const listeners = new Map();

  return {
    disabled: false,
    classList: {
      toggle() {}
    },
    textContent: '',
    addEventListener(type, handler) {
      if (!listeners.has(type)) {
        listeners.set(type, new Set());
      }

      listeners.get(type).add(handler);
    },
    removeEventListener(type, handler) {
      listeners.get(type)?.delete(handler);
    },
    dispatch(type, event = {}) {
      Array.from(listeners.get(type) || []).forEach(handler => handler(event));
    },
    listenerCount(type) {
      return listeners.get(type)?.size || 0;
    }
  };
}

function countMatches(text, pattern) {
  return (String(text || '').match(pattern) || []).length;
}

test('handleRunSingle restores buttons when executeSingle resolves without a completion callback', async (t) => {
  const originalExecuteSingle = inspectionController.executeSingle;
  const originalSetProject = inspectionController.setProject;
  const selectedProjectIds = [];

  inspectionController.setProject = (projectId) => {
    selectedProjectIds.push(projectId);
  };
  inspectionController.executeSingle = async () => ({
    id: 'deduped-result',
    projectId: 'project-current',
    status: 'OK'
  });
  setCurrentProject({ id: 'project-current', name: 'Current project' });

  t.after(() => {
    inspectionController.executeSingle = originalExecuteSingle;
    inspectionController.setProject = originalSetProject;
    setCurrentProject(null);
  });

  const { panel, calls } = createPanel({ projectId: 'project-current' });

  await panel.handleRunSingle();

  assert.deepEqual(selectedProjectIds, ['project-current']);
  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [true, false]
  );
  assert.equal(calls.some(([kind]) => kind === 'clearProtectionWatchdog'), true);
});

test('handleRunSingle ignores completion after panel disposal', async (t) => {
  const originalExecuteSingle = inspectionController.executeSingle;
  const originalSetProject = inspectionController.setProject;
  let resolveExecute;

  inspectionController.setProject = () => {};
  inspectionController.executeSingle = () => new Promise(resolve => {
    resolveExecute = resolve;
  });
  setCurrentProject({ id: 'project-current', name: 'Current project' });

  t.after(() => {
    inspectionController.executeSingle = originalExecuteSingle;
    inspectionController.setProject = originalSetProject;
    setCurrentProject(null);
  });

  const { panel, calls } = createPanel({ projectId: 'project-current' });
  Object.assign(panel, {
    _isDisposed: false,
    _eventDisposers: [],
    _materialTimeoutHandle: null
  });

  const runPromise = panel.handleRunSingle();
  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [true]
  );

  panel.dispose();
  resolveExecute({ id: 'late-result', projectId: 'project-current', status: 'OK' });
  await runPromise;

  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [true]
  );
  assert.equal(calls.some(([kind, status]) => kind === 'status' && status === 'error'), false);
});

test('handleRunContinuous ignores failed startup after panel disposal', async (t) => {
  const originalStartRealtime = inspectionController.startRealtime;
  const originalStartRealtimeFlowMode = inspectionController.startRealtimeFlowMode;
  const originalSetProject = inspectionController.setProject;
  let rejectStart;

  inspectionController.setProject = () => {};
  inspectionController.startRealtimeFlowMode = () => new Promise((resolve, reject) => {
    rejectStart = reject;
  });
  setCurrentProject({ id: 'project-current', name: 'Current project' });

  t.after(() => {
    inspectionController.startRealtime = originalStartRealtime;
    inspectionController.startRealtimeFlowMode = originalStartRealtimeFlowMode;
    inspectionController.setProject = originalSetProject;
    setCurrentProject(null);
  });

  const { panel, calls } = createPanel({ projectId: 'project-current' });
  Object.assign(panel, {
    _isDisposed: false,
    _eventDisposers: [],
    _materialTimeoutHandle: null
  });

  const runPromise = panel.handleRunContinuous();
  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [true]
  );

  panel.dispose();
  rejectStart(new Error('late startup failure'));
  await runPromise;

  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [true]
  );
  assert.equal(calls.some(([kind, status]) => kind === 'status' && status === 'error'), false);
});

test('run mode defaults follow flow purpose and acquisition topology', () => {
  const panel = Object.create(InspectionPanel.prototype);

  setCurrentProject({
    id: 'commissioning-project',
    flow: { purpose: 'Commissioning', operators: [{ type: 'ImageAcquisition' }] }
  });
  assert.equal(panel.getDefaultRunMode(), 'flow');

  setCurrentProject({
    id: 'flow-project',
    flow: { purpose: 'Inspection', operators: [{ type: 'ModbusCommunication' }] }
  });
  assert.equal(panel.getDefaultRunMode(), 'flow');

  setCurrentProject({
    id: 'camera-project',
    flow: { purpose: 'Inspection', operators: [{ type: 'ImageAcquisition' }] }
  });
  assert.equal(panel.getDefaultRunMode(), 'camera');

  setCurrentProject(null);
});

test('setProjectContext recalculates and updates the visible run mode', () => {
  const runModeSelect = { value: 'camera' };
  const panel = Object.create(InspectionPanel.prototype);
  setCurrentProject({
    id: 'commissioning-project',
    flow: { purpose: 'Commissioning', operators: [] }
  });
  Object.assign(panel, {
    projectId: 'old-project',
    selectedRunMode: 'camera',
    container: {
      querySelector(selector) {
        return selector === '#run-mode' ? runModeSelect : null;
      }
    },
    reset() {}
  });

  assert.equal(panel.setProjectContext('commissioning-project'), true);
  assert.equal(panel.selectedRunMode, 'flow');
  assert.equal(runModeSelect.value, 'flow');

  setCurrentProject(null);
});

test('handleInspectionResult ignores stale project data but restores single-run buttons', () => {
  const { panel, calls } = createPanel({
    projectId: 'project-current',
    isContinuous: false
  });

  panel.handleInspectionResult({
    id: 'old-result',
    projectId: 'project-old',
    status: 'OK',
    processingTimeMs: 12,
    outputImageBase64: 'not-for-current-project'
  });

  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [false]
  );
  assert.equal(panel._lastProtectionMessage, '已忽略其他工程的检测结果，当前工程显示保持不变。');
  assert.equal(calls.some(([kind]) => kind === 'updateCounters'), false);
  assert.equal(calls.some(([kind]) => kind === 'addRecentResult'), false);
  assert.equal(calls.some(([kind]) => kind === 'analysisUpdate'), false);
  assert.equal(calls.some(([kind]) => kind === 'analysisClear'), false);
});

test('handleInspectionResult keeps continuous controls running for stale project data', () => {
  const { panel, calls } = createPanel({
    projectId: 'project-current',
    isContinuous: true
  });

  panel.handleInspectionResult({
    id: 'old-result',
    projectId: 'project-old',
    status: 'NG'
  });

  assert.deepEqual(
    calls.filter(([kind]) => kind === 'buttons').map(([, isRunning]) => isRunning),
    [true]
  );
  assert.equal(
    calls.some(([kind, reason]) => kind === 'armProtectionWatchdog' && reason === '等待下一次触发结果'),
    true
  );
  assert.equal(calls.some(([kind]) => kind === 'updateCounters'), false);
});

test('hidden inspection view stores only compact pending analysis payloads', () => {
  const { panel } = createPanel({
    projectId: 'project-current',
    isContinuous: false
  });
  const longText = 'A'.repeat(900);
  const imagePayload = 'B'.repeat(512);
  Object.assign(panel, {
    isPanelVisible() {
      return false;
    },
    getAnalysisPayload(result) {
      return result.analysisData;
    }
  });

  panel.handleInspectionResult({
    id: 'large-hidden-result',
    projectId: 'project-current',
    status: 'OK',
    processingTimeMs: 12,
    analysisData: {
      version: 1,
      cards: Array.from({ length: 30 }, (_, cardIndex) => ({
        category: 'structured',
        title: `Card ${cardIndex}`,
        message: longText,
        fields: Array.from({ length: 20 }, (_, fieldIndex) => ({
          key: fieldIndex === 1 ? 'OutputImageBase64' : `Field${fieldIndex}`,
          label: `Field ${fieldIndex}`,
          value: fieldIndex === 0
            ? longText
            : (fieldIndex === 1 ? imagePayload : Array.from({ length: 40 }, (__, itemIndex) => `item-${itemIndex}`))
        }))
      }))
    }
  });

  const pendingPayload = panel._pendingAnalysisUpdate.analysisPayload;
  assert.equal(pendingPayload.cards.length, 24);
  assert.equal(pendingPayload.hiddenCardCount, 6);
  assert.equal(pendingPayload.cards[0].fields.length, 16);
  assert.equal(pendingPayload.cards[0].hiddenFieldCount, 4);
  assert.equal(pendingPayload.cards[0].fields[0].value.length, 515);
  assert.match(pendingPayload.cards[0].fields[0].value, /\.\.\.$/);
  assert.equal(pendingPayload.cards[0].fields[1].value, '[image omitted]');
  assert.equal(pendingPayload.cards[0].fields[2].value.length, 25);
  assert.equal(pendingPayload.cards[0].fields[2].value.at(-1), '+16 more');
  assert.equal(JSON.stringify(pendingPayload).includes(longText), false);
  assert.equal(JSON.stringify(pendingPayload).includes(imagePayload), false);
});

test('addRecentResult renders only the latest three inspection results', (t) => {
  const originalGetElementById = globalThis.document.getElementById;
  const container = { innerHTML: '' };
  const panel = Object.create(InspectionPanel.prototype);

  Object.assign(panel, {
    container: { querySelector: () => null },
    analysisCardsPanel: { clear() {} },
    clearProtectionWatchdog() {},
    updateStatus() {},
    updateCounters() {},
    setButtonsState() {},
    updateProtectionNotice() {}
  });

  globalThis.document.getElementById = (id) => (
    id === 'inspection-recent-results-grid' ? container : null
  );

  t.after(() => {
    panel.reset();
    globalThis.document.getElementById = originalGetElementById;
  });

  for (let index = 1; index <= 4; index += 1) {
    panel.addRecentResult({
      id: `result-${index}`,
      status: 'NG',
      timestamp: `2026-05-21T10:30:0${index}Z`,
      errorMessage: `preview-${index}`
    });
  }

  const renderedItems = container.innerHTML.match(/recent-result-item/g) || [];
  assert.equal(renderedItems.length, 3);
  assert.match(container.innerHTML, /preview-4/);
  assert.match(container.innerHTML, /preview-3/);
  assert.match(container.innerHTML, /preview-2/);
  assert.doesNotMatch(container.innerHTML, /preview-1/);
});

test('dispose releases inspection subscriptions and analysis card resources', () => {
  const calls = [];
  const panel = Object.create(InspectionPanel.prototype);

  Object.assign(panel, {
    unsubscribeCompleted() {
      calls.push('unsubscribeCompleted');
    },
    unsubscribeError() {
      calls.push('unsubscribeError');
    },
    clearProtectionWatchdog() {
      calls.push('clearProtectionWatchdog');
    },
    analysisCardsPanel: {
      dispose() {
        calls.push('analysisDispose');
      }
    }
  });

  panel.dispose();

  assert.deepEqual(calls, [
    'unsubscribeCompleted',
    'unsubscribeError',
    'clearProtectionWatchdog',
    'analysisDispose'
  ]);
  assert.equal(panel.unsubscribeCompleted, null);
  assert.equal(panel.unsubscribeError, null);
  assert.equal(panel.analysisCardsPanel, null);
});

test('dispose releases DOM listeners bound by inspection controls', () => {
  const calls = [];
  const runSingleBtn = createEventTarget();
  const runContinuousBtn = createEventTarget();
  const stopBtn = createEventTarget();
  const runModeSelect = createEventTarget();
  const runModeDesc = createEventTarget();
  const elements = new Map([
    ['#btn-run-single', runSingleBtn],
    ['#btn-run-continuous', runContinuousBtn],
    ['#btn-stop', stopBtn],
    ['#run-mode', runModeSelect],
    ['#run-mode-desc', runModeDesc]
  ]);
  const panel = Object.create(InspectionPanel.prototype);

  Object.assign(panel, {
    container: {
      querySelector(selector) {
        return elements.get(selector) || null;
      }
    },
    _eventDisposers: [],
    selectedRunMode: 'camera',
    handleRunSingle() {
      calls.push('single');
    },
    handleRunContinuous() {
      calls.push('continuous');
    },
    handleStop() {
      calls.push('stop');
    },
    clearProtectionWatchdog() {
      calls.push('clearProtectionWatchdog');
    },
    analysisCardsPanel: null
  });

  panel.bindEvents();

  assert.equal(runSingleBtn.listenerCount('click'), 1);
  assert.equal(runContinuousBtn.listenerCount('click'), 1);
  assert.equal(stopBtn.listenerCount('click'), 1);
  assert.equal(runModeSelect.listenerCount('change'), 1);

  runSingleBtn.dispatch('click', { target: runSingleBtn });
  runContinuousBtn.dispatch('click', { target: runContinuousBtn });
  stopBtn.dispatch('click', { target: stopBtn });
  runModeSelect.dispatch('change', { target: { value: 'flow' } });

  assert.deepEqual(calls, ['single', 'continuous', 'stop']);
  assert.equal(panel.selectedRunMode, 'flow');
  assert.match(runModeDesc.textContent, /PLC/);

  panel.dispose();

  assert.equal(runSingleBtn.listenerCount('click'), 0);
  assert.equal(runContinuousBtn.listenerCount('click'), 0);
  assert.equal(stopBtn.listenerCount('click'), 0);
  assert.equal(runModeSelect.listenerCount('change'), 0);

  runSingleBtn.dispatch('click', { target: runSingleBtn });
  runContinuousBtn.dispatch('click', { target: runContinuousBtn });
  stopBtn.dispatch('click', { target: stopBtn });
  runModeSelect.dispatch('change', { target: { value: 'camera' } });

  assert.deepEqual(calls, ['single', 'continuous', 'stop', 'clearProtectionWatchdog']);
  assert.equal(panel.selectedRunMode, 'flow');
});

test('dispose prevents pending runtime config load from updating panel state', async (t) => {
  const originalGet = httpClient.get;
  let resolveSettings;

  httpClient.get = (url) => {
    assert.equal(url, '/settings');
    return new Promise(resolve => {
      resolveSettings = resolve;
    });
  };

  t.after(() => {
    httpClient.get = originalGet;
  });

  const calls = [];
  const panel = Object.create(InspectionPanel.prototype);
  const initialRuntimeConfig = {
    autoRun: false,
    stopOnConsecutiveNg: 0,
    missingMaterialTimeoutSeconds: 120,
    applyProtectionRules: true
  };

  Object.assign(panel, {
    runtimeConfig: initialRuntimeConfig,
    _isDisposed: false,
    _runtimeConfigLoadedAt: 0,
    _runtimeConfigLoadPromise: null,
    _pendingAnalysisUpdate: { large: 'payload' },
    _eventDisposers: [],
    _materialTimeoutHandle: null,
    updateProtectionNotice() {
      calls.push('notice');
    },
    tryAutoRunIfNeeded() {
      calls.push('autoRun');
    },
    analysisCardsPanel: {
      dispose() {
        calls.push('analysisDispose');
      }
    }
  });

  const loadPromise = panel.loadRuntimeConfig({ force: true });
  panel.dispose();

  resolveSettings({
    runtime: {
      autoRun: true,
      stopOnConsecutiveNg: 3,
      missingMaterialTimeoutSeconds: 1,
      applyProtectionRules: false
    }
  });

  const loadedConfig = await loadPromise;

  assert.equal(loadedConfig, initialRuntimeConfig);
  assert.equal(panel.runtimeConfig, initialRuntimeConfig);
  assert.equal(panel._isDisposed, true);
  assert.equal(panel._runtimeConfigLoadPromise, null);
  assert.equal(panel._pendingAnalysisUpdate, null);
  assert.deepEqual(calls, ['analysisDispose']);
});

test('analysis cards use one delegated toggle listener across rerenders', (t) => {
  const originalGetElementById = globalThis.document.getElementById;
  const container = createAnalysisContainer();

  globalThis.document.getElementById = (id) => (
    id === 'analysis-cards-container' ? container : null
  );

  t.after(() => {
    globalThis.document.getElementById = originalGetElementById;
  });

  const panel = new AnalysisCardsPanel('analysis-cards-container');
  const data = {
    version: 1,
    cards: [
      {
        category: 'diagnostic',
        title: 'Sequence',
        status: 'OK',
        priority: 1,
        fields: [{ label: 'Result', value: 'match', variant: 'status' }]
      }
    ]
  };

  panel._renderAnalysisData(data, 'OK');
  panel._renderAnalysisData(data, 'OK');

  assert.equal(container.listenerCount('click'), 1);

  const card = { classList: createClassList() };
  const icon = { style: { transform: '' } };
  const button = {
    closest(selector) {
      if (selector === '.ac-card-toggle') {
        return button;
      }

      if (selector === '.ac-card') {
        return card;
      }

      return null;
    },
    querySelector(selector) {
      return selector === 'svg' ? icon : null;
    }
  };

  container.dispatch('click', { target: button });
  assert.equal(card.classList.contains('collapsed'), true);
  assert.equal(icon.style.transform, 'rotate(180deg)');

  panel.dispose();
  assert.equal(container.listenerCount('click'), 0);
});

test('analysis cards limit rendered field rows for long-running result payloads', (t) => {
  const originalGetElementById = globalThis.document.getElementById;
  const container = createAnalysisContainer();

  globalThis.document.getElementById = (id) => (
    id === 'analysis-cards-container' ? container : null
  );

  t.after(() => {
    globalThis.document.getElementById = originalGetElementById;
  });

  const panel = new AnalysisCardsPanel('analysis-cards-container');
  panel.maxFieldsPerCard = 3;
  panel.updateCards({
    version: 1,
    cards: [
      {
        category: 'measurement',
        title: 'Measurements',
        status: 'OK',
        fields: Array.from({ length: 6 }, (_, index) => ({
          key: `M${index}`,
          label: `M${index}`,
          value: index
        }))
      },
      {
        category: 'structured',
        title: 'Structured',
        status: 'OK',
        fields: Array.from({ length: 5 }, (_, index) => ({
          key: `S${index}`,
          label: `S${index}`,
          value: `value-${index}`
        }))
      }
    ]
  }, 'OK');

  assert.equal(countMatches(container.innerHTML, /ac-measurement-row/g), 3);
  assert.equal(countMatches(container.innerHTML, /class="cv-result-field /g), 3);
  assert.match(container.innerHTML, /\+3 more fields/);
  assert.match(container.innerHTML, /\+2 more fields/);
  assert.doesNotMatch(container.innerHTML, /M5/);
  assert.doesNotMatch(container.innerHTML, /S4/);
});

test('analysis cards limit rendered card count for long-running result payloads', (t) => {
  const originalGetElementById = globalThis.document.getElementById;
  const container = createAnalysisContainer();

  globalThis.document.getElementById = (id) => (
    id === 'analysis-cards-container' ? container : null
  );

  t.after(() => {
    globalThis.document.getElementById = originalGetElementById;
  });

  const panel = new AnalysisCardsPanel('analysis-cards-container');
  panel.maxCardsPerUpdate = 3;
  panel.maxFieldsPerCard = 2;
  panel.updateCards({
    version: 1,
    cards: Array.from({ length: 7 }, (_, index) => ({
      category: 'structured',
      title: `Card ${index}`,
      status: 'OK',
      priority: index,
      fields: [
        { key: `field-${index}-0`, value: index },
        { key: `field-${index}-1`, value: index },
        { key: `field-${index}-2`, value: index }
      ]
    }))
  }, 'OK');

  assert.equal(countMatches(container.innerHTML, /class="ac-card cv-result-card/g), 3);
  assert.match(container.innerHTML, /\+4 more analysis cards/);
  assert.match(container.innerHTML, /Card 6/);
  assert.match(container.innerHTML, /Card 5/);
  assert.match(container.innerHTML, /Card 4/);
  assert.doesNotMatch(container.innerHTML, /Card 3/);
  assert.doesNotMatch(container.innerHTML, /field-6-2/);
});

test('analysis cards truncate long recognition text and diagnostic messages', (t) => {
  const originalGetElementById = globalThis.document.getElementById;
  const container = createAnalysisContainer();

  globalThis.document.getElementById = (id) => (
    id === 'analysis-cards-container' ? container : null
  );

  t.after(() => {
    globalThis.document.getElementById = originalGetElementById;
  });

  const longRecognitionText = 'R'.repeat(320);
  const longDiagnosticMessage = 'D'.repeat(480);
  const panel = new AnalysisCardsPanel('analysis-cards-container');
  panel.updateCards({
    version: 1,
    cards: [
      {
        category: 'recognition',
        title: 'OCR',
        status: 'OK',
        fields: [{ key: 'Text', value: longRecognitionText }]
      },
      {
        category: 'diagnostic',
        title: 'Diagnostic',
        status: 'NG',
        message: longDiagnosticMessage,
        fields: [{ label: 'Message', value: longDiagnosticMessage }]
      }
    ]
  }, 'OK');

  assert.match(container.innerHTML, /\.\.\./);
  assert.equal(container.innerHTML.includes(longRecognitionText), false);
  assert.equal(container.innerHTML.includes(longDiagnosticMessage), false);
  assert.match(container.innerHTML, new RegExp(`${'R'.repeat(240)}\\.\\.\\.`));
  assert.match(container.innerHTML, new RegExp(`${'D'.repeat(360)}\\.\\.\\.`));
});

test('diagnostic sequence fields render as compact single-line text', () => {
  const html = renderDiagnosticsCardsHtml({
    expectedLabels: ['wire_brown', 'wire_black', 'wire_blue'],
    actualOrder: ['wire_brown', 'wire_black', 'wire_blue'],
    isMatch: true,
    message: 'Sequence matched: wire_brown -> wire_black -> wire_blue.'
  }, 'OK');

  assert.match(html, /ac-diagnostic-row-sequence/);
  assert.match(html, /ac-diagnostic-sequence-text/);
  assert.match(html, /wire_brown-&gt;wire_black-&gt;wire_blue/);
  assert.doesNotMatch(html, /ac-diagnostic-sequence"><span class="ac-diagnostic-chip"/);
});

test('diagnostic sequence and label fields are capped in inline analysis cards', () => {
  const labels = Array.from({ length: 30 }, (_, index) => `wire_${index}`);
  const html = renderDiagnosticsCardsHtml({
    expectedLabels: labels,
    actualOrder: labels,
    missingLabels: labels,
    duplicateLabels: labels,
    isMatch: false
  }, 'NG');

  assert.match(html, /\+6 more/);
  assert.match(html, /ac-diagnostic-chip-muted">\+6</);
  assert.doesNotMatch(html, /wire_29-&gt;/);
});
