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

test('station monitor preserves explicit zero execution failures over legacy error count', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  const statistics = view.normalizeResultStatistics({
    totalAttemptCount: 2,
    executionSucceededCount: 2,
    invalidCount: 2,
    failedCount: 0,
    timedOutCount: 0,
    executionFailureCount: 0,
    errorCount: 2
  });

  assert.equal(statistics.invalid, 2);
  assert.equal(statistics.executionFailures, 0);
});

test('station monitor command replay deduplicates updates and never regresses a terminal command state', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  view.selectedStationId = 'station-command';
  view.selectedStationDetail = {
    stationId: 'station-command',
    recentCommands: []
  };
  view.stations = new Map([['station-command', { stationId: 'station-command' }]]);
  view.canReadSensitiveMonitoring = () => true;
  view.markDirty = () => {};

  view.applyCommandEvent({
    commandId: 'cmd-1',
    stationId: 'station-command',
    status: 'Succeeded',
    progressPercent: 100,
    completedAtUtc: '2026-08-31T00:02:00Z'
  });
  view.applyCommandEvent({
    commandId: 'cmd-1',
    stationId: 'station-command',
    status: 'Running',
    progressPercent: 80,
    startedAtUtc: '2026-08-31T00:01:00Z'
  });
  view.applyCommandEvent({
    commandId: 'cmd-1',
    stationId: 'station-command',
    status: 'Succeeded',
    progressPercent: 100,
    completedAtUtc: '2026-08-31T00:02:00Z'
  });

  assert.equal(view.selectedStationDetail.recentCommands.length, 1);
  assert.equal(view.selectedStationDetail.recentCommands[0].status, 'Succeeded');
  assert.equal(view.selectedStationDetail.recentCommands[0].progressPercent, 100);
});

test('shared CSV sanitizer neutralizes whitespace-prefixed formulas while preserving normal Unicode', async () => {
  const { formatCsvField, sanitizeCsvText } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/csvSanitizer.js'
  );

  for (const value of ['=SUM(1,2)', ' +SUM(1,2)', '\t-1+1', '\r\n@HYPERLINK("https://example.test")']) {
    assert.match(sanitizeCsvText(value), /^'/);
    assert.match(formatCsvField(value), /^(?:'|"')/);
  }

  assert.equal(formatCsvField('正常中文 · 检测通过 ✅'), '正常中文 · 检测通过 ✅');
});

test('station monitor CSV export uses the shared sanitizer for Station-controlled fields', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  const csv = view.convertResultsToCsv([{
    stationId: 'station-a',
    stationLabel: '工站 A',
    sequenceId: 1,
    status: 'NG',
    diagnosticCode: ' \t=CMD()',
    diagnosticMessage: '\r\n@HYPERLINK("https://example.test")',
    executionTimeMs: 12,
    completedAtUtc: '2026-08-31T00:00:00Z',
    packageName: '正常包名'
  }]);

  assert.match(csv, /' \t=CMD\(\)/);
  assert.match(csv, /"'\r\n@HYPERLINK\(""https:\/\/example\.test""\)"/);
  assert.doesNotMatch(csv, /\.xls/i);
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

test('station sensitive monitoring is capability-driven and never inferred from role', async () => {
  const { Capabilities } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/auth/auth.js'
  );
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );
  const previousWindow = global.window;

  try {
    global.window = {
      currentUser: {
        role: 'Admin',
        capabilities: []
      }
    };
    const view = Object.create(StationMonitorView.prototype);
    assert.equal(view.canReadSensitiveMonitoring(), false);

    global.window.currentUser.capabilities = [Capabilities.STATION_SENSITIVE_READ];
    assert.equal(view.canReadSensitiveMonitoring(), true);
  } finally {
    global.window = previousWindow;
  }
});

test('station safe monitor hides command, log, package and production-output content', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );
  const previousWindow = global.window;

  try {
    global.window = {
      currentUser: {
        role: 'Operator',
        capabilities: []
      }
    };
    const view = Object.create(StationMonitorView.prototype);
    view.focus = { innerHTML: '' };
    view.focusMeta = { textContent: '' };
    view.selectedStationId = 'station-safe';
    view.stations = new Map([['station-safe', {
      stationId: 'station-safe',
      stationName: 'Safe station',
      lineName: 'Line 1',
      state: 'Running',
      runtimeState: 'Running',
      onlineState: 'Online',
      isOnline: true,
      lastSeenAtUtc: '2026-08-30T00:00:00Z'
    }]]);
    view.selectedStationDetail = {
      ...view.stations.get('station-safe'),
      packageId: 'PACKAGE-SECRET',
      lastDiagnosticMessage: 'DIAGNOSTIC-SECRET',
      sessionOutcomeStatistics: {
        ok: 2,
        ng: 1,
        executionFailures: 0,
        invalid: 0,
        undetermined: 0
      },
      averageExecutionTimeMs: 12,
      recentResults: [{
        stationId: 'station-safe',
        sequenceId: 8,
        outcome: 'Ng',
        diagnosticCode: 'WIRE_SWAP',
        runId: 'RUN-SECRET',
        imageId: 'IMAGE-SECRET',
        completedAtUtc: '2026-08-30T00:00:00Z',
        executionTimeMs: 12
      }],
      recentHealth: [{
        sequenceId: 2,
        runtimeState: 'Running',
        healthState: 'Online',
        currentPackageHealth: 'PACKAGE-HEALTH-SECRET',
        diskFreeMb: 1,
        diskTotalMb: 2,
        createdAtUtc: '2026-08-30T00:00:00Z'
      }],
      recentLogs: [{ renderedMessage: 'LOG-SECRET' }],
      recentCommands: [{ commandId: 'COMMAND-SECRET' }]
    };
    view.commandBusy = false;
    view.commandStatusMessage = '';
    view.commandStatusLevel = 'idle';
    view.computeIsOnline = () => true;
    view.getProductionPackages = () => [];
    view.canPerformStationAction = () => false;
    view.escapeHtml = (value) => String(value ?? '');
    view.formatState = () => '运行中';
    view.formatOutcome = () => 'NG';
    view.formatRelativeTime = () => '刚刚';
    view.formatMilliseconds = (value) => `${value}ms`;
    view.formatBytes = (value) => `${value}B`;
    view.formatDisk = () => 'disk-secret';
    view.renderStationDiagnosticAdvice = () => 'DIAGNOSTIC-ADVICE-SECRET';

    view.renderFocus();

    assert.match(view.focus.innerHTML, /Line 1/);
    assert.match(view.focus.innerHTML, /健康采样/);
    assert.match(view.focus.innerHTML, /近期结果/);
    assert.doesNotMatch(view.focus.innerHTML, /指令队列|日志|COMMAND-SECRET|LOG-SECRET/);
    assert.doesNotMatch(view.focus.innerHTML, /PACKAGE-SECRET|PACKAGE-HEALTH-SECRET|RUN-SECRET|IMAGE-SECRET/);
    assert.doesNotMatch(view.focus.innerHTML, /data-station-action|DIAGNOSTIC-ADVICE-SECRET|disk-secret/);

    view.globalLogs = [{ stationId: 'station-safe', log: { renderedMessage: 'SSE-LOG-SECRET' } }];
    view.selectedStationDetail = { recentLogs: [], recentCommands: [] };
    view.markDirty = () => {};
    view.applyLogEvent({ log: { renderedMessage: 'LIVE-LOG-SECRET' } });
    view.applyCommandEvent({ commandId: 'LIVE-COMMAND-SECRET', stationId: 'station-safe' });
    assert.equal(view.globalLogs.length, 1);
    assert.deepEqual(view.selectedStationDetail, { recentLogs: [], recentCommands: [] });

    view.toCssToken = () => 'ng';
    const resultHtml = view.renderMonitorResultCard({
      status: 'NG',
      outcomeCategory: 'ng',
      stationLabel: 'Safe station',
      sequenceId: 8,
      diagnosticCode: 'WIRE_SWAP',
      decisionSource: 'DECISION-SOURCE-SECRET',
      reasonCode: 'Reason',
      executionTimeMs: 12,
      packageName: 'PACKAGE-SECRET',
      diagnosticMessage: 'DIAGNOSTIC-SECRET',
      primaryOutputsPreview: { serialNumber: 'SN-SECRET' },
      completedAtUtc: '2026-08-30T00:00:00Z'
    });
    assert.match(resultHtml, /WIRE_SWAP/);
    assert.doesNotMatch(resultHtml, /PACKAGE-SECRET|DIAGNOSTIC-SECRET|DECISION-SOURCE-SECRET|SN-SECRET|主输出预览/);
  } finally {
    global.window = previousWindow;
  }
});
