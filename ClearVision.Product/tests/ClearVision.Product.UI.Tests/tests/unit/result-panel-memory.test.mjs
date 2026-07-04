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

globalThis.window = {
  location: {
    protocol: 'http:',
    hostname: 'localhost',
    port: '5000',
    href: 'http://localhost:5000/'
  },
  sessionStorage: createMemoryStorage(),
  localStorage: createMemoryStorage(),
  setTimeout,
  clearTimeout
};

globalThis.document = {
  getElementById() {
    return null;
  },
  querySelectorAll() {
    return [];
  },
  createElement() {
    return {
      textContent: '',
      innerHTML: '',
      appendChild() {},
      removeChild() {},
      addEventListener() {},
      querySelector() {
        return null;
      }
    };
  },
  body: {
    appendChild() {},
    removeChild() {}
  }
};

const { ResultPanel } = await import(
  '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultPanel.js'
);

function createClassList() {
  const classes = new Set();

  return {
    add(value) {
      classes.add(value);
    },
    remove(value) {
      classes.delete(value);
    },
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

function createMockElement({ dataset = {}, value = '' } = {}) {
  const listeners = new Map();
  const element = {
    dataset,
    value,
    classList: createClassList(),
    children: [],
    firstElementChild: null,
    textContent: '',
    innerHTML: '',
    appendChild(child) {
      this.children.push(child);
      this.firstElementChild = this.children[0] || null;
    },
    removeChild(child) {
      this.children = this.children.filter(item => item !== child);
      this.firstElementChild = this.children[0] || null;
    },
    insertBefore(child) {
      this.children.unshift(child);
      this.firstElementChild = this.children[0] || null;
    },
    querySelector() {
      return null;
    },
    querySelectorAll() {
      return [];
    },
    contains(target) {
      return target === element || this.children.includes(target);
    },
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
      const payload = { target: element, ...event };
      Array.from(listeners.get(type) || []).forEach(handler => handler(payload));
    },
    listenerCount(type) {
      return listeners.get(type)?.size || 0;
    }
  };

  return element;
}

function countOccurrences(text, needle) {
  return String(text).split(needle).length - 1;
}

function createResultPanelDomFixture() {
  const nodes = new Map();
  const documentTarget = createMockElement();
  const timeRangeButton = createMockElement({ dataset: { range: 'week' } });
  const exportDropdown = createMockElement();
  const exportItem = createMockElement({ dataset: { format: 'csv' } });

  exportDropdown.querySelectorAll = () => [exportItem];
  exportDropdown.contains = target => target === exportDropdown || target === exportItem;

  nodes.set('results-list-container', createMockElement());
  nodes.set('results-filters-bar', createMockElement());
  nodes.set('filter-data-source', createMockElement({ value: 'inspection' }));
  nodes.set('filter-status', createMockElement({ value: 'all' }));
  nodes.set('filter-defect-type', createMockElement({ value: 'all' }));
  nodes.set('export-dropdown', exportDropdown);
  nodes.set('btn-export-results', createMockElement());
  nodes.set('btn-advanced-report', createMockElement());

  const documentFixture = {
    ...documentTarget,
    getElementById(id) {
      return nodes.get(id) || null;
    },
    querySelectorAll(selector) {
      return selector === '.time-range-btn' ? [timeRangeButton] : [];
    },
    createElement() {
      return createMockElement();
    },
    body: createMockElement()
  };

  return {
    document: documentFixture,
    exportBtn: nodes.get('btn-export-results'),
    exportDropdown
  };
}

function createDetailModalDomFixture() {
  const body = createMockElement();
  const modal = createMockElement();
  const modalBody = createMockElement();
  const closeButton = createMockElement();
  const overlay = createMockElement();
  const created = [];
  let modalReturned = false;

  modal.remove = () => {
    body.removeChild(modal);
    modal.removed = true;
  };
  modal.querySelector = selector => {
    if (selector === '.result-detail-close') {
      return closeButton;
    }

    if (selector === '.result-detail-overlay') {
      return overlay;
    }

    if (selector === '.result-detail-body') {
      return modalBody;
    }

    return null;
  };

  const createEscapingElement = () => {
    let text = '';
    const element = {
      innerHTML: '',
      set textContent(value) {
        text = String(value ?? '');
        this.innerHTML = text
          .replace(/&/g, '&amp;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;')
          .replace(/"/g, '&quot;');
      },
      get textContent() {
        return text;
      }
    };

    return element;
  };

  return {
    closeButton,
    document: {
      getElementById() {
        return null;
      },
      querySelectorAll() {
        return [];
      },
      createElement() {
        if (!modalReturned) {
          modalReturned = true;
          created.push(modal);
          return modal;
        }

        const element = createEscapingElement();
        created.push(element);
        return element;
      },
      body
    },
    modal,
    modalBody,
    overlay
  };
}

function createPanel() {
  const panel = Object.create(ResultPanel.prototype);
  Object.assign(panel, {
    results: [],
    filteredResults: [],
    serverPaged: false,
    projectId: null,
    dataSource: 'inspection',
    filters: {
      status: 'all',
      defectType: 'all',
      startTime: null,
      endTime: null
    },
    statistics: {
      total: 0,
      ok: 0,
      ng: 0,
      error: 0,
      avgTime: 0
    },
    trendData: [],
    defectTypes: {},
    currentPage: 1,
    pageSize: 12,
    totalPages: 1,
    totalResultCount: 0,
    serverAnalysisSource: 'local',
    _renderFrameHandle: null,
    _renderFrameCancel: null,
    resultsSseMaxFrameChars: 2 * 1024 * 1024,
    resultsSseMaxBufferChars: 4 * 1024 * 1024,
    applyFilters() {
      this.filteredResults = [...this.results];
    },
    updateDefectTypeFilter() {},
    render() {}
  });

  return panel;
}

function createResult(index) {
  const inlineImage = `inline-image-${index}`;
  return {
    id: `result-${index}`,
    status: index % 2 === 0 ? 'OK' : 'NG',
    processingTime: 10 + index,
    timestamp: `2026-06-01T00:00:${String(index % 60).padStart(2, '0')}Z`,
    imageId: `image-${index}`,
    imageData: inlineImage,
    outputImageBase64: inlineImage,
    resultImageBase64: inlineImage,
    outputData: { Score: index },
    analysisData: { cards: [] },
    defects: []
  };
}

test('live result history is bounded and old inline images are discarded', () => {
  const panel = createPanel();

  for (let index = 0; index < 520; index += 1) {
    panel.addResult(createResult(index));
  }

  assert.equal(panel.results.length, 500);
  assert.equal(panel.filteredResults.length, 500);
  assert.equal(panel.results[0].id, 'result-519');
  assert.equal(panel.results.at(-1).id, 'result-20');

  assert.equal(panel.results[0].imageData, 'inline-image-519');
  assert.equal(panel.results[0].outputImageBase64, null);
  assert.equal(panel.results[0].resultImageBase64, null);
  assert.equal(panel.results[11].imageData, 'inline-image-508');
  assert.equal(panel.results[11].outputImageBase64, null);

  assert.equal(panel.results[12].imageData, null);
  assert.equal(panel.results[12].outputImageBase64, null);
  assert.equal(panel.results[12].resultImageBase64, null);
  assert.equal(panel.results[12].inlineImageDiscarded, true);
  assert.equal(panel.results[12].imageId, 'image-507');
});

test('local result analytics are recalculated from retained history only', () => {
  const panel = createPanel();

  for (let index = 0; index < 520; index += 1) {
    panel.addResult({
      ...createResult(index),
      defects: [{ type: `DEFECT_${index}` }]
    });
  }

  assert.equal(panel.statistics.total, 500);
  assert.equal(panel.statistics.ok, 250);
  assert.equal(panel.statistics.ng, 250);
  assert.equal(Object.keys(panel.defectTypes).length, 500);
  assert.equal(panel.defectTypes.DEFECT_0, undefined);
  assert.equal(panel.defectTypes.DEFECT_19, undefined);
  assert.equal(panel.defectTypes.DEFECT_20, 1);
  assert.equal(panel.defectTypes.DEFECT_519, 1);
  assert.equal(panel.trendData.length, 100);
  assert.equal(panel.trendData.every(point => point.defectCount === 1), true);
});

test('local live result renders are coalesced per frame', async () => {
  const panel = createPanel();
  let renderCount = 0;
  panel.render = () => {
    renderCount += 1;
  };

  for (let index = 0; index < 20; index += 1) {
    panel.addResult(createResult(index));
  }

  assert.equal(renderCount, 0);
  assert.notEqual(panel._renderFrameHandle, null);

  await new Promise(resolve => setTimeout(resolve, 5));

  assert.equal(renderCount, 1);
  assert.equal(panel._renderFrameHandle, null);
});

test('local result history keeps a single inline image field per retained result', () => {
  const panel = createPanel();

  panel.addResult({
    id: 'alias-only-result',
    status: 'OK',
    processingTime: 12,
    outputImageBase64: 'alias-image',
    OutputImageBase64: 'alias-image-upper',
    outputData: {},
    defects: []
  });

  assert.equal(panel.results.length, 1);
  assert.equal(panel.results[0].imageData, 'alias-image');
  assert.equal(panel.results[0].outputImageBase64, null);
  assert.equal(panel.results[0].OutputImageBase64, null);
});

test('local result history stores compact output and analysis payloads', () => {
  const panel = createPanel();
  const longText = 'A'.repeat(900);
  const imagePayload = 'B'.repeat(512);
  const original = {
    id: 'large-local-result',
    status: 'OK',
    processingTime: 12,
    imageId: 'image-large',
    outputData: {
      Text: longText,
      OutputImageBase64: imagePayload,
      Items: Array.from({ length: 40 }, (_, index) => ({
        label: `item-${index}`,
        nested: { value: longText }
      }))
    },
    analysisData: {
      cards: Array.from({ length: 30 }, (_, index) => ({
        title: `card-${index}`,
        fields: [{ key: 'Text', value: longText }]
      }))
    },
    defects: Array.from({ length: 30 }, (_, index) => ({
      type: `defect-${index}`,
      confidenceScore: 0.9
    }))
  };

  panel.addResult(original);

  const stored = panel.results[0];
  assert.equal(stored.outputData.Text.length, 515);
  assert.match(stored.outputData.Text, /\.\.\.$/);
  assert.equal(stored.outputData.OutputImageBase64, undefined);
  assert.equal(stored.outputData.__omittedImageFieldCount, 1);
  assert.equal(stored.outputData.Items.length, 25);
  assert.equal(stored.outputData.Items.at(-1), '+16 more');
  assert.equal(stored.analysisData.cards.length, 25);
  assert.equal(stored.analysisData.cards.at(-1), '+6 more');
  assert.equal(stored.defects.length, 25);
  assert.equal(stored.defects.at(-1), '+6 more');
  assert.equal(JSON.stringify(stored).includes(longText), false);
  assert.equal(JSON.stringify(stored).includes(imagePayload), false);
  assert.equal(original.outputData.Text, longText);
  assert.equal(original.outputData.OutputImageBase64, imagePayload);
});

test('server-paged result loads retain images without duplicate inline aliases', () => {
  const panel = createPanel();
  const results = Array.from({ length: 20 }, (_, index) => createResult(index));

  panel.loadResults(results, {
    serverPaged: true,
    totalCount: 20,
    pageIndex: 0,
    pageSize: 20
  });

  assert.equal(panel.results.length, 20);
  assert.equal(panel.results[19].imageData, 'inline-image-19');
  assert.equal(panel.results[19].outputImageBase64, null);
  assert.equal(panel.results[19].inlineImageDiscarded, undefined);
});

test('oversized results SSE frames are dropped before parsing', () => {
  const panel = createPanel();
  panel.resultsSseMaxFrameChars = 16;

  const accepted = panel.dispatchBoundedResultsSseFrame(
    `event: resultProduced\ndata: ${JSON.stringify({ resultId: 'oversized', status: 'OK' })}`
  );

  assert.equal(accepted, false);
  assert.equal(panel.results.length, 0);
});

test('openResultsStream fails and releases the reader when results SSE buffer has no frame boundary', async (t) => {
  const previousFetch = globalThis.fetch;
  const panel = createPanel();
  const encoder = new TextEncoder();
  let releaseCount = 0;
  let readCount = 0;

  panel.resultsSseMaxBufferChars = 16;
  globalThis.fetch = async () => ({
    ok: true,
    body: {
      getReader() {
        return {
          async read() {
            readCount += 1;
            return {
              done: false,
              value: encoder.encode(readCount === 1 ? 'partial-result-frame' : 'more-data')
            };
          },
          releaseLock() {
            releaseCount += 1;
          }
        };
      }
    }
  });

  t.after(() => {
    globalThis.fetch = previousFetch;
  });

  await assert.rejects(
    () => panel.openResultsStream('/events', null, new AbortController().signal),
    /Results SSE buffer exceeded 16 characters/
  );
  assert.equal(releaseCount, 1);
});

test('dispose releases result panel listeners, streams, and queued refreshes', async () => {
  const previousDocument = globalThis.document;
  const fixture = createResultPanelDomFixture();
  globalThis.document = fixture.document;

  try {
    const panel = new ResultPanel('results-list-container');
    let historyCalls = 0;
    let analyticsCalls = 0;
    let abortCalls = 0;

    panel.projectId = 'project-1';
    panel.historyLoader = () => {
      historyCalls += 1;
      return Promise.resolve(true);
    };
    panel.loadServerAnalytics = () => {
      analyticsCalls += 1;
      return Promise.resolve(true);
    };
    panel._resultsStreamController = {
      abort() {
        abortCalls += 1;
      }
    };
    panel._resultsStreamReconnectTimer = setTimeout(() => {
      historyCalls += 100;
    }, 20);

    panel.queueServerHistoryRefresh(5);
    panel.queueServerAnalyticsRefresh(5);

    assert.equal(fixture.document.listenerCount('click'), 1);
    fixture.exportBtn.dispatch('click');
    assert.equal(fixture.exportDropdown.classList.contains('open'), true);

    fixture.document.dispatch('click', { target: {} });
    assert.equal(fixture.exportDropdown.classList.contains('open'), false);

    panel.dispose();

    assert.equal(abortCalls, 1);
    assert.equal(panel._historyRefreshTimer, null);
    assert.equal(panel._analyticsRefreshTimer, null);
    assert.equal(panel._resultsStreamReconnectTimer, null);
    assert.equal(fixture.document.listenerCount('click'), 0);

    fixture.exportBtn.dispatch('click');
    assert.equal(fixture.exportDropdown.classList.contains('open'), false);

    await new Promise(resolve => setTimeout(resolve, 30));
    assert.equal(historyCalls, 0);
    assert.equal(analyticsCalls, 0);
  } finally {
    globalThis.document = previousDocument;
  }
});

test('result detail rendering is bounded for high-volume payloads', () => {
  const panel = createPanel();
  Object.assign(panel, {
    resultDetailMaxAnalysisCards: 2,
    resultDetailMaxStructuredCards: 1,
    resultDetailMaxFieldsPerCard: 3,
    resultDetailMaxRawOutputRows: 4,
    resultDetailMaxDefectRows: 3,
    resultDetailMaxFieldValueChars: 12
  });
  panel.escapeHtml = value => String(value ?? '');

  const analysisHtml = panel.renderAnalysisDataSection({
    cards: Array.from({ length: 4 }, (_, cardIndex) => ({
      title: `card-${cardIndex}`,
      fields: Array.from({ length: 5 }, (_, fieldIndex) => ({
        key: `field-${cardIndex}-${fieldIndex}`,
        value: fieldIndex === 0 ? 'abcdefghijklmnop' : fieldIndex
      }))
    }))
  });

  assert.equal(countOccurrences(analysisHtml, 'class="detail-section-title"'), 2);
  assert.equal(countOccurrences(analysisHtml, 'Hidden 2 more fields'), 2);
  assert.match(analysisHtml, /Hidden 2 more analysis cards/);
  assert.match(analysisHtml, /abcdefghijkl\.\.\./);
  assert.doesNotMatch(analysisHtml, /card-2/);
  assert.doesNotMatch(analysisHtml, /field-0-3/);

  const structuredHtml = panel.renderStructuredOutputSection(
    Object.fromEntries(Array.from({ length: 8 }, (_, index) => [`metric${index}`, index])),
    'OK'
  );
  assert.equal(countOccurrences(structuredHtml, 'class="cv-result-field'), 3);
  assert.match(structuredHtml, /\+5 more fields/);

  const rawHtml = panel.renderOutputDataTable(
    Object.fromEntries(Array.from({ length: 7 }, (_, index) => [`raw${index}`, `value-${index}`]))
  );
  assert.equal(countOccurrences(rawHtml, 'type-string'), 4);
  assert.match(rawHtml, /Hidden 3 output fields/);

  const defectsHtml = panel.renderDefectsSection(
    Array.from({ length: 5 }, (_, index) => ({ type: `defect-${index}`, confidenceScore: 0.91 }))
  );
  assert.equal(countOccurrences(defectsHtml, '<span class="detail-label">defect-'), 3);
  assert.match(defectsHtml, /Hidden 2 more defects/);
  assert.doesNotMatch(defectsHtml, /defect-3/);
});

test('dispose removes active result detail modal listeners and DOM', () => {
  const previousDocument = globalThis.document;
  const previousRequestAnimationFrame = globalThis.window.requestAnimationFrame;
  const fixture = createDetailModalDomFixture();
  globalThis.document = fixture.document;
  globalThis.window.requestAnimationFrame = callback => {
    callback();
    return 1;
  };

  try {
    const panel = createPanel();
    Object.assign(panel, {
      _eventDisposers: [],
      _isDisposed: false,
      _activeDetailModals: new Set(),
      _resultsStreamController: null,
      _resultsStreamReconnectTimer: null,
      _analyticsRefreshTimer: null,
      _historyRefreshTimer: null
    });

    panel.showResultDetail({
      id: 'result-detail',
      status: 'OK',
      timestamp: '2026-06-01T00:00:00Z',
      processingTime: 12,
      outputData: {},
      analysisData: { cards: [] },
      defects: []
    });

    assert.equal(fixture.document.body.children.length, 1);
    assert.equal(fixture.closeButton.listenerCount('click'), 1);
    assert.equal(fixture.overlay.listenerCount('click'), 1);
    assert.equal(panel._activeDetailModals.size, 1);

    panel.dispose();

    assert.equal(fixture.document.body.children.length, 0);
    assert.equal(fixture.modal.removed, true);
    assert.equal(fixture.closeButton.listenerCount('click'), 0);
    assert.equal(fixture.overlay.listenerCount('click'), 0);
    assert.equal(panel._activeDetailModals.size, 0);
  } finally {
    globalThis.document = previousDocument;
    if (previousRequestAnimationFrame) {
      globalThis.window.requestAnimationFrame = previousRequestAnimationFrame;
    } else {
      delete globalThis.window.requestAnimationFrame;
    }
  }
});

test('server history detail loads on demand and renders traceability warnings', async () => {
  const previousDocument = globalThis.document;
  const previousRequestAnimationFrame = globalThis.window.requestAnimationFrame;
  const fixture = createDetailModalDomFixture();
  globalThis.document = fixture.document;
  globalThis.window.requestAnimationFrame = callback => {
    callback();
    return 1;
  };

  try {
    const panel = createPanel();
    Object.assign(panel, {
      _eventDisposers: [],
      _isDisposed: false,
      _activeDetailModals: new Set(),
      serverPaged: true,
      projectId: 'project-1',
      historyDetailLoader: async () => ({
        id: 'history-1',
        projectId: 'project-1',
        status: 'NG',
        timestamp: '2026-07-04T08:00:00Z',
        processingTime: 45,
        hasImage: true,
        imageMissing: true,
        imageMissingMessage: '图像文件不存在或已清理',
        outputDataPreview: {
          value: { score: 42 },
          wasTruncated: true,
          wasRedacted: true,
          message: 'JSON preview truncated.'
        },
        outputData: { score: 42 },
        analysisData: { cards: [] },
        defects: []
      })
    });

    panel.showResultDetail({
      id: 'history-1',
      projectId: 'project-1',
      status: 'NG',
      timestamp: '2026-07-04T08:00:00Z',
      processingTime: 45,
      hasOutputData: true,
      hasAnalysisData: false,
      defects: []
    });

    assert.match(fixture.modal.innerHTML, /正式检测历史/);
    assert.match(fixture.modal.innerHTML, /正在加载检测详情/);

    await new Promise(resolve => setTimeout(resolve, 0));

    assert.match(fixture.modalBody.innerHTML, /追溯信息/);
    assert.match(fixture.modalBody.innerHTML, /旧数据未记录/);
    assert.match(fixture.modalBody.innerHTML, /图像缺失/);
    assert.match(fixture.modalBody.innerHTML, /图像文件不存在或已清理/);
    assert.match(fixture.modalBody.innerHTML, /大 JSON 已截断/);
    assert.match(fixture.modalBody.innerHTML, /敏感字段已脱敏/);
    assert.match(fixture.modalBody.innerHTML, /输出数据/);
  } finally {
    globalThis.document = previousDocument;
    if (previousRequestAnimationFrame) {
      globalThis.window.requestAnimationFrame = previousRequestAnimationFrame;
    } else {
      delete globalThis.window.requestAnimationFrame;
    }
  }
});
