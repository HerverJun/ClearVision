import { expect, test, type Page, type Route } from '@playwright/test';
import {
  auditF02Request,
  expectGetOnly,
  f02BrowserFixture,
  fulfillF02Json,
  installF02BrowserStartup,
  type F02MethodAuditEntry
} from './f02-browser-fixture';

const stationFixture = Object.freeze({
  schemaVersion: 'f02-stations-read.v1',
  endpoint: Object.freeze([
    'GET /api/stations',
    'GET /api/stations/summary',
    'GET /api/stations/statistics',
    'GET /api/stations/{stationId}',
    'GET /api/stations/{stationId}/results',
    'GET /api/stations/{stationId}/health'
  ]),
  sourceSha: f02BrowserFixture.sourceSha,
  dataSource: f02BrowserFixture.dataSource
});

function outcomeStatistics(): Record<string, unknown> {
  return {
    totalAttemptCount: 9,
    executionSucceededCount: 5,
    validDecisionCount: 2,
    okCount: 1,
    ngCount: 1,
    undeterminedCount: 1,
    notApplicableCount: 1,
    invalidCount: 1,
    failedCount: 1,
    cancelledCount: 1,
    timedOutCount: 1,
    skippedCount: 1
  };
}

function station(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    stationId: 'station-a',
    stationName: '一号检测站',
    lineName: '一号线',
    machineName: 'CV-STATION-A',
    clientVersion: '2.1.0',
    areaName: 'A 区',
    workcellName: '单元 1',
    inspectionNodeName: '瓶盖检测',
    cameraAlias: '顶视相机',
    stationRole: 'Inspection',
    owner: '生产一组',
    isEnabled: true,
    remark: '只读 fixture',
    onlineState: 1,
    state: 'Running',
    runtimeState: 2,
    isOnline: true,
    startedAtUtc: '2026-07-15T01:00:00Z',
    lastSeenAtUtc: '2026-07-15T02:00:00Z',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageFlowHash: 'sha256:package',
    executionFlowHash: 'sha256:execution',
    flowHash: 'sha256:execution',
    executionSnapshotId: '11111111-1111-4111-8111-111111111111',
    projectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    executionRunMode: 'Production',
    currentRunId: 'run-a',
    sessionOutcomeStatistics: outcomeStatistics(),
    sessionOutcomeStatisticsIsLegacyProjection: false,
    lastExecutionOutcome: 'Succeeded',
    lastDecisionOutcome: 'Ng',
    lastDiagnosticCode: 'WIRE_SWAP',
    lastDiagnosticMessage: '线序错误',
    lastResultAtUtc: '2026-07-15T01:59:30Z',
    averageExecutionTimeMs: 24.5,
    spoolPendingCount: 2,
    spoolBytes: 4096,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Connected',
    currentPackageHealth: 'Healthy',
    ...overrides
  };
}

function result(): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    lineName: '一号线',
    sequenceId: 9,
    messageId: 'message-9',
    runId: 'run-9',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    outcome: 1,
    inspectionStatus: 'NG',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ng',
    executionTimeMs: 25,
    diagnosticCode: 'WIRE_SWAP',
    diagnosticMessage: '线序错误',
    completedAtUtc: '2026-07-15T02:00:00Z'
  };
}

function health(): Record<string, unknown> {
  return {
    schemaVersion: 2,
    stationId: 'station-a',
    sequenceId: 10,
    messageId: 'health-10',
    runtimeState: 2,
    processUptimeSeconds: 3600,
    cpuUsagePercent: 32.5,
    workingSetMb: 256,
    privateMemoryMb: 220,
    diskFreeMb: 20480,
    diskTotalMb: 51200,
    spoolPendingCount: 2,
    spoolBytes: 4096,
    cameraStatusSummary: 'Ready',
    plcStatusSummary: 'Connected',
    currentPackageId: 'pkg-a',
    currentPackageHealth: 'Healthy',
    lastErrorCode: null,
    lastErrorMessage: null,
    createdAtUtc: '2026-07-15T02:00:00Z'
  };
}

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, stationFixture.schemaVersion);
}

async function bootStations(
  page: Page,
  listPayload: unknown = [station()]
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  await installF02BrowserStartup(page);
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, 200, { userId: 'fixture-user', username: 'fixture-engineer', role: 'Engineer' });
      return;
    }
    if (url.pathname === '/api/stations') {
      await fulfill(route, 200, listPayload);
      return;
    }
    if (url.pathname === '/api/stations/summary') {
      await fulfill(route, 200, {
        totalStations: 1,
        onlineStations: 1,
        offlineStations: 0,
        runningStations: 1,
        faultedStations: 0,
        alertCount: 0,
        warningStations: 0,
        criticalStations: 0,
        outcomeStatistics: outcomeStatistics(),
        averageExecutionTimeMs: 24.5,
        offlineThresholdSeconds: 15,
        updatedAtUtc: '2026-07-15T02:00:00Z'
      });
      return;
    }
    if (url.pathname === '/api/stations/statistics') {
      await fulfill(route, 200, {
        fromUtc: null,
        toUtc: null,
        outcomeStatistics: outcomeStatistics(),
        averageExecutionTimeMs: 24.5,
        byStation: [],
        byDiagnosticCode: [],
        hourlyTrend: []
      });
      return;
    }
    if (url.pathname === '/api/stations/station-a/results') {
      await fulfill(route, 200, [result()]);
      return;
    }
    if (url.pathname === '/api/stations/station-a/health') {
      await fulfill(route, 200, [health()]);
      return;
    }
    if (url.pathname === '/api/stations/station-a') {
      await fulfill(route, 403, { error: 'StationAdminRequired' });
      return;
    }
    await fulfill(route, 404, { error: 'NotFound' });
  });
  return audit;
}

test('Station list uses URL filters, preserves nine outcomes and stays GET-only', async ({ page }) => {
  const audit = await bootStations(page);
  await page.goto('/studio/index.html#/stations?q=一号&online=Online&range=week&outcome=Ng&diagnosticCode=WIRE_SWAP');

  await expect(page.locator('[data-capability="stations-read"]')).toBeVisible();
  await expect(page.getByRole('cell', { name: /一号检测站/ })).toBeVisible();
  const outcomeCounters = page.locator('.stations-page__outcomes');
  await expect(outcomeCounters.getByText('未判定', { exact: true })).toBeVisible();
  await expect(outcomeCounters.getByText('不适用', { exact: true })).toBeVisible();
  await expect(outcomeCounters.getByText('判定无效', { exact: true })).toBeVisible();
  await expect(outcomeCounters.getByText('执行失败', { exact: true })).toBeVisible();
  await expect.poll(() => audit.some(entry => entry.path.includes(
    '/api/stations/statistics?range=week&status=Ng&diagnosticCode=WIRE_SWAP'
  ))).toBe(true);
  expect(expectGetOnly(audit)).toBe(true);
});

test('Station detail degrades only Admin 403 and keeps results and health visible', async ({ page }) => {
  const audit = await bootStations(page);
  await page.goto('/studio/index.html#/stations/station-a');

  await expect(page.locator('[data-capability="stations-read-detail"]')).toBeVisible();
  await expect(page.getByText('管理员增强信息不可用')).toBeVisible();
  await expect(page.getByText('判定 NG')).toBeVisible();
  await expect(page.getByText('进程运行时长')).toBeVisible();
  await expect(page.getByText('Station 普通详情读取失败')).toHaveCount(0);
  expect(expectGetOnly(audit)).toBe(true);
  expect(audit.some(entry => entry.path.includes('/events'))).toBe(false);
  expect(audit.some(entry => /commands|logs|audit|packages|download/.test(entry.path))).toBe(false);
});

test('Station list surfaces frozen-contract malformed and empty responses', async ({ page }) => {
  await bootStations(page, { items: [] });
  await page.goto('/studio/index.html#/stations');
  await expect(page.getByText('Station 列表读取失败')).toBeVisible();

  await page.unrouteAll({ behavior: 'wait' });
  await bootStations(page, []);
  await page.reload();
  await expect(page.getByText('暂无 Station')).toBeVisible();
});
