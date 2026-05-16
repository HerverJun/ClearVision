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

function buildResult(stationId: string, sequenceId: number, outcome: string, diagnosticCode: string) {
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

async function mockStationApis(page: Page, eventBody = '') {
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
        `data: ${JSON.stringify({ summary: { offlineThresholdSeconds: 15 }, stations, recentResults: [] })}`,
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
      const station = stations.find(item => item.stationId === stationId);
      await route.fulfill({
        status: station ? 200 : 404,
        contentType: 'application/json',
        body: station ? JSON.stringify({
          ...station,
          recentResults: allResults.filter(item => item.stationId === stationId),
          recentHealth: [],
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
        body: JSON.stringify(stations),
      });
      return;
    }

    await route.fallback();
  });
}

async function openMonitor(page: Page) {
  await mockBaseApis(page);
  await bootAuthenticatedApp(page);
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
});
