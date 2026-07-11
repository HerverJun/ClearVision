import test from 'node:test';
import assert from 'node:assert/strict';

test('station monitor refresh tick marks time-derived UI dirty without forcing result workbench redraw', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );
  const previousWindow = global.window;
  const previousDocument = global.document;
  let intervalCallback = null;
  let requested = 0;

  global.window = {
    setInterval(callback) {
      intervalCallback = callback;
      return 42;
    },
    clearInterval() {}
  };
  global.document = { hidden: false };

  try {
    const view = Object.create(StationMonitorView.prototype);
    view.isActive = true;
    view.refreshTimer = null;
    view._renderDirty = false;
    view._resultsDirty = false;
    view._renderQueued = false;
    view.requestRender = function requestRender() {
      requested += 1;
    };

    view.startRefreshTimer();
    assert.equal(typeof intervalCallback, 'function');

    intervalCallback();

    assert.equal(view._renderDirty, true);
    assert.equal(view._resultsDirty, false);
    assert.equal(requested, 1);
  } finally {
    global.window = previousWindow;
    global.document = previousDocument;
  }
});

test('station monitor caches online state while sorting stations within one render', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  view._renderContextActive = true;
  view._stationEntriesCache = null;
  view.stations = new Map([
    ['b', { stationId: 'b', state: 'Idle', online: true }],
    ['a', { stationId: 'a', state: 'Idle', online: true }],
    ['c', { stationId: 'c', state: 'Idle', online: false }]
  ]);

  let calls = 0;
  view.computeIsOnline = (station) => {
    calls += 1;
    return station.online;
  };

  const firstEntries = view.getStationRenderEntries();
  const secondEntries = view.getStationRenderEntries();

  assert.equal(calls, 3);
  assert.deepEqual(firstEntries.map((entry) => entry.station.stationId), ['a', 'b', 'c']);
  assert.deepEqual(secondEntries.map((entry) => entry.station.stationId), ['a', 'b', 'c']);
});

test('station monitor normalizes server statistics for the result workbench', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  const stats = view.normalizeResultStatistics({
    totalCount: 3,
    okCount: 2,
    ngCount: 1,
    errorCount: 0,
    averageExecutionTimeMs: 25,
    byDiagnosticCode: [{ diagnosticCode: 'WIRE_SWAP', count: 1 }],
    hourlyTrend: [{ hourUtc: '2026-03-20T10:00:00Z', totalCount: 3 }]
  });

  assert.equal(stats.total, 3);
  assert.equal(stats.ok, 2);
  assert.equal(stats.ng, 1);
  assert.equal(stats.byDiagnosticCode[0].key, 'WIRE_SWAP');
  assert.equal(stats.hourlyTrend[0].count, 3);
});

test('station monitor skips KPI DOM rebuild when summary signature is unchanged', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  let writes = 0;
  let alertCount = 0;
  view.summaryGrid = {
    _html: '',
    set innerHTML(value) {
      writes += 1;
      this._html = value;
    },
    get innerHTML() {
      return this._html;
    }
  };
  view.summary = null;
  view.offlineThresholdSeconds = 15;
  view._summaryRenderSignature = '';
  view.escapeHtml = (value) => String(value ?? '');
  view.getStationRenderSnapshot = () => ({
    totalOk: 2,
    totalNg: 1,
    totalError: 0,
    averageExecutionTimeMs: 35,
    onlineCount: 1,
    alertCount,
    stations: [{ stationId: 'station-a' }]
  });

  view.renderSummary();
  view.renderSummary();

  assert.equal(writes, 1);

  alertCount = 1;
  view.renderSummary();

  assert.equal(writes, 2);
});

test('result panel accepts station statistics shape for trace dashboard analytics', async () => {
  const { ResultPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultPanel.js'
  );

  const panel = Object.create(ResultPanel.prototype);
  const normalizedStats = panel.normalizeStatistics({
    total: 4,
    ok: 3,
    ng: 1,
    error: 0,
    averageExecutionTimeMs: 31
  });
  const defects = panel.normalizeDefectDistribution([
    { diagnosticCode: 'WIRE_SWAP', count: 2 }
  ]);
  const trend = panel.normalizeTrendPoints([
    { hourUtc: '2026-03-20T10:00:00Z', total: 4, ngCount: 1 }
  ]);

  assert.equal(normalizedStats.total, 4);
  assert.equal(normalizedStats.ok, 3);
  assert.equal(normalizedStats.ng, 1);
  assert.equal(normalizedStats.validDecisions, 4);
  assert.equal(normalizedStats.yieldRate, 0.75);
  assert.equal(normalizedStats.executionFailures, 0);
  assert.equal(normalizedStats.avgTime, 31);
  assert.equal(defects.WIRE_SWAP, 2);
  assert.equal(trend[0].count, 4);
  assert.equal(trend[0].status, 'ng');
});

test('result panel can request station history without a project context', async () => {
  const { ResultPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultPanel.js'
  );

  const panel = Object.create(ResultPanel.prototype);
  let requestedParams = null;
  panel.historyLoader = async (params) => {
    requestedParams = params;
    return true;
  };
  panel.dataSource = 'station';
  panel.projectId = null;
  panel.pageSize = 20;
  panel.timeRange = 'all';
  panel.filters = {
    status: 'all',
    defectType: 'all',
    startTime: null,
    endTime: null
  };

  const result = await panel.requestHistoryPage(2);

  assert.equal(result, true);
  assert.equal(requestedParams.pageIndex, 2);
  assert.equal(requestedParams.pageSize, 20);
  assert.equal(requestedParams.dataSource, 'station');
});

test('result panel clears stale analytics when server distribution is empty', async () => {
  const { ResultPanel } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/results/resultPanel.js'
  );

  const panel = Object.create(ResultPanel.prototype);
  let filterUpdated = false;
  panel.defectTypes = { OLD_DEFECT: 2 };
  panel.trendData = [{ time: new Date(), status: 'NG', defectCount: 1 }];
  panel.updateDefectTypeFilter = () => {
    filterUpdated = true;
  };

  panel.applyServerAnalysis({
    statistics: {
      total: 0,
      ok: 0,
      ng: 0,
      error: 0,
      averageExecutionTimeMs: 0,
      byDiagnosticCode: [],
      hourlyTrend: []
    }
  });

  assert.deepEqual(panel.defectTypes, {});
  assert.deepEqual(panel.trendData, []);
  assert.equal(filterUpdated, true);
});
