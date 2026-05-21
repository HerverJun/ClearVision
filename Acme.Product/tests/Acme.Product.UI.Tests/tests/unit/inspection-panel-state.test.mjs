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
  '../../../../src/Acme.Product.Desktop/wwwroot/src/features/inspection/inspectionController.js'
);
const { setCurrentProject } = await import(
  '../../../../src/Acme.Product.Desktop/wwwroot/src/features/project/projectManager.js'
);
const { InspectionPanel } = await import(
  '../../../../src/Acme.Product.Desktop/wwwroot/src/features/inspection/inspectionPanel.js'
);
const { renderDiagnosticsCardsHtml } = await import(
  '../../../../src/Acme.Product.Desktop/wwwroot/src/features/inspection/analysisCardsPanel.js'
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
