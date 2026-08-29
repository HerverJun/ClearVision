import { test, expect, Page } from '@playwright/test';
import { bootAuthenticatedApp } from './authHelper';

const now = '2026-05-17T02:00:00Z';

const stations = [
  {
    stationId: 'station-a',
    stationName: 'Station A',
    lineName: 'Line 1',
    machineName: 'Machine A',
    state: 'Running',
    onlineState: 'Online',
    isOnline: true,
    lastSeenAtUtc: now,
    sessionOkCount: 8,
    sessionNgCount: 1,
    sessionErrorCount: 0,
    averageExecutionTimeMs: 24,
    lastOutcome: 'Ng',
    lastDiagnosticCode: 'WIRE_SWAP',
  },
  {
    stationId: 'station-b',
    stationName: 'Station B',
    lineName: 'Line 2',
    machineName: 'Machine B',
    state: 'Running',
    onlineState: 'Online',
    isOnline: true,
    lastSeenAtUtc: now,
    sessionOkCount: 5,
    sessionNgCount: 0,
    sessionErrorCount: 1,
    averageExecutionTimeMs: 31,
    lastOutcome: 'Error',
    lastDiagnosticCode: 'CAMERA_TIMEOUT',
  },
];

function buildResult(
  stationId: string,
  sequenceId: number,
  outcome: string,
  diagnosticCode: string,
  canonical: { executionOutcome?: string; decisionOutcome?: string; hasJudgmentSignal?: boolean } = {},
) {
  return {
    stationId,
    lineName: stationId === 'station-a' ? 'Line 1' : 'Line 2',
    sequenceId,
    messageId: `${stationId}-${sequenceId}`,
    runId: `run-${sequenceId}`,
    packageId: 'pkg-live',
    packageName: 'Live Package',
    packageVersion: '1.0.0',
    flowHash: 'sha256:test',
    imageId: `image-${sequenceId}`,
    outcome,
    inspectionStatus: outcome === 'Ok' ? 'OK' : outcome === 'Ng' ? 'NG' : 'Error',
    executionOutcome: canonical.executionOutcome ?? (outcome === 'Ok' || outcome === 'Ng' ? 'Succeeded' : 'Failed'),
    decisionOutcome: canonical.decisionOutcome ?? (outcome === 'Ok' ? 'Ok' : outcome === 'Ng' ? 'Ng' : 'Undetermined'),
    hasJudgmentSignal: canonical.hasJudgmentSignal ?? (outcome === 'Ok' || outcome === 'Ng'),
    executionTimeMs: 20 + sequenceId,
    diagnosticCode,
    diagnosticMessage: diagnosticCode,
    primaryOutputsPreview: {
      Station: stationId,
      Wire: diagnosticCode,
    },
    startedAtUtc: '2026-05-17T01:59:59Z',
    completedAtUtc: `2026-05-17T02:00:${String(sequenceId).padStart(2, '0')}Z`,
    createdAtUtc: `2026-05-17T02:00:${String(sequenceId).padStart(2, '0')}Z`,
  };
}

async function mockBaseApis(page: Page) {
  await page.route('**/api/auth/setup-status', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ requiresInitialAdminSetup: false }),
    });
  });

  await page.route('**/api/projects', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });

  await page.route('**/api/operators/library', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });
}

async function mockStationApis(page: Page, eventBody = '', stationList = stations, healthByStation: Record<string, unknown[]> = {}) {
  const allResults = [
    buildResult('station-a', 2, 'Ng', 'WIRE_SWAP'),
    buildResult('station-b', 1, 'Error', 'CAMERA_TIMEOUT'),
  ];

  await page.route('**/api/station-packages**', async route => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });

  await page.route('**/api/stations**', async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;

    if (path.endsWith('/api/stations/summary')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          totalStations: 2,
          onlineStations: 2,
          offlineStations: 0,
          runningStations: 2,
          alertCount: 1,
          totalOkCount: 13,
          totalNgCount: 1,
          totalErrorCount: 1,
          averageExecutionTimeMs: 27,
          offlineThresholdSeconds: 15,
          updatedAtUtc: now,
        }),
      });
      return;
    }

    if (path.endsWith('/api/stations/results')) {
      const stationId = url.searchParams.get('stationId');
      const items = stationId
        ? allResults.filter(item => item.stationId === stationId)
        : allResults;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items,
          totalCount: items.length,
          pageIndex: 0,
          pageSize: 12,
        }),
      });
      return;
    }

    if (path.endsWith('/api/stations/events')) {
      const initialState = [
        'event: initialState',
        `data: ${JSON.stringify({ summary: { offlineThresholdSeconds: 15 }, stations: stationList, recentResults: [] })}`,
        '',
        '',
      ].join('\n');
      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        body: `${initialState}${eventBody}`,
      });
      return;
    }

    const detailMatch = path.match(/\/api\/stations\/([^/]+)$/);
    if (detailMatch) {
      const stationId = decodeURIComponent(detailMatch[1]);
      const station = stationList.find(item => item.stationId === stationId);
      await route.fulfill({
        status: station ? 200 : 404,
        contentType: 'application/json',
        body: station ? JSON.stringify({
          ...station,
          recentResults: allResults.filter(item => item.stationId === stationId),
          recentHealth: healthByStation[stationId] ?? [],
          recentLogs: [],
          recentCommands: [],
        }) : '{}',
      });
      return;
    }

    if (path.endsWith('/api/stations')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(stationList),
      });
      return;
    }

    await route.fallback();
  });
}

async function openMonitor(page: Page, user?: Parameters<typeof bootAuthenticatedApp>[1]) {
  await mockBaseApis(page);
  await bootAuthenticatedApp(page, user);
  await page.locator('.nav-btn[data-view="stations"]').click();
  await expect(page.locator('#sm-results-workbench')).toBeVisible();
}

test.describe('Station monitor', () => {
  test('renders all-station real results and filters when a station is selected', async ({ page }) => {
    await mockStationApis(page);
    await openMonitor(page);

    await expect(page.locator('#sm-results-title')).toContainText('全站结果明细');
    await expect(page.locator('#sm-result-list')).toContainText('WIRE_SWAP');
    await expect(page.locator('#sm-result-list')).toContainText('CAMERA_TIMEOUT');

    await page.locator('[data-station-id="station-a"]').click();

    await expect(page.locator('#sm-results-title')).toContainText('Station A结果明细');
    await expect(page.locator('#sm-result-list')).toContainText('WIRE_SWAP');
    await expect(page.locator('#sm-result-list')).not.toContainText('CAMERA_TIMEOUT');
  });

  test('adds live Station SSE results to the merged result list', async ({ page }) => {
    const liveResult = buildResult('station-a', 9, 'Ng', 'LIVE_NG');
    const eventBody = [
      'id: 9',
      'event: stationResultAdded',
      `data: ${JSON.stringify({ stationId: 'station-a', station: stations[0], result: liveResult })}`,
      '',
      '',
    ].join('\n');

    await mockStationApis(page, eventBody);
    await openMonitor(page);

    await expect(page.locator('#sm-result-list')).toContainText('LIVE_NG');
  });

  test('renders canonical Invalid as decision invalid instead of execution failure', async ({ page }) => {
    const invalidResult = buildResult('station-a', 10, 'Error', 'LIVE_INVALID', {
      executionOutcome: 'Succeeded',
      decisionOutcome: 'Invalid',
      hasJudgmentSignal: false,
    });
    const eventBody = [
      'id: 10',
      'event: stationResultAdded',
      `data: ${JSON.stringify({ stationId: 'station-a', station: stations[0], result: invalidResult })}`,
      '',
      '',
    ].join('\n');

    await mockStationApis(page, eventBody);
    await openMonitor(page);

    const invalidCard = page.locator('.sm-monitor-result').filter({ hasText: 'LIVE_INVALID' });
    await expect(invalidCard).toContainText('判定无效');
    await expect(invalidCard).not.toContainText('执行失败');
  });

  test('surfaces backpressure troubleshooting advice in station detail', async ({ page }) => {
    const backpressureMessage = 'Station 结果同步出现背压。请检查：Studio 连接、工站到 Studio 的网络、防火墙规则、spool 磁盘空间/权限、StationSync 队列容量。 queued=1000; spoolPending=1500';
    const stationList = [
      {
        ...stations[0],
        lastDiagnosticCode: 'StationResultBackpressure',
        lastDiagnosticMessage: backpressureMessage,
        spoolPendingCount: 1500,
        spoolBytes: 1048576,
      },
      stations[1],
    ];
    const recentHealth = {
      'station-a': [{
        stationId: 'station-a',
        sequenceId: 4,
        runtimeState: 'Running',
        spoolPendingCount: 1500,
        spoolBytes: 1048576,
        diskFreeMb: 2048,
        diskTotalMb: 4096,
        currentPackageHealth: 'Loaded',
        lastErrorCode: 'StationResultBackpressure',
        lastErrorMessage: backpressureMessage,
        createdAtUtc: now,
      }],
    };

    await mockStationApis(page, '', stationList, recentHealth);
    await openMonitor(page);
    await page.locator('[data-station-id="station-a"]').click();

    const focus = page.locator('#sm-detail');
    await expect(focus).toContainText('排查建议');
    await expect(focus).toContainText('Studio 连接');
    await expect(focus).toContainText('防火墙规则');
    await expect(focus).toContainText('spool 磁盘空间/权限');
    await expect(focus).toContainText('StationSync 队列容量');
  });

  test('non-Admin capability gate blocks Station mutation requests at the handler boundary', async ({ page }) => {
    const mutationRequests: string[] = [];
    page.on('request', request => {
      if (request.method() !== 'GET' && new URL(request.url()).pathname.startsWith('/api/stations/')) {
        mutationRequests.push(`${request.method()} ${new URL(request.url()).pathname}`);
      }
    });

    await mockStationApis(page);
    await openMonitor(page, {
      userId: 'operator-e2e',
      id: 'operator-e2e',
      username: 'operator-e2e',
      displayName: 'E2E Operator',
      role: 'Operator',
      capabilities: ['station.packages.read'],
      passwordPolicy: { minimumLength: 12 },
    });
    await page.locator('[data-station-id="station-a"]').click();

    const pingButton = page.locator('[data-station-action="ping"]');
    await expect(pingButton).toBeDisabled();
    await expect(page.locator('[data-station-action="deploy"]')).toBeDisabled();
    await expect(page.locator('[data-station-action="testDeploy"]')).toBeDisabled();

    await pingButton.evaluate((button: HTMLButtonElement) => {
      button.disabled = false;
      button.click();
    });

    await expect(page.locator('.sm-command-status')).toContainText('没有执行此 Station 操作的权限');
    expect(mutationRequests).toEqual([]);
  });
});
