import { expect, test, type Page, type Route } from '@playwright/test';
import {
  auditF02Request,
  captureF02VisualEvidence,
  createF02RuntimeErrorAudit,
  expectGetOnly,
  f02BrowserFixture,
  f02G3VisualMatrix,
  fulfillF02Json,
  hasF02VisualEvidenceTarget,
  installF02BrowserStartup,
  installF02VisualPreferences,
  type F02MethodAuditEntry
} from './f02-browser-fixture';
import {
  captureF04VisualEvidence,
  hasF04VisualEvidenceTarget
} from './f04-browser-evidence';

const stationFixture = Object.freeze({
  schemaVersion: 'f02-stations-read.v1',
  endpoint: Object.freeze([
    'GET /api/stations',
    'GET /api/stations/events',
    'GET /api/stations/summary',
    'GET /api/stations/statistics',
    'GET /api/stations/{stationId}',
    'GET /api/stations/{stationId}/results',
    'GET /api/stations/{stationId}/health',
    'GET /api/stations/{stationId}/logs',
    'GET /api/stations/{stationId}/commands',
    'GET /api/stations/{stationId}/commands/by-client-request/{clientRequestId}',
    'GET /api/stations/audit',
    'GET /api/station-packages',
    'POST /api/stations/{stationId}/commands',
    'PATCH /api/stations/{stationId}/identity',
    'POST /api/stations/{stationId}/deploy-package'
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
    offlineReason: null,
    remark: '只读 fixture',
    onlineState: 1,
    state: 'Running',
    runtimeState: 2,
    isOnline: true,
    startedAtUtc: '2026-07-15T01:00:00Z',
    lastSeenAtUtc: '2026-07-15T02:00:00Z',
    packageId: 'pkg-a',
    packageName: '瓶盖检测包',
    packageVersion: '1.0.0',
    packageSha256: `sha256:${'a'.repeat(64)}`,
    sourceProjectId: '11111111-2222-3333-4444-555555555555',
    sourceProjectRevision: 12,
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

function summary(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
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
    updatedAtUtc: '2026-07-15T02:00:00Z',
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
    packageFlowHash: 'sha256:package',
    executionFlowHash: 'sha256:execution',
    flowHash: 'sha256:execution',
    executionSnapshotId: '11111111-1111-4111-8111-111111111111',
    projectRevision: 12,
    decisionConfigurationHash: 'sha256:decision',
    executionRunMode: 'Production',
    outcome: 1,
    inspectionStatus: 'NG',
    executionOutcome: 'Succeeded',
    decisionOutcome: 'Ng',
    hasJudgmentSignal: true,
    decisionSource: 'FinalDecision',
    reasonCode: 'WIRE_SWAP',
    executionTimeMs: 25,
    diagnosticCode: 'WIRE_SWAP',
    diagnosticMessage: '线序错误',
    primaryOutputsPreview: { score: '0.91' },
    startedAtUtc: '2026-07-15T01:59:59Z',
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

function command(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    schemaVersion: 2, commandId: 'command-a', stationId: 'station-a', commandType: 'Ping', payloadJson: '{}',
    createdAtUtc: new Date().toISOString(), expiresAtUtc: '2026-07-26T03:00:00Z', issuedBy: 'fixture-admin',
    correlationId: 'correlation-a', clientRequestId: 'request-a', status: 'Created', progressPercent: 0, deliveredAtUtc: null,
    acceptedAtUtc: null, startedAtUtc: null, completedAtUtc: null, resultMessage: null, errorCode: null,
    ...overrides
  };
}

const stationLog = {
  schemaVersion: 2, stationId: 'station-a', sequenceId: 11, messageId: 'log-11', timestampUtc: '2026-07-15T02:00:00Z',
  level: 'WARN', source: 'RuntimeHost', eventId: 'runtime-warning', messageTemplate: null,
  renderedMessage: '运行包健康状态降级', exceptionType: null, exceptionMessage: null,
  correlationId: null, runId: 'run-a', packageId: 'pkg-a', createdAtUtc: '2026-07-15T02:00:01Z'
};

const stationPackage = {
  schemaVersion: 2, packageId: 'pkg-a', packageName: '瓶盖检测包', packageVersion: '1.0.0', packageKind: 'Production',
  flowHash: 'sha256:package', sourceProjectId: '11111111-2222-3333-4444-555555555555', sourceProjectRevision: 12,
  decisionConfigurationHash: 'sha256:decision', createdBy: 'fixture-admin', minStationVersion: '2.0.0', requiredOperators: ['Threshold'],
  sizeBytes: 4096, sha256: 'a'.repeat(64), createdAtUtc: '2026-07-15T01:00:00Z'
};

function deploymentPayload(packageRecord = stationPackage): Record<string, unknown> {
  return {
    packageId: packageRecord.packageId,
    packageName: packageRecord.packageName,
    packageVersion: packageRecord.packageVersion,
    packageKind: packageRecord.packageKind,
    sha256: packageRecord.sha256,
    flowHash: packageRecord.flowHash,
    sourceProjectId: packageRecord.sourceProjectId,
    sourceProjectRevision: packageRecord.sourceProjectRevision,
    decisionConfigurationHash: packageRecord.decisionConfigurationHash,
    downloadUrl: `/api/station-packages/${encodeURIComponent(packageRecord.packageId)}/download`
  };
}

async function fulfill(route: Route, status: number, body: unknown): Promise<void> {
  await fulfillF02Json(route, status, body, stationFixture.schemaVersion);
}

async function bootStations(
  page: Page,
  listPayload: unknown = [station()],
  summaryPayload: unknown = summary(),
  role: 'Admin' | 'Engineer' = 'Engineer',
  initialCommands: Record<string, unknown>[] = [command({
    status: 'Succeeded', progressPercent: 100, completedAtUtc: '2026-07-15T02:01:00Z'
  })],
  options: Readonly<{ commandPostUnknown?: boolean }> = {}
): Promise<F02MethodAuditEntry[]> {
  const audit: F02MethodAuditEntry[] = [];
  const commands = [...initialCommands];
  let identity = station();
  await installF02BrowserStartup(page, { 'Studio2.StationsRead': true });
  await page.route('**/health', route => fulfill(route, 200, { status: 'Healthy', port: 5177 }));
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    audit.push(auditF02Request(request));
    if (url.pathname === '/api/auth/setup-status') {
      await fulfill(route, 200, { requiresInitialAdminSetup: false, usernameMinLength: 3, passwordMinLength: 6, requiresUppercase: false, requiresLowercase: false, requiresDigit: false });
      return;
    }
    if (url.pathname === '/api/auth/me') {
      await fulfill(route, 200, { userId: 'fixture-user', username: `fixture-${role.toLowerCase()}`, role });
      return;
    }
    if (url.pathname === '/api/stations') {
      await fulfill(route, 200, listPayload);
      return;
    }
    if (url.pathname === '/api/stations/events') {
      const stations = Array.isArray(listPayload) ? listPayload : [];
      const body = [
        'event: initialState',
        `data: ${JSON.stringify({ eventSequenceId: 12, summary: summaryPayload, stations, recentResults: [] })}`,
        '',
        ':keepalive',
        '',
        ''
      ].join('\n');
      await route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        headers: { 'Cache-Control': 'no-cache', 'X-F02-Fixture': stationFixture.schemaVersion },
        body
      });
      return;
    }
    if (url.pathname === '/api/stations/summary') {
      await fulfill(route, 200, summaryPayload);
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
    if (url.pathname === '/api/stations/results') {
      const pageIndex = Number(url.searchParams.get('pageIndex') ?? 0);
      const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
      const item = result();
      const items = !url.searchParams.get('stationId') || url.searchParams.get('stationId') === item.stationId
        ? [item]
        : [];
      await fulfill(route, 200, { items, totalCount: items.length, pageIndex, pageSize });
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
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? identity : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/logs') {
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? [stationLog] : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/commands') {
      if (request.method() === 'POST') {
        const body = request.postDataJSON();
        const created = command({
          commandId: `command-${commands.length + 1}`, commandType: body.commandType,
          clientRequestId: body.clientRequestId, payloadJson: body.payloadJson
        });
        commands.unshift(created);
        if (options.commandPostUnknown) {
          await route.abort('failed');
          return;
        }
        await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? created : { error: 'StationAdminRequired' });
      } else {
        await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? commands : { error: 'StationAdminRequired' });
      }
      return;
    }
    if (url.pathname.startsWith('/api/stations/station-a/commands/by-client-request/')) {
      const requestId = decodeURIComponent(url.pathname.split('/').at(-1) ?? '');
      const found = commands.find(item =>
        item.clientRequestId === requestId && item.commandType === url.searchParams.get('commandType')
      );
      await fulfill(route, found ? 200 : 404, found ?? { error: 'StationCommandNotFound' });
      return;
    }
    if (url.pathname === '/api/stations/audit') {
      const deployCommand = commands.find(item => item.commandType === 'DeployPackage');
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? [{
        auditId: 'audit-a', userName: 'fixture-admin', action: 'StationCommandCreated', targetStationId: 'station-a',
        commandId: deployCommand?.commandId ?? 'command-a', payloadSummary: deployCommand ? 'DeployPackage pkg-a' : 'Ping',
        createdAtUtc: '2026-07-15T02:00:00Z', result: deployCommand?.status ?? 'Created', clientIp: '127.0.0.1'
      }] : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/station-packages') {
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? [stationPackage] : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/identity') {
      identity = { ...identity, ...request.postDataJSON() };
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? identity : { error: 'StationAdminRequired' });
      return;
    }
    if (url.pathname === '/api/stations/station-a/deploy-package') {
      const body = request.postDataJSON();
      const created = command({
        commandId: `deploy-${commands.length + 1}`, commandType: 'DeployPackage', clientRequestId: body.clientRequestId,
        payloadJson: JSON.stringify(deploymentPayload())
      });
      commands.unshift(created);
      await fulfill(route, role === 'Admin' ? 200 : 403, role === 'Admin' ? created : { error: 'StationAdminRequired' });
      return;
    }
    await fulfill(route, 404, { error: 'NotFound' });
  });
  return audit;
}

test('Station list uses URL filters, preserves nine outcomes and stays GET-only', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF02RuntimeErrorAudit(page);
  const audit = await bootStations(page);
  await page.goto('/studio/index.html#/stations?q=一号&online=Online&range=week&outcome=Ng&diagnosticCode=WIRE_SWAP');

  await expect(page.locator('[data-capability="stations-read"]')).toBeVisible();
  const viewTabs = page.getByRole('tablist', { name: '工作站监控视图' });
  await expect(viewTabs.getByRole('tab', { name: '异常调查' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('cell', { name: /一号检测站/ })).toBeVisible();
  await viewTabs.getByRole('tab', { name: '全站概览' }).click();
  const outcomeCounters = page.locator('.stations-page__outcomes');
  await expect(outcomeCounters).toContainText('未判定 1');
  await expect(outcomeCounters).toContainText('不适用 1');
  await expect(outcomeCounters).toContainText('判定无效 1');
  await expect(outcomeCounters).toContainText('执行失败 1');
  await expect.poll(() => audit.some(entry => entry.path.includes(
    '/api/stations/statistics?range=week&status=Ng&diagnosticCode=WIRE_SWAP'
  ))).toBe(true);
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'stations', viewport, runtimeErrors, requestAudit: audit
    });
  }
  expect(expectGetOnly(audit)).toBe(true);
});

test('Engineer Station journey remains read-only and never mounts the Admin control domain', async ({ page }) => {
  const viewport = { width: 1600, height: 1000 } as const;
  await page.setViewportSize(viewport);
  const runtimeErrors = createF02RuntimeErrorAudit(page);
  const audit = await bootStations(page);
  await page.goto('/studio/index.html#/stations/station-a');

  await expect(page.locator('[data-capability="stations-read-detail"]')).toBeVisible();
  await expect(page.locator('[data-capability="station-admin-control"]')).toHaveCount(0);
  await expect(page.getByText('判定 NG')).toBeVisible();
  await expect(page.getByText('进程运行时长')).toBeVisible();
  await expect(page.getByText('工作站详情读取失败')).toHaveCount(0);
  await expect(page.getByText('TCP', { exact: true })).toBeVisible();
  await expect(page.getByText('未上报/不可确认', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('运行包状态正常', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('已就绪', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('已连接', { exact: true }).first()).toBeVisible();
  await expect(page.getByTestId('station-production-trace')).toContainText('当前角色仅能查看监控摘要');
  if (hasF04VisualEvidenceTarget()) {
    await captureF04VisualEvidence(page, {
      scenario: 'station-detail-abnormal', viewport, runtimeErrors, requestAudit: audit
    });
  }
  expect(expectGetOnly(audit)).toBe(true);
  expect(audit.some(entry => entry.path === '/api/stations/events')).toBe(true);
  expect(audit.some(entry => /commands|logs|audit|packages|download/.test(entry.path))).toBe(false);

  await page.getByTestId('station-result-link').click();
  await expect(page).toHaveURL(/#\/results\?source=station&stationId=station-a&resultId=message-9/);
  await expect(page.getByText('远程结果仅保留摘要')).toBeVisible();
  await expect(page.locator('[data-remote-image-status="not-uploaded"]')).toBeVisible();
  await expect.poll(() => audit.some(entry =>
    entry.path.includes('/api/stations/results?stationId=station-a')
  )).toBe(true);
  expect(audit.some(entry => entry.path.startsWith('/api/images/'))).toBe(false);

  await page.getByTestId('results-open-station').click();
  await expect(page).toHaveURL(/#\/stations\/station-a\?returnTo=/);
  await expect(page.getByText('返回检测结果')).toBeVisible();
});

test('Station investigation preserves fleet filters through detail and Results return', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await bootStations(page);
  const fleetPath = '/stations?q=一号&online=Online&runtime=Running&range=week&outcome=Ng&diagnosticCode=WIRE_SWAP';
  await page.goto(`/studio/index.html#${fleetPath}`);

  await page.getByRole('link', { name: '一号检测站', exact: true }).first().click();
  await expect(page).toHaveURL(/#\/stations\/station-a\?returnTo=/);
  await page.getByTestId('station-result-link').click();
  await expect(page).toHaveURL(/#\/results\?source=station&stationId=station-a&resultId=message-9&returnTo=/);

  await page.getByTestId('results-return-workspace').click();
  await expect(page).toHaveURL(/#\/stations\/station-a\?returnTo=/);
  await page.getByRole('link', { name: '返回工作站列表' }).click();
  await expect.poll(() => decodeURIComponent(new URL(page.url()).hash.slice(1))).toBe(fleetPath);
});

test('Admin Station journey mounts controls and creates command, identity and package operations', async ({ page, context }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  const runtimeErrors = createF02RuntimeErrorAudit(page);
  const commandAudit = await bootStations(page, [station()], summary(), 'Admin');
  await page.goto('/studio/index.html#/stations/station-a');

  const admin = page.locator('[data-capability="station-admin-control"]');
  await expect(admin).toBeVisible();
  await expect(admin.getByText('运行包健康状态降级')).toBeVisible();
  await admin.getByTestId('station-issue-command').click();
  await expect(admin.getByText('命令已创建；执行结果尚未确认。')).toBeVisible();

  const identityPage = await context.newPage();
  const identityErrors = createF02RuntimeErrorAudit(identityPage);
  const identityAudit = await bootStations(identityPage, [station()], summary(), 'Admin');
  await identityPage.goto('/studio/index.html#/stations/station-a');
  const identityAdmin = identityPage.locator('[data-capability="station-admin-control"]');
  await identityAdmin.getByLabel('工作站名称').fill('一号检测站（修订）');
  await identityAdmin.getByTestId('station-save-identity').click();
  await expect(identityAdmin.getByText('工作站身份已由后端修订。')).toBeVisible();

  await identityPage.close();

  const deployPage = await context.newPage();
  const deployErrors = createF02RuntimeErrorAudit(deployPage);
  const deployAudit = await bootStations(deployPage, [station()], summary(), 'Admin');
  await deployPage.goto('/studio/index.html#/stations/station-a');
  const deployAdmin = deployPage.locator('[data-capability="station-admin-control"]');
  await deployAdmin.getByLabel('生产运行包').selectOption('pkg-a');
  await deployAdmin.getByTestId('station-deploy-package').click();
  await expect(deployAdmin.getByText('部署命令已创建；仅在命令成功且工作站激活身份匹配后才算部署完成。')).toBeVisible();
  await expect(deployAdmin.getByTestId('station-deployment-status').getByText('命令已创建')).toBeVisible();

  const audits = [...commandAudit, ...identityAudit, ...deployAudit];
  expect(audits.some(entry => entry.method === 'POST' && entry.path === '/api/stations/station-a/commands')).toBe(true);
  expect(audits.some(entry => entry.method === 'PATCH' && entry.path === '/api/stations/station-a/identity')).toBe(true);
  expect(audits.some(entry => entry.method === 'POST' && entry.path === '/api/stations/station-a/deploy-package')).toBe(true);
  expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(identityErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  expect(deployErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  await deployPage.close();
});

test('F10 Station preserves unknown outcome, blocks duplicate submit and reconciles by request identity', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  const audit = await bootStations(page, [station()], summary(), 'Admin', [], { commandPostUnknown: true });
  await page.goto('/studio/index.html#/stations/station-a');

  const admin = page.locator('[data-capability="station-admin-control"]');
  const submit = admin.getByTestId('station-issue-command');
  await submit.click();
  await expect(admin.getByText('操作结果未知')).toBeVisible();
  await expect(submit).toBeDisabled();
  expect(audit.filter(entry => entry.method === 'POST' && entry.path === '/api/stations/station-a/commands')).toHaveLength(1);

  await admin.getByRole('button', { name: '读取后端状态' }).click();
  await expect(admin.getByText('已按请求标识确认命令记录；执行终态仍以后端命令状态为准。')).toBeVisible();
  await expect(submit).toBeDisabled();
  expect(audit.filter(entry => entry.method === 'POST' && entry.path === '/api/stations/station-a/commands')).toHaveLength(1);
  expect(audit.filter(entry => entry.method === 'GET' && entry.path.includes('/commands/by-client-request/'))).toHaveLength(1);
});

test('Station list surfaces frozen-contract malformed and empty responses', async ({ page }) => {
  await bootStations(page, { items: [] });
  await page.goto('/studio/index.html#/stations');
  await expect(page.getByRole('heading', { name: '工作站列表读取失败', exact: true })).toBeVisible();

  await page.unrouteAll({ behavior: 'wait' });
  await bootStations(page, []);
  await page.reload();
  await expect(page.getByRole('heading', { name: '暂无工作站', exact: true })).toBeVisible();
});

for (const visual of f02G3VisualMatrix) {
  const scenario = `stations-${visual.viewport.width}x${visual.viewport.height}-${visual.theme}-${visual.density}`;
  test(`captures ${scenario} Browser fixture evidence`, async ({ page }) => {
    test.skip(
      !hasF02VisualEvidenceTarget() && !hasF04VisualEvidenceTarget(),
      'Visual evidence output was not requested.'
    );
    await page.setViewportSize(visual.viewport);
    await installF02VisualPreferences(page, visual.theme, visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const mixedStations = [
      station({
        lastExecutionOutcome: 'Succeeded',
        lastDecisionOutcome: 'Ok',
        lastDiagnosticCode: null,
        lastDiagnosticMessage: null,
        spoolPendingCount: 0,
        spoolBytes: 0
      }),
      station({
        stationId: 'station-b',
        stationName: '二号检测站',
        lineName: '二号线',
        machineName: 'CV-STATION-B',
        onlineState: 'Offline',
        runtimeState: 'Faulted',
        isOnline: false,
        offlineReason: 'HeartbeatExpired',
        packageName: '端子检测包',
        lastExecutionOutcome: 'Failed',
        lastDecisionOutcome: 'Undetermined',
        lastDiagnosticCode: 'CAMERA_DISCONNECTED',
        lastDiagnosticMessage: '相机连接中断'
      }),
      station({
        stationId: 'station-c',
        stationName: '三号检测站',
        lineName: '三号线',
        machineName: 'CV-STATION-C',
        onlineState: 'Degraded',
        runtimeState: 'Paused',
        packageId: null,
        packageName: null,
        packageVersion: null,
        currentPackageHealth: 'NoPackage',
        lastExecutionOutcome: 'Succeeded',
        lastDecisionOutcome: 'Ng',
        lastDiagnosticCode: 'QUALITY_GATE',
        lastDiagnosticMessage: '连续 NG，等待复核'
      })
    ];
    const audit = await bootStations(page, mixedStations, summary({
      totalStations: 3,
      onlineStations: 1,
      offlineStations: 1,
      runningStations: 1,
      faultedStations: 1,
      alertCount: 2,
      warningStations: 1,
      criticalStations: 1
    }));
    await page.goto('/studio/index.html#/stations');
    await expect(page.locator('[data-capability="stations-read"]')).toBeVisible();
    const viewTabs = page.getByRole('tablist', { name: '工作站监控视图' });
    await expect(viewTabs.getByRole('tab', { name: '全站概览' })).toHaveAttribute('aria-selected', 'true');
    const priorityItems = page.locator('.stations-page__priority-list li');
    await expect(priorityItems).toHaveCount(2);
    await expect(priorityItems.nth(0)).toContainText('二号检测站');
    await expect(priorityItems.nth(1)).toContainText('三号检测站');
    if (hasF02VisualEvidenceTarget()) {
      await captureF02VisualEvidence(page, {
        scenario: `${scenario}-overview`,
        viewport: visual.viewport,
        theme: visual.theme,
        density: visual.density,
        requests: audit,
        runtimeErrors
      });
      await viewTabs.getByRole('tab', { name: '异常调查' }).click();
      await expect(viewTabs.getByRole('tab', { name: '异常调查' })).toHaveAttribute('aria-selected', 'true');
      const rows = page.locator('.stations-page__list-panel tbody tr');
      await expect(rows.nth(0)).toContainText('二号检测站');
      await expect(rows.nth(1)).toContainText('三号检测站');
      await expect(rows.nth(1)).toContainText('未激活运行包');
      await expect(rows.nth(2)).toContainText('一号检测站');
      await captureF02VisualEvidence(page, {
        scenario: `${scenario}-investigation`,
        viewport: visual.viewport,
        theme: visual.theme,
        density: visual.density,
        requests: audit,
        runtimeErrors
      });
      await page.goto('/studio/index.html#/stations/station-a');
      await expect(page.locator('[data-capability="stations-read-detail"]')).toBeVisible();
      await expect(page.locator('[data-capability="station-admin-control"]')).toHaveCount(0);
      await captureF02VisualEvidence(page, {
        scenario: `${scenario}-detail`,
        viewport: visual.viewport,
        theme: visual.theme,
        density: visual.density,
        requests: audit,
        runtimeErrors
      });
    }
    if (hasF04VisualEvidenceTarget()) {
      await captureF04VisualEvidence(page, {
        scenario,
        viewport: visual.viewport,
        runtimeErrors,
        requestAudit: audit,
        notes: ['F04.1 product baseline short-screen evidence.']
      });
    }
    expect(expectGetOnly(audit)).toBe(true);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}

for (const visual of [
  { id: 'station-admin-wide-light-compact', width: 1920, height: 1080, density: 'compact' },
  { id: 'station-admin-pressure-light-comfortable', width: 1536, height: 864, density: 'comfortable' }
] as const) {
  test(`captures ${visual.id} deployment identity evidence`, async ({ page }) => {
    test.skip(
      !hasF02VisualEvidenceTarget(),
      'Visual evidence output was not requested.'
    );
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, 'light', visual.density);
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const deploymentCommand = command({
      commandId: 'deploy-succeeded', commandType: 'DeployPackage', clientRequestId: 'deploy-request-1',
      payloadJson: JSON.stringify(deploymentPayload()), status: 'Succeeded', progressPercent: 100,
      completedAtUtc: '2026-07-15T02:02:00Z', resultMessage: 'Package pkg-a deployed.'
    });
    const audit = await bootStations(page, [station()], summary(), 'Admin', [deploymentCommand]);
    await page.goto('/studio/index.html#/stations/station-a');
    const admin = page.locator('[data-capability="station-admin-control"]');
    await expect(admin).toBeVisible();
    const deploymentStatus = admin.getByTestId('station-deployment-status');
    await expect(deploymentStatus.getByText('部署完成')).toBeVisible();
    await expect(page.getByTestId('station-production-trace')).toContainText('身份闭合');
    await deploymentStatus.evaluate(element => element.scrollIntoView({ block: 'center' }));
    await captureF02VisualEvidence(page, {
      scenario: visual.id,
      viewport: { width: visual.width, height: visual.height },
      theme: 'light',
      density: visual.density,
      requests: audit,
      runtimeErrors
    });
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}

for (const visual of [
  { id: 'g6-production-trace-wide', width: 1920, height: 1080 },
  { id: 'g6-production-trace-standard', width: 1366, height: 768 },
  { id: 'g6-production-trace-short', width: 1366, height: 600 }
] as const) {
  test(`captures ${visual.id} identity-chain evidence`, async ({ page }) => {
    test.skip(!hasF02VisualEvidenceTarget(), 'Visual evidence output was not requested.');
    await page.setViewportSize({ width: visual.width, height: visual.height });
    await installF02VisualPreferences(page, 'light', 'compact');
    const runtimeErrors = createF02RuntimeErrorAudit(page);
    const deploymentCommand = command({
      commandId: 'deploy-succeeded',
      commandType: 'DeployPackage',
      clientRequestId: 'deploy-request-g6',
      payloadJson: JSON.stringify(deploymentPayload()),
      status: 'Succeeded',
      progressPercent: 100,
      completedAtUtc: '2026-07-15T02:02:00Z',
      resultMessage: 'Package pkg-a deployed.'
    });
    const audit = await bootStations(page, [station()], summary(), 'Admin', [deploymentCommand]);
    await page.goto('/studio/index.html#/stations/station-a');
    const trace = page.getByTestId('station-production-trace');
    await expect(trace).toContainText('身份闭合');
    await expect(trace).toContainText('deploy-succeeded · Succeeded');
    await expect(trace).toContainText('run-9 · message-9');
    await expect(trace.locator('[data-remote-image-status="not-uploaded"]')).toBeVisible();
    await trace.evaluate(element => {
      element.style.scrollMarginTop = '60px';
      element.scrollIntoView({ block: 'start' });
    });
    await captureF02VisualEvidence(page, {
      scenario: visual.id,
      viewport: { width: visual.width, height: visual.height },
      theme: 'light',
      density: 'compact',
      requests: audit,
      runtimeErrors
    });
    expect(audit.some(entry => entry.path.startsWith('/api/images/'))).toBe(false);
    expect(runtimeErrors).toEqual({ consoleErrors: [], pageErrors: [] });
  });
}
